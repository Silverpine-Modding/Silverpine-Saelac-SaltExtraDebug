using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using SaltExtraDebug;
using UnityEngine;

// Run with the existing Items/MonoHost host and the game's Mono runtime.
// Tests use synthetic UI objects, not a running game or player saves.
internal static class Program
{
    private static int checks;
    private static string capturedScreen;

    private static int Main()
    {
        try
        {
            CheckState();
            CheckNativeRouting();
            Console.WriteLine($"PASS: {checks} scroll-state and native menu-routing checks (Unity layout still needs in-game verification).");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void CheckState()
    {
        var state = new DebugMenuScrollState();
        state.Begin("main", 0f);
        Equal(1f, state.Position, "New main menu starts at top, not previous panel bottom.");
        Check(state.RestorePending, "Wait for layout before capturing.");
        state.Restored();
        state.Suspend(0.35f);
        state.Suspend(0f); // Close prefix followed by OnDisable.
        Equal(0.35f, state.Position, "Duplicate close does not overwrite the capture.");
        state.Begin("teleport", 0f);
        Equal(1f, state.Position, "New teleport menu starts at top.");
        state.Restored();
        state.Suspend(0.1f);
        state.Begin("main", 0.1f);
        Equal(0.35f, state.Position, "Main does not inherit teleport position.");
        state.Restored();
        state.Begin("spectate", 0.5f); // Draw without Close also saves outgoing.
        Equal(1f, state.Position, "Spectate is independent of main and teleport.");
        state.Restored();
        state.Suspend(0.2f);
        state.Begin("teleport", 0.2f);
        Equal(0.1f, state.Position, "Teleport remembers its own location.");
        state.Restored();
        state.Begin("spectate", 0.15f);
        Equal(0.2f, state.Position, "Spectate remembers its own location.");
        state.Suspend(0f); // Close before layout restoration.
        state.Begin("main", 0f);
        Equal(0.5f, state.Position, "Rapid switching preserves main's last real location.");
        state.Restored();
        state.Suspend(0.48f);
        state.Begin("main", 0f); // Toggle redraws the same screen.
        Equal(0.48f, state.Position, "Cheat-toggle rebuild preserves main position.");
        state.Restored();
        state.Begin(null, 0.6f);
        Check(state.Screen == null && !state.RestorePending, "Unrelated Draw detaches tracking and cancels restore.");
        state.Save(0f);
        state.Suspend(0f);
        Check(!state.RestorePending, "Unrelated menu must not get a pending restore on close.");
        state.Begin("main", 0f);
        Equal(0.6f, state.Position, "Unrelated menus cannot overwrite main.");
        state.Begin(null, 0f);
        state.Begin("main", 0f);
        Equal(0.6f, state.Position, "Unrelated Draw during pending restoration preserves memory.");
        state.Restored();
        state.Save(float.NaN);
        state.Save(float.PositiveInfinity);
        state.Save(float.NegativeInfinity);
        Equal(0.6f, state.Position, "Invalid positions cannot poison memory.");
        state.Save(-0.3f);
        Equal(0f, state.Position, "Bottom overscroll is clamped.");
        state.Save(1.5f);
        Equal(1f, state.Position, "Top overscroll is clamped.");
        state.Suspend(0.4f);
        state.RequestRestore(); // Close/Open without Draw.
        Equal(0.4f, state.Position, "Reopening unchanged rows restores location.");
        state.Restored();
        Check(!state.RestorePending, "Restoration completes once.");

        foreach (string screen in new[] { "custom-item:base:Food", "custom-item:base:Weapon",
            "custom-item:appearance:Food", "custom-item:quality", "custom-item:enchantment" })
        {
            state.Begin(screen, 0.4f);
            Equal(1f, state.Position, "Wizard screen starts independently: " + screen);
            state.Restored();
        }
        var replacement = new DebugMenuScrollState();
        replacement.Begin("main", 0f);
        Equal(1f, replacement.Position, "Replacement UI gets clean session state.");
    }

    private static void CheckNativeRouting()
    {
        Assembly plugin = typeof(Plugin).Assembly;
        Type routing = plugin.GetType("SaltExtraDebug.NativeDebugMenuScreenPatch", true);
        Type screens = plugin.GetType("SaltExtraDebug.DebugMenuScreens", true);
        MethodInfo draw = AccessTools.Method(typeof(GenericListUI), "Draw");
        MethodInfo rememberedDraw = AccessTools.Method(screens, "Draw");
        var methods = ((IEnumerable<MethodBase>)AccessTools.Method(routing, "TargetMethods").Invoke(null, null)).ToArray();
        Check(methods.Contains(AccessTools.Method(typeof(DebugUI), "ShowDebugOptions")), "Native main menu is routed.");
        Type spectateType = AccessTools.Inner(typeof(DebugUI), "DebugAction_SpectateNPC");
        MethodInfo spectate = AccessTools.DeclaredMethod(spectateType, "OnCalled");
        Check(methods.Contains(spectate), "Native Spectate NPC picker is routed.");
        Check(methods.Any(method => method.DeclaringType.Name.Contains("DisplayClass")), "Nested callback screens are included.");
        Check(methods.All(method => method.DeclaringType.FullName.StartsWith("DebugUI")), "No non-debug native screens patched.");

        var keys = new HashSet<string>();
        foreach (MethodBase method in methods)
        {
            var original = PatchProcessor.GetOriginalInstructions(method).ToArray();
            var rewritten = ((IEnumerable<CodeInstruction>)AccessTools.Method(routing, "Transpiler")
                .Invoke(null, new object[] { original, method })).ToArray();
            int count = original.Count(instruction => instruction.Calls(draw));
            Check(count > 0, "Only direct Draw callers patched.");
            Check(rewritten.Count(instruction => instruction.Calls(rememberedDraw)) == count,
                "Each native draw receives a screen key: " + method);
            Check(!rewritten.Any(instruction => instruction.Calls(draw)), "No unkeyed draws remain.");
            for (int i = 1; i < rewritten.Length; i++)
            {
                if (!rewritten[i].Calls(rememberedDraw)) continue;
                Check(rewritten[i - 1].opcode == OpCodes.Ldstr, "Key is passed as wrapper's third argument.");
                Check(keys.Add((string)rewritten[i - 1].operand), "Screen keys are unique across native menus.");
            }
        }

        var generator = new DynamicMethod("LabelFixture", typeof(void), Type.EmptyTypes).GetILGenerator();
        Label label = generator.DefineLabel();
        var call = new CodeInstruction(OpCodes.Callvirt, draw);
        call.labels.Add(label);
        call.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        call.blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
        var converted = ((IEnumerable<CodeInstruction>)AccessTools.Method(routing, "Transpiler")
            .Invoke(null, new object[] { new[] { call, new CodeInstruction(OpCodes.Callvirt, draw) }, spectate })).ToArray();
        Check(converted[0].labels.Contains(label), "Branches now target key load, not wrapper call.");
        Check(converted[0].blocks.Single().blockType == ExceptionBlockType.BeginExceptionBlock, "Try block starts before key load.");
        Check(converted[1].blocks.Single().blockType == ExceptionBlockType.EndExceptionBlock, "Try block ends after wrapper call.");
        Check((string)converted[0].operand != (string)converted[2].operand, "Multiple draws in one method remain separate screens.");

        var harmony = new Harmony("saltextradebug.tests.scroll");
        // Validate the real Harmony patches can all be installed on Unity Mono.
        foreach (string name in new[] { "DebugMenuScrollDrawPatch", "DebugMenuScrollClosePatch", "NativeDebugMenuScreenPatch" })
        {
            var patched = harmony.CreateClassProcessor(plugin.GetType("SaltExtraDebug." + name, true)).Patch();
            Check(patched.Count > 0, "Harmony installed " + name);
        }
        harmony.Patch(rememberedDraw, prefix: new HarmonyMethod(typeof(Program), nameof(CaptureDraw)));
        harmony.Patch(AccessTools.Method(typeof(GenericListUI), "Open"), prefix: new HarmonyMethod(typeof(Program), nameof(SkipOpen)));

        GenericListUI.Instance = (GenericListUI)FormatterServices.GetUninitializedObject(typeof(GenericListUI));
        var debug = (DebugUI)FormatterServices.GetUninitializedObject(typeof(DebugUI));
        AccessTools.Method(typeof(DebugUI), "ShowDebugOptions").Invoke(debug, null);
        string mainKey = capturedScreen;
        Check(!string.IsNullOrEmpty(mainKey), "Real main menu reaches keyed wrapper.");
        NeuralNPC.neuralNPCs = new Dictionary<NPCName, NeuralNPC>();
        spectate.Invoke(Activator.CreateInstance(spectateType, true), null);
        string spectateKey = capturedScreen;
        Check(spectateKey != mainKey && keys.Contains(spectateKey), "Real Spectate NPC receives its own screen key.");
        AccessTools.Method(typeof(DebugUI), "ShowDebugOptions").Invoke(debug, null);
        Check(capturedScreen == mainKey, "Reopened native main uses original key.");
        spectate.Invoke(Activator.CreateInstance(spectateType, true), null);
        Check(capturedScreen == spectateKey, "New action instance does not change Spectate key.");
        AccessTools.Method(typeof(DebugUI), "Inspect").Invoke(debug, new object[] { new GameObject[0] });
        Check(capturedScreen != mainKey && capturedScreen != spectateKey, "Inspection menu is independently routed.");
        Console.WriteLine($"Native routing: {methods.Length} methods / {keys.Count} debug-list screens.");
    }

    private static bool CaptureDraw(string screen, ref List<GameObject> __result)
    {
        capturedScreen = screen;
        __result = new List<GameObject>();
        return false;
    }
    private static bool SkipOpen() => false;
    private static void Equal(float expected, float actual, string description) => Check(Math.Abs(expected - actual) < 0.0001f, description);
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        checks++;
    }
}
