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

// Lives on the actual list UI, not BepInEx's temporary startup host.
[DefaultExecutionOrder(10000)]
internal sealed class DebugMenuScroll : MonoBehaviour
{
    private readonly DebugMenuScrollState _state = new();
    private ScrollRect _scroll;
    private int _drawFrame;

    internal void Begin(string screen, GameObject contentRoot)
    {
        if (_scroll == null && contentRoot != null)
            _scroll = contentRoot.GetComponentInParent<ScrollRect>(true);

        _state.Begin(screen, ReadPosition());
        if (screen != null && _scroll != null)
            _scroll.StopMovement();
        ScheduleRestore();
    }

    internal void Suspend() => _state.Suspend(ReadPosition());

    internal void ScheduleRestore()
    {
        _state.RequestRestore();
        _drawFrame = Time.frameCount;
    }

    private float ReadPosition()
    {
        if (_scroll == null || _scroll.content == null)
            return 1f;
        RectTransform viewport = _scroll.viewport != null ? _scroll.viewport : (RectTransform)_scroll.transform;
        return _scroll.content.rect.height <= viewport.rect.height ? 1f : _scroll.verticalNormalizedPosition;
    }

    private void OnEnable() => ScheduleRestore();

    // Covers X, action selections, significant player actions, and direct hiding.
    private void OnDisable() => Suspend();

    private void LateUpdate()
    {
        if (!_state.RestorePending || _scroll == null || !_scroll.isActiveAndEnabled
            || Time.frameCount <= _drawFrame)
            return;

        // Draw destroys old rows at the end of the frame. The next frame also
        // includes appended cheat buttons and other postfix-added rows.
        _scroll.StopMovement();
        Canvas.ForceUpdateCanvases();
        if (_scroll.content != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_scroll.content);
        Canvas.ForceUpdateCanvases();
        _scroll.verticalNormalizedPosition = _state.Position;
        _scroll.StopMovement();
        _state.Restored();
    }
}

[HarmonyPatch(typeof(GenericListUI), nameof(GenericListUI.Draw))]
internal static class DebugMenuScrollDrawPatch
{
    private static void Prefix(GenericListUI __instance, GameObject ___instanceRoot)
    {
        DebugMenuScroll memory = __instance.GetComponent<DebugMenuScroll>();
        if (memory == null && DebugMenuScreens.DrawingScreen != null)
            memory = __instance.gameObject.AddComponent<DebugMenuScroll>();

        // An unrelated menu using GenericListUI detaches tracking. Its scrolling
        // must neither overwrite a debug screen nor receive a delayed restore.
        if (memory != null)
            memory.Begin(DebugMenuScreens.DrawingScreen, ___instanceRoot);
    }
}

[HarmonyPatch(typeof(GenericListUI), nameof(GenericListUI.Close))]
internal static class DebugMenuScrollClosePatch
{
    private static void Prefix(GenericListUI __instance)
    {
        DebugMenuScroll memory = __instance.GetComponent<DebugMenuScroll>();
        if (memory != null)
            memory.Suspend();
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
