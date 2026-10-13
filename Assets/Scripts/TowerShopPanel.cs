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
    private TextMeshProUGUI budget, income;
    private RectTransform coreBanner, moveCore;
    private TextMeshProUGUI legendNote;
    private Image wallButton, sellButton, moveTowerButton, moveButton, rerollButton;
    private TextMeshProUGUI toolNote, rerollLabel, rerollNote;
    private CanvasGroup moveGroup, moveTowerGroup, rerollGroup;
    private RectTransform rerollArea;
    private CanvasGroup sellGroup;
    private RectTransform tooltip;
    private TextMeshProUGUI title, subtitle, gearTitle;
    private RectTransform gearArea;
    private readonly List<Card> gearRows = new List<Card>();
    private TextMeshProUGUI tooltipText;
    private RectTransform canvasRect;

    private RectTransform overlay, bigRow, showCards;
    private TextMeshProUGUI overlayTitle, overlayNote, overlayRerollLabel;
    private Image overlayReroll;
    private CanvasGroup overlayRerollGroup;
    private readonly List<Card> bigCards = new List<Card>();
    private bool cardsHidden;
    private int shownRoll = -1;
    private const float BigCardWidth = 230f;
    private const float BigCardHeight = 320f;

    private const float CardHeight = 72f;
    private const float CardGap = 6f;
    private const float ToolHeight = 44f;
    private const float GearRowHeight = 34f;
    private const float RerollHeight = 40f;
    private const float LowerHeight = 214f;

    private SimHost Host => simHost != null ? simHost : (simHost = SimHost.Find());

    void Start()
    {
        canvasRect = GetComponent<RectTransform>();
        Build();

        // The line at the top that names the next wave grows with the wave ("30 basic +
        // 15 sapper + 15 flyer, from 3 sides"): it shrinks to its box and never runs
        // over the health readout beside it.
        CanvasDashboard dashboard = GetComponent<CanvasDashboard>();
        if (dashboard != null && dashboard.waveText != null)
        {
            dashboard.waveText.fontSizeMax = dashboard.waveText.fontSize;
            dashboard.waveText.fontSizeMin = 14f;
            dashboard.waveText.enableAutoSizing = true;
            dashboard.waveText.overflowMode = TextOverflowModes.Ellipsis;
        }
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

        title = Label(panel, "TOWER SHOP", 26, Bright, TextAlignmentOptions.TopLeft, new Vector2(18f, -14f), new Vector2(260f, 34f), true);
        subtitle = Label(panel, "new offers after every wave", 17, Dim, TextAlignmentOptions.TopLeft, new Vector2(18f, -46f), new Vector2(300f, 24f), false);
        Label(panel, "Budget", 17, Dim, TextAlignmentOptions.TopRight, new Vector2(-18f, -12f), new Vector2(140f, 24f), false, true);
        budget = Label(panel, "", 30, Gold, TextAlignmentOptions.TopRight, new Vector2(-18f, -30f), new Vector2(160f, 36f), true, true);
        income = Label(panel, "", 14, Dim, TextAlignmentOptions.TopRight, new Vector2(-18f, -62f), new Vector2(170f, 18f), false, true);

        cardArea = Rect("Cards", panel);
        Top(cardArea, 80f, 0f);

        // One button under the cards: pay for a new set of them.
        rerollArea = Rect("Reroll", panel);
        Top(rerollArea, 80f, RerollHeight);
        RectTransform rr = Rect("Button", rerollArea);
        rr.anchorMin = new Vector2(0f, 1f);
        rr.anchorMax = new Vector2(1f, 1f);
        rr.pivot = new Vector2(0.5f, 1f);
        rr.sizeDelta = new Vector2(-28f, RerollHeight - 6f);
        rr.anchoredPosition = Vector2.zero;
        rerollGroup = rr.gameObject.AddComponent<CanvasGroup>();
        rerollButton = rr.gameObject.AddComponent<Image>();
        rerollButton.color = Edge;
        RectTransform rrFill = Rect("Fill", rr);
        rrFill.anchorMin = Vector2.zero;
        rrFill.anchorMax = Vector2.one;
        rrFill.offsetMin = new Vector2(2f, 2f);
        rrFill.offsetMax = new Vector2(-2f, -2f);
        Image rrImage = rrFill.gameObject.AddComponent<Image>();
        rrImage.color = CardColor;
        rrImage.raycastTarget = false;
        rerollLabel = Label(rr, "REROLL", 19, Bright, TextAlignmentOptions.TopLeft, new Vector2(14f, -5f), new Vector2(150f, 26f), true);
        rerollNote = Label(rr, "", 15, Dim, TextAlignmentOptions.TopRight, new Vector2(-14f, -7f), new Vector2(250f, 22f), false, true);
        Button rrButton = rr.gameObject.AddComponent<Button>();
        rrButton.transition = Selectable.Transition.None;
        rrButton.onClick.AddListener(() => playerBuilder.Reroll());

        gearArea = Rect("GearShop", panel);
        Top(gearArea, 80f, 0f);
        gearTitle = Label(gearArea, "GEAR SHOP", 17, Dim, TextAlignmentOptions.TopLeft, new Vector2(18f, -2f), new Vector2(390f, 24f), true);

        lower = Rect("Lower", panel);
        Top(lower, 80f, LowerHeight);

        Label(lower, "TOOLS", 17, Dim, TextAlignmentOptions.TopLeft, new Vector2(18f, -6f), new Vector2(200f, 24f), true);
        // What the tool in the hand does, in one line beside the heading.
        toolNote = Label(lower, "", 15, Dim, TextAlignmentOptions.TopRight, new Vector2(-18f, -7f), new Vector2(320f, 24f), false, true);
        float quarter = (width - 36f - 3f * 8f) / 4f;
        wallButton = ToolButton("WALL [W]", new Vector2(18f, -32f), quarter, () => playerBuilder.SelectWall());
        sellButton = ToolButton("SELL [X]", new Vector2(18f + (quarter + 8f), -32f), quarter, () => playerBuilder.SelectSell());
        sellGroup = sellButton.gameObject.AddComponent<CanvasGroup>();
        moveTowerButton = ToolButton("MOVE [M]", new Vector2(18f + 2f * (quarter + 8f), -32f), quarter, () => playerBuilder.SelectMoveTower());
        moveTowerGroup = moveTowerButton.gameObject.AddComponent<CanvasGroup>();
        moveButton = ToolButton("GEAR", new Vector2(18f + 3f * (quarter + 8f), -32f), quarter, () => playerBuilder.SelectMoveGear());
        moveGroup = moveButton.gameObject.AddComponent<CanvasGroup>();

        Label(lower, "ROUTES", 17, Dim, TextAlignmentOptions.TopLeft, new Vector2(18f, -86f), new Vector2(120f, 24f), true);
        legendNote = Label(lower, "", 15, Dim, TextAlignmentOptions.TopRight, new Vector2(-18f, -87f), new Vector2(290f, 24f), false, true);
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

        BuildOverlay();
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

        card.name = Label(root, "", 22, Bright, TextAlignmentOptions.TopLeft, new Vector2(82f, -4f), new Vector2(200f, 28f), true);
        // Each text has a box of its own on the card: the type on the left of the middle row and the
        // card's state on its right, under the price; the tags have the bottom row.
        card.type = Label(root, "", 15, Bright, TextAlignmentOptions.TopLeft, new Vector2(82f, -30f), new Vector2(172f, 20f), true);
        card.tags = Label(root, "", 14, Dim, TextAlignmentOptions.TopLeft, new Vector2(82f, -49f), new Vector2(304f, 20f), false);
        card.price = Label(root, "", 24, Gold, TextAlignmentOptions.TopRight, new Vector2(-16f, -4f), new Vector2(100f, 30f), true, true);
        card.state = Label(root, "", 14, Dim, TextAlignmentOptions.TopRight, new Vector2(-16f, -31f), new Vector2(128f, 20f), false, true);

        AddClickOrDrag(card.root, slot);
        return card;
    }

    // A card is clicked, as always; with the bottom bar (the tilted trial scene) it can
    // also be dragged onto a tile, a tower of its type, or a bench slot.
    private void AddClickOrDrag(GameObject root, int slot)
    {
        if (playerBuilder.deckBar != null)
        {
            DragCard drag = root.AddComponent<DragCard>();
            drag.kind = DragCard.Kind.Offer;
            drag.slot = slot;
            playerBuilder.deckBar.Wire(drag);
            return;
        }
        Button button = root.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => playerBuilder.SelectOffer(slot));
    }

    // One line of the gear shop: a dot, the piece, what it does, its price.
    private Card AddGearRow(int slot)
    {
        var row = new Card();
        RectTransform root = Rect("Gear " + (slot + 1), gearArea);
        root.anchorMin = new Vector2(0f, 1f);
        root.anchorMax = new Vector2(1f, 1f);
        root.pivot = new Vector2(0.5f, 1f);
        root.sizeDelta = new Vector2(-28f, GearRowHeight - 4f);
        root.anchoredPosition = new Vector2(0f, -26f - slot * GearRowHeight);
        row.root = root.gameObject;
        row.group = row.root.AddComponent<CanvasGroup>();
        row.outline = row.root.AddComponent<Image>();
        row.outline.color = Edge;
        RectTransform inner = Rect("Fill", root);
        inner.anchorMin = Vector2.zero;
        inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(2f, 2f);
        inner.offsetMax = new Vector2(-2f, -2f);
        row.background = inner.gameObject.AddComponent<Image>();
        row.background.color = CardColor;
        row.background.raycastTarget = false;
        RectTransform icon = Rect("Icon", root);
        icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
        icon.pivot = new Vector2(0.5f, 0.5f);
        icon.anchoredPosition = new Vector2(22f, 0f);
        icon.sizeDelta = new Vector2(18f, 18f);
        row.icon = icon.gameObject.AddComponent<Image>();
        row.icon.raycastTarget = false;
        row.name = Label(root, "", 18, Bright, TextAlignmentOptions.TopLeft, new Vector2(42f, -3f), new Vector2(130f, 24f), true);
        row.tags = Label(root, "", 15, Dim, TextAlignmentOptions.TopLeft, new Vector2(170f, -5f), new Vector2(190f, 22f), false);
        row.price = Label(root, "", 19, Gold, TextAlignmentOptions.TopRight, new Vector2(-12f, -2f), new Vector2(80f, 24f), true, true);
        Button button = row.root.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => playerBuilder.SelectShopGear(slot));
        return row;
    }

    private Image ToolButton(string title, Vector2 at, float buttonWidth, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform root = Rect(title, lower);
        root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
        root.pivot = new Vector2(0f, 1f);
        root.anchoredPosition = at;
        root.sizeDelta = new Vector2(buttonWidth, ToolHeight);
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

        Label(root, title, 15, Bright, TextAlignmentOptions.Top, new Vector2(0f, -12f), new Vector2(buttonWidth, 22f), true);

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
        Set(income, "+" + Host.Config.StipendPerWave + " after the wave");
        bool choosingCore = playerBuilder.ChoosingCore;
        if (coreBanner.gameObject.activeSelf != choosingCore) coreBanner.gameObject.SetActive(choosingCore);
        bool mayReopen = playerBuilder.MayReopenCore;
        if (moveCore.gameObject.activeSelf != mayReopen) moveCore.gameObject.SetActive(mayReopen);

        // A draft (SimConfig.PicksPerRoll): the cards are free and only so many are taken.
        bool draft = state.Shop.PicksLeft >= 0;
        Set(title, draft ? (state.Shop.PicksLeft == 1 ? "PICK ONE CARD" : state.Shop.PicksLeft > 1 ? "PICK " + state.Shop.PicksLeft + " CARDS" : "CARDS") : "TOWER SHOP");
        int cap = playerBuilder.TowerCap;
        string towersText = cap > 0 ? "towers " + playerBuilder.TowersStanding + " / " + cap + "  \u00b7  " : "";
        Set(subtitle, towersText + (draft ? (state.Shop.MayPick ? "cards are free" : "picked; new cards next wave") : "new offers after every wave"));
        int setSize = Host.Config.CombineCount;

        while (cards.Count < offers.Length) cards.Add(AddCard(cards.Count));
        for (int i = 0; i < cards.Count; i++)
        {
            Card card = cards[i];
            card.root.SetActive(i < offers.Length);
            if (i >= offers.Length) continue;

            Fill(card, i, state, offers, draft, setSize);
        }

        // The tools and the key sit under however many cards there are.
        float cardsHeight = offers.Length * (CardHeight + CardGap);

        // The gear shop, under the cards: what is on sale, for budget.
        TowerOffer[] stock = state.GearShop.Offers;
        // The reroll button, between the cards and the gear shop.
        bool rerolls = playerBuilder.RerollsOn;
        float rerollHeight = rerolls ? RerollHeight : 0f;
        rerollArea.gameObject.SetActive(rerolls);
        Top(rerollArea, 80f + cardsHeight, RerollHeight);
        if (rerolls)
        {
            float price = playerBuilder.RerollPrice;
            bool can = playerBuilder.CanReroll;
            Set(rerollLabel, "REROLL [D]   " + price.ToString("0.#"));
            rerollLabel.color = can ? Gold : Dim;
            Set(rerollNote, !state.Shop.MayPick ? "nothing left to pick" : state.BuildBudget < price ? "not enough budget" : "new cards; dearer each time");
            rerollGroup.alpha = can ? 1f : 0.45f;
        }
        cardsHeight += rerollHeight;

        float gearHeight = stock.Length > 0 ? 30f + stock.Length * GearRowHeight : 0f;
        gearArea.gameObject.SetActive(stock.Length > 0);
        Top(gearArea, 80f + cardsHeight + 4f, gearHeight);
        while (gearRows.Count < stock.Length) gearRows.Add(AddGearRow(gearRows.Count));
        for (int i = 0; i < gearRows.Count; i++)
        {
            Card row = gearRows[i];
            row.root.SetActive(i < stock.Length);
            if (i >= stock.Length) continue;
            GearDef piece = Host.Sim.GearLibrary[stock[i].GearIndex];
            bool inHand = playerBuilder.Tool == BuildTool.Gear && playerBuilder.SelectedShopGear == i;
            bool tooDear = !stock[i].Bought && state.BuildBudget < stock[i].Price;
            Set(row.name, Title(piece.Id));
            Set(row.tags, stock[i].Bought ? "sold" : inHand ? "click a tower" : piece.Label);
            Set(row.price, stock[i].Price.ToString());
            row.price.color = tooDear ? Red : Gold;
            row.icon.color = TowerView.ColorOfGear(stock[i].GearIndex);
            row.group.alpha = stock[i].Bought ? 0.4f : 1f;
            row.outline.color = inHand ? Teal : Edge;
            row.background.color = inHand ? CardSelected : CardColor;
        }
        if (stock.Length > 0)
        {
            int every = Host.Config.GearShopRefreshWaves;
            int left = every > 0 ? every - state.WaveIndex % every : 0;
            Set(gearTitle, "GEAR SHOP" + (every > 0 ? "   new stock in " + left + (left == 1 ? " wave" : " waves") : ""));
        }

        Top(lower, 80f + cardsHeight + 4f + gearHeight + 4f, LowerHeight);

        bool maySell = playerBuilder.MaySell;
        wallButton.color = playerBuilder.Tool == BuildTool.Wall ? Teal : Edge;
        sellButton.color = playerBuilder.Tool == BuildTool.Sell ? Teal : Edge;
        sellGroup.alpha = maySell ? 1f : 0.4f;
        int wallPrice = playerBuilder.WallPrice;
        moveTowerButton.color = playerBuilder.Tool == BuildTool.MoveTower ? Teal : Edge;
        moveTowerGroup.alpha = playerBuilder.MayMoveTower ? 1f : 0.4f;
        moveButton.color = playerBuilder.Tool == BuildTool.MoveGear ? Teal : Edge;
        moveGroup.alpha = playerBuilder.MayMoveGear ? 1f : 0.4f;
        string note = "";
        switch (playerBuilder.Tool)
        {
            case BuildTool.Wall: note = playerBuilder.WallsFromStock ? "click or drag  \u00b7  " + state.WallsLeft + " tiles left" : "click or drag" + (wallPrice >= 0 ? "  \u00b7  " + wallPrice + " a tile" : ""); break;
            case BuildTool.Sell: note = !maySell ? "between waves" : playerBuilder.WallsFromStock ? "take up a wall, or sell a tower" : "click it: half its price back"; break;
            case BuildTool.Core: note = "click the tile your core goes on"; break;
            case BuildTool.Deploy: note = "click a tile, or a tower to change places"; break;
            case BuildTool.EquipGear: note = "drop it on a tower"; break;
            case BuildTool.MoveTower: note = !playerBuilder.MayMoveTower ? "between waves" : playerBuilder.TowerFrom != null ? "now click a free tile" : "click a tower, then a free tile; free"; break;
            case BuildTool.MoveGear: note = !playerBuilder.MayMoveGear ? "between waves" : playerBuilder.GearFrom != null ? "now click where it goes" : "gear from tower to tower; free"; break;
            case BuildTool.Tower: note = "a tile, the bench, or a tower like it"; break;
            case BuildTool.Gear: note = "click the tower it goes on"; break;
        }
        Set(toolNote, note);

        bool inWave = !playerBuilder.IsBuildPhase;
        Set(legendNote, inWave ? "the wave now on the board" : state.NextPlan != null ? "the wave that comes next" : "every kind you have met");
        float y = -112f;
        for (int i = 0; i < legend.Count; i++)
        {
            bool shown = pathPreviewer != null && pathPreviewer.Shows(legend[i].movement);
            legend[i].root.SetActive(shown);
            if (!shown) continue;
            ((RectTransform)legend[i].root.transform).anchoredPosition = new Vector2(0f, y);
            y -= 30f;
        }

        UpdateOverlay(state, offers, draft, setSize);
        UpdateTooltip();
    }

    // Everything one card says about the offer in slot i.
    private void Fill(Card card, int i, SimState state, TowerOffer[] offers, bool draft, int setSize)
    {
        bool selected = (playerBuilder.Tool == BuildTool.Tower || playerBuilder.Tool == BuildTool.Gear) && playerBuilder.SelectedOffer == i;
        bool poor = !offers[i].Bought && state.BuildBudget < offers[i].Price;
        bool passed = draft && !offers[i].Bought && !state.Shop.MayPick; // the pick went to another card
        string hint;

        switch (offers[i].Kind)
        {
            case CardKind.Coin:
                Set(card.name, "Coin");
                Set(card.type, "budget");
                card.type.color = Gold;
                card.icon.color = Gold;
                Set(card.tags, "click to take it");
                Set(card.price, "+" + offers[i].Amount);
                hint = "";
                break;
            case CardKind.Wall:
                Set(card.name, "Walls");
                Set(card.type, "wall tiles");
                card.type.color = playerBuilder.wallColor;
                card.icon.color = playerBuilder.wallColor;
                Set(card.tags, "added to your stock");
                Set(card.price, "+" + offers[i].Amount);
                hint = "";
                break;
            case CardKind.Gear:
                GearDef gear = Host.Sim.GearLibrary[offers[i].GearIndex];
                Color gearColor = TowerView.ColorOfGear(offers[i].GearIndex);
                Set(card.name, Title(gear.Id));
                Set(card.type, "gear");
                card.type.color = gearColor;
                card.icon.color = gearColor;
                Set(card.tags, gear.Label + "  \u00b7  goes on a tower");
                Set(card.price, offers[i].Price.ToString());
                hint = "click a tower";
                break;
            default:
                TowerDef def = Host.Sim.TowerLibrary[offers[i].TowerIndex];
                Color typeColor = TowerView.ColorOf(def.DamageType);
                int level = offers[i].Level;
                Set(card.name, Title(def.Id));
                Set(card.type, level > 1 ? TowerView.LevelName(level) + ": " + LevelLabels(def, level) : def.DamageType.ToString().ToLowerInvariant());
                card.type.color = level > 1 ? TowerView.ColorOfLevel(level) : typeColor;
                card.icon.color = typeColor;
                Set(card.tags, Tags(def));
                Set(card.price, offers[i].Price.ToString());
                hint = "click a tile to place";
                break;
        }
        // What a tower card would do to the towers that stand: complete a set, add to
        // one, or find no room under the cap.
        string setNote = "";
        Color setColor = Dim;
        if (offers[i].Kind == CardKind.Tower && !offers[i].Bought)
        {
            int copies = playerBuilder.CopiesOf(offers[i]);
            if (playerBuilder.WouldCombine(offers[i])) { setNote = "completes a set: " + TowerView.LevelName(offers[i].Level + 1); setColor = Teal; }
            else if (!playerBuilder.HasRoomFor(offers[i])) { setNote = "tower limit reached"; setColor = Red; }
            else if (setSize > 1 && copies > 0 && offers[i].Level < TowerState.MaxLevel) setNote = "you have " + copies + " of " + setSize;
            if (playerBuilder.CanFeedSomewhere(offers[i])) { setNote = "you own one: place it, or merge"; setColor = Teal; }
            else if (!playerBuilder.HasRoomFor(offers[i]) && Host.Config.BenchSlots > 0) { setNote = "board full: goes on the bench"; setColor = Dim; }
        }
        if (draft && (offers[i].Kind == CardKind.Tower || offers[i].Kind == CardKind.Gear)) Set(card.price, "FREE");
        card.price.color = poor ? Red : Gold;
        Set(card.state, offers[i].Bought ? (draft ? "PICKED" : offers[i].Kind == CardKind.Coin ? "TAKEN" : "BOUGHT") : passed ? "" : poor ? "not enough budget" : selected && setNote.Length == 0 ? hint : setNote);
        card.state.color = poor ? Red : setNote.Length > 0 ? setColor : selected ? Teal : Dim;
        card.group.alpha = offers[i].Bought ? 0.55f : passed ? 0.25f : 1f;
        card.outline.color = selected ? Teal : Edge;
        card.background.color = selected ? CardSelected : CardColor;
    }

    // ------------------------------------------------------------------ the cards in the middle of the screen

    // When there is a card to pick, the cards are shown large over the board, so the
    // pick is the first thing the player deals with (the student, 2026-10-13). VIEW
    // BOARD puts them away to look at the board first; SHOW CARDS brings them back.
    // The small cards in the panel stay and work the same way.
    private void BuildOverlay()
    {
        overlay = Rect("CardOverlay", transform);
        overlay.anchorMin = Vector2.zero;
        overlay.anchorMax = Vector2.one;
        overlay.offsetMin = overlay.offsetMax = Vector2.zero;
        overlay.gameObject.AddComponent<Image>().color = new Color(0.02f, 0.03f, 0.06f, 0.72f); // also takes the clicks meant for the board
        overlayGroup = overlay.gameObject.AddComponent<CanvasGroup>();

        overlayTitle = Label(overlay, "", 40, Bright, TextAlignmentOptions.Top, Vector2.zero, new Vector2(900f, 52f), true);
        Centre(overlayTitle.rectTransform, 0f, 250f);
        overlayNote = Label(overlay, "", 20, Dim, TextAlignmentOptions.Top, Vector2.zero, new Vector2(900f, 28f), false);
        Centre(overlayNote.rectTransform, 0f, 200f);

        bigRow = Rect("Cards", overlay);
        Centre(bigRow, 0f, 0f);
        bigRow.sizeDelta = new Vector2(10f, BigCardHeight);

        overlayReroll = OverlayButton(overlay, "", new Vector2(-150f, -210f), 280f, () => playerBuilder.Reroll(), out overlayRerollLabel, out overlayRerollGroup);
        OverlayButton(overlay, "VIEW BOARD", new Vector2(150f, -210f), 280f, () => cardsHidden = true, out _, out _);

        showCards = OverlayButton(transform, "SHOW CARDS", Vector2.zero, 240f, () => cardsHidden = false, out _, out _).rectTransform;
        showCards.anchorMin = showCards.anchorMax = new Vector2(0.5f, 1f);
        showCards.pivot = new Vector2(0.5f, 1f);
        showCards.anchoredPosition = new Vector2(-120f, -104f);
        ShowOverlay(false);
        showCards.gameObject.SetActive(false);

        // The first thing the game asks: where the core goes. Large, in the middle, and
        // with the one button that ends it (a tester did not find the small one).
        coreBanner = Rect("CoreBanner", transform);
        coreBanner.anchorMin = coreBanner.anchorMax = new Vector2(0.5f, 1f);
        coreBanner.pivot = new Vector2(0.5f, 1f);
        coreBanner.anchoredPosition = new Vector2(-120f, -100f);
        coreBanner.sizeDelta = new Vector2(860f, 196f);
        Image bannerBack = coreBanner.gameObject.AddComponent<Image>();
        bannerBack.color = new Color(0.16f, 0.1f, 0.03f, 0.93f);
        bannerBack.raycastTarget = false; // the board behind it is still clicked through
        TextMeshProUGUI bannerTitle = Label(coreBanner, "STEP 1:  PLACE YOUR CORE", 40, Gold, TextAlignmentOptions.Top, new Vector2(20f, -12f), new Vector2(820f, 50f), true);
        TextMeshProUGUI bannerText = Label(coreBanner, "The glowing ball is your core. CLICK ANY TILE inside the gold frame to move it there.\nEnemies come in from the edges and walk to it. You can change your mind until the first wave.", 20, Bright, TextAlignmentOptions.Top, new Vector2(20f, -64f), new Vector2(820f, 56f), false);
        bannerText.textWrappingMode = TextWrappingModes.Normal;
        bannerTitle.raycastTarget = bannerText.raycastTarget = false;
        Image confirm = OverlayButton(coreBanner, "DONE: THE CORE STAYS HERE", new Vector2(0f, -54f), 420f, () => playerBuilder.ConfirmCore(), out _, out _);
        confirm.color = Gold;
        coreBanner.gameObject.SetActive(false);

        moveCore = OverlayButton(transform, "MOVE CORE", Vector2.zero, 200f, () => playerBuilder.ReopenCore(), out _, out _).rectTransform;
        moveCore.anchorMin = moveCore.anchorMax = new Vector2(0.5f, 1f);
        moveCore.pivot = new Vector2(0.5f, 1f);
        moveCore.anchoredPosition = new Vector2(-380f, -104f);
        moveCore.gameObject.SetActive(false);
    }

    private static void Centre(RectTransform rect, float x, float y)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, y);
    }

    private Image OverlayButton(Transform parent, string text, Vector2 at, float buttonWidth, UnityEngine.Events.UnityAction onClick, out TextMeshProUGUI label, out CanvasGroup group)
    {
        RectTransform root = Rect("Button", parent);
        Centre(root, at.x, at.y);
        root.sizeDelta = new Vector2(buttonWidth, 50f);
        Image outline = root.gameObject.AddComponent<Image>();
        outline.color = Teal;
        group = root.gameObject.AddComponent<CanvasGroup>();
        RectTransform inner = Rect("Fill", root);
        inner.anchorMin = Vector2.zero;
        inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(2f, 2f);
        inner.offsetMax = new Vector2(-2f, -2f);
        Image fill = inner.gameObject.AddComponent<Image>();
        fill.color = CardColor;
        fill.raycastTarget = false;
        label = Label(root, text, 21, Bright, TextAlignmentOptions.Center, Vector2.zero, new Vector2(buttonWidth, 50f), true);
        Button button = root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(onClick);
        return outline;
    }

    // Hidden means unseen and untouchable, not switched off: a card dragged off the
    // overlay hides it, and switching its object off would end the drag.
    private void ShowOverlay(bool show)
    {
        overlayShown = show;
        overlayGroup.alpha = show ? 1f : 0f;
        overlayGroup.blocksRaycasts = show;
        overlayGroup.interactable = show;
    }

    private CanvasGroup overlayGroup;
    private bool overlayShown;

    private Card AddBigCard(int slot)
    {
        var card = new Card();
        RectTransform root = Rect("Big offer " + (slot + 1), bigRow);
        Centre(root, 0f, 0f);
        root.sizeDelta = new Vector2(BigCardWidth, BigCardHeight);
        card.root = root.gameObject;
        card.group = card.root.AddComponent<CanvasGroup>();
        card.outline = card.root.AddComponent<Image>();
        card.outline.color = Edge;
        RectTransform inner = Rect("Fill", root);
        inner.anchorMin = Vector2.zero;
        inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(3f, 3f);
        inner.offsetMax = new Vector2(-3f, -3f);
        card.background = inner.gameObject.AddComponent<Image>();
        card.background.color = CardColor;
        card.background.raycastTarget = false;

        RectTransform icon = Rect("Icon", root);
        icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 1f);
        icon.pivot = new Vector2(0.5f, 1f);
        icon.anchoredPosition = new Vector2(0f, -22f);
        icon.sizeDelta = new Vector2(64f, 64f);
        card.icon = icon.gameObject.AddComponent<Image>();
        card.icon.raycastTarget = false;

        float w = BigCardWidth - 20f;
        card.name = Label(root, "", 28, Bright, TextAlignmentOptions.Top, new Vector2(10f, -98f), new Vector2(w, 36f), true);
        card.type = Label(root, "", 17, Bright, TextAlignmentOptions.Top, new Vector2(10f, -136f), new Vector2(w, 46f), true);
        card.tags = Label(root, "", 16, Dim, TextAlignmentOptions.Top, new Vector2(10f, -184f), new Vector2(w, 66f), false);
        card.price = Label(root, "", 26, Gold, TextAlignmentOptions.Top, new Vector2(10f, -252f), new Vector2(w, 32f), true);
        card.state = Label(root, "", 16, Dim, TextAlignmentOptions.Top, new Vector2(10f, -286f), new Vector2(w, 22f), false);
        card.type.textWrappingMode = TextWrappingModes.Normal;
        card.tags.textWrappingMode = TextWrappingModes.Normal;

        AddClickOrDrag(card.root, slot);
        return card;
    }

    private void UpdateOverlay(SimState state, TowerOffer[] offers, bool draft, int setSize)
    {
        // A new set of cards after a wave comes forward again, whatever was hidden before.
        if (state.Shop.WaveIndex != shownRoll)
        {
            shownRoll = state.Shop.WaveIndex;
            cardsHidden = false;
        }

        bool toPick = draft && state.Shop.MayPick && offers.Length > 0 && playerBuilder.IsBuildPhase && !playerBuilder.IsBuildingBlocked;
        bool inHand = (playerBuilder.Tool == BuildTool.Tower || playerBuilder.Tool == BuildTool.Gear) && playerBuilder.SelectedOffer >= 0;
        if (playerBuilder.ChoosingCore) toPick = false; // the core first
        bool show = toPick && !cardsHidden && !inHand;
        ShowOverlay(show);
        bool offerBack = toPick && cardsHidden && !inHand;
        if (showCards.gameObject.activeSelf != offerBack) showCards.gameObject.SetActive(offerBack);
        if (!show) return;

        Set(overlayTitle, state.Shop.PicksLeft == 1 ? "PICK ONE CARD" : "PICK " + state.Shop.PicksLeft + " CARDS");
        Set(overlayNote, "free  \u00b7  a tower or gear card is then placed on the board");
        while (bigCards.Count < offers.Length) bigCards.Add(AddBigCard(bigCards.Count));
        float step = BigCardWidth + 18f;
        for (int i = 0; i < bigCards.Count; i++)
        {
            Card card = bigCards[i];
            card.root.SetActive(i < offers.Length);
            if (i >= offers.Length) continue;
            ((RectTransform)card.root.transform).anchoredPosition = new Vector2((i - (offers.Length - 1) / 2f) * step, 0f);
            Fill(card, i, state, offers, draft, setSize);
            if (card.state.text.Length == 0 && !offers[i].Bought) Set(card.state, "click to pick");
        }

        bool rerolls = playerBuilder.RerollsOn;
        overlayReroll.gameObject.SetActive(rerolls);
        if (rerolls)
        {
            Set(overlayRerollLabel, "REROLL  " + playerBuilder.RerollPrice.ToString("0.#") + "   [D]");
            overlayRerollGroup.alpha = playerBuilder.CanReroll ? 1f : 0.4f;
        }
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

    // What a tower card of this level adds over a common one, in the roster's own words.
    private static string LevelLabels(TowerDef def, int level)
    {
        string text = def.LevelMod(2).Label;
        if (level >= 3) text += " + " + def.LevelMod(3).Label;
        return text;
    }

    private static string Tags(TowerDef def)
    {
        string what = def.SplashRadiusTiles > 0f ? "splash" : def.SlowTicks > 0 ? "slows" : def.Targets > 1 ? "hits " + def.Targets + " at once" : "single target";
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
        // A text too long for its box shrinks to fit, and past that is cut with dots; it never runs over its neighbour.
        label.enableAutoSizing = true;
        label.fontSizeMax = size;
        label.fontSizeMin = Mathf.Max(10f, size * 0.6f);
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        return label;
    }
}
