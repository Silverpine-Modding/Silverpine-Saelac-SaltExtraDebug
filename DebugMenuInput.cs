using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SaltExtraDebug;

// Attached to the list UI, never the disposable BepInEx startup host.
[DefaultExecutionOrder(-10000)]
internal sealed class DebugMenuInput : MonoBehaviour
{
    private static readonly DebugMenuInputState InputState = new();
    private static DebugMenuInput _active;
    private bool _debugScreen;
    private RectTransform _shield;
    private readonly Vector3[] _canvasCorners = new Vector3[4];

    internal static bool BlocksWorldInput()
    {
        bool visible = _active != null && _active._debugScreen && _active.isActiveAndEnabled;
        return InputState.ShouldBlock(visible, Time.frameCount, MouseButtons(false), MouseButtons(true));
    }

    private static int MouseButtons(bool pressed)
    {
        int buttons = 0;
        for (int i = 0; i < 3; i++)
        {
            KeyCode key = (KeyCode)((int)KeyCode.Mouse0 + i);
            if (pressed ? Input.GetKeyDown(key) : Input.GetKey(key))
                buttons |= 1 << i;
        }
        return buttons;
    }

    internal void SetScreen(bool debugScreen)
    {
        if (_debugScreen && !debugScreen)
            Deactivate();
        _debugScreen = debugScreen;
        if (_debugScreen && isActiveAndEnabled)
            Activate();
        else if (_shield != null)
            _shield.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (_debugScreen)
            Activate();
    }

    private void OnDisable() => Deactivate();

    private void Activate()
    {
        _active = this;
        // Put the modal list above ordinary sibling UI without moving any of
        // its children or changing the debug window's anchors/scroll layout.
        transform.SetAsLastSibling();
        if (_shield == null)
        {
            var shieldObject = new GameObject("SaltExtraDebug_ClickShield",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
            _shield = shieldObject.GetComponent<RectTransform>();
            _shield.SetParent(transform, false);
            _shield.SetAsFirstSibling();
            _shield.anchorMin = _shield.anchorMax = _shield.pivot = new Vector2(0.5f, 0.5f);
            shieldObject.GetComponent<LayoutElement>().ignoreLayout = true;
            shieldObject.GetComponent<CanvasRenderer>().cullTransparentMesh = false;
            Image image = shieldObject.GetComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = true;
        }
        _shield.gameObject.SetActive(true);
        SizeShield();
        // Do not let a previously focused hotbar/inventory button receive a
        // submit key while the modal list has focus.
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void SizeShield()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        RectTransform canvasRect = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
        if (canvasRect == null || _shield == null)
            return;
        canvasRect.GetWorldCorners(_canvasCorners);
        Vector3 lower = transform.InverseTransformPoint(_canvasCorners[0]);
        Vector3 upper = transform.InverseTransformPoint(_canvasCorners[2]);
        _shield.localPosition = new Vector3((lower.x + upper.x) * 0.5f, (lower.y + upper.y) * 0.5f, 0f);
        _shield.sizeDelta = new Vector2(Mathf.Abs(upper.x - lower.x), Mathf.Abs(upper.y - lower.y));
    }

    private void Deactivate()
    {
        if (_active == this)
        {
            InputState.Closed(Time.frameCount, MouseButtons(false));
            _active = null;
        }
        if (_shield != null)
            _shield.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (_active != this || !_debugScreen)
            return;
        // Submenus can always be dismissed without forwarding Escape to world
        // input. X and menu actions go through the same OnDisable protection.
        if (SettingsUI.Instance != null && Input.GetKeyDown(SettingsUI.Instance.closeMenuKeyCode))
            GetComponent<GenericListUI>().Close();
    }

    private void LateUpdate()
    {
        if (_debugScreen)
            SizeShield();
    }
}

[HarmonyPatch(typeof(Player), "Update")]
internal static class DebugMenuPlayerInputPatch
{
    private static bool Prefix(ref bool ___movementBlock)
    {
        if (!DebugMenuInput.BlocksWorldInput())
            return true;

        // Preserve the native movement-key release bookkeeping while skipping
        // world interaction, hotkeys, forced movement, and real-time turn input.
        SettingsUI settings = SettingsUI.Instance;
        if (settings != null && (Input.GetKeyUp(settings.upKeyCode) || Input.GetKeyUp(settings.downKeyCode)
            || Input.GetKeyUp(settings.leftKeyCode) || Input.GetKeyUp(settings.rightKeyCode)))
            ___movementBlock = false;
        return false;
    }
}

[HarmonyPatch]
internal static class DebugMenuWorldClickPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Clickable), "OnMouseUpAsButton");
        yield return AccessTools.Method(typeof(Pickupable), "OnMouseUpAsButton");
        // P inspects the world directly; do not inspect through an open menu.
        yield return AccessTools.Method(typeof(DebugUI), "Update");
    }

    private static bool Prefix() => !DebugMenuInput.BlocksWorldInput();
}

[HarmonyPatch]
internal static class DebugMenuPlacementInputPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(PlacementModeManager), "PlacementCoroutine"));
        yield return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(PlacementModeManager), "RotationIndicatorCoroutine"));
    }

    private static bool Prefix(ref bool __result)
    {
        if (!DebugMenuInput.BlocksWorldInput())
            return true;
        // Pause, don't finish/dispose the coroutine. Its next MoveNext resumes
        // only after the menu's closing press AND release have been consumed.
        __result = true;
        return false;
    }
}
