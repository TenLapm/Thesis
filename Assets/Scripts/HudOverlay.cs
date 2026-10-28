using System.Collections.Generic;
using Thesis.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The HUD of the approved mockup (2026-10-27): bars and icons where the screen had lines of
// text. Top left the core's health and the wave's progress; top centre the director's line;
// top right ore, power and research as badges over the base panel; under it the enemies of
// the wave as coloured chips, then the damage panel; bottom right pause, speed and the wave
// button (start, or restart while a wave runs). It hides the old texts and buttons it
// replaces, and calls the same methods they did. A view only, like the rest of the HUD.
//
// DeckBar adds it to the Canvas, so no scene holds it.
public class HudOverlay : MonoBehaviour
{
    public static readonly Color Panel = new Color(0.067f, 0.102f, 0.169f, 0.96f);
    public static readonly Color Edge = new Color(0.14f, 0.2f, 0.29f, 1f);
    public static readonly Color Ink = new Color(0.043f, 0.071f, 0.125f, 1f);
    public static readonly Color Track = new Color(0.118f, 0.161f, 0.231f, 1f);
    public static readonly Color Gold = new Color(0.96f, 0.72f, 0.18f, 1f);
    public static readonly Color Teal = new Color(0.18f, 0.83f, 0.75f, 1f);
    public static readonly Color Bright = new Color(0.9f, 0.91f, 0.92f, 1f);
    public static readonly Color Dim = new Color(0.58f, 0.64f, 0.72f, 1f);
    private static readonly Color Red = new Color(0.97f, 0.44f, 0.44f, 1f);
    private static readonly Color Amber = new Color(0.98f, 0.75f, 0.14f, 1f);

    private SimHost host;
    private PlayerBuilder builder;
    private CanvasDashboard dashboard;
    private TowerShopPanel shop;
    private DamagePanel damage;
    private GameSpeedController speed;
    private WaveSpawner waves;
    private RectTransform canvasRect;

    private Image hpFill, waveFill;
    private TextMeshProUGUI hpText, waveTitle, waveNote;
    private RectTransform banner;
    private TextMeshProUGUI bannerText;
    private TextMeshProUGUI oreText, incomeText, powerText, researchText;
    private RectTransform powerBadge;
    private RectTransform chipsCard;
    private TextMeshProUGUI chipsTitle;
    private readonly List<(GameObject root, Image swatch, TextMeshProUGUI label)> chips = new List<(GameObject, Image, TextMeshProUGUI)>();
    private readonly List<(Image outline, TextMeshProUGUI label)> speedButtons = new List<(Image, TextMeshProUGUI)>();
    private Image pauseOutline, pauseIcon, mainOutline, mainIcon, againOutline;
    private TextMeshProUGUI mainLabel, againLabel;
    private GameObject againRoot;

    void Start()
    {
        host = SimHost.Find();
        builder = FindFirstObjectByType<PlayerBuilder>();
        dashboard = GetComponent<CanvasDashboard>() ?? FindFirstObjectByType<CanvasDashboard>();
        shop = FindFirstObjectByType<TowerShopPanel>();
        damage = GetComponent<DamagePanel>();
        speed = FindFirstObjectByType<GameSpeedController>();
        waves = FindFirstObjectByType<WaveSpawner>();
        canvasRect = GetComponent<RectTransform>();
        if (shop != null) { shop.hudHeader = true; HudArt.Skin(shop.PanelRect, 12); }
        Build();
    }

