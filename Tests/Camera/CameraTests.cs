using System;
using System.Collections.Generic;
using System.Reflection;
using SaltExtraDebug;
using UnityEngine;
using UnityEngine.U2D;

internal static class CameraTests
{
    private static int checks;

    private static void Main()
    {
        TestMath();
        TestOverviewAndRestoration();
        TestExistingFovAndDisabledPixelPerfect();
        TestResizeAndTileSwitch();
        TestLifetimeAndTargetChanges();
        Console.WriteLine($"PASS: {checks} whole-tile view assertions (modeled engine).");
    }

    private static void TestMath()
    {
        Near(DebugViewMath.TileCenter(0), -0.5f, "Origin tile center includes half-turf offset.");
        Near(DebugViewMath.TileCenter(-2), -200.5f, "Negative world tile center.");
        Near(DebugViewMath.TileCenter(3), 299.5f, "Positive world tile center.");
        Near(DebugViewMath.FitOrthographicSize(100, 100, 16f / 9f), 52f, "Landscape fits vertical extent.");
        Near(DebugViewMath.FitOrthographicSize(100, 100, 0.5f), 104f, "Portrait also fits horizontal extent.");
        Near(DebugViewMath.FitOrthographicSize(200, 80, 2f), 51f, "Wide dungeon fits width.");
        Near(DebugViewMath.FitOrthographicSize(100, 100, 0f), 52f, "Zero aspect has safe fallback.");
        Near(DebugViewMath.FitOrthographicSize(100, 100, float.NaN), 52f, "Invalid aspect has safe fallback.");
        float normalHeight = DebugViewMath.WorldHeightForPixels(24f, 5f, 1080);
        float overviewHeight = DebugViewMath.WorldHeightForPixels(24f, 52f, 1080);
        Near(normalHeight * 1080 / 10f, 24f, "Normal labels are 24 pixels high.");
        Near(overviewHeight * 1080 / 104f, 24f, "Overview labels remain 24 pixels high.");
        Check(float.IsFinite(DebugViewMath.WorldHeightForPixels(24, 5, 0)), "Zero-height viewport stays finite.");
    }

    private static Camera Setup(bool pixelPerfectEnabled = true, bool fovDebug = false)
    {
        WorldTileOverview.StopActive();
        Screen.width = 1920; Screen.height = 1080; Time.unscaledTime = 0;
        var playerObject = new GameObject();
        Player.Instance = playerObject.AddComponent<Player>();
        playerObject.transform.position = new Vector3(12, -8, 0);
        var cameraObject = new GameObject();
        cameraObject.transform.parent = playerObject.transform;
        cameraObject.transform.localPosition = new Vector3(0.2f, 0.3f, -10f);
        Camera camera = cameraObject.AddComponent<Camera>();
        Camera.main = camera;
        PixelPerfectCamera pixel = cameraObject.AddComponent<PixelPerfectCamera>();
        pixel.enabled = pixelPerfectEnabled;
        camera.orthographic = true;
        camera.orthographicSize = 6.25f;
        camera.rect = new Rect(0.1f, 0, 0.8f, 1);
        camera.aspect = 1.5f;
        camera.projectionMatrix = new Matrix4x4(123);
        camera.forceIntoRenderTexture = true;
        FOVManager.Instance = new FOVManager();
        FOVManager.Instance.SetDebug(fovDebug);
        DungeonGenerationManager.Instance = new DungeonGenerationManager();
        DungeonGenerationManager.Instance.worldTiles[default] = new WorldTile { position = default };
        return camera;
    }

    private static void TestOverviewAndRestoration()
    {
        Camera camera = Setup();
        var pixel = camera.GetComponent<PixelPerfectCamera>();
        Transform parent = camera.transform.parent;
        WorldTileOverview.Toggle();
        Check(WorldTileOverview.IsActive, "Toggle enables overview.");
        Check(camera.transform.parent == parent, "Camera stays owned by gameplay parent.");
        Near(camera.transform.position.x, -0.5f, "Camera centered on tile X.");
        Near(camera.transform.position.y, -0.5f, "Camera centered on tile Y.");
        Near(Player.Instance.transform.position.x, 12f, "Player is not moved.");
        Near(camera.orthographicSize, 52f, "Overview overrides pixel-perfect zoom.");
        Check(!pixel.enabled, "Pixel-perfect component temporarily suspended.");
        Check(FOVManager.Instance.DebugValue, "Visibility mask removed.");
        Check(camera.projectionMatrix.Token != 123, "Custom projection replaced for zoom.");
        var cull = new GameObject().AddComponent<CullInvisible>();
        var sprite = cull.gameObject.AddComponent<SpriteRenderer>();
        sprite.enabled = false;
        Check(!(bool)Call(typeof(WholeTileVisibilityPatch), "Prefix", null, cull), "Overview bypasses sprite hiding.");
        Check(sprite.enabled, "Occluded sprite becomes visible.");

        Player.Instance.transform.position = new Vector3(20, -8, 0);
        WorldTileOverview.StopActive();
        Check(!WorldTileOverview.IsActive, "Toggle clears active state.");
        Near(camera.transform.localPosition.x, 0.2f, "Original follow offset restored.");
        Near(camera.transform.position.x, 20.2f, "Restoration follows current player position.");
        Near(camera.orthographicSize, 6.25f, "Original zoom restored.");
        Near(camera.aspect, 1.5f, "Original aspect restored.");
        Near(camera.rect.width, 0.8f, "Original viewport restored.");
        Check(camera.projectionMatrix.Token == 123, "Original custom projection restored.");
        Check(camera.forceIntoRenderTexture && pixel.enabled, "Pixel-perfect/render flags restored.");
        Check(!FOVManager.Instance.DebugValue, "Original FOV state restored.");
        Check((bool)Call(typeof(WholeTileVisibilityPatch), "Prefix", null, cull), "Normal sprite visibility resumes.");
        WorldTileOverview.Toggle(); WorldTileOverview.Toggle();
        Near(camera.orthographicSize, 6.25f, "Repeated toggles do not replace original zoom snapshot.");
        Check(camera.GetComponents<WorldTileOverview>().Length == 1, "Repeated toggles reuse runtime component.");
    }

