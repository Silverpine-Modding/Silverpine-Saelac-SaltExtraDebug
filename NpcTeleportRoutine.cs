using HarmonyLib;
using UnityEngine;

namespace SaltExtraDebug;

internal static class NpcTeleportRoutine
{
    internal const string WaitRoutineId = "salt.extradebug.teleport_wait";
    internal const int WaitTurns = 3;

    internal static bool IsWait(NPCRoutine routine)
    {
        return routine != null && routine.id == WaitRoutineId;
    }

    internal static bool IsWaiting(NPCRoutineExecutor executor)
    {
        return executor != null && IsWait(executor.currentRoutine);
    }

    internal static void StartWaiting(NPCRoutineExecutor executor, Vector2Int position)
    {
        if (executor == null || executor.currentRoutine == null)
        {
            return;
        }

        var wait = new NPCRoutine(
            "standing still beside %PLAYER_NAME% for a short break",
            "standing still beside %PLAYER_NAME% for a short break",
            "Waiting (3 turns)",
            position,
            new RoutineArgument_EndOverrideIf_AfterTurns(WaitTurns))
        {
            id = WaitRoutineId
        };

        if (!IsWaiting(executor))
        {
            // StartOverrideRoutine calls OnAfterOverridden on an older override,
            // which can delete it. Suspend the actual routine without that
            // destructive callback; StartNPCRoutine still runs OnUnset/OnSet.
            executor.preOverrideRoutines.AddFirst(executor.currentRoutine);
        }
        // Repeated teleport replaces only the wait and resets its native timer.
        // Vanilla routine types keep both timer and stack save-compatible.
        executor.StartNPCRoutine(wait);
    }

    internal static bool TryFinishWait(NPCRoutineExecutor executor, NPCRoutine routine)
    {
        if (executor == null || !IsWait(routine))
        {
            return false;
        }

        // Respect removal of a suspended wait without disturbing the active one.
        if (executor.preOverrideRoutines.Remove(routine))
        {
            return true;
        }
        if (!ReferenceEquals(executor.currentRoutine, routine))
        {
            return true;
        }

        NPCRoutine previous = null;
        while (executor.preOverrideRoutines.Count > 0 && previous == null)
        {
            previous = executor.preOverrideRoutines.First.Value;
            executor.preOverrideRoutines.RemoveFirst();
            if (IsWait(previous))
            {
                previous = null;
            }
        }
        if (previous != null)
        {
            // Vanilla StopOverrideRoutine rebuilds the daily schedule when the
            // stack empties. Resume the exact saved routine instead.
            executor.StartNPCRoutine(previous);
        }
        else
        {
            // Another action/mod may have removed the saved routine meanwhile.
            executor.CheckRoutine(WorldInfoManager.Instance.GetCurrentHourOfDay(), false);
        }
        return true;
    }
}

[HarmonyPatch(typeof(NPCRoutineExecutor), nameof(NPCRoutineExecutor.StopOverrideRoutine))]
internal static class FinishTeleportWaitPatch
{
    private static bool Prefix(NPCRoutineExecutor __instance, NPCRoutine overrideRoutine)
    {
        return !NpcTeleportRoutine.TryFinishWait(__instance, overrideRoutine);
    }
}

[HarmonyPatch(typeof(NPCRoutineExecutor), nameof(NPCRoutineExecutor.StartOverrideRoutine))]
internal static class KeepTeleportWaitPatch
{
    private static bool Prefix(NPCRoutineExecutor __instance)
    {
        // Automatic walk-ups/pursuit must not replace the three-turn wait.
        return !NpcTeleportRoutine.IsWaiting(__instance);
    }
}

[HarmonyPatch(typeof(EntityMover), nameof(EntityMover.TryMove))]
internal static class KeepTeleportedNpcStillPatch
{
    private static bool Prefix(EntityMover __instance, ref bool __result)
    {
        if (!NpcTeleportRoutine.IsWaiting(__instance.GetComponent<NPCRoutineExecutor>()))
        {
            return true;
        }
        __result = false;
        return false;
    }
}
