using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SaltExtraDebug;
using UnityEngine;

internal static class RoutineTests
{
    private static int assertions;

    private static void Main()
    {
        RestoresOriginalAfterExactlyThreeTurns();
        PreservesExistingOverride();
        RepeatedTeleportRestartsTimer();
        TimerSurvivesSerialization();
        StaleAndBuriedWaitsDoNotRestoreWrongRoutine();
        MissingPriorRoutineFallsBackToSchedule();
        PatchesOnlyAffectWaitingNpc();
        Console.WriteLine($"PASS: {assertions} teleport-routine assertions.");
        OverlayLifecycleTests.Run();
        PropertyGeometryTests.Run();
    }

    private static void RestoresOriginalAfterExactlyThreeTurns()
    {
        var original = new NPCRoutine { id = "working" };
        var executor = new NPCRoutineExecutor { currentRoutine = original };
        NpcTeleportRoutine.StartWaiting(executor, new Vector2Int(10, 20));
        Check(NpcTeleportRoutine.IsWaiting(executor), "Wait becomes active.");
        Check(executor.preOverrideRoutines.First.Value == original, "Original saved by identity.");
        Check(executor.currentRoutine.targetPosition.x == 10, "Landing position is the wait target.");
        Tick(executor);
        Check(NpcTeleportRoutine.IsWaiting(executor), "Still waiting after turn 1.");
        Tick(executor);
        Check(NpcTeleportRoutine.IsWaiting(executor), "Still waiting after turn 2.");
        Tick(executor);
        Check(executor.currentRoutine == original, "Exact original restored after turn 3.");
        Check(executor.preOverrideRoutines.Count == 0, "Wait leaves no stack entry.");
        Check(executor.ScheduleChecks == 0, "Original is not replaced by current schedule.");
    }

    private static void PreservesExistingOverride()
    {
        var baseline = new NPCRoutine { id = "daily_schedule" };
        var existingOverride = new NPCRoutine { id = "following" };
        var probe = new LifecycleProbe();
        existingOverride.routineArguments.Add(probe);
        var executor = new NPCRoutineExecutor { currentRoutine = existingOverride };
        executor.preOverrideRoutines.AddFirst(baseline);
        NpcTeleportRoutine.StartWaiting(executor, default);
        Check(probe.UnsetCount == 1, "Previous routine receives OnUnset.");
        Check(probe.OverriddenCount == 0, "No destructive OnAfterOverridden callback.");
        Check(executor.preOverrideRoutines.Count == 2, "Nested stack retained.");
        Tick(executor); Tick(executor); Tick(executor);
        Check(executor.currentRoutine == existingOverride, "Follower override resumed.");
        Check(probe.SetCount == 1, "Restored routine receives OnSet.");
        Check(executor.preOverrideRoutines.Single() == baseline, "Older base routine preserved.");
    }

    private static void RepeatedTeleportRestartsTimer()
    {
        var original = new NPCRoutine { id = "original" };
        var executor = new NPCRoutineExecutor { currentRoutine = original };
        NpcTeleportRoutine.StartWaiting(executor, default);
        NPCRoutine oldWait = executor.currentRoutine;
        Tick(executor); Tick(executor);
        NpcTeleportRoutine.StartWaiting(executor, new Vector2Int(30, 40));
        Check(executor.currentRoutine != oldWait, "Repeat uses a fresh timer.");
        Check(executor.preOverrideRoutines.Single() == original, "Repeat does not stack waits.");
        Check(executor.currentRoutine.targetPosition.x == 30, "Repeat updates wait location.");
        Check(NpcTeleportRoutine.TryFinishWait(executor, oldWait), "Stale completion is intercepted.");
        Check(NpcTeleportRoutine.IsWaiting(executor), "Stale completion cannot end a new wait.");
        Tick(executor); Tick(executor);
        Check(NpcTeleportRoutine.IsWaiting(executor), "New wait lasts the full duration.");
        Tick(executor);
        Check(executor.currentRoutine == original, "Repeat ultimately restores original.");
    }

