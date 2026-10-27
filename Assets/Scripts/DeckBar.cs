using System.Collections.Generic;
using Thesis.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// The bar along the bottom of the screen (2026-10-14, the "Arknights look" trial): what
// a turn allows (moves, wall tiles), the bench as a row of tower cards, and the gear
// inventory. A card or a piece of gear is used by DRAGGING it: onto a tile, onto a
// tower, onto a bench slot. Clicking still works (click the card, then the place).
// A card that explains a tower in full is shown beside the mouse's tower or bench card.
//
// It builds its own UI under the Canvas, shows state every frame and forwards what the
// mouse did to PlayerBuilder. Every rule is the simulation's.
public class DeckBar : MonoBehaviour
{
    public PlayerBuilder playerBuilder;
    public SimHost simHost;
    public CanvasDashboard dashboard;

    [Header("Layout, in the Canvas's reference pixels")]
    public float height = 200f;
    public float rightInset = 458f; // the shop panel and its margins

    private static readonly Color PanelColor = new Color(0.07f, 0.1f, 0.17f, 0.94f);
    private static readonly Color CardColor = new Color(0.06f, 0.09f, 0.15f, 1f);
    private static readonly Color CardSelected = new Color(0.06f, 0.2f, 0.18f, 1f);
    private static readonly Color Edge = new Color(0.14f, 0.19f, 0.29f, 1f);
    private static readonly Color Teal = new Color(0.18f, 0.83f, 0.75f, 1f);
    private static readonly Color Gold = new Color(0.98f, 0.75f, 0.14f, 1f);
    private static readonly Color Dim = new Color(0.58f, 0.64f, 0.72f, 1f);
    private static readonly Color Bright = new Color(0.9f, 0.91f, 0.92f, 1f);

    private class Slot
    {
        public DragCard card;
        public Image outline, fill, icon;
        public TextMeshProUGUI name, line1, line2, line3;
    }

    private class Action
    {
        public GameObject root;
        public Image outline;
        public TextMeshProUGUI label;
        public CanvasGroup group;
    }

    private RectTransform bar, benchArea, gearArea, info, dragIcon, spareArea;
    private TextMeshProUGUI spareText;
    private readonly List<Slot> spare = new List<Slot>();
    private readonly List<Action> spareSell = new List<Action>();
    private TextMeshProUGUI movesValue, wallsValue, wallsNote, benchTitle, gearTitle, infoText, coreText;
    private Action buyMove, buyWalls, confirmCore;
    private Image dragImage;
    private readonly List<Slot> bench = new List<Slot>();
    private readonly List<Slot> gear = new List<Slot>();
    private readonly List<RaycastResult> hits = new List<RaycastResult>();
    private RectTransform canvasRect;
    private int hoverBench = -1;

    // The build section (SimConfig.BuildMenu, 2026-10-23), after RimWorld's architect
    // menu: a column of categories, and beside it what the chosen one holds.
    private class Item
    {
        public GameObject root;
        public Image outline, fill, icon, gem;
        public TextMeshProUGUI name, price, note;
        public CanvasGroup group;
        public System.Action onClick;
        public string tip;
    }

    private RectTransform buildArea, tipBox;
    private TextMeshProUGUI tipText, movesLabel, wallsLabel;
    private Action undoButton, redoButton, retryButton;
    // The inspect pane's commands (2026-10-24): for a tower, upgrade, target, move, sell;
    // for a collector, sell.
    private readonly List<Action> gizmos = new List<Action>();
    private TextMeshProUGUI buildTitle;
    private readonly List<Action> categoryButtons = new List<Action>();
    private readonly List<Item> items = new List<Item>();
    private int category;
    private static readonly string[] Categories = { "TOWERS  1", "ECONOMY  2", "GEAR  3", "ORDERS  4" };
    private static readonly string[] CategoryIcons = { "tower", "gem", "sword", "flag" };
    private const float ItemWidth = 140f;
    private const float ItemHeight = 70f;
    private const int ItemsPerRow = 4;

    private const float BenchCardWidth = 178f;
    private const float GearSlotWidth = 134f;
    private const float GearSlotHeight = 72f;

    private SimHost Host => simHost != null ? simHost : (simHost = SimHost.Find());

    // True from the moment something is dragged off the HUD until it is dropped.
    public bool Dragging { get; private set; }

    void Awake()
    {
        if (playerBuilder == null) playerBuilder = FindFirstObjectByType<PlayerBuilder>();
        if (playerBuilder != null) playerBuilder.deckBar = this;
    }

    void Start()
    {
        canvasRect = GetComponent<RectTransform>();
        if (dashboard == null) dashboard = GetComponent<CanvasDashboard>();
        Build();
        if (GetComponent<DamagePanel>() == null) gameObject.AddComponent<DamagePanel>(); // the damage panel (2026-10-27)
        if (GetComponent<HudOverlay>() == null) gameObject.AddComponent<HudOverlay>(); // the HUD of the approved mockup (2026-10-27)
        HudArt.Skin(bar);
        HudArt.Skin(info);
        HudArt.Skin(tipBox);
        HudArt.Skin(spareArea);
        MoveGameButtons();
    }

    // ------------------------------------------------------------------ building the UI

