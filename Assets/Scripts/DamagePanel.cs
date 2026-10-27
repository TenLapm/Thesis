using System.Collections.Generic;
using Thesis.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// The damage panel (2026-10-27, the student: the player should see what each tower does).
// One bar a tower, longest first, for the wave running or just run, or for the whole game;
// Tab switches between the two and off. When a wave ends, its three best towers are shown
// for a few seconds at the top of the screen. A view only: every number is the simulation's
// (TowerState.WaveDamage, WaveKills, DamageDealt, Kills).
//
// DeckBar adds it to the Canvas, so no scene holds it.
public class DamagePanel : MonoBehaviour
{
    private const int Rows = 8;
    private const float Width = 430f;

    // Where its top edge stands, from the top of the Canvas; HudOverlay puts it under the wave's chips.
    [System.NonSerialized] public float top = -262f;
    private const float RowHeight = 30f;
    private const float BarWidth = 150f;

    private static readonly Color Back = new Color(0.04f, 0.06f, 0.1f, 0.9f);
    private static readonly Color Track = new Color(0.14f, 0.19f, 0.29f, 1f);
    private static readonly Color Gold = new Color(0.98f, 0.75f, 0.14f, 1f);
    private static readonly Color Dim = new Color(0.58f, 0.64f, 0.72f, 1f);
    private static readonly Color Bright = new Color(0.9f, 0.91f, 0.92f, 1f);

    private enum Mode { Wave, Game, Off }
    private Mode mode = Mode.Wave;

    private SimHost host;
    private RectTransform panel, toast;
    private TextMeshProUGUI title, footer, toastText;
    private float toastUntil;

    private class Row
    {
        public GameObject root;
        public Image swatch, bar;
        public TextMeshProUGUI name, value;
    }
    private readonly List<Row> rows = new List<Row>();
    private readonly List<TowerState> sorted = new List<TowerState>();

    void Start()
    {
        host = SimHost.Find();
        if (host != null) host.SimEventRaised += OnSimEvent;
        Build();
    }

    void OnDestroy()
    {
        if (host != null) host.SimEventRaised -= OnSimEvent;
    }

    private void Build()
    {
        panel = Rect("DamagePanel", transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-24f, -262f);
        panel.sizeDelta = new Vector2(Width, 56f + Rows * RowHeight + 30f);
        panel.gameObject.AddComponent<Image>().color = Back;
        panel.GetComponent<Image>().sprite = HudArt.Rounded(10);
        panel.GetComponent<Image>().type = Image.Type.Sliced;
        Button toggle = panel.gameObject.AddComponent<Button>();
        toggle.transition = Selectable.Transition.None;
        toggle.onClick.AddListener(Cycle);

        title = Label(panel, "", 15, Dim, TextAlignmentOptions.Left, new Vector2(14f, -10f), new Vector2(Width - 28f, 24f), true);
        for (int i = 0; i < Rows; i++)
        {
            var row = new Row();
            RectTransform r = Rect("Row", panel);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(14f, -42f - i * RowHeight);
            r.sizeDelta = new Vector2(Width - 28f, RowHeight - 4f);
            row.root = r.gameObject;
            row.swatch = Box(r, new Vector2(0f, -6f), new Vector2(14f, 14f), Color.white);
            row.name = Label(r, "", 14, Bright, TextAlignmentOptions.Left, new Vector2(22f, -1f), new Vector2(124f, 24f), false);
            Box(r, new Vector2(150f, -8f), new Vector2(BarWidth, 10f), Track);
            row.bar = Box(r, new Vector2(150f, -8f), new Vector2(BarWidth, 10f), Gold);
            row.value = Label(r, "", 13, Dim, TextAlignmentOptions.Right, new Vector2(Width - 28f - 100f, -1f), new Vector2(100f, 24f), false);
            rows.Add(row);
        }
        footer = Label(panel, "", 13, Dim, TextAlignmentOptions.Left, new Vector2(14f, -46f - Rows * RowHeight), new Vector2(Width - 28f, 22f), false);

        toast = Rect("TopTowers", transform);
        toast.anchorMin = toast.anchorMax = toast.pivot = new Vector2(0.5f, 1f);
        toast.anchoredPosition = new Vector2(0f, -150f);
        toast.sizeDelta = new Vector2(620f, 40f);
        toast.gameObject.AddComponent<Image>().color = Back;
        toast.GetComponent<Image>().sprite = HudArt.Rounded(10);
        toast.GetComponent<Image>().type = Image.Type.Sliced;
        toastText = Label(toast, "", 17, Bright, TextAlignmentOptions.Center, new Vector2(10f, -7f), new Vector2(600f, 26f), false);
        toast.gameObject.SetActive(false);
    }

    private void Cycle() { mode = mode == Mode.Wave ? Mode.Game : mode == Mode.Game ? Mode.Off : Mode.Wave; }

    void Update()
    {
        if (host == null || host.State == null) return;
        if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame) Cycle();
        if (toast.gameObject.activeSelf && Time.unscaledTime > toastUntil) toast.gameObject.SetActive(false);

