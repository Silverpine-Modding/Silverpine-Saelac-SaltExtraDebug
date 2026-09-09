using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SaltExtraDebug;

[DefaultExecutionOrder(10000)]
internal sealed class WorldTileOverview : MonoBehaviour
{
    private static readonly FieldInfo FovDebugField = AccessTools.Field(typeof(FOVManager), "debug");
    private static WorldTileOverview _active;

    private Camera _camera;
    private bool _viewEnabled;
    private Player _player;
    private WorldTile _tile;
    private Vector2 _center;
    private Vector2 _size;
    private float _nextVisibilityRefresh;
    private FOVManager _fov;
    private bool _originalFovDebug;
    private Transform _originalParent;
    private Vector3 _originalLocalPosition;
    private Quaternion _originalLocalRotation;
    private Vector3 _originalWorldPosition;
    private float _originalOrthoSize;
    private bool _originalOrthographic;
    private float _originalAspect;
    private Rect _originalRect;
    private Matrix4x4 _originalProjection;
    private bool _originalForceIntoRenderTexture;
    private readonly List<Behaviour> _suspendedPixelPerfect = new();

    internal static bool IsActive => _active != null && _active._viewEnabled;

    internal static void Toggle()
    {
        if (IsActive)
        {
            StopActive();
            return;
        }

        Camera camera = Camera.main;
        if (camera == null || Player.Instance == null || !TryGetCurrentTile(out WorldTile tile))
        {
            if (UpperNotificationUI.Instance != null)
            {
                UpperNotificationUI.Instance.OneOff("Enter the world before enabling the whole-tile view.");
            }
            return;
        }

        // Camera is a live gameplay object, never the discarded plugin host.
        WorldTileOverview overview = camera.GetComponent<WorldTileOverview>();
        if (overview == null)
        {
            overview = camera.gameObject.AddComponent<WorldTileOverview>();
        }
        overview.enabled = true;
        overview.Begin(camera, tile);
    }

    internal static void StopActive()
    {
        if (_active != null)
        {
            _active.End();
        }
    }

    private static bool TryGetCurrentTile(out WorldTile tile)
    {
        tile = null;
        DungeonGenerationManager manager = DungeonGenerationManager.Instance;
        return manager != null
            && manager.worldTiles.TryGetValue(manager.currentPlayerWorldTilePosition, out tile)
            && tile != null;
    }

    private void Begin(Camera camera, WorldTile tile)
    {
        _camera = camera;
        _player = Player.Instance;
        _originalParent = camera.transform.parent;
        _originalLocalPosition = camera.transform.localPosition;
        _originalLocalRotation = camera.transform.localRotation;
        _originalWorldPosition = camera.transform.position;
        _originalOrthoSize = camera.orthographicSize;
        _originalOrthographic = camera.orthographic;
        _originalAspect = camera.aspect;
        _originalRect = camera.rect;
        _originalProjection = camera.projectionMatrix;
        _originalForceIntoRenderTexture = camera.forceIntoRenderTexture;
        _fov = FOVManager.Instance;
        _originalFovDebug = _fov != null && (bool)FovDebugField.GetValue(_fov);

        // PixelPerfectCamera writes size/projection during render callbacks.
        // Only suspend enabled pixel-perfect components, not unrelated mods.
        foreach (Behaviour component in camera.GetComponents<Behaviour>())
        {
            if (component != null && component.enabled
                && component.GetType().FullName == "UnityEngine.U2D.PixelPerfectCamera")
            {
                _suspendedPixelPerfect.Add(component);
                component.enabled = false;
            }
        }
        camera.rect = _originalRect;
        _viewEnabled = true;
        _active = this;
        UpdateBounds(tile);
        ApplyView();
        if (_fov != null)
        {
            _fov.SetDebug(true);
        }
        CullInvisible.CheckAll();
        _nextVisibilityRefresh = Time.unscaledTime + 0.5f;
    }

    private void UpdateBounds(WorldTile tile)
    {
        _tile = tile;
        _center = new Vector2(DebugViewMath.TileCenter(tile.position.x), DebugViewMath.TileCenter(tile.position.y));
        _size = new Vector2(100f, 100f);
        if (!tile.IsDungeonInstance())
        {
            return;
        }

        // Dungeon instances can extend past the normal 100x100 turf square.
        List<Vector2Int> turfs = Turfs.GetWorldTileTurfs(tile);
        if (turfs.Count > 0)
        {
            int minX = turfs.Min(p => p.x), maxX = turfs.Max(p => p.x);
            int minY = turfs.Min(p => p.y), maxY = turfs.Max(p => p.y);
            _center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            _size = new Vector2(maxX - minX + 1f, maxY - minY + 1f);
        }
    }

