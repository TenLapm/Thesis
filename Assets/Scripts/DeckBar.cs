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
        Label(bar, "MOVES", 16, Dim, TextAlignmentOptions.TopLeft, new Vector2(16f, -10f), new Vector2(90f, 22f), true);
        movesValue = Label(bar, "", 30, Bright, TextAlignmentOptions.TopLeft, new Vector2(16f, -28f), new Vector2(60f, 36f), true);
        buyMove = Button(bar, "", new Vector2(84f, -30f), 150f, () => playerBuilder.BuyMove());
        Label(bar, "WALL TILES", 16, Dim, TextAlignmentOptions.TopLeft, new Vector2(16f, -78f), new Vector2(120f, 22f), true);
        wallsValue = Label(bar, "", 30, Bright, TextAlignmentOptions.TopLeft, new Vector2(16f, -96f), new Vector2(60f, 36f), true);
        buyWalls = Button(bar, "", new Vector2(84f, -98f), 150f, () => playerBuilder.BuyWalls());
        wallsNote = Label(bar, "", 13, Dim, TextAlignmentOptions.TopLeft, new Vector2(16f, -140f), new Vector2(230f, 20f), false);
        coreText = Label(bar, "", 13, Gold, TextAlignmentOptions.TopLeft, new Vector2(16f, -160f), new Vector2(110f, 34f), false);
        confirmCore = Button(bar, "CORE STAYS", new Vector2(120f, -160f), 114f, () => playerBuilder.ConfirmCore());

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

        // The card that explains the tower under the mouse.
        info = Rect("TowerInfo", transform);
        info.anchorMin = info.anchorMax = new Vector2(0f, 1f);
        info.pivot = new Vector2(0f, 1f);
        info.anchoredPosition = new Vector2(14f, -96f);
        info.sizeDelta = new Vector2(330f, 250f);
        Image infoBack = info.gameObject.AddComponent<Image>();
        infoBack.color = new Color(0.04f, 0.06f, 0.1f, 0.94f);
        infoBack.raycastTarget = false;
        infoText = Label(info, "", 17, Bright, TextAlignmentOptions.TopLeft, new Vector2(14f, -12f), new Vector2(302f, 230f), false);
        infoText.textWrappingMode = TextWrappingModes.Normal;
        infoText.richText = true;
        info.gameObject.SetActive(false);

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

        if (playerBuilder.Tool == BuildTool.Deploy || playerBuilder.Tool == BuildTool.EquipGear || playerBuilder.Tool == BuildTool.Tower) playerBuilder.SelectWall();
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

        // Moves and walls.
        bool moves = playerBuilder.MovesLimited;
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
        Set(wallsValue, walls ? state.WallsLeft.ToString() : "");
        wallsValue.color = state.WallsLeft > 0 ? Bright : Dim;
        Set(wallsNote, !walls ? "" : config.FreeWallPickup ? "take any wall up again with SELL [X]" : "old walls you may still take up: " + state.RelaysLeft);
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
            slot.icon.color = TowerView.ColorOf(tower.Def.DamageType);
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

        UpdateSpare(state);
        UpdateInfo();
        UpdateDragIcon();
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

    // The full card of the tower under the mouse: on the board, or on the bench.
    private void UpdateInfo()
    {
        TowerState tower = hoverBench >= 0 ? playerBuilder.Benched(hoverBench) : playerBuilder.TowerAt(playerBuilder.HoverNode);
        bool show = tower != null && !Dragging;
        if (info.gameObject.activeSelf != show) info.gameObject.SetActive(show);
        if (show) Set(infoText, Describe(tower));
    }

    private string Describe(TowerState t)
    {
        TowerDef d = t.Def;
        string hex = ColorUtility.ToHtmlStringRGB(TowerView.ColorOfLevel(t.Rarity));
        var text = new System.Text.StringBuilder();
        text.Append("<size=24><b>").Append(Title(d.Id)).Append("</b></size>   <color=#").Append(hex).Append("><b>").Append(TowerView.LevelName(t.Rarity)).Append("</b></color>\n");
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
        if (playerBuilder.Tool == BuildTool.Deploy && playerBuilder.Benched(playerBuilder.SelectedBench) != null) color = TowerView.ColorOf(playerBuilder.Benched(playerBuilder.SelectedBench).Def.DamageType);
        else if (playerBuilder.Tool == BuildTool.EquipGear && playerBuilder.SelectedOverflow >= 0 && playerBuilder.SelectedOverflow < Host.State.GearOverflow.Count) color = TowerView.ColorOfGear(Host.State.GearOverflow[playerBuilder.SelectedOverflow]);
        else if (playerBuilder.Tool == BuildTool.EquipGear && playerBuilder.SelectedInventory >= 0) color = TowerView.ColorOfGear(Host.State.GearInventory[playerBuilder.SelectedInventory]);
        else if (playerBuilder.Tool == BuildTool.Tower && playerBuilder.OfferDef(playerBuilder.SelectedOffer) != null) color = TowerView.ColorOf(playerBuilder.OfferDef(playerBuilder.SelectedOffer).DamageType);
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