    private static void TimerSurvivesSerialization()
    {
        var original = new NPCRoutine();
        var executor = new NPCRoutineExecutor { currentRoutine = original };
        NpcTeleportRoutine.StartWaiting(executor, default);
        Tick(executor);
        RoutineArgument timer = executor.currentRoutine.routineArguments.Single();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
        timer.Serialize(writer);
        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        var loadedTimer = new RoutineArgument_EndOverrideIf_AfterTurns();
        loadedTimer.Deserialize(reader);
        loadedTimer.routine = executor.currentRoutine;
        executor.currentRoutine.routineArguments[0] = loadedTimer;
        Tick(executor);
        Check(NpcTeleportRoutine.IsWaiting(executor), "Loaded timer retains two remaining turns.");
        Tick(executor);
        Check(executor.currentRoutine == original, "Loaded timer expires on the correct turn.");
    }

    private static void StaleAndBuriedWaitsDoNotRestoreWrongRoutine()
    {
        var original = new NPCRoutine();
        var executor = new NPCRoutineExecutor { currentRoutine = original };
        NpcTeleportRoutine.StartWaiting(executor, default);
        var wait = executor.currentRoutine;
        var newerRoutine = new NPCRoutine { id = "externally_replaced" };
        executor.preOverrideRoutines.AddFirst(wait);
        executor.currentRoutine = newerRoutine;
        Check(NpcTeleportRoutine.TryFinishWait(executor, wait), "Buried wait removal handled.");
        Check(executor.currentRoutine == newerRoutine, "Removing buried wait leaves active routine.");
        Check(executor.preOverrideRoutines.Single() == original, "Buried removal preserves original.");
        Check(!NpcTeleportRoutine.TryFinishWait(executor, original), "Normal cleanup uses vanilla code.");
    }

    private static void MissingPriorRoutineFallsBackToSchedule()
    {
        var executor = new NPCRoutineExecutor { currentRoutine = new NPCRoutine() };
        NpcTeleportRoutine.StartWaiting(executor, default);
        executor.preOverrideRoutines.Clear();
        Tick(executor); Tick(executor); Tick(executor);
        Check(executor.ScheduleChecks == 1, "Missing saved routine requests a schedule fallback.");
        Check(!NpcTeleportRoutine.IsWaiting(executor), "Missing saved routine does not freeze NPC forever.");
        var empty = new NPCRoutineExecutor();
        NpcTeleportRoutine.StartWaiting(empty, default);
        Check(empty.currentRoutine == null && empty.preOverrideRoutines.Count == 0,
            "Uninitialized NPC is not given an orphaned wait.");
    }

    private static void PatchesOnlyAffectWaitingNpc()
    {
        var executor = new NPCRoutineExecutor { currentRoutine = new NPCRoutine() };
        var mover = new EntityMover { Executor = executor };
        Check(InvokePrefix(typeof(KeepTeleportWaitPatch), executor), "Normal overrides allowed.");
        Check(InvokePrefix(typeof(KeepTeleportedNpcStillPatch), mover, true), "Normal movement allowed.");
        Check(InvokePrefix(typeof(KeepTeleportedNpcStillPatch), new EntityMover(), true),
            "Player/non-NPC movement unaffected.");
        NpcTeleportRoutine.StartWaiting(executor, default);
        Check(!InvokePrefix(typeof(KeepTeleportWaitPatch), executor), "Wait cannot be preempted by overrides.");
        object[] arguments = { mover, true };
        Check(!InvokePrefix(typeof(KeepTeleportedNpcStillPatch), arguments), "Waiting NPC cannot move.");
        Check(!(bool)arguments[1], "Blocked movement reports failure.");
        Tick(executor); Tick(executor); Tick(executor);
        Check(InvokePrefix(typeof(KeepTeleportWaitPatch), executor), "Overrides allowed after expiry.");
        Check(InvokePrefix(typeof(KeepTeleportedNpcStillPatch), mover, true), "Movement allowed after expiry.");
    }

    private static bool InvokePrefix(Type patch, params object[] arguments)
    {
        return (bool)patch.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, arguments);
    }

    private static void Tick(NPCRoutineExecutor executor)
    {
        foreach (RoutineArgument argument in executor.currentRoutine.routineArguments.ToArray())
        {
            argument.OnPostProcessTurn();
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++;
    }

    private sealed class LifecycleProbe : RoutineArgument
    {
        internal int UnsetCount;
        internal int SetCount;
        internal int OverriddenCount;
        public override void OnUnset() => UnsetCount++;
        public override void OnSet() => SetCount++;
        public override void OnAfterOverridden() => OverriddenCount++;
    }
}
