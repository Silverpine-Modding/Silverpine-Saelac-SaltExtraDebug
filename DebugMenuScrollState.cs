using System;
using System.Collections.Generic;

namespace SaltExtraDebug;

// Session-only state, kept separately from Unity's shared scrolling panel.
internal sealed class DebugMenuScrollState
{
    private readonly Dictionary<string, float> _positions = new(StringComparer.Ordinal);

    internal string Screen { get; private set; }
    internal bool RestorePending { get; private set; }
    internal float Position => Screen != null && _positions.TryGetValue(Screen, out float value)
        ? value : 1f;

    internal void Begin(string screen, float outgoingPosition)
    {
        Save(outgoingPosition);
        Screen = screen;
        RequestRestore();
    }

    internal void Save(float position)
    {
        // A rebuild may still contain the preceding screen's rows/position.
        // Closing or switching before restoration must not overwrite memory.
        if (Screen == null || RestorePending || float.IsNaN(position) || float.IsInfinity(position))
            return;

        _positions[Screen] = Math.Max(0f, Math.Min(1f, position));
    }

    internal void Suspend(float position)
    {
        Save(position);
        RequestRestore();
    }

    internal void RequestRestore() => RestorePending = Screen != null;

    internal void Restored() => RestorePending = false;
}
