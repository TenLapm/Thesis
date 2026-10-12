using System.Collections.Generic;
using Thesis.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// The right-hand panel of the build screen (WP-C4, GDD §9.2): the shop's offers as
// cards, the two tools (Wall, Sell), the key to the route lines, and the tooltip that
// follows the mouse over the board.
//
// It builds its own UI under the scene's Canvas when the game starts, so the layout is
// in one readable place and not spread over a hundred scene objects. Functional, not
// styled: the pass over the whole screen is the second semester's.
//
// The panel shows state and forwards clicks. A card calls PlayerBuilder.SelectOffer;
// whether an offer can be afforded, is bought, or may be placed is read from the
// simulation every frame and decided by the simulation when the command arrives.
public class TowerShopPanel : MonoBehaviour
{
    public PlayerBuilder playerBuilder;
    public GhostPreviewer ghostPreviewer;
    public PathPreviewer pathPreviewer;
    public SimHost simHost;

    [Header("Layout, in the Canvas's reference pixels")]
    public float width = 430f;
    public float topInset = 96f;
    public float bottomInset = 196f;

    private static readonly Color PanelColor = new Color(0.07f, 0.1f, 0.17f, 0.94f);
    private static readonly Color CardColor = new Color(0.06f, 0.09f, 0.15f, 1f);
    private static readonly Color CardSelected = new Color(0.06f, 0.2f, 0.18f, 1f);
    private static readonly Color Edge = new Color(0.14f, 0.19f, 0.29f, 1f);
    private static readonly Color Teal = new Color(0.18f, 0.83f, 0.75f, 1f);
    private static readonly Color Gold = new Color(0.98f, 0.75f, 0.14f, 1f);
    private static readonly Color Red = new Color(0.97f, 0.44f, 0.44f, 1f);
    private static readonly Color Dim = new Color(0.58f, 0.64f, 0.72f, 1f);
    private static readonly Color Bright = new Color(0.9f, 0.91f, 0.92f, 1f);

    private class Card
    {
        public GameObject root;
        public Image background, outline, icon;
        public TextMeshProUGUI name, type, tags, price, state;
        public CanvasGroup group;
    }

    private class LegendRow
    {
        public GameObject root;
        public MovementClass movement;
    }

    private readonly List<Card> cards = new List<Card>();
    private readonly List<LegendRow> legend = new List<LegendRow>();
    private RectTransform panel;
    private RectTransform cardArea;
    private RectTransform lower;
    private TextMeshProUGUI budget;
    private TextMeshProUGUI legendNote;
    private Image wallButton, sellButton;
    private TextMeshProUGUI wallNote, sellNote;
    private CanvasGroup sellGroup;
    private RectTransform tooltip;
    private TextMeshProUGUI tooltipText;
    private RectTransform canvasRect;

    private const float CardHeight = 100f;
    private const float CardGap = 10f;

    private SimHost Host => simHost != null ? simHost : (simHost = SimHost.Find());

    void Start()
    {
        canvasRect = GetComponent<RectTransform>();
        Build();
    }

    // ------------------------------------------------------------------ building the UI

