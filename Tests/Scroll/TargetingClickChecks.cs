using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using SaltExtraDebug;
using UnityEngine;

internal static class TargetingClickChecks
{
    private static int checks;
    private static readonly List<string> order = new List<string>();
    private static bool markerAlive, markerAttached, cleanupQueued, targetDestroyed;
    private static Vector2Int livePosition, facedPosition;

    internal static int Run()
    {
        Type fix = typeof(Plugin).Assembly.GetType("SaltExtraDebug.TargetingClickFix", true);
        MethodInfo start = AccessTools.Method(typeof(Targetable), "StartTargetingMode");
        MethodInfo assign = AccessTools.Method(fix, "AssignCallback");
        MethodInfo facing = (MethodInfo)AccessTools.Field(fix, "FacingCallback").GetValue(null);
        MethodInfo capture = AccessTools.Method(fix, "CaptureAndRetireMarker");
        MethodInfo facePosition = AccessTools.Method(fix, "FaceCapturedPosition");
        FieldInfo callbackField = AccessTools.Field(typeof(Clickable), "callback");
        var original = PatchProcessor.GetOriginalInstructions(start).ToArray();
        var rewritten = ((IEnumerable<CodeInstruction>)AccessTools.Method(fix, "Transpiler")
            .Invoke(null, new object[] { original })).ToArray();
        Check(original.Count(i => i.opcode == OpCodes.Stfld && Equals(i.operand, callbackField)) == 3,
            "Native callback is assembled in the expected three steps.");
        Check(rewritten.Count(i => i.Calls(assign)) == 3, "All callback assignments use the safe setter.");
        Check(original.Length == rewritten.Length, "No other native instructions inserted/removed.");
        for (int i = 0; i < original.Length; i++)
        {
            if (rewritten[i].Calls(assign)) continue;
            if (original[i].opcode != rewritten[i].opcode || !Equals(original[i].operand, rewritten[i].operand))
                throw new Exception("Unrelated targeting instruction changed.");
        }
        Check(true, "Filtering, auto-selection, native delegate wiring and event subscription are unchanged.");
        var captureIl = PatchProcessor.GetOriginalInstructions(capture).ToArray();
        int detach = Array.FindIndex(captureIl, i => i.Calls(AccessTools.Method(typeof(Transform), "SetParent", new[] { typeof(Transform), typeof(bool) })));
        int retire = Array.FindIndex(captureIl, i => i.Calls(AccessTools.Method(typeof(UnityEngine.Object), "Destroy", new[] { typeof(UnityEngine.Object) })));
        Check(detach >= 0 && retire > detach, "Marker is detached before deferred destruction.");
        Check(!captureIl.Any(i => i.operand is MethodInfo method && method.Name == "DestroyImmediate"),
            "Clicked marker never uses immediate destruction.");
        Check(!PatchProcessor.GetOriginalInstructions(facePosition).Any(i => i.Calls(AccessTools.PropertyGetter(typeof(Component), "transform"))),
            "Facing after the action never reads a marker/target transform.");

        var harmony = new Harmony("saltextradebug.tests.targeting");
        Check(harmony.CreateClassProcessor(fix).Patch().Count == 1, "Actual targeting patch installs on the game's Mono runtime.");
        harmony.Patch(capture, prefix: new HarmonyMethod(typeof(TargetingClickChecks), nameof(CaptureMarkerModel)));
        harmony.Patch(facePosition, prefix: new HarmonyMethod(typeof(TargetingClickChecks), nameof(FaceModel)));

        // Native auto-selection with no targets never creates a click callback.
        Targetable.allTargetables.Clear();
        Player.Instance = (Player)FormatterServices.GetUninitializedObject(typeof(Player));
        int emptyCalls = 0;
        Targetable.StartTargetingMode(_ => true, target => { if (ReferenceEquals(target, null)) emptyCalls++; }, true);
        Check(emptyCalls == 1, "No-target callback still runs exactly once.");

        object nativeClosure = FormatterServices.GetUninitializedObject(facing.DeclaringType);
        Action unsafeNativeFace = (Action)facing.CreateDelegate(typeof(Action), nativeClosure);
        var marker = (Clickable)FormatterServices.GetUninitializedObject(typeof(Clickable));
        MethodInfo click = AccessTools.Method(typeof(Clickable), "OnMouseUpAsButton");
        Action action = () =>
        {
            order.Add("action");
            targetDestroyed = true; // Model DestroyImmediate(target).
            if (markerAttached) markerAlive = false;
            livePosition = new Vector2Int(999, 999);
        };
        Action stop = () => order.Add("stop");
        assign.Invoke(null, new object[] { marker, action });
        Check(ReferenceEquals(marker.callback, action), "Ordinary callback assignment isn't wrapped early.");
        Action actionAndStop = action + stop;
        assign.Invoke(null, new object[] { marker, actionAndStop });
        Check(ReferenceEquals(marker.callback, actionAndStop), "Native cleanup delegate isn't reordered.");
        assign.Invoke(null, new object[] { marker, actionAndStop + unsafeNativeFace });
        Check(marker.callback.GetInvocationList().Length == 1, "Final native facing callback replaced by one safe wrapper.");

        ResetModel();
        InputProtectionChecks.SetBlocked(true);
        click.Invoke(marker, null);
        Check(order.Count == 0 && !cleanupQueued, "An open debug menu still blocks the entire targeting action.");
        InputProtectionChecks.SetBlocked(false);
        click.Invoke(marker, null);
        Check(order.SequenceEqual(new[] { "capture", "action", "stop", "face" }), "Capture, selected action, cleanup and facing execute in order.");
        Check(targetDestroyed, "Instant deconstruction still takes place.");
        Check(markerAlive && !markerAttached, "Clicked marker survives target destruction for Unity's following mouse event.");
        Check(cleanupQueued, "Marker cleanup is queued even if removed from allTargetables.");
        Check(facedPosition.x == 12 && facedPosition.y == -7, "Facing uses coordinates captured before the target changed/died.");
        Check(marker.callback == null, "No duplicate callback can fire before frame-end cleanup.");
        click.Invoke(marker, null);
        Check(order.Count == 4, "Duplicate native mouse callback is harmless.");
        markerAlive = false; // Model frame-end Destroy.

        assign.Invoke(null, new object[] { marker, actionAndStop + unsafeNativeFace });
        order.Clear();
        click.Invoke(marker, null);
        Check(order.Count == 0, "Already-destroyed marker does not execute a stale target action.");

        ResetModel();
        Action throwingAction = () => { order.Add("throw"); throw new InvalidOperationException("test action failure"); };
        assign.Invoke(null, new object[] { marker, throwingAction + stop + unsafeNativeFace });
        try { marker.callback(); throw new Exception("Selected-action exception was swallowed."); }
        catch (InvalidOperationException error) { Check(error.Message == "test action failure", "Unrelated selected-action errors are not hidden."); }
        Check(order.SequenceEqual(new[] { "capture", "throw" }), "Exceptions preserve native callback short-circuiting.");
        Check(cleanupQueued && marker.callback == null, "Marker is retired even if the selected action fails.");
        assign.Invoke(null, new object[] { marker, null });
        Check(marker.callback == null, "Null callback assignment preserved.");

        harmony.Unpatch(facePosition, HarmonyPatchType.Prefix, harmony.Id);
        facePosition.Invoke(null, new object[] { null, new Vector2Int(12, -7) });
        Check(true, "Missing player safely skips post-action facing.");
        Console.WriteLine($"Targeting lifetime: {checks} checks (marker destruction modeled; callback routing uses real Mono/Harmony).");
        return checks;
    }

    private static void ResetModel()
    {
        order.Clear();
        markerAlive = markerAttached = true;
        cleanupQueued = targetDestroyed = false;
        livePosition = new Vector2Int(12, -7);
        facedPosition = default;
    }

    private static bool CaptureMarkerModel(Clickable marker, ref Player player, ref Vector2Int position, ref bool __result)
    {
        player = null;
        position = default;
        __result = markerAlive;
        if (markerAlive)
        {
            order.Add("capture");
            position = livePosition;
            markerAttached = false;
            cleanupQueued = true;
            marker.callback = null;
        }
        return false;
    }
    private static bool FaceModel(Vector2Int position) { order.Add("face"); facedPosition = position; return false; }
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        checks++;
    }
}
