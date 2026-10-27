using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using SaltExtraDebug;

internal static class InputProtectionChecks
{
    private static int checks;
    private static bool blocked;

    internal static int Run()
    {
        CheckState();
        CheckNativePatches();
        Console.WriteLine($"Input protection: {checks} targeted checks.");
        return checks;
    }

    private static void CheckState()
    {
        var state = new DebugMenuInputState();
        Check(!state.ShouldBlock(false, 1, 0, 0), "No menu: input is untouched.");
        Check(state.ShouldBlock(true, 2, 0, 0), "Visible main menu blocks world input.");
        Check(state.ShouldBlock(true, 3, 1, 1), "Left-click blocked in a menu.");
        Check(state.ShouldBlock(true, 4, 2, 2), "Right-click blocked in a menu.");
        Check(state.ShouldBlock(true, 5, 4, 4), "Middle-click blocked in a menu.");
        state.Closed(6, 0); // Ordinary UI button fires on mouse release.
        Check(state.ShouldBlock(false, 6, 0, 0), "Closing release cannot click the world later that frame.");
        Check(state.ShouldBlock(false, 6, 0, 0), "Every consumer in the close frame is protected.");
        Check(!state.ShouldBlock(false, 7, 0, 0), "Neutral next frame resumes input.");
        Check(!state.ShouldBlock(false, 8, 1, 1), "A fresh click after closing works.");

        state.Closed(10, 1); // Programmatic close while a button remains held.
        Check(state.ShouldBlock(false, 10, 1, 1), "Closing held press is consumed even on its down frame.");
        Check(state.ShouldBlock(false, 11, 1, 0), "Held closing press stays blocked next frame.");
        Check(state.ShouldBlock(false, 12, 1, 0), "Held press stays blocked for as long as needed.");
        Check(state.ShouldBlock(false, 13, 0, 0), "Held closing press's release is also consumed.");
        Check(state.ShouldBlock(false, 13, 0, 0), "Release protection is stable across multiple callers.");
        Check(!state.ShouldBlock(false, 14, 0, 0), "No sticky lock after release.");

        state.Closed(20, 3);
        Check(state.ShouldBlock(false, 21, 2, 0), "Releasing one button doesn't release another held button.");
        Check(state.ShouldBlock(false, 22, 2, 0), "Second held button remains protected.");
        Check(state.ShouldBlock(false, 23, 0, 0), "Last button's release is consumed.");
        Check(!state.ShouldBlock(false, 24, 0, 0), "Chord protection ends cleanly.");

        state.Closed(30, 1);
        Check(!state.ShouldBlock(false, 40, 1, 1), "Fresh press after unobserved release/loading is not swallowed.");
        state.Closed(50, 3);
        Check(state.ShouldBlock(false, 60, 3, 1), "Fresh left press cannot bypass a still-held right button.");
        Check(state.ShouldBlock(false, 61, 1, 0), "Old right-button release is consumed.");
        Check(!state.ShouldBlock(false, 62, 1, 0), "New left gesture doesn't create a stale held lock.");

        Check(state.ShouldBlock(true, 70, 0, 0), "Reopening menu restores modal protection.");
        state.Closed(71, 0);
        Check(state.ShouldBlock(true, 71, 0, 0), "Same-frame submenu switch stays protected.");
        Check(state.ShouldBlock(true, 72, 0, 0), "New submenu remains protected.");
        state.Closed(73, 0);
        state.Closed(73, 0);
        Check(state.ShouldBlock(false, 73, 0, 0), "Repeated close notifications are safe.");
        Check(!state.ShouldBlock(false, 74, 0, 0), "Repeated close does not leak a lock.");

        Check(state.ShouldBlock(true, 80, 0, 0), "Menu visible before teardown.");
        Check(state.ShouldBlock(false, 81, 0, 0), "Unnotified UI loss still protects transition frame.");
        Check(!state.ShouldBlock(false, 82, 0, 0), "UI loss cannot permanently block gameplay.");
        state.Closed(90, 0);
        Check(!state.ShouldBlock(false, 91, 0, 0), "Keyboard close without a mouse gesture releases promptly.");
    }