    private void Build()
    {
        panel = Rect("TowerShopPanel", transform);
        panel.anchorMin = new Vector2(1f, 0f);
        panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(1f, 0.5f);
        panel.offsetMin = new Vector2(-width - 14f, bottomInset);
        panel.offsetMax = new Vector2(-14f, -topInset);
        panel.gameObject.AddComponent<Image>().color = PanelColor;
        // Behind everything else on the Canvas, so the game-over screen covers it.
        panel.SetAsFirstSibling();

        Label(panel, "TOWER SHOP", 26, Bright, TextAlignmentOptions.TopLeft, new Vector2(18f, -14f), new Vector2(260f, 34f), true);
        Label(panel, "new offers after every wave", 17, Dim, TextAlignmentOptions.TopLeft, new Vector2(18f, -46f), new Vector2(300f, 24f), false);
        Label(panel, "Budget", 17, Dim, TextAlignmentOptions.TopRight, new Vector2(-18f, -12f), new Vector2(140f, 24f), false, true);
        budget = Label(panel, "", 30, Gold, TextAlignmentOptions.TopRight, new Vector2(-18f, -32f), new Vector2(160f, 38f), true, true);

        cardArea = Rect("Cards", panel);
        Top(cardArea, 80f, 0f);

        lower = Rect("Lower", panel);
        Top(lower, 80f, 250f);

        Label(lower, "TOOLS", 17, Dim, TextAlignmentOptions.TopLeft, new Vector2(18f, -6f), new Vector2(200f, 24f), true);
        float half = (width - 36f - 10f) / 2f;
        wallButton = ToolButton("WALL", new Vector2(18f, -32f), half, out wallNote, () => playerBuilder.SelectWall());
        sellButton = ToolButton("SELL", new Vector2(18f + half + 10f, -32f), half, out sellNote, () => playerBuilder.SelectSell());
        sellGroup = sellButton.gameObject.AddComponent<CanvasGroup>();

        Label(lower, "ROUTES", 17, Dim, TextAlignmentOptions.TopLeft, new Vector2(18f, -126f), new Vector2(120f, 24f), true);
        legendNote = Label(lower, "", 15, Dim, TextAlignmentOptions.TopRight, new Vector2(-18f, -127f), new Vector2(290f, 24f), false, true);
        Color walker = pathPreviewer != null ? pathPreviewer.walkerColor : Color.magenta;
        Color sapper = pathPreviewer != null ? pathPreviewer.sapperColor : new Color(1f, 0.6f, 0.2f);
        Color flyer = pathPreviewer != null ? pathPreviewer.flyerColor : Color.cyan;
        LegendLine(MovementClass.Ground, walker, "Walkers", "round your walls");
        LegendLine(MovementClass.Sapper, sapper, "Sappers", "through thin walls");
        LegendLine(MovementClass.Flying, flyer, "Flyers", "straight, over everything");

        tooltip = Rect("BuildTooltip", transform);
        tooltip.anchorMin = tooltip.anchorMax = new Vector2(0.5f, 0.5f);
        tooltip.pivot = new Vector2(0f, 1f);
        tooltip.sizeDelta = new Vector2(360f, 40f);
        Image tipBack = tooltip.gameObject.AddComponent<Image>();
        tipBack.color = new Color(0.04f, 0.06f, 0.1f, 0.92f);
        tipBack.raycastTarget = false;
        tooltipText = Label(tooltip, "", 20, Bright, TextAlignmentOptions.MidlineLeft, new Vector2(12f, 0f), new Vector2(340f, 40f), false);
        tooltipText.rectTransform.anchorMin = new Vector2(0f, 0f);
        tooltipText.rectTransform.anchorMax = new Vector2(1f, 1f);
        tooltipText.rectTransform.offsetMin = new Vector2(12f, 0f);
        tooltipText.rectTransform.offsetMax = new Vector2(-12f, 0f);
        tooltip.gameObject.SetActive(false);
    }

