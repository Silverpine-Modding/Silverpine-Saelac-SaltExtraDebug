using System;
using System.Collections.Generic;
using System.Reflection;
using SaltExtraDebug;

// Exercises the production Player.Start hook/ownership helper unchanged.
// The fake hosts below model component attachment; rendering still needs Unity.
internal static class OverlayLifecycleTests
{
    private static int assertions;

    internal static void Run()
    {
        NpcPropertyOverlayLifecycle.EnsureForPlayer(null);
        var bootstrapHost = new OverlayTestHost();
        var player = new Player();
        typeof(NpcPropertyOverlayLifecycle)
            .GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { player });
        var overlay = player.GetComponent<NpcPropertyBoundaryOverlay>();
        Check(overlay != null, "Player.Start creates the runtime component.");
        Check(bootstrapHost.ComponentCount == 0, "No dependency on bootstrap host.");
        Check(overlay.enabled && overlay.RefreshCount == 1, "Runtime component enabled and refreshed.");

        bootstrapHost.Clear();
        Check(player.GetComponent<NpcPropertyBoundaryOverlay>() == overlay,
            "Destroying startup host leaves player's runtime component intact.");
        NpcPropertyOverlayLifecycle.EnsureForPlayer(player);
        Check(player.gameObject.ComponentCount == 1, "Opening menu does not duplicate component.");
        Check(overlay.RefreshCount == 2, "Menu/toggle refreshes existing component.");
        overlay.enabled = false;
        NpcPropertyOverlayLifecycle.EnsureForPlayer(player);
        Check(overlay.enabled, "Existing disabled runtime component can recover.");

        player.gameObject.Clear();
        NpcPropertyOverlayLifecycle.EnsureForPlayer(player);
        Check(player.GetComponent<NpcPropertyBoundaryOverlay>() != overlay,
            "Missing component is recreated without rerunning plugin Awake.");
        var replacementPlayer = new Player();
        NpcPropertyOverlayLifecycle.EnsureForPlayer(replacementPlayer);
        Check(replacementPlayer.GetComponent<NpcPropertyBoundaryOverlay>() != null,
            "Replacement player after save load gets a runtime component.");
        Check(replacementPlayer.GetComponent<NpcPropertyBoundaryOverlay>()
                != player.GetComponent<NpcPropertyBoundaryOverlay>(),
            "New player does not reuse old player's component.");
        Console.WriteLine($"PASS: {assertions} overlay-lifecycle assertions.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++;
    }
}

public class Player
{
    public readonly OverlayTestHost gameObject = new OverlayTestHost();
    public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
}

public sealed class OverlayTestHost
{
    private readonly Dictionary<Type, object> components = new Dictionary<Type, object>();
    public int ComponentCount => components.Count;
    public T AddComponent<T>() where T : new()
    {
        var component = new T();
        components[typeof(T)] = component;
        return component;
    }
    public T GetComponent<T>() where T : class
        => components.TryGetValue(typeof(T), out var component) ? component as T : null;
    public void Clear() => components.Clear();
}

namespace SaltExtraDebug
{
    internal sealed class NpcPropertyBoundaryOverlay
    {
        public bool enabled;
        internal int RefreshCount;
        internal void RefreshNow() => RefreshCount++;
    }
}
