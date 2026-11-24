using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Small building blocks for the screens of WP14 (the start screen, the end of the practice run,
// the director overlay), drawn in code in the HUD's colours like the rest of the interface
// (HudOverlay, DeckBar): there are no art files. View code only; no rule lives here.
public static class StudyUi
{
    public static readonly Color Shade = new Color(0.01f, 0.02f, 0.04f, 0.82f);

    // A screen-space canvas of its own, drawn above the HUD, with an event system when the scene
    // has none (the start scene).
    public static Canvas Overlay(string name, int order)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        CanvasScaler scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null)
        {
            var events = new GameObject("EventSystem");
            events.AddComponent<EventSystem>();
            events.AddComponent<InputSystemUIInputModule>();
        }
        return canvas;
    }

    // A full-screen shade that also swallows clicks meant for the board below.
    public static RectTransform Backdrop(Transform parent, Color color)
    {
        RectTransform r = Stretch("Backdrop", parent);
        Image image = r.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = true;
        return r;
    }

    public static RectTransform Stretch(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var r = (RectTransform)go.transform;
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
        return r;
    }

    // A box of the given size, its centre at `at` from the parent's centre.
    public static RectTransform Centered(string name, Transform parent, Vector2 at, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var r = (RectTransform)go.transform;
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = at;
        r.sizeDelta = size;
        return r;
    }

    public static void Card(RectTransform r, Color fill, Color edge)
    {
        Image outline = r.gameObject.AddComponent<Image>();
        outline.sprite = HudArt.Rounded(10);
        outline.type = Image.Type.Sliced;
        outline.color = edge;
        outline.raycastTarget = false;
        RectTransform inner = Stretch("Fill", r);
        inner.offsetMin = new Vector2(1.5f, 1.5f);
        inner.offsetMax = new Vector2(-1.5f, -1.5f);
        Image back = inner.gameObject.AddComponent<Image>();
        back.sprite = HudArt.Rounded(9);
        back.type = Image.Type.Sliced;
        back.color = fill;
        back.raycastTarget = false;
    }

    public static TextMeshProUGUI Label(Transform parent, string text, float size, Color color, Vector2 at, Vector2 box, bool bold = false, TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        RectTransform rect = Centered("Text", parent, at, box);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = align;
        label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.raycastTarget = false;
        return label;
    }

    public static Button Button(Transform parent, string text, Vector2 at, Vector2 size, Action onClick)
    {
        RectTransform r = Centered("Button", parent, at, size);
        Card(r, HudOverlay.Ink, HudOverlay.Teal);
        r.GetComponent<Image>().raycastTarget = true;
        Button button = r.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        button.targetGraphic = r.GetComponent<Image>();
        button.onClick.AddListener(() => onClick());
        Label(r, text, 22, HudOverlay.Bright, Vector2.zero, size, true);
        return button;
    }

    // A one-line text field. TMP_InputField needs a viewport, a text and a placeholder of its own.
    public static TMP_InputField Field(Transform parent, string placeholder, Vector2 at, Vector2 size)
    {
        RectTransform r = Centered("Field", parent, at, size);
        Card(r, HudOverlay.Ink, HudOverlay.Edge);
        r.GetComponent<Image>().raycastTarget = true;
        RectTransform area = Stretch("Text Area", r);
        area.offsetMin = new Vector2(16f, 6f);
        area.offsetMax = new Vector2(-16f, -6f);
        area.gameObject.AddComponent<RectMask2D>();

        TextMeshProUGUI hint = Label(area, placeholder, 26, HudOverlay.Dim, Vector2.zero, size, false, TextAlignmentOptions.Left);
        hint.rectTransform.anchorMin = Vector2.zero; hint.rectTransform.anchorMax = Vector2.one;
        hint.rectTransform.offsetMin = hint.rectTransform.offsetMax = Vector2.zero;
        hint.fontStyle = FontStyles.Italic;
        TextMeshProUGUI text = Label(area, "", 26, HudOverlay.Bright, Vector2.zero, size, true, TextAlignmentOptions.Left);
        text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        text.textWrappingMode = TextWrappingModes.NoWrap;

        TMP_InputField field = r.gameObject.AddComponent<TMP_InputField>();
        field.textViewport = area;
        field.textComponent = text;
        field.placeholder = hint;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.characterLimit = 8;
        field.targetGraphic = r.GetComponent<Image>();
        return field;
    }
}