    private void Build()
    {
        bar = Rect("DeckBar", transform);
        bar.anchorMin = new Vector2(0f, 0f);
        bar.anchorMax = new Vector2(1f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.offsetMin = new Vector2(14f, 10f);
        bar.offsetMax = new Vector2(-rightInset, 10f + height);
        bar.gameObject.AddComponent<Image>().color = PanelColor;
        bar.SetAsFirstSibling();

        // What the turn allows.
        movesLabel = Label(bar, "MOVES", 16, Dim, TextAlignmentOptions.TopLeft, new Vector2(16f, -10f), new Vector2(90f, 22f), true);
        movesValue = Label(bar, "", 30, Bright, TextAlignmentOptions.TopLeft, new Vector2(16f, -28f), new Vector2(60f, 36f), true);
        buyMove = Button(bar, "", new Vector2(84f, -30f), 150f, () => playerBuilder.BuyMove());
        wallsLabel = Label(bar, "WALL TILES", 16, Dim, TextAlignmentOptions.TopLeft, new Vector2(16f, -78f), new Vector2(120f, 22f), true);
        wallsValue = Label(bar, "", 30, Bright, TextAlignmentOptions.TopLeft, new Vector2(16f, -96f), new Vector2(60f, 36f), true);
        buyWalls = Button(bar, "", new Vector2(84f, -98f), 150f, () => playerBuilder.BuyWalls());
        wallsNote = Label(bar, "", 13, Dim, TextAlignmentOptions.TopLeft, new Vector2(16f, -140f), new Vector2(230f, 20f), false);
        coreText = Label(bar, "", 13, Gold, TextAlignmentOptions.TopLeft, new Vector2(16f, -160f), new Vector2(110f, 34f), false);
        confirmCore = Button(bar, "CORE STAYS", new Vector2(120f, -160f), 114f, () => playerBuilder.ConfirmCore());
        undoButton = Button(bar, "UNDO   ctrl+Z", new Vector2(16f, -14f), 218f, () => playerBuilder.Undo());
        redoButton = Button(bar, "REDO   ctrl+Y", new Vector2(16f, -54f), 218f, () => playerBuilder.Redo());
        // The last wave again, from its build phase, also when it was won (2026-10-24). Ctrl+Z takes it back.
        retryButton = Button(bar, "", new Vector2(16f, -94f), 218f, () => Host.RetryWave());

        benchArea = Rect("Bench", bar);
        benchArea.anchorMin = benchArea.anchorMax = new Vector2(0f, 1f);
        benchArea.pivot = new Vector2(0f, 1f);
        benchArea.anchoredPosition = new Vector2(256f, 0f);
        benchTitle = Label(benchArea, "BENCH", 16, Dim, TextAlignmentOptions.TopLeft, new Vector2(0f, -8f), new Vector2(700f, 22f), true);

        gearArea = Rect("Gear", bar);
        gearArea.anchorMin = gearArea.anchorMax = new Vector2(1f, 1f);
        gearArea.pivot = new Vector2(1f, 1f);
        gearArea.anchoredPosition = new Vector2(-12f, 0f);
        gearArea.sizeDelta = new Vector2(3f * (GearSlotWidth + 6f), height);
        gearTitle = Label(gearArea, "GEAR", 16, Dim, TextAlignmentOptions.TopLeft, new Vector2(0f, -8f), new Vector2(400f, 22f), true);

        buildArea = Rect("Build", bar);
        buildArea.anchorMin = buildArea.anchorMax = new Vector2(0f, 1f);
        buildArea.pivot = new Vector2(0f, 1f);
        buildArea.anchoredPosition = new Vector2(256f, 0f);
        buildArea.sizeDelta = new Vector2(132f + ItemsPerRow * (ItemWidth + 6f), height);
        buildTitle = Label(buildArea, "", 16, Dim, TextAlignmentOptions.TopLeft, new Vector2(0f, -8f), new Vector2(720f, 22f), true);
        for (int c = 0; c < Categories.Length; c++)
        {
            int which = c;
            Action button = Button(buildArea, Categories[c], new Vector2(0f, -32f - c * 38f), 122f, () => category = which);
            HudArt.AddIcon(button.root.transform, CategoryIcons[c], Bright, new Vector2(8f, -8f), 18f);
            button.label.rectTransform.offsetMin = new Vector2(32f, button.label.rectTransform.offsetMin.y);
            button.label.alignment = TextAlignmentOptions.Left;
            categoryButtons.Add(button);
        }
        for (int i = 0; i < 2 * ItemsPerRow; i++) items.Add(AddItem(i));
        buildArea.gameObject.SetActive(false);

        // The card that explains the tower under the mouse.
        info = Rect("TowerInfo", transform);
        info.anchorMin = info.anchorMax = new Vector2(0f, 0f);
        info.pivot = new Vector2(0f, 0f);
        info.anchoredPosition = new Vector2(14f, 18f + height);
        info.sizeDelta = new Vector2(330f, 308f);
        Image infoBack = info.gameObject.AddComponent<Image>();
        infoBack.color = new Color(0.04f, 0.06f, 0.1f, 0.94f);
        infoBack.raycastTarget = false;
        BuildInfoHeader();
        infoText = Label(info, "", 14, Bright, TextAlignmentOptions.TopLeft, new Vector2(14f, -156f), new Vector2(302f, 92f), false); // ends above the commands
        infoText.textWrappingMode = TextWrappingModes.Normal;
        infoText.richText = true;
        string[] gizmoNames = { "", "AIM", "MOVE", "" };
        string[] gizmoIcons = { "up", "target", "move", "gem" };
        for (int g = 0; g < gizmoNames.Length; g++)
        {
            int which = g;
            Action gizmo = Button(info, gizmoNames[g], new Vector2(8f + g * 79f, -262f), 74f, () => UseGizmo(which));
            HudArt.AddIcon(gizmo.root.transform, gizmoIcons[g], Bright, new Vector2(7f, -8f), 18f);
            gizmo.label.rectTransform.offsetMin = new Vector2(29f, gizmo.label.rectTransform.offsetMin.y);
            gizmo.label.alignment = TextAlignmentOptions.Left;
            gizmos.Add(gizmo);
        }
        info.gameObject.SetActive(false);

        // The tooltip of a build button, over the bar.
        tipBox = Rect("BuildTip", transform);
        tipBox.anchorMin = tipBox.anchorMax = new Vector2(0f, 0f);
        tipBox.pivot = new Vector2(0f, 0f);
        tipBox.sizeDelta = new Vector2(320f, 150f);
        Image tipBack = tipBox.gameObject.AddComponent<Image>();
        tipBack.color = new Color(0.03f, 0.05f, 0.09f, 0.96f);
        tipBack.raycastTarget = false;
        tipText = Label(tipBox, "", 15, Bright, TextAlignmentOptions.TopLeft, new Vector2(12f, -10f), new Vector2(296f, 132f), false);
        tipText.textWrappingMode = TextWrappingModes.Normal;
        tipText.richText = true;
        tipBox.gameObject.SetActive(false);

        // Spare gear: pieces that came back and found the inventory full. Shown only then.
        spareArea = Rect("SpareGear", transform);
        spareArea.anchorMin = spareArea.anchorMax = new Vector2(0.5f, 0f);
        spareArea.pivot = new Vector2(0.5f, 0f);
        spareArea.anchoredPosition = new Vector2(-rightInset / 2f, 20f + height);
        spareArea.sizeDelta = new Vector2(760f, 150f);
        spareArea.gameObject.AddComponent<Image>().color = new Color(0.16f, 0.1f, 0.04f, 0.96f);
        spareText = Label(spareArea, "", 17, Gold, TextAlignmentOptions.TopLeft, new Vector2(16f, -10f), new Vector2(730f, 44f), true);
        spareText.textWrappingMode = TextWrappingModes.Normal;
        spareArea.gameObject.SetActive(false);

        // What is being dragged, while the mouse is still over the HUD.
        dragIcon = Rect("DragIcon", transform);
        dragIcon.anchorMin = dragIcon.anchorMax = new Vector2(0.5f, 0.5f);
        dragIcon.sizeDelta = new Vector2(46f, 46f);
        dragImage = dragIcon.gameObject.AddComponent<Image>();
        dragImage.raycastTarget = false;
        dragIcon.gameObject.SetActive(false);
    }

    private Item AddItem(int index)
    {
        var item = new Item();
        RectTransform root = Rect("Item " + (index + 1), buildArea);
        root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
        root.pivot = new Vector2(0f, 1f);
        root.anchoredPosition = new Vector2(132f + (index % ItemsPerRow) * (ItemWidth + 6f), -32f - (index / ItemsPerRow) * (ItemHeight + 6f));
        root.sizeDelta = new Vector2(ItemWidth, ItemHeight);
        item.root = root.gameObject;
        item.outline = root.gameObject.AddComponent<Image>();
        item.outline.color = Edge;
        item.group = root.gameObject.AddComponent<CanvasGroup>();
        RectTransform inner = Rect("Fill", root);
        inner.anchorMin = Vector2.zero;
        inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(2f, 2f);
        inner.offsetMax = new Vector2(-2f, -2f);
        item.fill = inner.gameObject.AddComponent<Image>();
        item.fill.color = CardColor;
        item.fill.raycastTarget = false;
        RectTransform icon = Rect("Icon", root);
        icon.anchorMin = icon.anchorMax = new Vector2(0f, 1f);
        icon.pivot = new Vector2(0f, 1f);
        icon.anchoredPosition = new Vector2(10f, -10f);
        icon.sizeDelta = new Vector2(30f, 30f);
        item.icon = icon.gameObject.AddComponent<Image>();
        item.icon.raycastTarget = false;
        item.name = Label(root, "", 16, Bright, TextAlignmentOptions.TopLeft, new Vector2(48f, -7f), new Vector2(ItemWidth - 54f, 22f), true);
        item.gem = HudArt.AddIcon(root, "gem", Gold, new Vector2(48f, -32f), 14f);
        item.price = Label(root, "", 16, Gold, TextAlignmentOptions.TopLeft, new Vector2(66f, -28f), new Vector2(60f, 22f), true);
        item.note = Label(root, "", 11, Dim, TextAlignmentOptions.TopLeft, new Vector2(10f, -50f), new Vector2(ItemWidth - 16f, 18f), false);
        item.note.textWrappingMode = TextWrappingModes.Normal;
        Button button = root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => { if (item.onClick != null) item.onClick(); });
        return item;
    }

    // The Start, speed and pause buttons sat in the middle of the bottom edge, where the
    // bar is now: they go under the shop panel.
    private void MoveGameButtons()
    {
        if (dashboard == null || dashboard.startWaveButton == null) return;
        // The three sit in one group; the group moves, and the strip it sat on is hidden.
        var group = (RectTransform)dashboard.startWaveButton.transform.parent;
        Image strip = group.parent != null ? group.parent.GetComponent<Image>() : null;
        if (strip != null && group.parent != transform) strip.enabled = false;
        group.SetParent(transform, false);
        group.anchorMin = group.anchorMax = new Vector2(1f, 0f);
        group.pivot = new Vector2(0.5f, 0.5f);
        group.anchoredPosition = new Vector2(-rightInset / 2f + 7f, 100f);
    }