    private static void TestExistingFovAndDisabledPixelPerfect()
    {
        Camera camera = Setup(false, true);
        WorldTileOverview.Toggle(); WorldTileOverview.StopActive();
        Check(!camera.GetComponent<PixelPerfectCamera>().enabled, "Already-disabled pixel-perfect stays disabled.");
        Check(FOVManager.Instance.DebugValue, "Already-enabled FOV debug stays enabled.");
    }

    private static void TestResizeAndTileSwitch()
    {
        Camera camera = Setup();
        WorldTileOverview.Toggle();
        var view = camera.GetComponent<WorldTileOverview>();
        Screen.width = 800; Screen.height = 1600;
        Call(typeof(WorldTileOverview), "OnPreCull", view);
        Near(camera.orthographicSize, 130f, "Resized portrait viewport still fits full tile.");
        var key = new Vector2Int(2, -3);
        var tile = new WorldTile { position = key };
        DungeonGenerationManager.Instance.worldTiles[key] = tile;
        DungeonGenerationManager.Instance.currentPlayerWorldTilePosition = key;
        Call(typeof(WorldTileOverview), "LateUpdate", view);
        Near(camera.transform.position.x, 199.5f, "View follows changed world tile X.");
        Near(camera.transform.position.y, -300.5f, "View follows changed world tile Y.");
        tile = new WorldTile { position = key, Dungeon = true };
        DungeonGenerationManager.Instance.worldTiles[key] = tile;
        Turfs.Tiles = new List<Vector2Int> { new Vector2Int(100, -400), new Vector2Int(299, -321) };
        Call(typeof(WorldTileOverview), "LateUpdate", view);
        Near(camera.transform.position.y, -360.5f, "Dungeon uses actual loaded turf center.");
        Near(camera.orthographicSize, 255f, "Dungeon fit includes its actual width.");
        WorldTileOverview.StopActive();
    }

    private static void TestLifetimeAndTargetChanges()
    {
        Camera camera = Setup();
        WorldTileOverview.Toggle();
        Call(typeof(EndOverviewBeforeLoadPatch), "Prefix", null);
        Check(!WorldTileOverview.IsActive && !FOVManager.Instance.DebugValue, "Save load ends view before teardown.");
        WorldTileOverview.Toggle();
        Call(typeof(EndOverviewBeforeSpectatingPatch), "Prefix", null);
        Check(!WorldTileOverview.IsActive, "Spectate action ends view first.");
        WorldTileOverview.Toggle();
        var view = camera.GetComponent<WorldTileOverview>();
        view.enabled = false;
        Check(!WorldTileOverview.IsActive, "Disabling camera-hosted controller restores state.");
        Near(camera.orthographicSize, 6.25f, "OnDisable restores zoom.");
        WorldTileOverview.Toggle();
        Transform newParent = new GameObject().transform;
        camera.transform.parent = newParent;
        camera.transform.localPosition = new Vector3(1, 2, -10);
        Call(typeof(WorldTileOverview), "LateUpdate", view);
        Check(!WorldTileOverview.IsActive && camera.transform.parent == newParent, "New camera target is respected.");
        Near(camera.transform.localPosition.x, 1f, "New target's camera offset is not overwritten.");
        WorldTileOverview.Toggle();
        Player.Instance = new GameObject().AddComponent<Player>();
        Call(typeof(WorldTileOverview), "LateUpdate", view);
        Check(!WorldTileOverview.IsActive, "Replacing gameplay player ends old view.");
        Camera.main = null;
        WorldTileOverview.Toggle();
        Check(!WorldTileOverview.IsActive, "No camera means no orphaned view.");
    }

    private static object Call(Type type, string method, object target, params object[] args)
        => type.GetMethod(method, BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    private static void Near(float actual, float expected, string message)
        => Check(Math.Abs(actual - expected) < 0.001f, $"{message} Expected {expected}, got {actual}.");
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
}
