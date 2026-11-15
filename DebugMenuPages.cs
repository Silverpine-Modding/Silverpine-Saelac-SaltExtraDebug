using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaltExtraDebug;

// Uses native row objects/callbacks. All temporary hierarchy/layout changes are
// restored before an unrelated GenericListUI screen is drawn. No OnDestroy hook.
[DefaultExecutionOrder(10000)]
internal sealed class DebugMenuPages : MonoBehaviour
{
    private readonly DebugMenuPageState _state = new();
    private readonly List<(GameObject Object, bool Active)> _nativeRoots = new();
    private readonly List<(Behaviour Component, bool Enabled)> _nativeLayout = new();
    private readonly Vector3[] _corners = new Vector3[4];
    private RectTransform _panel, _body, _content;
    private RectSnapshot _originalContent;
    private CanvasGroup _visibility;
    private float _originalAlpha;
    private bool _originalBlocks, _originalInteractable;
    private bool _attached, _pending;
    private int _drawFrame, _page;
    private Vector2 _lastSize;
    private Row[] _rows = Array.Empty<Row>();
    private DebugMenuPageLayout _layout;
    private Button _previous, _next, _close;
    private TextMeshProUGUI _pageLabel;

    internal void Begin(string screen, GameObject contentRoot)
    {
        _state.Begin(screen);
        if (screen == null)
        {
            RestoreNative();
            return;
        }
        if (!_attached) Attach(contentRoot);
        if (!_attached) return;
        // Hide only rendering/raycasting, never the content GameObject: native
        // HandleInstantiated needs to find the new rows' active text children.
        _visibility.alpha = 0;
        _visibility.blocksRaycasts = false;
        _pending = true;
        _drawFrame = Time.frameCount;
        _rows = Array.Empty<Row>();
        _layout = null;
        _panel.gameObject.SetActive(true);
        _previous.interactable = _next.interactable = false;
        _pageLabel.text = "";
    }

    private void Attach(GameObject contentRoot)
    {
        _content = contentRoot != null ? contentRoot.transform as RectTransform : null;
        if (_content == null) return;
        _originalContent = new RectSnapshot(_content);
        // Capture before adding the panel. The modal input shield is separate.
        foreach (Transform child in transform)
        {
            if (child == _panel || child.name == "SaltExtraDebug_ClickShield") continue;
            _nativeRoots.Add((child.gameObject, child.gameObject.activeSelf));
        }
        ScrollRect scroll = _content.GetComponentInParent<ScrollRect>(true);
        if (scroll != null) SaveAndDisable(scroll);
        foreach (LayoutGroup group in _content.GetComponents<LayoutGroup>()) SaveAndDisable(group);
        foreach (ContentSizeFitter fitter in _content.GetComponents<ContentSizeFitter>()) SaveAndDisable(fitter);
        _visibility = _content.GetComponent<CanvasGroup>();
        if (_visibility == null) _visibility = _content.gameObject.AddComponent<CanvasGroup>();
        _originalAlpha = _visibility.alpha;
        _originalBlocks = _visibility.blocksRaycasts;
        _originalInteractable = _visibility.interactable;
        if (_panel == null) CreatePanel();
        _content.SetParent(_body, false);
        Stretch(_content, Vector2.zero, Vector2.zero);
        _content.localScale = Vector3.one;
        _content.localRotation = Quaternion.identity;
        foreach (var entry in _nativeRoots) entry.Object.SetActive(false);
        _attached = true;
        SizePanel();
    }

    private void SaveAndDisable(Behaviour component)
    {
        _nativeLayout.Add((component, component.enabled));
        component.enabled = false;
    }

    private void RestoreNative()
    {
        _pending = false;
        _layout = null;
        _rows = Array.Empty<Row>();
        if (!_attached) return;
        _originalContent.Restore(_content);
        _visibility.alpha = _originalAlpha;
        _visibility.blocksRaycasts = _originalBlocks;
        _visibility.interactable = _originalInteractable;
        foreach (var entry in _nativeLayout)
            if (entry.Component != null) entry.Component.enabled = entry.Enabled;
        foreach (var entry in _nativeRoots)
            if (entry.Object != null) entry.Object.SetActive(entry.Active);
        _nativeLayout.Clear();
        _nativeRoots.Clear();
        _panel.gameObject.SetActive(false);
        _attached = false;
    }

