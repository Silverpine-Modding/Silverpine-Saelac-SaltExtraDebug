// Engine-free harness: the production wait/patch code and native game timer
// are compiled unchanged. These stand-ins model routine lifecycle and storage;
// they do not substitute for an in-game movement/UI/save compatibility test.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SaltExtraDebug;
using UnityEngine;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type, string methodName) { }
    }
}

namespace UnityEngine
{
    public struct Vector2Int
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
    }
}

public interface ISerializable
{
    void Serialize(BinaryWriter writer);
    void Deserialize(BinaryReader reader);
}

public class NPCRoutine
{
    public string id = "new_routine";
    public Vector2Int targetPosition;
    public NPCRoutineExecutor executor;
    public List<RoutineArgument> routineArguments = new List<RoutineArgument>();
    public NPCRoutine() { }
    public NPCRoutine(string pathing, string activity, string visible, Vector2Int position,
        params RoutineArgument[] arguments)
    {
        targetPosition = position;
        routineArguments = arguments.ToList();
    }
}

public class NPCRoutineExecutor
{
    public NPCRoutine currentRoutine;
    public LinkedList<NPCRoutine> preOverrideRoutines = new LinkedList<NPCRoutine>();
    public int ScheduleChecks;

    public void StartNPCRoutine(NPCRoutine routine)
    {
        if (currentRoutine != null)
        {
            foreach (RoutineArgument argument in currentRoutine.routineArguments.ToArray())
                argument.OnUnset();
        }
        currentRoutine = routine;
        routine.executor = this;
        foreach (RoutineArgument argument in routine.routineArguments)
        {
            argument.routine = routine;
            argument.DeclareOwnership();
        }
        foreach (RoutineArgument argument in routine.routineArguments.ToArray())
            argument.OnSetBeforePathing();
        foreach (RoutineArgument argument in routine.routineArguments.ToArray())
            argument.OnSet();
    }

    public void StartOverrideRoutine(NPCRoutine routine)
    {
        NPCRoutine previousOverride = preOverrideRoutines.Count > 0 ? currentRoutine : null;
        preOverrideRoutines.AddFirst(currentRoutine);
        StartNPCRoutine(routine);
        if (previousOverride != null)
        {
            foreach (RoutineArgument argument in previousOverride.routineArguments.ToArray())
                argument.OnAfterOverridden();
        }
    }

    public void StopOverrideRoutine(NPCRoutine routine)
    {
        if (!NpcTeleportRoutine.TryFinishWait(this, routine))
            throw new InvalidOperationException("Unexpected vanilla stop in wait-timer test.");
    }

    public void CheckRoutine(int hour, bool calledByOnNewHour)
    {
        ScheduleChecks++;
        StartNPCRoutine(new NPCRoutine { id = "schedule_fallback" });
    }
}

public class EntityMover
{
    public NPCRoutineExecutor Executor;
    public T GetComponent<T>() where T : class => Executor as T;
    public bool TryMove() => true;
}

public class WorldInfoManager
{
    public static WorldInfoManager Instance = new WorldInfoManager();
    public int GetCurrentHourOfDay() => 12;
}

public class VersionSafeStorage
{
    private readonly Dictionary<string, int> values = new Dictionary<string, int>();
    public void AddToStorage(string key, int value) => values[key] = value;
    public int GetInt(string key, int fallback) => values.TryGetValue(key, out int value) ? value : fallback;
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(values.Count);
        foreach (var pair in values) { writer.Write(pair.Key); writer.Write(pair.Value); }
    }
    public void Deserialize(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++) values[reader.ReadString()] = reader.ReadInt32();
    }
}