    private Slot AddSlot(DragCard.Kind kind, int index, RectTransform parent, Vector2 at, Vector2 size)
    {
        var slot = new Slot();
        RectTransform root = Rect(kind + " " + (index + 1), parent);
        root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
        root.pivot = new Vector2(0f, 1f);
        root.anchoredPosition = at;
        root.sizeDelta = size;
        slot.outline = root.gameObject.AddComponent<Image>();
        slot.outline.color = Edge;
        RectTransform inner = Rect("Fill", root);
        inner.anchorMin = Vector2.zero;
        inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(2f, 2f);
        inner.offsetMax = new Vector2(-2f, -2f);
        slot.fill = inner.gameObject.AddComponent<Image>();
        slot.fill.color = CardColor;
        slot.fill.raycastTarget = false;

        bool big = kind == DragCard.Kind.Bench;
        RectTransform icon = Rect("Icon", root);
        icon.anchorMin = icon.anchorMax = new Vector2(0f, 1f);
        icon.pivot = new Vector2(0f, 1f);
        icon.anchoredPosition = big ? new Vector2(12f, -12f) : new Vector2(10f, -12f);
        icon.sizeDelta = big ? new Vector2(40f, 40f) : new Vector2(18f, 18f);
        slot.icon = icon.gameObject.AddComponent<Image>();
        slot.icon.raycastTarget = false;

        float w = size.x - 24f;
        if (big)
        {
            slot.name = Label(root, "", 21, Bright, TextAlignmentOptions.TopLeft, new Vector2(60f, -8f), new Vector2(w - 48f, 28f), true);
            slot.line1 = Label(root, "", 14, Dim, TextAlignmentOptions.TopLeft, new Vector2(60f, -34f), new Vector2(w - 48f, 20f), true);
            slot.line2 = Label(root, "", 14, Dim, TextAlignmentOptions.TopLeft, new Vector2(12f, -64f), new Vector2(w, 40f), false);
            slot.line3 = Label(root, "", 14, Dim, TextAlignmentOptions.TopLeft, new Vector2(12f, -112f), new Vector2(w, 40f), false);
            slot.line2.textWrappingMode = TextWrappingModes.Normal;
            slot.line3.textWrappingMode = TextWrappingModes.Normal;
        }
        else
        {
            slot.name = Label(root, "", 17, Bright, TextAlignmentOptions.TopLeft, new Vector2(34f, -8f), new Vector2(w - 20f, 24f), true);
            slot.line1 = Label(root, "", 13, Dim, TextAlignmentOptions.TopLeft, new Vector2(10f, -36f), new Vector2(w + 6f, 30f), false);
            slot.line1.textWrappingMode = TextWrappingModes.Normal;
        }

        slot.card = root.gameObject.AddComponent<DragCard>();
        slot.card.kind = kind;
        slot.card.slot = index;
        Wire(slot.card);
        return slot;
    }

    // Makes a HUD element behave as a card of this bar: click, drag off, hover. The shop
    // panel wires its own cards through this too.
    public void Wire(DragCard card)
    {
        card.Clicked = Click;
        card.DragBegan = BeginDrag;
        card.DragEnded = c => Drop();
        card.Entered = c => { if (c.kind == DragCard.Kind.Bench) hoverBench = c.slot; };
        card.Exited = c => { if (c.kind == DragCard.Kind.Bench && hoverBench == c.slot) hoverBench = -1; };
    }

    // ------------------------------------------------------------------ clicking and dragging

    private void Click(DragCard card)
    {
        switch (card.kind)
        {
            case DragCard.Kind.Bench: playerBuilder.ClickBench(card.slot); break;
            case DragCard.Kind.Gear: playerBuilder.ClickInventory(card.slot); break;
            case DragCard.Kind.Offer: playerBuilder.SelectOffer(card.slot); break;
            case DragCard.Kind.Spare: playerBuilder.ClickOverflow(card.slot); break;
        }
    }

    // Dragging something off the HUD is picking it up; where the mouse is let go is
    // where it is used.
    private void BeginDrag(DragCard card)
    {
        if (playerBuilder.IsBuildingBlocked) return;
        switch (card.kind)
        {
            case DragCard.Kind.Bench:
                if (playerBuilder.Benched(card.slot) == null) return;
                playerBuilder.PutDown();
                playerBuilder.ClickBench(card.slot);
                break;
            case DragCard.Kind.Gear:
                if (Host.State.GearInventory[card.slot] == TowerState.NoGear) return;
                playerBuilder.PutDown();
                playerBuilder.ClickInventory(card.slot);
                break;
            case DragCard.Kind.Spare:
                playerBuilder.PutDown();
                playerBuilder.ClickOverflow(card.slot);
                break;
            case DragCard.Kind.Offer:
                if (playerBuilder.Tool == BuildTool.Tower && playerBuilder.SelectedOffer == card.slot) break;
                playerBuilder.SelectOffer(card.slot);
                break;
        }
        Dragging = playerBuilder.Tool == BuildTool.Deploy || playerBuilder.Tool == BuildTool.EquipGear || playerBuilder.Tool == BuildTool.Tower;
    }

    // The mouse was let go: over a tile, the thing in the hand is used there; over a
    // bench or gear slot, there; anywhere else it goes back.
    public void Drop()
    {
        if (!Dragging) return;
        Dragging = false;

        Node tile = playerBuilder.NodeUnderPointer();
        DragCard target = CardUnderPointer();
        if (target != null && target.kind == DragCard.Kind.Bench) playerBuilder.ClickBench(target.slot);
        else if (target != null && target.kind == DragCard.Kind.Gear) playerBuilder.ClickInventory(target.slot);
        else if (target == null && tile != null && !PointerOverHud()) playerBuilder.UseToolAt(tile);

        if (playerBuilder.Tool == BuildTool.Deploy || playerBuilder.Tool == BuildTool.EquipGear || playerBuilder.Tool == BuildTool.Tower) playerBuilder.PutDown();
    }

    // The bench slot under the mouse, or -1 (PlayerBuilder asks when a tower dragged off
    // the board is let go).
    public int BenchSlotUnderPointer()
    {
        DragCard card = CardUnderPointer();
        return card != null && card.kind == DragCard.Kind.Bench ? card.slot : -1;
    }

    private DragCard CardUnderPointer()
    {
        if (EventSystem.current == null || Mouse.current == null) return null;
        var data = new PointerEventData(EventSystem.current) { position = Mouse.current.position.ReadValue() };
        hits.Clear();
        EventSystem.current.RaycastAll(data, hits);
        for (int i = 0; i < hits.Count; i++)
        {
            DragCard card = hits[i].gameObject.GetComponentInParent<DragCard>();
            if (card != null) return card;
        }
        return null;
    }

    private bool PointerOverHud()
    {
        if (EventSystem.current == null || Mouse.current == null) return false;
        var data = new PointerEventData(EventSystem.current) { position = Mouse.current.position.ReadValue() };
        hits.Clear();
        EventSystem.current.RaycastAll(data, hits);
        return hits.Count > 0;
    }

    // ------------------------------------------------------------------ every frame