    private void CreatePanel()
    {
        Image nativeBackground = transform.Find("Background")?.GetComponent<Image>();
        _panel = MakeRect("SaltExtraDebug_Window", transform);
        Image background = _panel.gameObject.AddComponent<Image>();
        if (nativeBackground != null) CopyImage(nativeBackground, background);
        else background.color = new Color(0.18f, 0.21f, 0.27f, 0.98f);
        _body = MakeRect("Pages", _panel);
        Stretch(_body, new Vector2(14, 58), new Vector2(-14, -62));
        _body.gameObject.AddComponent<RectMask2D>();

        TextMeshProUGUI template = _content.GetComponentInChildren<TextMeshProUGUI>(true);
        if (template == null)
            template = Resources.Load<GameObject>("MiscPrefabs/ListUI/ListUIItemPrefab_Generic")
                .GetComponentInChildren<TextMeshProUGUI>(true);
        TextMeshProUGUI title = MakeText("Title", _panel, template, "Debug Menu");
        Stretch(title.rectTransform, new Vector2(18, -54), new Vector2(-70, -8));
        title.rectTransform.anchorMin = new Vector2(0, 1);
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.fontSize = 28;
        _previous = MakeNavigation("Previous", _panel, template, () => ShowPage(_page - 1));
        Place(_previous.transform as RectTransform, new Vector2(0, 0), new Vector2(14, 10), new Vector2(150, 38));
        _next = MakeNavigation("Next", _panel, template, () => ShowPage(_page + 1));
        Place(_next.transform as RectTransform, new Vector2(1, 0), new Vector2(-14, 10), new Vector2(150, 38));
        _pageLabel = MakeText("PageNumber", _panel, template, "");
        Stretch(_pageLabel.rectTransform, new Vector2(176, 10), new Vector2(-176, 48));
        _pageLabel.rectTransform.anchorMax = new Vector2(1, 0);
        RefreshCloseButton();
    }

    internal void RefreshCloseButton()
    {
        if (_panel == null || _close != null) return;
        Button inventoryClose = PauseMenuManager.Instance != null
            ? AccessTools.Field(typeof(PauseMenuManager), "closeButton").GetValue(PauseMenuManager.Instance) as Button : null;
        if (inventoryClose != null)
        {
            // Clone the actual inventory selection-mode X (sprite, font,
            // colors and button transitions), not a visual approximation.
            _close = Instantiate(inventoryClose.gameObject, _panel, false).GetComponent<Button>();
            _close.gameObject.name = "SaltExtraDebug_CloseButton";
            // Remove BOTH persistent and runtime inventory callbacks.
            _close.onClick = new Button.ButtonClickedEvent();
            // An inactive clone has not had Awake yet; let its native Awake
            // initialize the sound on opening instead of clearing its clip.
            if (_close.gameObject.activeInHierarchy)
                _close.GetComponent<ButtonSoundPlayer>()?.Subscribe();
            foreach (TextMeshProUGUI text in _close.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                Stretch(text.rectTransform, Vector2.zero, Vector2.zero);
                text.raycastTarget = false;
            }
        }
        else
        {
            _close = MakeNavigation("X", _panel, _pageLabel, null);
            _close.gameObject.name = "SaltExtraDebug_CloseButton";
        }
        _close.interactable = true;
        // Do not retain explicit keyboard-navigation links into the inventory.
        _close.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        _close.onClick.AddListener(() => GetComponent<GenericListUI>().Close());
        Place(_close.transform as RectTransform, Vector2.one, new Vector2(-8, -8), new Vector2(44, 44));
        _close.gameObject.SetActive(true);
        _close.transform.SetAsLastSibling();
    }

