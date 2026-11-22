using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using BepInEx.Configuration;
using HarmonyLib;
using SaltExtraDebug;

internal static class FreeBuildSignChecks
{
    private static int checks;

    internal static int Run()
    {
        Type patch = typeof(Plugin).Assembly.GetType("SaltExtraDebug.FreeBuildSignEditing", true);
        MethodInfo native = AccessTools.Method(typeof(Sign), nameof(Sign.OnInteract));
        MethodInfo ownership = AccessTools.Method(typeof(WorldInfoManager), nameof(WorldInfoManager.IsOnPlayerOwnedPlot));
        MethodInfo permission = AccessTools.Method(patch, "CanEditSign");
        MethodInfo transpiler = AccessTools.Method(patch, "Transpiler");
        var original = PatchProcessor.GetOriginalInstructions(native).ToArray();
        var rewritten = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { original })).ToArray();
        Check(original.Count(i => i.Calls(ownership)) == 1, "Native sign has exactly one ownership gate.");
        Check(rewritten.Count(i => i.Calls(permission)) == 1 && rewritten.Length == original.Length + 1,
            "Only one permission override is inserted.");
        int gate = Array.FindIndex(rewritten, i => i.Calls(ownership));
        Check(rewritten[gate + 1].Calls(permission), "Override receives the native ownership result before branching.");
        var unchanged = rewritten.Where(i => !i.Calls(permission)).ToArray();
        Check(original.Zip(unchanged, (a, b) => a.opcode == b.opcode && Equals(a.operand, b.operand)
            && a.labels.SequenceEqual(b.labels) && a.blocks.SequenceEqual(b.blocks)).All(equal => equal),
            "Native read/edit branches and their callbacks are unchanged.");

        var generator = new DynamicMethod("SignLabelFixture", typeof(void), Type.EmptyTypes).GetILGenerator();
        Label label = generator.DefineLabel();
        var call = new CodeInstruction(OpCodes.Callvirt, ownership);
        call.labels.Add(label);
        call.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        call.blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
        var converted = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { new[] { call } })).ToArray();
        Check(converted[0].labels.Contains(label), "Branches still enter the native ownership call.");
        Check(converted[0].blocks.Single().blockType == ExceptionBlockType.BeginExceptionBlock
            && converted[1].blocks.Single().blockType == ExceptionBlockType.EndExceptionBlock,
            "Permission evaluation stays inside the original exception scope.");
        Check(call.blocks.Count == 2, "Transpiler does not mutate its input instructions.");

        FieldInfo freeBuild = AccessTools.Field(typeof(Plugin), "FreeBuildEnabled");
        object priorConfig = freeBuild.GetValue(null);
        var canEdit = (Func<bool, bool>)permission.CreateDelegate(typeof(Func<bool, bool>));
        try
        {
            freeBuild.SetValue(null, null);
            Check(!canEdit(false) && canEdit(true), "Missing setting preserves native permissions.");
            // This headless host doesn't initialize BepInEx's global paths.
            // Use the real ConfigEntry.Value getter with an in-memory backing
            // field, avoiding any reads/writes of the user's configuration.
            var setting = (ConfigEntry<bool>)FormatterServices.GetUninitializedObject(typeof(ConfigEntry<bool>));
            FieldInfo value = typeof(ConfigEntry<bool>).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Single(field => field.FieldType == typeof(bool));
            freeBuild.SetValue(null, setting);
            Check(!canEdit(false), "Free Build off: NPC/unowned property stays read-only.");
            Check(canEdit(true), "Free Build off: player-owned sign remains editable.");
            value.SetValue(setting, true);
            Check(canEdit(false), "Free Build on: NPC/unowned property becomes editable.");
            Check(canEdit(true), "Free Build on: player-owned sign remains editable.");
            value.SetValue(setting, false);
            Check(!canEdit(false), "Turning the toggle off restores restriction immediately for the next open.");
            Check(canEdit(true), "Turning it off never removes normal player ownership rights.");
        }
        finally { freeBuild.SetValue(null, priorConfig); }

        var harmony = new Harmony("saltextradebug.tests.sign-editing");
        try
        {
            var patched = harmony.CreateClassProcessor(patch).Patch();
            // Patch() returns the generated replacement, not the original.
            Check(patched.Count == 1 && Harmony.GetPatchInfo(native)?.Owners.Contains(harmony.Id) == true,
                "Actual Harmony patch installs on Sign.OnInteract only.");
            foreach (MethodInfo untouched in new[] { ownership,
                AccessTools.Method(typeof(Sign), nameof(Sign.OnNPCInteract)),
                AccessTools.Method(typeof(Sign), nameof(Sign.Serialize)),
                AccessTools.Method(typeof(Sign), nameof(Sign.Deserialize)),
                AccessTools.Method(typeof(SimpleSign), nameof(SimpleSign.OnInteract)) })
                Check(Harmony.GetPatchInfo(untouched) == null, "Unrelated method untouched: " + untouched);
        }
        finally { harmony.Unpatch(native, HarmonyPatchType.All, harmony.Id); }
        Console.WriteLine($"Free Build signs: {checks} permission/native-IL checks.");
        return checks;
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        checks++;
    }
}
