// Minimal engine model for exercising unchanged production camera state code.
// Does not implement Unity rendering, projection math or actual Harmony hooks.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    internal class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(Type type, string method) { }
    }
    internal static class AccessTools
    {
        public static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        public static Type Inner(Type type, string name) => type.GetNestedType(name, BindingFlags.NonPublic);
        public static MethodInfo Method(Type type, string name) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
    }
}

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)]
    internal class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int value) { } }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }
    public struct Vector2Int
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
    }
    public struct Quaternion { public static Quaternion identity => default; }
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
    }
    public struct Matrix4x4
    {
        public float Token;
        public Matrix4x4(float token) { Token = token; }
    }
    public class Transform
    {
        public Transform parent;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Quaternion rotation { get => localRotation; set => localRotation = value; }
        public Vector3 position
        {
            get => parent == null ? localPosition : parent.position + localPosition;
            set => localPosition = parent == null ? value : value - parent.position;
        }
    }
    public class GameObject
    {
        private readonly List<Component> components = new();
        public readonly Transform transform = new();
        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            components.Add(component);
            return component;
        }
        public T GetComponent<T>() where T : class => components.OfType<T>().FirstOrDefault();
        public T[] GetComponents<T>() => components.OfType<T>().ToArray();
    }
    public class Component
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public T[] GetComponents<T>() => gameObject.GetComponents<T>();
    }
    public class Behaviour : Component
    {
        private bool active = true;
        public bool enabled
        {
            get => active;
            set
            {
                if (value == active) return;
                active = value;
                GetType().GetMethod(value ? "OnEnable" : "OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(this, null);
            }
        }
    }
    public class MonoBehaviour : Behaviour { }
    public class Camera : Behaviour
    {
        public static Camera main;
        public bool orthographic;
        public float orthographicSize;
        public Rect rect = new Rect(0, 0, 1, 1);
        public float aspect;
        public Matrix4x4 projectionMatrix;
        public bool forceIntoRenderTexture;
        public void ResetAspect() => aspect = Screen.width * rect.width / (Screen.height * rect.height);
        public void ResetWorldToCameraMatrix() { }
        public void ResetProjectionMatrix() => projectionMatrix = new Matrix4x4(orthographicSize);
    }
    public class SpriteRenderer : Behaviour { }
    public static class Screen { public static int width, height; }
    public static class Time { public static float unscaledTime; }
}

namespace UnityEngine.U2D
{
    public class PixelPerfectCamera : Behaviour
    {
        // Model the native OnDisable mutation to catch capturing too late.
        private void OnDisable()
        {
            var camera = GetComponent<Camera>();
            camera.orthographicSize = 1;
            camera.rect = new Rect(0, 0, 1, 1);
        }
    }
}

public class Player : MonoBehaviour { public static Player Instance; }
public class WorldTile
{
    public Vector2Int position;
    public bool Dungeon;
    public bool IsDungeonInstance() => Dungeon;
}
public class DungeonGenerationManager
{
    public static DungeonGenerationManager Instance;
    public Dictionary<Vector2Int, WorldTile> worldTiles = new();
    public Vector2Int currentPlayerWorldTilePosition;
}
public static class Turfs
{
    public static List<Vector2Int> Tiles = new();
    public static List<Vector2Int> GetWorldTileTurfs(WorldTile tile) => Tiles;
}
public class FOVManager
{
    public static FOVManager Instance;
    private bool debug;
    public bool DebugValue => debug;
    public void Initialize() { }
    public void SetDebug(bool state) { debug = state; Initialize(); }
}
public class CullInvisible : MonoBehaviour { public static void CheckAll() { } }
public static class SpriteExtensions
{
    public static SpriteRenderer GetSpriteRendererExtended(this GameObject obj) => obj.GetComponent<SpriteRenderer>();
}
public class UpperNotificationUI
{
    public static UpperNotificationUI Instance = new();
    public void OneOff(string message) { }
}
public static class SerializationManager { public static void Load(string path) { } }
public class DebugUI
{
    private class DebugAction_SpectateNPC { private void OnCalled() { } }
}