    void Update()
    {
        if (bar == null || Host == null || playerBuilder == null) return;
        SimState state = Host.State;
        SimConfig config = Host.Config;

        // Moves and walls: only what this game has. Undo and redo take their place.
        bool moves = playerBuilder.MovesLimited;
        movesLabel.gameObject.SetActive(moves);
        movesValue.gameObject.SetActive(moves);
        bool undo = playerBuilder.UndoOn && !moves;
        undoButton.root.SetActive(undo);
        redoButton.root.SetActive(undo);
        if (undo) { Enable(undoButton, playerBuilder.CanUndo); Enable(redoButton, playerBuilder.CanRedo); }
        // In a build phase it plays the last wave again; while a wave runs it restarts it from
        // its build phase (2026-10-27).
        bool again = false; // the HUD's wave buttons restart and replay waves now (HudOverlay)
        retryButton.root.SetActive(again);
        if (again) Set(retryButton.label, (playerBuilder.IsBuildPhase ? "PLAY WAVE " : "RESTART WAVE ") + Host.RetryWaveNumber + (playerBuilder.IsBuildPhase ? " AGAIN" : ""));
        bool freeMoves = !moves || playerBuilder.MovesAreFree;
        Set(movesValue, freeMoves ? "free" : state.MovesLeft.ToString());
        movesValue.color = freeMoves || state.MovesLeft > 0 ? Bright : Dim;
        bool moveSold = moves && !freeMoves && config.ExtraMovePrice >= 0f;
        buyMove.root.SetActive(moveSold);
        if (moveSold)
        {
            Set(buyMove.label, "+1 MOVE   " + Host.Sim.ExtraMovePrice.ToString("0.#"));
            Enable(buyMove, playerBuilder.CanBuyMove);
        }

        bool walls = playerBuilder.WallsFromStock;
        wallsLabel.gameObject.SetActive(walls);
        wallsValue.gameObject.SetActive(walls);
        Set(wallsValue, walls ? state.WallsLeft.ToString() : "");
        wallsValue.color = state.WallsLeft > 0 ? Bright : Dim;
        Set(wallsNote, !walls ? (playerBuilder.BuildMenu ? "walls " + playerBuilder.WallPrice + " ore a tile; selling one gives it back" : "") : config.FreeWallPickup ? "take any wall up again with SELL [X]" : "old walls you may still take up: " + state.RelaysLeft);
        bool wallSold = walls && config.WallPackSize > 0;
        buyWalls.root.SetActive(wallSold);
        if (wallSold)
        {
            Set(buyWalls.label, "+" + config.WallPackSize + " WALLS   " + config.WallPackPrice.ToString("0.#"));
            Enable(buyWalls, playerBuilder.CanBuyWalls);
        }

        bool choosing = playerBuilder.ChoosingCore;
        confirmCore.root.SetActive(false); // the banner over the board asks for the core now
        Set(coreText, "");

        // The bench: one card per slot.
        int count = state.Bench.Length;
        benchArea.gameObject.SetActive(count > 0);
        Set(benchTitle, playerBuilder.Tool == BuildTool.Tower ? (config.FeedDuplicates ? "BENCH   drop the card on a slot to keep it, or on a tower like it to merge" : "BENCH   drop the card on a slot to keep it for later")
                      : playerBuilder.Tool == BuildTool.Deploy ? "BENCH   drop it on a tile, or on a tower to change places"
                      : "BENCH   drag a tower onto the board");
        while (bench.Count < count) bench.Add(AddSlot(DragCard.Kind.Bench, bench.Count, benchArea, new Vector2(bench.Count * (BenchCardWidth + 8f), -32f), new Vector2(BenchCardWidth, height - 42f)));
        for (int i = 0; i < bench.Count; i++)
        {
            Slot slot = bench[i];
            TowerState tower = playerBuilder.Benched(i);
            bool inHand = playerBuilder.Tool == BuildTool.Deploy && playerBuilder.SelectedBench == i;
            slot.outline.color = inHand ? Teal : tower != null && tower.Rarity > 1 ? TowerView.ColorOfLevel(tower.Rarity) : Edge;
            slot.fill.color = inHand ? CardSelected : CardColor;
            slot.icon.enabled = tower != null;
            if (tower == null)
            {
                Set(slot.name, "");
                Set(slot.line1, "");
                Set(slot.line2, "empty");
                Set(slot.line3, "");
                continue;
            }
            slot.icon.color = TowerView.ColorOfTower(tower.Def);
            Set(slot.name, Title(tower.Def.Id));
            Set(slot.line1, TowerView.LevelName(tower.Rarity));
            slot.line1.color = TowerView.ColorOfLevel(tower.Rarity);
            Set(slot.line2, tower.Def.DamageType.ToString().ToLowerInvariant() + "  ·  range " + tower.RangeTiles.ToString("0.#") + "\n" + (tower.CanHitFlying ? "hits flyers" : "ground only"));
            Set(slot.line3, GearLine(tower));
        }

        // The gear inventory.
        int pieces = state.GearInventory.Length;
        gearArea.gameObject.SetActive(pieces > 0);
        Set(gearTitle, playerBuilder.Tool == BuildTool.EquipGear ? "GEAR   drop it on a tower" : "GEAR   drag a piece onto a tower");
        while (gear.Count < pieces)
        {
            int i = gear.Count;
            gear.Add(AddSlot(DragCard.Kind.Gear, i, gearArea, new Vector2((i % 3) * (GearSlotWidth + 6f), -32f - (i / 3) * (GearSlotHeight + 6f)), new Vector2(GearSlotWidth, GearSlotHeight)));
        }
        for (int i = 0; i < gear.Count; i++)
        {
            Slot slot = gear[i];
            int piece = i < pieces ? state.GearInventory[i] : TowerState.NoGear;
            bool inHand = playerBuilder.Tool == BuildTool.EquipGear && playerBuilder.SelectedInventory == i;
            slot.outline.color = inHand ? Teal : Edge;
            slot.fill.color = inHand ? CardSelected : CardColor;
            slot.icon.enabled = piece != TowerState.NoGear;
            if (piece == TowerState.NoGear)
            {
                Set(slot.name, "");
                Set(slot.line1, "empty");
                continue;
            }
            GearDef def = Host.Sim.GearLibrary[piece];
            slot.icon.color = TowerView.ColorOfGear(piece);
            Set(slot.name, Title(def.Id));
            Set(slot.line1, def.Label);
        }

        bool menu = playerBuilder.BuildMenu;
        if (buildArea.gameObject.activeSelf != menu) buildArea.gameObject.SetActive(menu);
        if (menu) UpdateBuild(state, config);

        UpdateSpare(state);
        UpdateInfo();
        UpdateDragIcon();
    }

    // ------------------------------------------------------------------ the build section