        bool on = mode != Mode.Off;
        panel.anchoredPosition = new Vector2(-14f, top);
        // Off, it shrinks to its title, so it can be clicked back on.
        // As tall as its rows (at least one), plus the title and the line under them.
        int rowsShown = Mathf.Clamp(CountShown(host.State), 1, Rows);
        panel.sizeDelta = new Vector2(Width, on ? 56f + rowsShown * RowHeight + 30f : 40f);
        footer.rectTransform.anchoredPosition = new Vector2(14f, -46f - rowsShown * RowHeight);
        bool wave = mode == Mode.Wave;
        SimState s = host.State;
        Set(title, (mode == Mode.Off ? "DAMAGE  (Tab)" : wave ? "DAMAGE  ·  WAVE " + Mathf.Max(1, s.WaveIndex) : "DAMAGE  ·  WHOLE GAME") + (on ? "   <color=#94a3b8><size=12>Tab: switch</size></color>" : ""));

        sorted.Clear();
        float total = 0f;
        var byType = new float[DamageTypes.Count];
        foreach (TowerState t in s.Towers)
        {
            if (t.Def.PowerCapacity > 0) continue;
            float d = wave ? t.WaveDamage : t.DamageDealt;
            if (!t.IsAlive && d <= 0f) continue;
            sorted.Add(t);
            total += d;
            byType[(int)t.Def.DamageType] += d;
        }
        sorted.Sort((a, b) =>
        {
            float da = wave ? a.WaveDamage : a.DamageDealt, db = wave ? b.WaveDamage : b.DamageDealt;
            return da != db ? db.CompareTo(da) : a.Id.CompareTo(b.Id);
        });
        float best = sorted.Count > 0 ? Mathf.Max(1f, wave ? sorted[0].WaveDamage : sorted[0].DamageDealt) : 1f;

        for (int i = 0; i < rows.Count; i++)
        {
            Row row = rows[i];
            bool shown = on && i < sorted.Count;
            if (row.root.activeSelf != shown) row.root.SetActive(shown);
            if (!shown) continue;
            TowerState t = sorted[i];
            float d = wave ? t.WaveDamage : t.DamageDealt;
            int kills = wave ? t.WaveKills : t.Kills;
            row.swatch.color = TowerView.ColorOfTower(t.Def);
            Set(row.name, Title(t.Def.Id) + (t.UpgradeCount > 0 ? " " + new string('+', t.UpgradeCount) : "") + (t.IsAlive ? "" : " (gone)"));
            row.bar.rectTransform.sizeDelta = new Vector2(BarWidth * Mathf.Clamp01(d / best), 10f);
            row.bar.color = TowerView.ColorOfTower(t.Def);
            Set(row.value, Number(d) + "  <color=#94a3b8>" + (total > 0f ? Mathf.RoundToInt(100f * d / total) : 0) + "%  " + kills + "k</color>");
        }
        footer.gameObject.SetActive(on);
        if (on)
        {
            string split = total > 0f
                ? "physical " + Pct(byType[(int)DamageType.Physical], total) + "  fire " + Pct(byType[(int)DamageType.Fire], total) + "  frost " + Pct(byType[(int)DamageType.Frost], total)
                : "no damage yet";
            if (wave && s.LastOutcome != null && s.LastOutcome.WaveIndex == s.WaveIndex) split += "   ·  leaked " + s.LastOutcome.Leaked;
            Set(footer, split);
        }
    }

    private static int CountShown(SimState s)
    {
        int n = 0;
        foreach (TowerState t in s.Towers) if (t.Def.PowerCapacity == 0 && (t.IsAlive || t.DamageDealt > 0f)) n++;
        return n;
    }

    // A wave ended: its three best towers, for a few seconds.
    private void OnSimEvent(SimEvent e)
    {
        if (e.Kind != SimEventKind.WaveResolved || host == null) return;
        var best = new List<TowerState>();
        foreach (TowerState t in host.State.Towers) if (t.WaveDamage > 0f) best.Add(t);
        best.Sort((a, b) => a.WaveDamage != b.WaveDamage ? b.WaveDamage.CompareTo(a.WaveDamage) : a.Id.CompareTo(b.Id));
        if (best.Count == 0) return;
        var text = new System.Text.StringBuilder("<color=#fbbf24>Wave " + host.State.WaveIndex + " best:</color>  ");
        for (int i = 0; i < best.Count && i < 3; i++)
        {
            if (i > 0) text.Append("   ·   ");
            text.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(TowerView.ColorOfTower(best[i].Def))).Append(">■</color> ")
                .Append(Title(best[i].Def.Id)).Append(' ').Append(Number(best[i].WaveDamage));
        }
        Set(toastText, text.ToString());
        toast.gameObject.SetActive(true);
        toastUntil = Time.unscaledTime + 5f;
    }

    private static string Number(float v) { return v >= 10000f ? (v / 1000f).ToString("0") + "k" : v >= 1000f ? (v / 1000f).ToString("0.0") + "k" : v.ToString("0"); }

    private static string Pct(float part, float total) { return Mathf.RoundToInt(100f * part / total) + "%"; }

    private static string Title(string id) { return string.IsNullOrEmpty(id) ? "" : char.ToUpperInvariant(id[0]) + id.Substring(1); }

    private static void Set(TextMeshProUGUI label, string text) { if (label.text != text) label.text = text; }

    private static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image Box(Transform parent, Vector2 at, Vector2 size, Color color)
    {
        RectTransform r = Rect("Box", parent);
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
        r.anchoredPosition = at;
        r.sizeDelta = size;
        Image image = r.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI Label(Transform parent, string text, float size, Color color, TextAlignmentOptions align, Vector2 at, Vector2 box, bool bold)
    {
        RectTransform rect = Rect("Text", parent);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = at;
        rect.sizeDelta = box;
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = align;
        label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        return label;
    }
}
