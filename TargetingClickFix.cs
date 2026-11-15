using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SaltExtraDebug;

[HarmonyPatch(typeof(Targetable), nameof(Targetable.StartTargetingMode))]
internal static class TargetingClickFix
{
    private static readonly FieldInfo CallbackField = AccessTools.Field(typeof(Clickable), nameof(Clickable.callback));
    private static readonly MethodInfo FacingCallback = FindFacingCallback();

    private static MethodInfo FindFacingCallback()
    {
        MethodInfo turn = AccessTools.Method(typeof(EntityMover), nameof(EntityMover.TurnTowards), new[] { typeof(Vector2Int) });
        // Find the native trailing facing delegate by its operation, rather than
        // depending on compiler-generated display-class/method names.
        return PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(Targetable), nameof(Targetable.StartTargetingMode)))
            .Where(instruction => instruction.opcode == OpCodes.Ldftn)
            .Select(instruction => (MethodInfo)instruction.operand)
            .Single(method => PatchProcessor.GetOriginalInstructions(method).Any(instruction => instruction.Calls(turn)));
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo assign = AccessTools.Method(typeof(TargetingClickFix), nameof(AssignCallback));
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Stfld && Equals(instruction.operand, CallbackField))
            {
                // Same stack signature: Clickable, Action. Only callback wiring
                // changes; target filtering, auto-selection and cleanup remain native.
                var replacement = new CodeInstruction(OpCodes.Call, assign);
                replacement.labels.AddRange(instruction.labels);
                replacement.blocks.AddRange(instruction.blocks);
                yield return replacement;
            }
            else
                yield return instruction;
        }
    }

    private static void AssignCallback(Clickable marker, Action callback)
    {
        Delegate[] handlers = callback?.GetInvocationList();
        if (handlers == null || handlers.Length == 0 || handlers[handlers.Length - 1].Method != FacingCallback)
        {
            marker.callback = callback;
            return;
        }

        // Keep the selected action and StopTargetingMode in their original
        // order. Replace only the final delegate that reads a destroyed marker.
        Action beforeFacing = (Action)Delegate.Remove(callback, handlers[handlers.Length - 1]);
        marker.callback = () =>
        {
            if (!CaptureAndRetireMarker(marker, out Player player, out Vector2Int position))
                return;
            beforeFacing?.Invoke();
            FaceCapturedPosition(player, position);
        };
    }

    private static bool CaptureAndRetireMarker(Clickable marker, out Player player, out Vector2Int position)
    {
        player = Player.Instance;
        position = default;
        if (marker == null)
            return false;

        Transform markerTransform = marker.transform;
        position = markerTransform.GetVector2IntPosition();
        marker.callback = null; // Ignore duplicate events during deferred cleanup.
        // Deconstruct destroys the target immediately, including its children.
        // Keep the clicked marker alive until Unity finishes SendMouseEvents
        // (OnMouseUpAsButton is followed by OnMouseUp on the same GameObject).
        markerTransform.SetParent(null, true);
        UnityEngine.Object.Destroy(marker.gameObject);
        return true;
    }

    private static void FaceCapturedPosition(Player player, Vector2Int position)
    {
        // A selected action may load a scene or replace the player as well as
        // destroying the target. Never turn a replacement player from this click.
        if (player != null && player == Player.Instance && player.entityMover != null)
            player.entityMover.TurnTowards(position);
    }
}