    private void LateUpdate()
    {
        if (!_viewEnabled)
        {
            return;
        }
        if (_player == null || _player != Player.Instance || _camera != Camera.main
            || !TryGetCurrentTile(out WorldTile tile))
        {
            End();
            return;
        }
        if (_camera.transform.parent != _originalParent)
        {
            // A different camera controller deliberately chose a new target.
            End(restoreTransform: false);
            return;
        }
        if (!ReferenceEquals(tile, _tile))
        {
            UpdateBounds(tile);
        }

        ApplyView();
        if (_fov != null && !(bool)FovDebugField.GetValue(_fov))
        {
            _fov.SetDebug(true);
        }
        if (Time.unscaledTime >= _nextVisibilityRefresh)
        {
            // Covers newly spawned/loaded objects and native delayed cull work.
            CullInvisible.CheckAll();
            _nextVisibilityRefresh = Time.unscaledTime + 0.5f;
        }
    }

    private void OnPreCull()
    {
        if (_viewEnabled)
        {
            // Also fit the actual viewport for resize and screenshot renders.
            ApplyView();
        }
    }

    private void ApplyView()
    {
        if (_camera == null)
        {
            return;
        }
        // Keep the camera parent intact: never detach it from save/scene cleanup.
        _camera.transform.position = new Vector3(_center.x, _center.y, _originalWorldPosition.z);
        _camera.transform.rotation = Quaternion.identity;
        _camera.orthographic = true;
        _camera.ResetAspect();
        _camera.orthographicSize = DebugViewMath.FitOrthographicSize(_size.x, _size.y, _camera.aspect);
        _camera.ResetWorldToCameraMatrix();
        // SetObliqueness installs a custom matrix that otherwise ignores zoom.
        _camera.ResetProjectionMatrix();
    }

    private void End(bool restoreTransform = true)
    {
        if (!_viewEnabled)
        {
            return;
        }
        _viewEnabled = false;
        if (_active == this)
        {
            _active = null;
        }

        if (_camera != null)
        {
            if (restoreTransform && _camera.transform.parent == _originalParent)
            {
                // Never reparent during teardown or undo another controller's
                // new target. A root camera's local pose is also its world pose.
                _camera.transform.localPosition = _originalLocalPosition;
                _camera.transform.localRotation = _originalLocalRotation;
            }
            foreach (Behaviour component in _suspendedPixelPerfect)
            {
                if (component != null)
                {
                    component.enabled = true;
                }
            }
            _camera.orthographic = _originalOrthographic;
            _camera.orthographicSize = _originalOrthoSize;
            _camera.rect = _originalRect;
            _camera.aspect = _originalAspect;
            _camera.forceIntoRenderTexture = _originalForceIntoRenderTexture;
            _camera.ResetWorldToCameraMatrix();
            _camera.projectionMatrix = _originalProjection;
        }
        _suspendedPixelPerfect.Clear();

        if (_fov != null)
        {
            FovDebugField.SetValue(_fov, _originalFovDebug);
            if (Player.Instance != null && Camera.main != null && _fov == FOVManager.Instance)
            {
                _fov.Initialize();
            }
        }
        if (Player.Instance != null)
        {
            CullInvisible.CheckAll();
        }
        _tile = null;
        _fov = null;
        _player = null;
    }

    private void OnDisable()
    {
        End();
    }
}

[HarmonyPatch(typeof(CullInvisible), "Check")]
internal static class WholeTileVisibilityPatch
{
    private static bool Prefix(CullInvisible __instance)
    {
        if (!WorldTileOverview.IsActive)
        {
            return true;
        }
        SpriteRenderer renderer = __instance.gameObject.GetSpriteRendererExtended();
        if (renderer != null)
        {
            renderer.enabled = true;
        }
        return false;
    }
}

[HarmonyPatch(typeof(SerializationManager), nameof(SerializationManager.Load))]
internal static class EndOverviewBeforeLoadPatch
{
    private static void Prefix() => WorldTileOverview.StopActive();
}

[HarmonyPatch]
internal static class EndOverviewBeforeSpectatingPatch
{
    private static MethodBase TargetMethod()
    {
        return AccessTools.Method(AccessTools.Inner(typeof(DebugUI), "DebugAction_SpectateNPC"), "OnCalled");
    }

    private static void Prefix() => WorldTileOverview.StopActive();
}