    private Card AddCard(int slot)
    {
        var card = new Card();
        RectTransform root = Rect("Offer " + (slot + 1), cardArea);
        root.anchorMin = new Vector2(0f, 1f);
        root.anchorMax = new Vector2(1f, 1f);
        root.pivot = new Vector2(0.5f, 1f);
        root.sizeDelta = new Vector2(-28f, CardHeight);
        root.anchoredPosition = new Vector2(0f, -slot * (CardHeight + CardGap));
        card.root = root.gameObject;
        card.group = card.root.AddComponent<CanvasGroup>();

        card.outline = card.root.AddComponent<Image>();
        card.outline.color = Edge;
        RectTransform inner = Rect("Fill", root);
        inner.anchorMin = Vector2.zero;
        inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(2f, 2f);
        inner.offsetMax = new Vector2(-2f, -2f);
        card.background = inner.gameObject.AddComponent<Image>();
        card.background.color = CardColor;
        card.background.raycastTarget = false;

        RectTransform icon = Rect("Icon", root);
        icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
        icon.pivot = new Vector2(0.5f, 0.5f);
        icon.anchoredPosition = new Vector2(44f, 0f);
        icon.sizeDelta = new Vector2(40f, 40f);
        card.icon = icon.gameObject.AddComponent<Image>();
        card.icon.raycastTarget = false;

        card.name = Label(root, "", 25, Bright, TextAlignmentOptions.TopLeft, new Vector2(82f, -10f), new Vector2(220f, 32f), true);
        card.type = Label(root, "", 18, Bright, TextAlignmentOptions.TopLeft, new Vector2(82f, -42f), new Vector2(220f, 24f), true);
        card.tags = Label(root, "", 16, Dim, TextAlignmentOptions.TopLeft, new Vector2(82f, -68f), new Vector2(240f, 24f), false);
        card.price = Label(root, "", 30, Gold, TextAlignmentOptions.TopRight, new Vector2(-16f, -10f), new Vector2(100f, 38f), true, true);
        card.state = Label(root, "", 15, Dim, TextAlignmentOptions.TopRight, new Vector2(-16f, -46f), new Vector2(200f, 22f), false, true);

        Button button = card.root.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => playerBuilder.SelectOffer(slot));
        return card;
    }

    private Image ToolButton(string title, Vector2 at, float buttonWidth, out TextMeshProUGUI note, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform root = Rect(title, lower);
        root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
        root.pivot = new Vector2(0f, 1f);
        root.anchoredPosition = at;
        root.sizeDelta = new Vector2(buttonWidth, 84f);
        Image outline = root.gameObject.AddComponent<Image>();
        outline.color = Edge;

        RectTransform inner = Rect("Fill", root);
        inner.anchorMin = Vector2.zero;
        inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(2f, 2f);
        inner.offsetMax = new Vector2(-2f, -2f);
        Image fill = inner.gameObject.AddComponent<Image>();
        fill.color = CardColor;
        fill.raycastTarget = false;

        Label(root, title, 21, Bright, TextAlignmentOptions.TopLeft, new Vector2(12f, -8f), new Vector2(buttonWidth - 20f, 28f), true);
        note = Label(root, "", 15, Dim, TextAlignmentOptions.TopLeft, new Vector2(12f, -38f), new Vector2(buttonWidth - 20f, 44f), false);
        note.textWrappingMode = TextWrappingModes.Normal;

        Button button = root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(onClick);
        return outline;
    }

    private void LegendLine(MovementClass movement, Color color, string name, string what)
    {
        RectTransform row = Rect("Route " + name, lower);
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.sizeDelta = new Vector2(0f, 28f);

        RectTransform bar = Rect("Line", row);
        bar.anchorMin = bar.anchorMax = new Vector2(0f, 0.5f);
        bar.pivot = new Vector2(0f, 0.5f);
        bar.anchoredPosition = new Vector2(18f, 0f);
        bar.sizeDelta = new Vector2(54f, 5f);
        Image image = bar.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;

        Label(row, name, 18, Bright, TextAlignmentOptions.TopLeft, new Vector2(84f, -2f), new Vector2(110f, 26f), true);
        Label(row, what, 16, Dim, TextAlignmentOptions.TopLeft, new Vector2(178f, -3f), new Vector2(240f, 26f), false);
        legend.Add(new LegendRow { root = row.gameObject, movement = movement });
    }

    // ------------------------------------------------------------------ every frame

    void Update()
    {
        if (panel == null || Host == null || playerBuilder == null) return;

        SimState state = Host.State;
        TowerOffer[] offers = state.Shop.Offers;
        Set(budget, state.BuildBudget.ToString("0.#"));

        while (cards.Count < offers.Length) cards.Add(AddCard(cards.Count));
        for (int i = 0; i < cards.Count; i++)
        {
            Card card = cards[i];
            card.root.SetActive(i < offers.Length);
            if (i >= offers.Length) continue;

            TowerDef def = Host.Sim.TowerLibrary[offers[i].TowerIndex];
            Color typeColor = TowerView.ColorOf(def.DamageType);
            bool selected = playerBuilder.Tool == BuildTool.Tower && playerBuilder.SelectedOffer == i;
            bool poor = !offers[i].Bought && state.BuildBudget < offers[i].Price;

            Set(card.name, Title(def.Id));
            Set(card.type, def.DamageType.ToString().ToLowerInvariant());
            card.type.color = typeColor;
            card.icon.color = typeColor;
            Set(card.tags, Tags(def));
            Set(card.price, offers[i].Price.ToString());
            card.price.color = poor ? Red : Gold;
            Set(card.state, offers[i].Bought ? "BOUGHT" : poor ? "not enough budget" : selected ? "click a tile to place" : "");
            card.state.color = poor ? Red : selected ? Teal : Dim;
            card.group.alpha = offers[i].Bought ? 0.42f : 1f;
            card.outline.color = selected ? Teal : Edge;
            card.background.color = selected ? CardSelected : CardColor;
        }

        // The tools and the key sit under however many cards there are.
        float cardsHeight = offers.Length * (CardHeight + CardGap);
        Top(lower, 80f + cardsHeight + 6f, 250f);

        bool maySell = playerBuilder.MaySell;
        wallButton.color = playerBuilder.Tool == BuildTool.Wall ? Teal : Edge;
        sellButton.color = playerBuilder.Tool == BuildTool.Sell ? Teal : Edge;
        sellGroup.alpha = maySell ? 1f : 0.4f;
        int wallPrice = playerBuilder.WallPrice;
        Set(wallNote, "click or drag\n" + (wallPrice >= 0 ? wallPrice + " budget a tile" : ""));
        Set(sellNote, maySell ? "click it: half its price back" : "opens again when the wave is over");

        bool inWave = !playerBuilder.IsBuildPhase;
        Set(legendNote, inWave ? "the wave now on the board" : "every kind you have met");
        float y = -154f;
        for (int i = 0; i < legend.Count; i++)
        {
            bool shown = pathPreviewer != null && pathPreviewer.Shows(legend[i].movement);
            legend[i].root.SetActive(shown);
            if (!shown) continue;
            ((RectTransform)legend[i].root.transform).anchoredPosition = new Vector2(0f, y);
            y -= 30f;
        }

        UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        string text = ghostPreviewer != null ? ghostPreviewer.HoverText : "";
        bool show = !string.IsNullOrEmpty(text) && Mouse.current != null;
        if (tooltip.gameObject.activeSelf != show) tooltip.gameObject.SetActive(show);
        if (!show) return;

        Set(tooltipText, text);
        tooltip.sizeDelta = new Vector2(Mathf.Min(520f, tooltipText.preferredWidth + 28f), 38f);
        Vector2 mouse = Mouse.current.position.ReadValue();
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, mouse, null, out Vector2 local))
        {
            tooltip.anchoredPosition = local + new Vector2(26f, -20f);
        }
    }

    // ------------------------------------------------------------------ small helpers

    private static string Title(string id)
    {
        return string.IsNullOrEmpty(id) ? "" : char.ToUpperInvariant(id[0]) + id.Substring(1);
    }

    private static string Tags(TowerDef def)
    {
        string what = def.SplashRadiusTiles > 0f ? "splash" : def.SlowTicks > 0 ? "slows" : "single target";
        return what + "  ·  " + (def.CanHitFlying ? "hits flyers" : "ground only") + "  ·  range " + def.RangeTiles.ToString("0.#");
    }

    // TextMeshPro rebuilds its mesh on every assignment, changed or not.
    private static void Set(TextMeshProUGUI label, string text)
    {
        if (label.text != text) label.text = text;
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    // Stretches a block across its parent's width, `fromTop` below the parent's top.
    private static void Top(RectTransform rect, float fromTop, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -fromTop);
        rect.sizeDelta = new Vector2(0f, height);
    }

    private static TextMeshProUGUI Label(Transform parent, string text, float size, Color color, TextAlignmentOptions align,
                                         Vector2 at, Vector2 box, bool bold, bool fromRight = false)
    {
        RectTransform rect = Rect("Text", parent);
        rect.anchorMin = rect.anchorMax = new Vector2(fromRight ? 1f : 0f, 1f);
        rect.pivot = new Vector2(fromRight ? 1f : 0f, 1f);
        rect.anchoredPosition = at;
        rect.sizeDelta = box;

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
}