    private void UpdateBuild(SimState state, SimConfig config)
    {
        if (Keyboard.current != null)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame) category = 0;
            if (Keyboard.current.digit2Key.wasPressedThisFrame) category = 1;
            if (Keyboard.current.digit3Key.wasPressedThisFrame) category = 2;
            if (Keyboard.current.digit4Key.wasPressedThisFrame) category = 3;
        }
        for (int c = 0; c < categoryButtons.Count; c++)
        {
            categoryButtons[c].outline.color = c == category ? Teal : Edge;
            categoryButtons[c].label.color = c == category ? Bright : Dim;
        }

        float ore = state.BuildBudget;
        int n = 0;
        string hint = "";
        switch (category)
        {
            case 0:
                hint = "click one, then a wall or a rock  \u00b7  right-click to stop";
                TowerDef[] roster = Host.Sim.TowerLibrary;
                for (int i = 0; i < roster.Length && n < items.Count; i++)
                {
                    int index = i;
                    TowerDef def = roster[i];
                    if (def.PowerCapacity > 0) continue; // power towers are under ECONOMY
                    bool unlocked = playerBuilder.TowerUnlocked(i);
                    Fill(items[n++], Title(def.Id), def.Cost.ToString(), unlocked ? Tags(def) : "research tier " + def.Tier,
                         TowerView.ColorOfTower(def), unlocked, unlocked && ore >= def.Cost,
                         playerBuilder.Tool == BuildTool.Tower && playerBuilder.SelectedMenuTower == i, () => playerBuilder.SelectMenuTower(index));
                    items[n - 1].tip = TowerTip(def, unlocked);
                }
                break;
            case 1:
                hint = "collectors pay ore after every wave they survive";
                if (playerBuilder.HasDeposits)
                    Fill(items[n++], "Collector", config.CollectorPrice.ToString("0.#"), "on an ore deposit: +2 to +4 a wave  [C]",
                         PlayerBuilder.CollectorColor, true, ore >= config.CollectorPrice, playerBuilder.Tool == BuildTool.Collector, () => playerBuilder.SelectCollector());
                // Power towers (the pylon): what they are for is the economy's outposts.
                TowerDef[] all = Host.Sim.TowerLibrary;
                for (int i = 0; i < all.Length && n < items.Count; i++)
                {
                    if (all[i].PowerCapacity <= 0) continue;
                    int index = i;
                    TowerDef def = all[i];
                    bool open = playerBuilder.TowerUnlocked(i);
                    Fill(items[n++], Title(def.Id), def.Cost.ToString(), open ? Tags(def) : "research tier " + def.Tier, TowerView.ColorOfTower(def), open, open && ore >= def.Cost,
                         playerBuilder.Tool == BuildTool.Tower && playerBuilder.SelectedMenuTower == i, () => playerBuilder.SelectMenuTower(index));
                    items[n - 1].tip = TowerTip(def, open);
                }
                float research = Host.Sim.ResearchPrice;
                Fill(items[n++], research < 0f ? "Researched" : "Research " + (state.ResearchLevel + 1), research < 0f ? "" : research.ToString("0.#"),
                     research < 0f ? "every tier is open" : "unlocks " + Unlocks(state.ResearchLevel + 1),
                     Teal, research >= 0f, research >= 0f && ore >= research, false, () => playerBuilder.Research());
                Fill(items[n++], "Repair core", config.RepairPrice.ToString("0.#"), "+" + config.HealCardAmount + " HP  (" + state.CoreHp + " / " + config.CoreMaxHp + ")",
                     Teal, true, playerBuilder.CanRepair, false, () => playerBuilder.RepairCore());
                break;
            case 2:
                hint = "crafted into the inventory on the right; drag a piece onto a tower";
                GearDef[] gear = Host.Sim.GearLibrary;
                bool room = System.Array.IndexOf(state.GearInventory, TowerState.NoGear) >= 0;
                for (int g = 0; g < gear.Length && n < items.Count; g++)
                {
                    if (!gear[g].OnSale(config)) continue;
                    int piece = g;
                    bool unlocked = gear[g].Tier <= state.ResearchLevel;
                    Fill(items[n++], Title(gear[g].Id), Host.Sim.CraftPrice(g).ToString(), !unlocked ? "research tier " + gear[g].Tier : !room ? "inventory full" : gear[g].Label,
                         TowerView.ColorOfGear(g), unlocked, unlocked && room && ore >= Host.Sim.CraftPrice(g), false, () => playerBuilder.CraftGear(piece));
                    items[n - 1].tip = "<b>" + Title(gear[g].Id) + "</b>   " + Host.Sim.CraftPrice(g) + " ore\n" + gear[g].Label
                                       + (unlocked ? "\n<color=#94a3b8>click: into the inventory, then drag it onto a tower</color>" : "\n<color=#f87171>needs research tier " + gear[g].Tier + "</color>");
                }
                break;
            default:
                hint = "what the hand does on the board";
                Fill(items[n++], "Wall  [W]", playerBuilder.WallPrice > 0 ? playerBuilder.WallPrice.ToString() : "", "click or drag; towers stand on walls", playerBuilder.wallColor, true, true, playerBuilder.Tool == BuildTool.Wall, () => playerBuilder.SelectWall());
                Fill(items[n++], "Sell  [X]", "", "half back; takes walls up", Teal, true, playerBuilder.MaySell, playerBuilder.Tool == BuildTool.Sell, () => playerBuilder.SelectSell());
                Fill(items[n++], "Move  [M]", "", "a tower to another wall or rock", Teal, true, playerBuilder.MayMoveTower, playerBuilder.Tool == BuildTool.MoveTower, () => playerBuilder.SelectMoveTower());
                if (playerBuilder.UpgradesOn)
                    Fill(items[n++], "Upgrade  [U]", "", "choose a buff; rare, then epic", Teal, true, playerBuilder.IsBuildPhase, playerBuilder.Tool == BuildTool.Upgrade, () => playerBuilder.SelectUpgrade());
                if (playerBuilder.TargetingOn)
                    Fill(items[n++], "Target  [T]", "", "which enemy a tower shoots", Teal, true, true, playerBuilder.Tool == BuildTool.Target, () => playerBuilder.SelectTarget());
                break;
        }
        for (int i = n; i < items.Count; i++) if (items[i].root.activeSelf) items[i].root.SetActive(false);

        // The button under the mouse explains itself, over the bar.
        Item hovered = null;
        if (Mouse.current != null)
        {
            Vector2 mouse = Mouse.current.position.ReadValue();
            for (int i = 0; i < n; i++)
                if (RectTransformUtility.RectangleContainsScreenPoint((RectTransform)items[i].root.transform, mouse, null)) hovered = items[i];
            if (hovered != null && !string.IsNullOrEmpty(hovered.tip)
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, mouse, null, out Vector2 local))
            {
                Set(tipText, hovered.tip);
                Vector2 corner = local + canvasRect.rect.size / 2f;
                tipBox.anchoredPosition = new Vector2(Mathf.Min(corner.x - 40f, canvasRect.rect.width - 340f), 18f + height);
            }
        }
        bool tipShown = hovered != null && !string.IsNullOrEmpty(hovered.tip);
        if (tipBox.gameObject.activeSelf != tipShown) tipBox.gameObject.SetActive(tipShown);
        Set(buildTitle, "BUILD   " + Categories[category].Split(' ')[0] + "   " + hint);
    }

    // One button of the build section: what it is, its price, a line about it.
    private void Fill(Item item, string name, string price, string note, Color icon, bool open, bool affordable, bool selected, System.Action onClick)
    {
        if (!item.root.activeSelf) item.root.SetActive(true);
        Set(item.name, name);
        Set(item.price, price);
        Set(item.note, note);
        item.icon.color = icon;
        if (item.gem.gameObject.activeSelf != (price.Length > 0)) item.gem.gameObject.SetActive(price.Length > 0);
        item.gem.color = !open ? Dim : affordable ? Gold : new Color(0.97f, 0.44f, 0.44f, 1f);
        item.price.color = !open ? Dim : affordable ? Gold : new Color(0.97f, 0.44f, 0.44f, 1f);
        item.group.alpha = open ? 1f : 0.45f;
        item.outline.color = selected ? Teal : Edge;
        item.fill.color = selected ? CardSelected : CardColor;
        item.onClick = onClick;
        item.tip = null;
    }

    private static string TowerTip(TowerDef d, bool unlocked)
    {
        float perSecond = d.Damage * d.Targets * 50f / Mathf.Max(1, d.FireIntervalTicks);
        var t = new System.Text.StringBuilder();
        t.Append("<b>").Append(Title(d.Id)).Append("</b>   ").Append(d.Cost).Append(" ore\n");
        if (d.PowerCapacity > 0)
        {
            t.Append("powers up to ").Append(d.PowerCapacity).Append(" towers within ").Append(d.PowerRadius.ToString("0.#")).Append(" tiles\nno attack of its own");
            t.Append(unlocked ? "\n<color=#94a3b8>click, then a wall or a rock</color>" : "\n<color=#f87171>needs research tier " + d.Tier + "</color>");
            return t.ToString();
        }
        t.Append(d.DamageType.ToString().ToLowerInvariant()).Append(": ").Append(d.Damage.ToString("0.#")).Append(" every ").Append((d.FireIntervalTicks / 50f).ToString("0.##"))
         .Append(" s (").Append(perSecond.ToString("0.#")).Append("/s)\nrange ").Append(d.RangeTiles.ToString("0.#"));
        if (d.SplashRadiusTiles > 0f) t.Append("  \u00b7  splash ").Append(d.SplashRadiusTiles.ToString("0.##"));
        if (d.SlowTicks > 0) t.Append("  \u00b7  slows to ").Append((d.SlowFactor * 100f).ToString("0")).Append("%");
        t.Append(d.CanHitFlying ? "\nhits flyers" : "\nground only");
        t.Append(unlocked ? "\n<color=#94a3b8>click, then a wall or a rock</color>" : "\n<color=#f87171>needs research tier " + d.Tier + "</color>");
        return t.ToString();
    }

    private static string Tags(TowerDef def)
    {
        if (def.PowerCapacity > 0) return "powers " + def.PowerCapacity + " towers  \u00b7  reach " + def.PowerRadius.ToString("0.#");
        return def.DamageType.ToString().ToLowerInvariant() + "  \u00b7  range " + def.RangeTiles.ToString("0.#")
               + (def.SplashRadiusTiles > 0f ? "  \u00b7  splash" : "") + (def.CanHitFlying ? "  \u00b7  hits flyers" : "");
    }

    // What a research tier opens, by name.
    private string Unlocks(int tier)
    {
        var names = new List<string>();
        if (Host.Config.ResearchUnlocksUpgrades) names.Add(tier == 2 ? "first upgrades" : "second upgrades");
        else foreach (TowerDef t in Host.Sim.TowerLibrary) if (t.Tier == tier) names.Add(t.Id);
        foreach (GearDef g in Host.Sim.GearLibrary) if (g.Tier == tier && g.OnSale(Host.Config)) names.Add(g.Id);
        return string.Join(", ", names);
    }

    private void UpdateSpare(SimState state)
    {
        int count = state.GearOverflow.Count;
        if (spareArea.gameObject.activeSelf != (count > 0)) spareArea.gameObject.SetActive(count > 0);
        if (count == 0) return;

        Set(spareText, "SPARE GEAR: your inventory is full. Drag each piece onto a tower with a free slot, or sell it. No wave starts until this is done.");
        while (spare.Count < count)
        {
            int i = spare.Count;
            spare.Add(AddSlot(DragCard.Kind.Spare, i, spareArea, new Vector2(16f + i * 236f, -62f), new Vector2(GearSlotWidth, GearSlotHeight)));
            spareSell.Add(Button(spareArea, "", new Vector2(16f + i * 236f, -62f - GearSlotHeight + 34f), GearSlotWidth, () => playerBuilder.SellOverflow(i)));
            RectTransform sellRect = (RectTransform)spareSell[i].root.transform;
            sellRect.anchoredPosition = new Vector2(16f + i * 236f + GearSlotWidth + 4f, -62f);
            sellRect.sizeDelta = new Vector2(84f, GearSlotHeight);
        }
        for (int i = 0; i < spare.Count; i++)
        {
            bool on = i < count && i < 3; // three at a time; the rest follow as these are dealt with
            spare[i].card.gameObject.SetActive(on);
            spareSell[i].root.SetActive(on);
            if (!on) continue;
            GearDef def = Host.Sim.GearLibrary[state.GearOverflow[i]];
            bool inHand = playerBuilder.Tool == BuildTool.EquipGear && playerBuilder.SelectedOverflow == i;
            spare[i].outline.color = inHand ? Teal : Edge;
            spare[i].icon.color = TowerView.ColorOfGear(state.GearOverflow[i]);
            Set(spare[i].name, Title(def.Id));
            Set(spare[i].line1, def.Label);
            Set(spareSell[i].label, "SELL +" + (def.Price * Host.Config.SellRefund).ToString("0.#"));
        }
    }

    // The inspect pane: what is selected, with its commands; with nothing selected, the
    // tower under the mouse (or on the bench), to read.
    private void UpdateInfo()
    {
        TowerState tower = playerBuilder.SelectedTower;
        bool selected = playerBuilder.Selected != null;
        if (!selected) tower = hoverBench >= 0 ? playerBuilder.Benched(hoverBench) : playerBuilder.TowerAt(playerBuilder.HoverNode);
        bool show = (tower != null || selected) && !Dragging;
        if (info.gameObject.activeSelf != show) info.gameObject.SetActive(show);
        if (!show) return;
        if (tower != null)
        {
            ShowHeader(Title(tower.Def.Id), TowerView.ColorOfTower(tower.Def), tower.Rarity, PathName(tower));
            ShowStats(tower);
            Set(infoText, Detail(tower));
        }
        else if (playerBuilder.SelectedIsCollector) { ShowHeader("Collector", PlayerBuilder.CollectorColor, 0, "outpost"); ShowStats(null); Set(infoText, DescribeCollector()); }
        else if (playerBuilder.SelectedIsCore) { ShowHeader("Core", Teal, 0, "lose it and the game is over"); ShowStats(null); Set(infoText, DescribeCore()); }

        // The commands, for what is selected only.
        bool towerSel = selected && playerBuilder.SelectedTower != null;
        bool collectorSel = selected && playerBuilder.SelectedIsCollector;
        bool[] shown = { towerSel && playerBuilder.UpgradesOn, towerSel && playerBuilder.TargetingOn, towerSel, towerSel || collectorSel };
        bool[] usable = { towerSel && playerBuilder.CanUpgrade(playerBuilder.SelectedTower), true, playerBuilder.MayMoveTower, playerBuilder.MaySell };
        for (int g = 0; g < gizmos.Count; g++)
        {
            if (gizmos[g].root.activeSelf != shown[g]) gizmos[g].root.SetActive(shown[g]);
            if (shown[g]) Enable(gizmos[g], usable[g]);
        }
        if (towerSel && playerBuilder.UpgradesOn)
            Set(gizmos[0].label, !playerBuilder.HasUpgradeLeft(playerBuilder.SelectedTower) ? "MAX" : playerBuilder.UpgradeNeedsResearch(playerBuilder.SelectedTower) ? "T" + (playerBuilder.SelectedTower.UpgradeCount + 2) : playerBuilder.UpgradeCostFor(playerBuilder.SelectedTower).ToString("0.#"));
        if (towerSel || collectorSel)
        {
            float back = towerSel ? playerBuilder.SelectedTower.PricePaid * Host.Config.SellRefund : Host.Config.CollectorPrice * Host.Config.SellRefund;
            Set(gizmos[3].label, "+" + back.ToString("0.#"));
        }
    }

    private void UseGizmo(int which)
    {
        switch (which)
        {
            case 0: playerBuilder.UpgradeSelected(); break;
            case 1: playerBuilder.TargetSelected(); break;
            case 2: playerBuilder.MoveSelected(); break;
            default: playerBuilder.SellSelected(); break;
        }
    }

    private string DescribeCollector()
    {
        Node n = playerBuilder.Selected;
        int d = Host.Map.DepositAt(new Thesis.Core.TileCoord(n.gridX, n.gridY));
        int yield = d >= 0 ? Host.Map.Deposits[d].Yield : 0;
        bool beacon = Simulation.NearBeacon(Host.State, new Thesis.Core.TileCoord(n.gridX, n.gridY));
        var text = new System.Text.StringBuilder();
        text.Append("+").Append(yield + (beacon ? 1 : 0)).Append(" ore after every wave it survives").Append(beacon ? " (a beacon adds 1)" : "").Append('\n');
        if (playerBuilder.PowerRule)
            text.Append("powers up to ").Append(Host.Config.CollectorPowerCapacity).Append(" towers on the gold tiles\n");
        text.Append("<color=#94a3b8>raiders come for it; destroyed, it pays half its price back</color>");
        return text.ToString();
    }

    private string DescribeCore()
    {
        var text = new System.Text.StringBuilder();
        text.Append("health ").Append(Host.State.CoreHp).Append(" / ").Append(Host.Config.CoreMaxHp).Append('\n');
        text.Append("+").Append(Host.Config.StipendPerWave).Append(" ore after every wave\n");
        if (playerBuilder.PowerRule)
            text.Append("powers up to ").Append(Host.Config.CorePowerCapacity).Append(" towers on the gold tiles\n");
        return text.ToString();
    }

    // --- the inspect pane's header and stat bars (2026-10-27, the approved mockup) ---
    private Image portrait, portraitBack;
    private TextMeshProUGUI infoName, infoRarity;
    private readonly Image[] pips = new Image[3];
    private GameObject statsRoot;
    private readonly Image[] statFill = new Image[3];
    private readonly TextMeshProUGUI[] statValue = new TextMeshProUGUI[3];
    private const float StatBar = 196f;

    private void BuildInfoHeader()
    {
        RectTransform back = Rect("Portrait", info);
        back.anchorMin = back.anchorMax = back.pivot = new Vector2(0f, 1f);
        back.anchoredPosition = new Vector2(14f, -14f);
        back.sizeDelta = new Vector2(52f, 52f);
        portraitBack = back.gameObject.AddComponent<Image>();
        portraitBack.color = HudOverlay.Ink;
        portraitBack.raycastTarget = false;
        RectTransform face = Rect("Face", back);
        face.anchorMin = face.anchorMax = face.pivot = new Vector2(0.5f, 0.5f);
        face.sizeDelta = new Vector2(28f, 28f);
        portrait = face.gameObject.AddComponent<Image>();
        portrait.raycastTarget = false;
        infoName = Label(info, "", 24, Bright, TextAlignmentOptions.TopLeft, new Vector2(78f, -12f), new Vector2(240f, 30f), true);
        for (int i = 0; i < 3; i++) pips[i] = HudArt.AddIcon(info, "dot", Teal, new Vector2(80f + i * 15f, -46f), 11f);
        infoRarity = Label(info, "", 14, Dim, TextAlignmentOptions.TopLeft, new Vector2(128f, -41f), new Vector2(190f, 20f), false);

        statsRoot = Rect("Stats", info).gameObject;
        RectTransform stats = (RectTransform)statsRoot.transform;
        stats.anchorMin = stats.anchorMax = stats.pivot = new Vector2(0f, 1f);
        stats.anchoredPosition = new Vector2(14f, -78f);
        stats.sizeDelta = new Vector2(302f, 76f);
        string[] icons = { "sword", "clock", "range" };
        for (int i = 0; i < 3; i++)
        {
            float y = -i * 25f;
            HudArt.AddIcon(stats, icons[i], Dim, new Vector2(0f, y), 18f);
            RectTransform track = Rect("Track", stats);
            track.anchorMin = track.anchorMax = track.pivot = new Vector2(0f, 1f);
            track.anchoredPosition = new Vector2(28f, y - 6f);
            track.sizeDelta = new Vector2(StatBar, 6f);
            Image t = track.gameObject.AddComponent<Image>();
            t.color = HudOverlay.Track;
            t.raycastTarget = false;
            RectTransform fill = Rect("Fill", track);
            fill.anchorMin = fill.anchorMax = fill.pivot = new Vector2(0f, 1f);
            fill.anchoredPosition = Vector2.zero;
            fill.sizeDelta = new Vector2(0f, 6f);
            statFill[i] = fill.gameObject.AddComponent<Image>();
            statFill[i].color = new Color(0.8f, 0.84f, 0.88f, 1f);
            statFill[i].raycastTarget = false;
            statValue[i] = Label(stats, "", 14, Bright, TextAlignmentOptions.TopRight, new Vector2(StatBar + 30f, y), new Vector2(72f, 20f), true);
        }
    }

    private void ShowHeader(string name, Color color, int rarity, string note)
    {
        Set(infoName, name);
        portrait.color = color;
        Color level = TowerView.ColorOfLevel(Mathf.Max(1, rarity));
        for (int i = 0; i < 3; i++)
        {
            bool shown = rarity > 0;
            if (pips[i].gameObject.activeSelf != shown) pips[i].gameObject.SetActive(shown);
            pips[i].sprite = HudArt.Icon(i < rarity ? "dot" : "ring");
            pips[i].color = i < rarity ? level : HudOverlay.Edge;
        }
        string head = rarity > 0 ? "<color=#" + ColorUtility.ToHtmlStringRGB(level) + ">" + TowerView.LevelName(rarity).Substring(0, 1) + TowerView.LevelName(rarity).Substring(1).ToLowerInvariant() + "</color>" : "";
        if (note.Length > 0) head += (head.Length > 0 ? "  \u00b7  " : "") + note;
        Set(infoRarity, head);
        infoRarity.rectTransform.anchoredPosition = new Vector2(rarity > 0 ? 128f : 80f, -41f);
    }

    // Damage a hit, shots a second, range: a bar each against a fixed top, the number beside it.
    // Hidden for what does not shoot (a pylon, a collector, the core).
    private void ShowStats(TowerState t)
    {
        bool shown = t != null && t.PowerCapacity == 0;
        if (statsRoot.activeSelf != shown) statsRoot.SetActive(shown);
        infoText.rectTransform.anchoredPosition = new Vector2(14f, shown ? -156f : -78f);
        infoText.rectTransform.sizeDelta = new Vector2(302f, shown ? 96f : 174f);
        if (!shown) return;
        float rate = 50f / Mathf.Max(1, t.FireIntervalTicks);
        float[] share = { t.Damage * t.Targets / 40f, rate / 10f, t.RangeTiles / 9f };
        string[] text = { t.Damage.ToString("0.#") + (t.Targets > 1 ? " \u00d7" + t.Targets : ""), rate.ToString("0.#") + "/s", t.RangeTiles.ToString("0.#") };
        for (int i = 0; i < 3; i++)
        {
            statFill[i].rectTransform.sizeDelta = new Vector2(StatBar * Mathf.Clamp01(share[i]), 6f);
            Set(statValue[i], text[i]);
        }
    }

    private static string PathName(TowerState t)
    {
        TowerLevelMod[] list = TowerUpgradeChoices.For(t.Def);
        return TowerUpgradeChoices.IsValidFor(t.Def, t.Upgrade) && list[t.Upgrade].Path > 0 ? list[t.Upgrade].PathName : "";
    }

    // Under the stat bars: what else the tower does, its upgrades, gear, power and target.
    private string Detail(TowerState t)
    {
        var text = new System.Text.StringBuilder();
        var tags = new List<string>();
        if (t.PowerCapacity > 0) tags.Add("powers " + t.PowerCapacity + " towers within " + t.PowerRadius.ToString("0.#"));
        else
        {
            tags.Add(t.Def.DamageType.ToString().ToLowerInvariant());
            if (t.CanHitFlying) tags.Add("hits flyers");
            if (t.SplashSquared > 0f) tags.Add("splash " + (Mathf.Sqrt(t.SplashSquared) / Mathf.Max(0.01f, Host.Map.NodeDiameter)).ToString("0.##"));
            if (t.SlowTicks > 0) tags.Add("slows to " + (t.SlowFactor * 100f).ToString("0") + "%");
            if (t.CritEvery > 0) tags.Add("every " + t.CritEvery + (t.CritEvery == 2 ? "nd" : t.CritEvery == 3 ? "rd" : "th") + " shot \u00d7" + t.CritMul.ToString("0.#"));
            if (t.FlyingDamageMul != 1f) tags.Add("\u00d7" + t.FlyingDamageMul.ToString("0.#") + " to flyers");
            if (t.BurnTicks > 0) tags.Add("burns " + t.BurnDps.ToString("0.#") + "/s");
            if (t.ExposeTicks > 0) tags.Add("+" + ((t.ExposeMul - 1f) * 100f).ToString("0") + "% damage taken");
            if (t.ExecuteBelow > 0f) tags.Add("finishes under " + (t.ExecuteBelow * 100f).ToString("0") + "%");
            if (t.IgnoresResist) tags.Add("ignores armour");
        }
        if (t.Lure) tags.Add("draws enemies");
        if (t.Beacon) tags.Add("+1 ore nearby");
        text.Append("<color=#cbd5e1>").Append(string.Join("  \u00b7  ", tags)).Append("</color>\n");
        if (playerBuilder.UpgradesOn)
        {
            string names = PlayerBuilder.UpgradeNames(t);
            text.Append("<color=#94a3b8>upgrades</color> ").Append(names.Length > 0 ? names : "none yet").Append('\n');
        }
        if (t.PowerCapacity == 0) text.Append("<color=#94a3b8>gear</color> ").Append(GearLine(t)).Append('\n');
        if (playerBuilder.PowerRule && !t.OnBench && !t.Powered) text.Append("<color=#f87171>no power: out of reach, or the sources are full</color>\n");
        if (playerBuilder.TargetingOn && t.PowerCapacity == 0) text.Append("<color=#94a3b8>shoots</color> ").Append(PlayerBuilder.ModeName(t.Mode));
        return text.ToString();
    }

    private string Describe(TowerState t)
    {
        TowerDef d = t.Def;
        string hex = ColorUtility.ToHtmlStringRGB(TowerView.ColorOfLevel(t.Rarity));
        var text = new System.Text.StringBuilder();
        text.Append("<size=24><b>").Append(Title(d.Id)).Append("</b></size>   <color=#").Append(hex).Append("><b>").Append(TowerView.LevelName(t.Rarity)).Append("</b></color>\n");
        if (t.PowerCapacity > 0)
        {
            text.Append("powers up to ").Append(t.PowerCapacity).Append(" towers within ").Append(t.PowerRadius.ToString("0.#")).Append(" tiles (the gold tiles)\nno attack of its own\n");
            if (playerBuilder.UpgradesOn)
            {
                string upgradeNames = PlayerBuilder.UpgradeNames(t);
                text.Append("<color=#94a3b8>upgrades:</color> ").Append(upgradeNames.Length > 0 ? upgradeNames : "none yet").Append('\n');
            }
            return text.ToString();
        }
        float perSecond = t.Damage * t.Targets * 50f / Mathf.Max(1, t.FireIntervalTicks);
        text.Append(d.DamageType.ToString().ToLowerInvariant()).Append("  ·  ").Append(t.Damage.ToString("0.#")).Append(" damage every ")
            .Append((t.FireIntervalTicks / 50f).ToString("0.##")).Append(" s  (").Append(perSecond.ToString("0.#")).Append(" a second)\n");
        text.Append("range ").Append(t.RangeTiles.ToString("0.#")).Append(" tiles  ·  ").Append(t.CanHitFlying ? "hits flyers" : "ground only").Append('\n');
        if (t.Targets > 1) text.Append("hits ").Append(t.Targets).Append(" enemies at once\n");
        if (t.SplashSquared > 0f) text.Append("splash ").Append((Mathf.Sqrt(t.SplashSquared) / Mathf.Max(0.01f, Host.Map.NodeDiameter)).ToString("0.##")).Append(" tiles\n");
        if (t.SlowTicks > 0) text.Append("slows to ").Append((t.SlowFactor * 100f).ToString("0")).Append("% for ").Append((t.SlowTicks / 50f).ToString("0.#")).Append(" s\n");
        text.Append("<color=#94a3b8>gear (").Append(t.GearSlots).Append(t.GearSlots == 1 ? " slot" : " slots").Append("):</color> ").Append(GearLine(t)).Append('\n');
        if (playerBuilder.UpgradesOn)
        {
            string names = PlayerBuilder.UpgradeNames(t);
            text.Append("<color=#94a3b8>upgrades:</color> ").Append(names.Length > 0 ? names : "none yet");
            if (playerBuilder.HasUpgradeLeft(t)) text.Append("  (next: ").Append(TowerView.LevelName(t.Rarity + 1).ToLowerInvariant()).Append(", ").Append(playerBuilder.UpgradeCostFor(t).ToString("0.#")).Append(", key U)");
            text.Append('\n');
        }
        if (playerBuilder.PowerRule && !t.OnBench)
            text.Append("<color=#94a3b8>power:</color> ").Append(t.Powered ? "on" : "<color=#f87171>OFF: out of reach of the core or a collector, or they are full</color>").Append('\n');
        if (playerBuilder.TargetingOn) text.Append("<color=#94a3b8>shoots:</color> ").Append(PlayerBuilder.ModeName(t.Mode)).Append("  (key T)").Append('\n');
        if (!Host.Config.FeedDuplicates) return text.ToString();
        if (t.Level < TowerState.MaxLevel)
            text.Append("<color=#94a3b8>next level:</color> ").Append(d.LevelMod(t.Level + 1).Label).Append("\n<color=#94a3b8>merge ").Append(playerBuilder.MergeCount).Append(' ').Append(TowerView.LevelName(t.Level).ToLowerInvariant()).Append(' ').Append(d.Id).Append("s: drag one onto another</color>");
        else text.Append("<color=#94a3b8>top level</color>");
        return text.ToString();
    }

    private string GearLine(TowerState tower)
    {
        string line = "";
        for (int g = 0; g < TowerState.MaxGearSlots; g++)
        {
            if (tower.Gear[g] == TowerState.NoGear) continue;
            if (line.Length > 0) line += ", ";
            line += Host.Sim.GearLibrary[tower.Gear[g]].Id;
        }
        return line.Length > 0 ? line : "no gear";
    }

    private void UpdateDragIcon()
    {
        bool show = Dragging && Mouse.current != null && PointerOverHud();
        if (dragIcon.gameObject.activeSelf != show) dragIcon.gameObject.SetActive(show);
        if (!show) return;

        Color color = Bright;
        if (playerBuilder.Tool == BuildTool.Deploy && playerBuilder.Benched(playerBuilder.SelectedBench) != null) color = TowerView.ColorOfTower(playerBuilder.Benched(playerBuilder.SelectedBench).Def);
        else if (playerBuilder.Tool == BuildTool.EquipGear && playerBuilder.SelectedOverflow >= 0 && playerBuilder.SelectedOverflow < Host.State.GearOverflow.Count) color = TowerView.ColorOfGear(Host.State.GearOverflow[playerBuilder.SelectedOverflow]);
        else if (playerBuilder.Tool == BuildTool.EquipGear && playerBuilder.SelectedInventory >= 0) color = TowerView.ColorOfGear(Host.State.GearInventory[playerBuilder.SelectedInventory]);
        else if (playerBuilder.Tool == BuildTool.Tower && playerBuilder.OfferDef(playerBuilder.SelectedOffer) != null) color = TowerView.ColorOfTower(playerBuilder.OfferDef(playerBuilder.SelectedOffer));
        color.a = 0.85f;
        dragImage.color = color;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, Mouse.current.position.ReadValue(), null, out Vector2 local)) dragIcon.anchoredPosition = local;
    }

    // ------------------------------------------------------------------ small helpers

    private Action Button(Transform parent, string text, Vector2 at, float buttonWidth, UnityEngine.Events.UnityAction onClick)
    {
        var action = new Action();
        RectTransform root = Rect("Button", parent);
        root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
        root.pivot = new Vector2(0f, 1f);
        root.anchoredPosition = at;
        root.sizeDelta = new Vector2(buttonWidth, 34f);
        action.root = root.gameObject;
        action.outline = root.gameObject.AddComponent<Image>();
        action.outline.color = Teal;
        action.group = root.gameObject.AddComponent<CanvasGroup>();
        RectTransform inner = Rect("Fill", root);
        inner.anchorMin = Vector2.zero;
        inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(2f, 2f);
        inner.offsetMax = new Vector2(-2f, -2f);
        Image fill = inner.gameObject.AddComponent<Image>();
        fill.color = CardColor;
        fill.raycastTarget = false;
        action.label = Label(root, text, 15, Gold, TextAlignmentOptions.Center, Vector2.zero, new Vector2(buttonWidth, 34f), true);
        action.label.rectTransform.anchorMin = Vector2.zero;
        action.label.rectTransform.anchorMax = Vector2.one;
        action.label.rectTransform.offsetMin = action.label.rectTransform.offsetMax = Vector2.zero;
        Button button = root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(onClick);
        return action;
    }

    private static void Enable(Action action, bool on)
    {
        action.group.alpha = on ? 1f : 0.4f;
        action.outline.color = on ? Teal : Edge;
    }

    private static string Title(string id)
    {
        return string.IsNullOrEmpty(id) ? "" : char.ToUpperInvariant(id[0]) + id.Substring(1);
    }

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
        // A text too long for its box shrinks to fit, and past that is cut with dots; it never runs over its neighbour.
        label.enableAutoSizing = true;
        label.fontSizeMax = size;
        label.fontSizeMin = Mathf.Max(10f, size * 0.6f);
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        return label;
    }
}