    private void LateUpdate()
    {
        if (!_attached || _state.Screen == null) return;
        Vector2 size = SizePanel();
        if (_pending)
        {
            if (Time.frameCount <= _drawFrame) return;
            // Old native rows use Destroy, not DestroyImmediate. Waiting one
            // frame also includes appended cheat buttons from ShowDebugOptions.
            _rows = _content.Cast<Transform>().Select(child => new Row(child as RectTransform)).ToArray();
            _pending = false;
            Reflow(size);
        }
        else if (size != _lastSize) Reflow(size);
    }

    private Vector2 SizePanel()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        RectTransform canvasRect = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
        if (canvasRect == null) return _body.rect.size;
        canvasRect.GetWorldCorners(_corners);
        Vector3 lower = transform.InverseTransformPoint(_corners[0]);
        Vector3 upper = transform.InverseTransformPoint(_corners[2]);
        _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0.5f, 0.5f);
        _panel.localPosition = (lower + upper) * 0.5f;
        _panel.sizeDelta = new Vector2(Mathf.Min(1440, Mathf.Abs(upper.x - lower.x) * 0.9f),
            Mathf.Min(820, Mathf.Abs(upper.y - lower.y) * 0.86f));
        return _body.rect.size;
    }

    private void Reflow(Vector2 size)
    {
        if (size.x < 1 || size.y < 1) return;
        _lastSize = size;
        _layout = DebugMenuPageLayout.Build(size.x, size.y,
            _rows.Select(row => row.NaturalWidth).ToArray(), (i, width) => _rows[i].Measure(width));
        ShowPage(_layout.PageForItem(_state.FirstItem));
        _visibility.alpha = 1;
        _visibility.blocksRaycasts = _visibility.interactable = true;
    }

    private void ShowPage(int page)
    {
        if (_layout == null) return;
        _page = Mathf.Clamp(page, 0, _layout.PageStarts.Count - 1);
        foreach (DebugMenuPageLayout.Cell cell in _layout.Cells)
        {
            Row row = _rows[cell.Item];
            bool visible = cell.Page == _page;
            row.Rect.gameObject.SetActive(visible);
            if (visible) row.Apply(cell);
        }
        _state.Save(_layout.PageStarts[_page]);
        _previous.interactable = _page > 0;
        _next.interactable = _page + 1 < _layout.PageStarts.Count;
        _pageLabel.text = $"Page {_page + 1} / {_layout.PageStarts.Count}";
    }

    private sealed class Row
    {
        internal readonly RectTransform Rect;
        private readonly TextMeshProUGUI _name, _suffix;
        internal Row(RectTransform rect)
        {
            Rect = rect;
            var texts = rect.GetComponentsInChildren<TextMeshProUGUI>(true);
            _name = texts.FirstOrDefault();
            _suffix = texts.Skip(1).FirstOrDefault();
            foreach (TextMeshProUGUI text in texts)
            {
                text.enableAutoSizing = false;
                text.fontSize = Mathf.Min(text.fontSize, 26);
                text.enableWordWrapping = true;
                text.overflowMode = TextOverflowModes.Overflow;
                text.raycastTarget = false;
                text.margin = Vector4.zero;
            }
        }
        private bool HasSuffix => _suffix != null && !string.IsNullOrEmpty(_suffix.text);
        private float NameWidth => _name != null ? _name.GetPreferredValues(_name.text, float.PositiveInfinity, float.PositiveInfinity).x : 0;
        private float SuffixWidth => HasSuffix ? _suffix.GetPreferredValues(_suffix.text, float.PositiveInfinity, float.PositiveInfinity).x : 0;
        internal float NaturalWidth => 64 + NameWidth + (HasSuffix ? SuffixWidth + 14 : 0);
        private float ReservedSuffix(float width) => HasSuffix ? Mathf.Min(SuffixWidth + 4, Mathf.Max(40, (width - 64) * 0.35f)) : 0;
        internal float Measure(float width)
        {
            float suffix = ReservedSuffix(width);
            float available = Mathf.Max(1, width - 60 - (HasSuffix ? suffix + 12 : 0));
            float nameHeight = _name != null ? _name.GetPreferredValues(_name.text, available, float.PositiveInfinity).y : 30;
            float suffixHeight = HasSuffix ? _suffix.GetPreferredValues(_suffix.text, suffix, float.PositiveInfinity).y : 0;
            return Mathf.Max(44, Mathf.Max(nameHeight, suffixHeight) + 16);
        }
        internal void Apply(DebugMenuPageLayout.Cell cell)
        {
            Place(Rect, new Vector2(0, 1), new Vector2(cell.X, -cell.Y), new Vector2(cell.Width, cell.Height));
            float suffix = ReservedSuffix(cell.Width);
            if (_name != null)
            {
                Stretch(_name.rectTransform, new Vector2(48, 8), new Vector2(-12 - (HasSuffix ? suffix + 12 : 0), -8));
                _name.alignment = TextAlignmentOptions.MidlineLeft;
            }
            if (_suffix != null)
            {
                Stretch(_suffix.rectTransform, new Vector2(-12 - suffix, 8), new Vector2(-12, -8));
                _suffix.rectTransform.anchorMin = new Vector2(1, 0);
                _suffix.alignment = TextAlignmentOptions.MidlineRight;
            }
            // Only exceptional multi-paragraph inspector values need shrinking;
            // ordinary labels wrap at their measured, unchanged reading size.
            bool oversized = Measure(cell.Width) > cell.Height + 0.1f;
            if (oversized && Rect.GetComponent<Tooltip>() == null)
                Rect.gameObject.AddComponent<Tooltip>().SetText(
                    (_name != null ? _name.text : "") + (HasSuffix ? "\n" + _suffix.text : ""));
            foreach (TextMeshProUGUI text in new[] { _name, _suffix })
                if (text != null)
                {
                    text.enableAutoSizing = oversized;
                    text.fontSizeMin = 8;
                    text.fontSizeMax = 26;
                }
        }
    }

    private static RectTransform MakeRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }
    private static TextMeshProUGUI MakeText(string name, Transform parent, TextMeshProUGUI template, string value)
    {
        TextMeshProUGUI text = MakeRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        if (template != null)
        {
            text.font = template.font;
            text.fontSharedMaterial = template.fontSharedMaterial;
        }
        text.text = value;
        text.fontSize = 24;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }
    private static Button MakeNavigation(string label, Transform parent, TextMeshProUGUI template, Action callback)
    {
        RectTransform rect = MakeRect(label, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        Image native = Resources.Load<GameObject>("MiscPrefabs/ListUI/ListUIItemPrefab_Generic").GetComponent<Image>();
        if (native != null) CopyImage(native, image);
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        if (callback != null) button.onClick.AddListener(() => callback());
        TextMeshProUGUI text = MakeText("Label", rect, template, label);
        Stretch(text.rectTransform, new Vector2(6, 0), new Vector2(-6, 0));
        return button;
    }
    private static void CopyImage(Image source, Image target)
    {
        target.sprite = source.sprite;
        target.type = source.type;
        target.color = source.color;
        target.material = source.material;
        target.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
    }
    private static void Stretch(RectTransform rect, Vector2 lower, Vector2 upper)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = lower;
        rect.offsetMax = upper;
    }
    private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
    private sealed class RectSnapshot
    {
        private readonly Transform _parent;
        private readonly int _sibling;
        private readonly Vector2 _min, _max, _pivot, _size;
        private readonly Vector3 _position, _scale;
        private readonly Quaternion _rotation;
        internal RectSnapshot(RectTransform rect)
        {
            _parent = rect.parent; _sibling = rect.GetSiblingIndex();
            _min = rect.anchorMin; _max = rect.anchorMax; _pivot = rect.pivot;
            _size = rect.sizeDelta; _position = rect.anchoredPosition3D;
            _scale = rect.localScale; _rotation = rect.localRotation;
        }
        internal void Restore(RectTransform rect)
        {
            rect.SetParent(_parent, false); rect.SetSiblingIndex(_sibling);
            rect.anchorMin = _min; rect.anchorMax = _max; rect.pivot = _pivot;
            rect.sizeDelta = _size; rect.anchoredPosition3D = _position;
            rect.localScale = _scale; rect.localRotation = _rotation;
        }
    }
}