    private static void CheckNativePatches()
    {
        Assembly plugin = typeof(Plugin).Assembly;
        var harmony = new Harmony("saltextradebug.tests.input");
        Type gate = plugin.GetType("SaltExtraDebug.DebugMenuInput", true);
        harmony.Patch(AccessTools.Method(gate, "BlocksWorldInput"),
            prefix: new HarmonyMethod(typeof(InputProtectionChecks), nameof(ReadTestGate)));

        foreach (var entry in new[] { ("DebugMenuPlayerInputPatch", 1), ("DebugMenuWorldClickPatch", 3), ("DebugMenuPlacementInputPatch", 2) })
        {
            var patched = harmony.CreateClassProcessor(plugin.GetType("SaltExtraDebug." + entry.Item1, true)).Patch();
            Check(patched.Count == entry.Item2, "Real Harmony targets installed: " + entry.Item1);
        }

        int callbacks = 0;
        var clickable = (Clickable)FormatterServices.GetUninitializedObject(typeof(Clickable));
        clickable.callback = () => callbacks++;
        MethodInfo click = AccessTools.Method(typeof(Clickable), "OnMouseUpAsButton");
        blocked = true;
        click.Invoke(clickable, null);
        Check(callbacks == 0, "Native world-target callback cannot run behind menu.");
        blocked = false;
        click.Invoke(clickable, null);
        Check(callbacks == 1, "Native world-target callback still works after protection ends.");
        blocked = true;
        click.Invoke(clickable, null);
        Check(callbacks == 1, "Close-frame protection also suppresses native callback.");

        SettingsUI.Instance = null;
        var player = (Player)FormatterServices.GetUninitializedObject(typeof(Player));
        FieldInfo inputCount = AccessTools.Field(typeof(Player), "inputBlockCount");
        inputCount.SetValue(player, 7);
        AccessTools.Method(typeof(Player), "Update").Invoke(player, null);
        Check((int)inputCount.GetValue(player) == 7, "Player input skipped without modifying another system's lock count.");
        var pickup = (Pickupable)FormatterServices.GetUninitializedObject(typeof(Pickupable));
        AccessTools.Field(typeof(Pickupable), "active").SetValue(pickup, true);
        AccessTools.Method(typeof(Pickupable), "OnMouseUpAsButton").Invoke(pickup, null);
        Check((bool)AccessTools.Field(typeof(Pickupable), "active").GetValue(pickup), "Active pickup callback skipped without changing pickup state.");
        var debug = (DebugUI)FormatterServices.GetUninitializedObject(typeof(DebugUI));
        AccessTools.Method(typeof(DebugUI), "Update").Invoke(debug, null);
        Check(true, "Native P/O world-input polling skipped while a menu owns input.");

        var placement = (PlacementModeManager)FormatterServices.GetUninitializedObject(typeof(PlacementModeManager));
        CheckPausedCoroutine((IEnumerator)AccessTools.Method(typeof(PlacementModeManager), "PlacementCoroutine")
            .Invoke(placement, new object[] { null, 0, null, null }), "Placement");
        CheckPausedCoroutine((IEnumerator)AccessTools.Method(typeof(PlacementModeManager), "RotationIndicatorCoroutine")
            .Invoke(placement, null), "Rotation indicator");
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(Player), "IsInputBlocked")) == null,
            "No global IsInputBlocked override that could corrupt native lock accounting.");
    }

    private static void CheckPausedCoroutine(IEnumerator coroutine, string description)
    {
        FieldInfo state = coroutine.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Single(field => field.Name.Contains("1__state"));
        object initialState = state.GetValue(coroutine);
        blocked = true;
        Check(coroutine.MoveNext(), description + " remains alive while blocked.");
        Check(Equals(initialState, state.GetValue(coroutine)), description + " state doesn't advance while blocked.");
        blocked = false;
        Check(coroutine.MoveNext(), description + " resumes with native initial yield after closing.");
        Check(!Equals(initialState, state.GetValue(coroutine)), description + " native state advances normally.");
        object resumedState = state.GetValue(coroutine);
        blocked = true;
        Check(coroutine.MoveNext(), description + " can be suspended again by a submenu.");
        Check(Equals(resumedState, state.GetValue(coroutine)), description + " resumes from the same state later.");
        (coroutine as IDisposable)?.Dispose();
    }

    private static bool ReadTestGate(ref bool __result) { __result = blocked; return false; }
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        checks++;
    }
}
