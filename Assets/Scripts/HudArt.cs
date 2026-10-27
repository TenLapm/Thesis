using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The HUD's shapes, drawn by code (2026-10-27, the student approved a mockup of a HUD with
// icons and rounded panels in place of lines of text): rounded panels as nine-sliced
// sprites, and a few flat icons, each made once from simple shapes (circles, rings,
// polygons, thick lines) at 64 pixels with 4 x 4 samples a pixel. White, so an Image's
// colour tints them. No art files, so nothing to license or import.
public static class HudArt
{
    private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    // A rounded rectangle for panels, cards and buttons: slice it (Image.Type.Sliced).
    public static Sprite Rounded(int radius = 10)
    {
        string key = "round" + radius;
        if (cache.TryGetValue(key, out Sprite s)) return s;
        int size = radius * 2 + 4;
        float r = radius;
        Texture2D tex = Draw(size, (x, y) =>
        {
            float px = x * size, py = y * size;
            float cx = Mathf.Clamp(px, r, size - r), cy = Mathf.Clamp(py, r, size - r);
            float dx = px - cx, dy = py - cy;
            return dx * dx + dy * dy <= r * r;
        });
        s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r + 1, r + 1, r + 1, r + 1));
        cache[key] = s;
        return s;
    }

    // Every Image under `root` that has no sprite becomes a rounded one. The panels of the
    // bar are an outline Image with a fill Image inset two pixels: both rounded, they read
    // as a rounded card with a border.
    public static void Skin(Transform root, int radius = 8)
    {
        if (root == null) return;
        foreach (Image image in root.GetComponentsInChildren<Image>(true))
        {
            if (image.sprite != null) continue;
            image.sprite = Rounded(radius);
            image.type = Image.Type.Sliced;
        }
    }

    public static Sprite Icon(string name)
    {
        if (cache.TryGetValue(name, out Sprite s)) return s;
        Func<float, float, bool> shape = Shape(name);
        Texture2D tex = Draw(64, shape);
        s = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 100f);
        cache[name] = s;
        return s;
    }

    // An Image of an icon, a child of `parent`, top-left anchored at `at`.
    public static Image AddIcon(Transform parent, string name, Color color, Vector2 at, float size)
    {
        var go = new GameObject("Icon " + name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = at;
        rect.sizeDelta = new Vector2(size, size);
        Image image = go.AddComponent<Image>();
        image.sprite = Icon(name);
        image.color = color;
        image.raycastTarget = false;
        image.preserveAspect = true;
        return image;
    }

    private static Func<float, float, bool> Shape(string name)
    {
        switch (name)
        {
            case "gem": return Poly(new[] { 0.5f, 0.95f, 0.92f, 0.62f, 0.72f, 0.06f, 0.28f, 0.06f, 0.08f, 0.62f });
            case "heart": return Any(Circle(0.31f, 0.64f, 0.23f), Circle(0.69f, 0.64f, 0.23f), Poly(new[] { 0.1f, 0.6f, 0.9f, 0.6f, 0.5f, 0.08f }));
            case "bolt": return Poly(new[] { 0.6f, 0.96f, 0.18f, 0.42f, 0.46f, 0.42f, 0.38f, 0.04f, 0.82f, 0.6f, 0.54f, 0.6f });
            case "pause": return Any(Box(0.22f, 0.14f, 0.42f, 0.86f), Box(0.58f, 0.14f, 0.78f, 0.86f));
            case "play": return Poly(new[] { 0.24f, 0.1f, 0.24f, 0.9f, 0.88f, 0.5f });
            case "restart": return Any(Arc(0.5f, 0.48f, 0.27f, 0.37f, 110f, 420f), Poly(new[] { 0.08f, 0.62f, 0.38f, 0.66f, 0.26f, 0.9f }));
            case "target": return Any(Ring(0.5f, 0.5f, 0.24f, 0.32f), Circle(0.5f, 0.5f, 0.07f), Box(0.46f, 0.0f, 0.54f, 0.22f), Box(0.46f, 0.78f, 0.54f, 1f), Box(0f, 0.46f, 0.22f, 0.54f), Box(0.78f, 0.46f, 1f, 0.54f));
            case "clock": return Any(Ring(0.5f, 0.5f, 0.32f, 0.42f), Line(0.5f, 0.5f, 0.5f, 0.78f, 0.05f), Line(0.5f, 0.5f, 0.7f, 0.42f, 0.05f));
            case "range": return Any(Ring(0.5f, 0.5f, 0.36f, 0.45f), Ring(0.5f, 0.5f, 0.14f, 0.23f), Circle(0.5f, 0.5f, 0.05f));
            case "sword": return Any(Line(0.3f, 0.3f, 0.88f, 0.88f, 0.075f), Line(0.16f, 0.44f, 0.44f, 0.16f, 0.06f), Line(0.1f, 0.1f, 0.26f, 0.26f, 0.06f));
            case "up": return Any(Poly(new[] { 0.5f, 0.92f, 0.12f, 0.5f, 0.88f, 0.5f }), Box(0.37f, 0.08f, 0.63f, 0.55f));
            case "move": return Any(Line(0.5f, 0.14f, 0.5f, 0.86f, 0.045f), Line(0.14f, 0.5f, 0.86f, 0.5f, 0.045f),
                                    Poly(new[] { 0.5f, 0.98f, 0.36f, 0.8f, 0.64f, 0.8f }), Poly(new[] { 0.5f, 0.02f, 0.36f, 0.2f, 0.64f, 0.2f }),
                                    Poly(new[] { 0.02f, 0.5f, 0.2f, 0.36f, 0.2f, 0.64f }), Poly(new[] { 0.98f, 0.5f, 0.8f, 0.36f, 0.8f, 0.64f }));
            case "tower": return Any(Box(0.24f, 0.06f, 0.76f, 0.64f), Box(0.16f, 0.6f, 0.36f, 0.9f), Box(0.42f, 0.6f, 0.58f, 0.9f), Box(0.64f, 0.6f, 0.84f, 0.9f));
            case "flag": return Any(Line(0.24f, 0.06f, 0.24f, 0.94f, 0.05f), Poly(new[] { 0.26f, 0.94f, 0.84f, 0.76f, 0.26f, 0.56f }));
            case "lock": return Any(Box(0.2f, 0.06f, 0.8f, 0.56f), (x, y) => y >= 0.5f && Ring(0.5f, 0.54f, 0.16f, 0.26f)(x, y));
            case "ring": return Ring(0.5f, 0.5f, 0.3f, 0.44f);
            case "flask": return Any(Box(0.4f, 0.6f, 0.6f, 0.92f), Poly(new[] { 0.4f, 0.62f, 0.6f, 0.62f, 0.88f, 0.08f, 0.12f, 0.08f }));
            case "wrench": return Any(Line(0.22f, 0.22f, 0.66f, 0.66f, 0.08f), Arc(0.72f, 0.72f, 0.1f, 0.22f, 80f, 370f));
            default: return Circle(0.5f, 0.5f, 0.44f); // "dot"
        }
    }

    private static Texture2D Draw(int size, Func<float, float, bool> inside)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
        var pixels = new Color32[size * size];
        const int n = 4;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < n; sy++)
                    for (int sx = 0; sx < n; sx++)
                        if (inside((x + (sx + 0.5f) / n) / size, (y + (sy + 0.5f) / n) / size)) hits++;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * hits / (n * n)));
            }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    private static Func<float, float, bool> Any(params Func<float, float, bool>[] parts)
    {
        return (x, y) => { foreach (var p in parts) if (p(x, y)) return true; return false; };
    }

    private static Func<float, float, bool> Circle(float cx, float cy, float r) { return (x, y) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r; }

    private static Func<float, float, bool> Ring(float cx, float cy, float r1, float r2)
    {
        return (x, y) => { float d = (x - cx) * (x - cx) + (y - cy) * (y - cy); return d >= r1 * r1 && d <= r2 * r2; };
    }

    // A ring between two angles, in degrees anticlockwise from the right; `to` may pass 360.
    private static Func<float, float, bool> Arc(float cx, float cy, float r1, float r2, float from, float to)
    {
        Func<float, float, bool> ring = Ring(cx, cy, r1, r2);
        return (x, y) =>
        {
            if (!ring(x, y)) return false;
            float a = Mathf.Atan2(y - cy, x - cx) * Mathf.Rad2Deg;
            if (a < 0f) a += 360f;
            return (a >= from && a <= to) || (a + 360f >= from && a + 360f <= to);
        };
    }

    private static Func<float, float, bool> Box(float x0, float y0, float x1, float y1) { return (x, y) => x >= x0 && x <= x1 && y >= y0 && y <= y1; }

    // A thick line with round ends.
    private static Func<float, float, bool> Line(float ax, float ay, float bx, float by, float half)
    {
        return (x, y) =>
        {
            float vx = bx - ax, vy = by - ay;
            float t = Mathf.Clamp01(((x - ax) * vx + (y - ay) * vy) / (vx * vx + vy * vy));
            float dx = x - (ax + t * vx), dy = y - (ay + t * vy);
            return dx * dx + dy * dy <= half * half;
        };
    }

    // Even-odd fill of a polygon given as x, y pairs.
    private static Func<float, float, bool> Poly(float[] p)
    {
        return (x, y) =>
        {
            bool inside = false;
            int n = p.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = p[2 * i], yi = p[2 * i + 1], xj = p[2 * j], yj = p[2 * j + 1];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
            }
            return inside;
        };
    }
}
