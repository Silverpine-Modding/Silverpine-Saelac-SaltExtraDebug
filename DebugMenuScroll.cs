using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SaltExtraDebug;

internal static class DebugMenuScreens
{
    [ThreadStatic] internal static string DrawingScreen;

    internal static List<GameObject> Draw(GenericListUI ui, IEnumerable<ListUIItem> items, string screen)
    {
        string previous = DrawingScreen;
        DrawingScreen = screen;
        try { return ui.Draw(items); }
        finally { DrawingScreen = previous; }
    }
}

[HarmonyPatch(typeof(GenericListUI), nameof(GenericListUI.Draw))]
internal static class DebugMenuDrawPatch
{
    private static void Prefix(GenericListUI __instance, GameObject ___instanceRoot)
    {
        DebugMenuPages pages = __instance.GetComponent<DebugMenuPages>();
        if (pages == null && DebugMenuScreens.DrawingScreen != null)
            pages = __instance.gameObject.AddComponent<DebugMenuPages>();

        // Restore the native hierarchy/layout before any non-debug Draw.
        if (pages != null)
            pages.Begin(DebugMenuScreens.DrawingScreen, ___instanceRoot);

        DebugMenuInput input = __instance.GetComponent<DebugMenuInput>();
        if (input == null && DebugMenuScreens.DrawingScreen != null)
            input = __instance.gameObject.AddComponent<DebugMenuInput>();
        if (input != null)
            input.SetScreen(DebugMenuScreens.DrawingScreen != null);
    }
}

[HarmonyPatch]
internal static class NativeDebugMenuScreenPatch
{
    private static readonly MethodInfo NativeDraw = AccessTools.Method(typeof(GenericListUI), nameof(GenericListUI.Draw));
    private static readonly MethodInfo RememberedDraw = AccessTools.Method(typeof(DebugMenuScreens), nameof(DebugMenuScreens.Draw));

    private static IEnumerable<MethodBase> TargetMethods()
    {
        // Include compiler-generated callbacks: Inspect and Start NPC Routine
        // draw further submenus from their row callbacks, not just OnCalled.
        return MethodsIn(typeof(DebugUI)).Where(method => method.GetMethodBody() != null
            && PatchProcessor.GetOriginalInstructions(method).Any(instruction => instruction.Calls(NativeDraw)));
    }

    private static IEnumerable<MethodInfo> MethodsIn(Type type)
    {
        foreach (MethodInfo method in type.GetMethods(BindingFlags.DeclaredOnly
            | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            yield return method;
        foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            foreach (MethodInfo method in MethodsIn(nested))
                yield return method;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        int screenIndex = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (!instruction.Calls(NativeDraw))
            {
                yield return instruction;
                continue;
            }

            // Identity belongs to the draw site, not the reused panel, row text,
            // selected NPC, or a stack trace (which JIT inlining can change).
            string key = "native:" + __originalMethod.DeclaringType.FullName + "."
                + __originalMethod.Name + ":" + __originalMethod.MetadataToken + ":" + screenIndex++;
            var loadKey = new CodeInstruction(OpCodes.Ldstr, key);
            loadKey.labels.AddRange(instruction.labels);
            var draw = new CodeInstruction(OpCodes.Call, RememberedDraw);
            foreach (ExceptionBlock block in instruction.blocks)
            {
                if (block.blockType == ExceptionBlockType.EndExceptionBlock)
                    draw.blocks.Add(block);
                else
                    loadKey.blocks.Add(block);
            }
            yield return loadKey;
            yield return draw;
        }
    }
}