    private void Build()
    {
        // Top left: the core and the wave.
        RectTransform status = Rect("Status", transform, new Vector2(0f, 1f), new Vector2(24f, -18f), new Vector2(420f, 80f));
        HudArt.AddIcon(status, "heart", Red, new Vector2(0f, 0f), 30f);
        Bar(status, new Vector2(42f, -9f), new Vector2(280f, 14f), Red, out hpFill);
        hpText = Label(status, "", 22, Bright, TextAlignmentOptions.Left, new Vector2(334f, -2f), new Vector2(90f, 28f), true);
        waveTitle = Label(status, "", 24, Gold, TextAlignmentOptions.Left, new Vector2(0f, -40f), new Vector2(130f, 30f), true);
        Bar(status, new Vector2(134f, -52f), new Vector2(188f, 8f), Gold, out waveFill);
        waveNote = Label(status, "", 16, Dim, TextAlignmentOptions.Left, new Vector2(334f, -44f), new Vector2(110f, 24f), false);

        // Top centre: the director's line, or what to do next.
        banner = Rect("Banner", transform, new Vector2(0.5f, 1f), new Vector2(-330f, -18f), new Vector2(660f, 46f));
        Card(banner, new Color(0.1f, 0.078f, 0.031f, 0.95f), new Color(0.63f, 0.38f, 0.03f, 1f));
        HudArt.AddIcon(banner, "flag", Amber, new Vector2(16f, -11f), 24f);
        bannerText = Label(banner, "", 19, new Color(0.99f, 0.9f, 0.54f, 1f), TextAlignmentOptions.Left, new Vector2(50f, -9f), new Vector2(596f, 28f), false);
        bannerText.textWrappingMode = TextWrappingModes.Normal; // a long line wraps (FitBanner)

        // Top right, over the base panel: ore, power, research.
        RectTransform ore = Badge("Ore", new Vector2(-270f, -112f), new Vector2(170f, 46f));
        HudArt.AddIcon(ore, "gem", Gold, new Vector2(12f, -10f), 26f);
        oreText = Label(ore, "", 28, Gold, TextAlignmentOptions.Left, new Vector2(46f, -6f), new Vector2(70f, 34f), true);
        incomeText = Label(ore, "", 16, new Color(0.53f, 0.94f, 0.67f, 1f), TextAlignmentOptions.Left, new Vector2(112f, -13f), new Vector2(56f, 22f), false);
        powerBadge = Badge("Power", new Vector2(-146f, -112f), new Vector2(116f, 46f));
        HudArt.AddIcon(powerBadge, "bolt", new Color(0.99f, 0.88f, 0.28f, 1f), new Vector2(12f, -11f), 24f);
        powerText = Label(powerBadge, "", 24, Bright, TextAlignmentOptions.Left, new Vector2(42f, -8f), new Vector2(70f, 30f), true);
        RectTransform research = Badge("Research", new Vector2(-28f, -112f), new Vector2(110f, 46f), true);
        HudArt.AddIcon(research, "flask", Teal, new Vector2(12f, -11f), 24f);
        researchText = Label(research, "", 20, Bright, TextAlignmentOptions.Left, new Vector2(42f, -10f), new Vector2(64f, 26f), true);

        // Under the base panel: the enemies of the wave.
        chipsCard = Rect("WaveChips", transform, new Vector2(1f, 1f), new Vector2(-444f, -300f), new Vector2(430f, 84f));
        Card(chipsCard, Panel, Edge);
        chipsTitle = Label(chipsCard, "", 15, Dim, TextAlignmentOptions.Left, new Vector2(14f, -8f), new Vector2(400f, 22f), true);
        for (int i = 0; i < 5; i++)
        {
            RectTransform chip = Rect("Chip", chipsCard, new Vector2(0f, 1f), new Vector2(14f + i * 82f, -36f), new Vector2(78f, 38f));
            Image back = chip.gameObject.AddComponent<Image>();
            back.sprite = HudArt.Rounded(8);
            back.type = Image.Type.Sliced;
            back.color = Ink;
            Image swatch = Square(chip, new Vector2(8f, -12f), 14f);
            TextMeshProUGUI label = Label(chip, "", 15, Bright, TextAlignmentOptions.Left, new Vector2(28f, -2f), new Vector2(50f, 34f), true);
            label.lineSpacing = -30f;
            chips.Add((chip.gameObject, swatch, label));
        }

        // Bottom right: pause, speed, the wave.
        RectTransform controls = Rect("Controls", transform, new Vector2(1f, 0f), new Vector2(-444f, 196f), new Vector2(430f, 186f));
        RectTransform pause = Button(controls, new Vector2(0f, 0f), new Vector2(64f, 48f), () => { if (speed != null) speed.TogglePause(); }, out pauseOutline);
        pauseIcon = HudArt.AddIcon(pause, "pause", Bright, new Vector2(20f, -12f), 24f);
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            RectTransform b = Button(controls, new Vector2(72f + i * 120f, 0f), new Vector2(112f, 48f), () => { if (speed != null) speed.SetSpeedIndex(index); }, out Image outline);
            string tag = speed != null && i < speed.speedLevels.Length ? Mathf.RoundToInt(speed.speedLevels[i]) + "×" : (i + 1) + "×";
            speedButtons.Add((outline, Label(b, tag, 22, Bright, TextAlignmentOptions.Center, new Vector2(0f, -9f), new Vector2(112f, 30f), true)));
        }
        RectTransform main = Button(controls, new Vector2(0f, -58f), new Vector2(430f, 56f), MainPressed, out mainOutline);
        mainIcon = HudArt.AddIcon(main, "play", Gold, new Vector2(112f, -15f), 26f);
        mainLabel = Label(main, "", 22, Gold, TextAlignmentOptions.Left, new Vector2(150f, -13f), new Vector2(270f, 30f), true);
        RectTransform again = Button(controls, new Vector2(0f, -124f), new Vector2(430f, 44f), () => { if (host != null) host.RetryWave(); }, out againOutline);
        HudArt.AddIcon(again, "restart", Amber, new Vector2(112f, -11f), 22f);
        againLabel = Label(again, "", 17, Amber, TextAlignmentOptions.Left, new Vector2(146f, -10f), new Vector2(270f, 26f), true);
        againRoot = again.gameObject;
    }

    private void MainPressed()
    {
        if (builder == null || host == null) return;
        if (builder.IsBuildPhase) { if (waves != null) waves.StartWave(); }
        else host.RetryWave();
    }

    void LateUpdate()
    {
        if (host == null || host.State == null || builder == null) return;
        SimState s = host.State;
        SimConfig c = host.Config;
        HideReplaced();

        // The core and the wave.
        float hp = Mathf.Clamp01(s.CoreHp / (float)Mathf.Max(1, c.CoreMaxHp));
        hpFill.rectTransform.sizeDelta = new Vector2(280f * hp, 14f);
        Set(hpText, Mathf.Max(0, s.CoreHp) + "/" + c.CoreMaxHp);
        bool build = builder.IsBuildPhase;
        if (build)
        {
            Set(waveTitle, "WAVE " + (s.WaveIndex + 1));
            waveFill.rectTransform.sizeDelta = new Vector2(0f, 8f);
            Set(waveNote, "build");
        }
        else
        {
            int total = s.CurrentPlan != null ? s.CurrentPlan.TotalAgents() : 0;
            int done = s.CurrentOutcome != null ? s.CurrentOutcome.Killed + s.CurrentOutcome.Leaked + s.CurrentOutcome.Removed : 0;
            Set(waveTitle, "WAVE " + s.WaveIndex);
            waveFill.rectTransform.sizeDelta = new Vector2(188f * (total > 0 ? Mathf.Clamp01(done / (float)total) : 0f), 8f);
            Set(waveNote, Mathf.Max(0, total - done) + " left");
        }

        // The director's line; in a build phase without one, what to do.
        WavePlan plan = build ? s.NextPlan : s.CurrentPlan;
        string line = plan != null ? RoughDirector.Line(plan.AnnouncementId) : null;
        if (line == null && build && !builder.ChoosingCore) line = "Build, then start the wave.";
        banner.gameObject.SetActive(line != null && !builder.ChoosingCore);
        if (line != null && line != bannerText.text) { Set(bannerText, line); FitBanner(); }

        // Ore, power, research.
        Set(oreText, s.BuildBudget.ToString("0"));
        Set(incomeText, "+" + (builder.BuildMenu ? builder.ExpectedIncome : c.StipendPerWave));
        powerBadge.gameObject.SetActive(builder.PowerRule);
        if (builder.PowerRule)
        {
            builder.PowerCount(out int powered, out int standing);
            Set(powerText, powered + "<color=#94a3b8>/" + standing + "</color>");
        }
        Set(researchText, "T" + s.ResearchLevel + "<color=#94a3b8>/3</color>");

        // The wave's enemies, by type.
        float chipsTop = 300f;
        if (shop != null && shop.PanelRect != null && canvasRect != null) chipsTop = canvasRect.rect.height - shop.PanelRect.offsetMin.y + 12f;
        chipsCard.anchoredPosition = new Vector2(-444f, -chipsTop);
        Set(chipsTitle, build ? "NEXT WAVE" : "IN THIS WAVE");
        var names = new List<string>();
        var counts = new List<int>();
        if (plan != null && plan.Groups != null)
            foreach (AgentGroup g in plan.Groups)
            {
                int at = names.IndexOf(g.Archetype);
                if (at < 0) { names.Add(g.Archetype); counts.Add(g.Count); } else counts[at] += g.Count;
            }
        chipsCard.gameObject.SetActive(names.Count > 0);
        for (int i = 0; i < chips.Count; i++)
        {
            bool shown = i < names.Count;
            if (chips[i].root.activeSelf != shown) chips[i].root.SetActive(shown);
            if (!shown) continue;
            chips[i].swatch.color = EnemyColor(names[i]);
            Set(chips[i].label, counts[i] + "\n<size=11><color=#94a3b8>" + names[i] + "</color></size>");
        }
        if (damage != null) damage.top = -(chipsTop + (names.Count > 0 ? 96f : 0f));

        // Pause, speed and the wave buttons.
        bool paused = speed != null && speed.IsPaused;
        pauseIcon.sprite = HudArt.Icon(paused ? "play" : "pause");
        pauseOutline.color = paused ? Teal : Edge;
        for (int i = 0; i < speedButtons.Count; i++)
        {
            bool on = speed != null && !paused && speed.SpeedIndex == i;
            speedButtons[i].outline.color = on ? Teal : Edge;
            speedButtons[i].label.color = on ? Teal : Dim;
        }
        bool canRestart = host.CanRetryWave;
        mainOutline.color = build ? Gold : Amber;
        mainIcon.sprite = HudArt.Icon(build ? "play" : "restart");
        mainIcon.gameObject.SetActive(build || canRestart);
        Set(mainLabel, build ? "START WAVE " + (s.WaveIndex + 1) : canRestart ? "RESTART WAVE " + host.RetryWaveNumber : "WAVE " + s.WaveIndex);
        bool again = build && canRestart && s.WaveIndex > 0;
        if (againRoot.activeSelf != again) againRoot.SetActive(again);
        if (again) Set(againLabel, "PLAY WAVE " + host.RetryWaveNumber + " AGAIN");
    }

    // What this HUD replaces: the old texts and the old Start, speed and pause buttons.
    private void HideReplaced()
    {
        if (dashboard == null) return;
        if (dashboard.healthText != null && dashboard.healthText.gameObject.activeSelf) dashboard.healthText.gameObject.SetActive(false);
        if (dashboard.waveText != null && dashboard.waveText.gameObject.activeSelf) dashboard.waveText.gameObject.SetActive(false);
        if (dashboard.startWaveButton != null)
        {
            GameObject group = dashboard.startWaveButton.transform.parent.gameObject;
            if (group.activeSelf) group.SetActive(false);
        }
    }

    public static Color EnemyColor(string archetype)
    {
        switch (archetype)
        {
            case "swarm": return new Color(0.94f, 0.67f, 0.99f);
            case "sapper": return new Color(0.98f, 0.57f, 0.24f);
            case "flyer": return new Color(0.38f, 0.65f, 0.98f);
            case "brute": return new Color(0.86f, 0.15f, 0.15f);
            case "runner": return new Color(0.97f, 0.98f, 0.99f);
            case "armored": return new Color(0.58f, 0.64f, 0.72f);
            case "ember": return new Color(0.98f, 0.45f, 0.09f);
            case "frostborn": return new Color(0.4f, 0.91f, 0.98f);
            case "bats": return new Color(0.65f, 0.55f, 0.98f);
            case "raider": return new Color(0.29f, 0.87f, 0.5f);
            case "siege": return new Color(0.85f, 0.64f, 0.15f);
            default: return new Color(0.91f, 0.47f, 0.98f); // the plain walker, the colour of its route
        }
    }

    // --- building blocks ---

    private RectTransform Badge(string name, Vector2 at, Vector2 size, bool rightmost = false)
    {
        RectTransform r = Rect(name, transform, new Vector2(1f, 1f), at, size);
        r.pivot = new Vector2(1f, 1f);
        r.anchoredPosition = at;
        Card(r, Ink, Edge);
        return r;
    }

    private static void Card(RectTransform r, Color fill, Color edge)
    {
        Image outline = r.gameObject.AddComponent<Image>();
        outline.sprite = HudArt.Rounded(10);
        outline.type = Image.Type.Sliced;
        outline.color = edge;
        outline.raycastTarget = false;
        RectTransform inner = Rect("Fill", r, new Vector2(0f, 0f), Vector2.zero, Vector2.zero);
        inner.anchorMin = Vector2.zero;
        inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(1.5f, 1.5f);
        inner.offsetMax = new Vector2(-1.5f, -1.5f);
        Image back = inner.gameObject.AddComponent<Image>();
        back.sprite = HudArt.Rounded(9);
        back.type = Image.Type.Sliced;
        back.color = fill;
        back.raycastTarget = false;
    }

    private static RectTransform Button(Transform parent, Vector2 at, Vector2 size, System.Action onClick, out Image outline)
    {
        RectTransform r = Rect("Button", parent, new Vector2(0f, 1f), at, size);
        Card(r, Ink, Edge);
        outline = r.GetComponent<Image>();
        outline.raycastTarget = true;
        Button button = r.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => onClick());
        return r;
    }

    private static void Bar(Transform parent, Vector2 at, Vector2 size, Color color, out Image fill)
    {
        RectTransform track = Rect("Track", parent, new Vector2(0f, 1f), at, size);
        Image t = track.gameObject.AddComponent<Image>();
        t.sprite = HudArt.Rounded(4);
        t.type = Image.Type.Sliced;
        t.color = Track;
        t.raycastTarget = false;
        RectTransform f = Rect("Fill", track, new Vector2(0f, 1f), Vector2.zero, size);
        fill = f.gameObject.AddComponent<Image>();
        fill.sprite = HudArt.Rounded(4);
        fill.type = Image.Type.Sliced;
        fill.color = color;
        fill.raycastTarget = false;
    }

    private static Image Square(Transform parent, Vector2 at, float size)
    {
        RectTransform r = Rect("Swatch", parent, new Vector2(0f, 1f), at, new Vector2(size, size));
        Image image = r.gameObject.AddComponent<Image>();
        image.sprite = HudArt.Rounded(3);
        image.type = Image.Type.Sliced;
        image.raycastTarget = false;
        return image;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 at, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var r = (RectTransform)go.transform;
        r.anchorMin = r.anchorMax = anchor;
        r.pivot = new Vector2(0f, 1f);
        r.anchoredPosition = at;
        r.sizeDelta = size;
        return r;
    }

    private static TextMeshProUGUI Label(Transform parent, string text, float size, Color color, TextAlignmentOptions align, Vector2 at, Vector2 box, bool bold)
    {
        RectTransform rect = Rect("Text", parent, new Vector2(0f, 1f), at, box);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = align;
        label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        return label;
    }

    // The banner's card fits its line (2026-10-28, the student: the director's warnings ran
    // over the outline): as wide as the text up to BannerMaxText, wrapped and taller beyond it.
    private const float BannerMaxText = 760f;
    private void FitBanner()
    {
        float width = Mathf.Min(BannerMaxText, Mathf.Max(200f, bannerText.GetPreferredValues(bannerText.text).x));
        float height = bannerText.GetPreferredValues(bannerText.text, width, 0f).y;
        bannerText.rectTransform.sizeDelta = new Vector2(width, height);
        banner.sizeDelta = new Vector2(width + 66f, Mathf.Max(46f, height + 18f));
        banner.anchoredPosition = new Vector2(-banner.sizeDelta.x * 0.5f, -18f);
    }

    private static void Set(TextMeshProUGUI label, string text) { if (label.text != text) label.text = text; }
}
