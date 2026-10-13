using System.Collections.Generic;
using Thesis.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The left-hand panel (2026-10-13): what a turn allows. Moves left and the button that
// buys one more, wall tiles left and the button that buys a pack, the bench, and,
// before the first wave, the core.
//
// Like TowerShopPanel it builds its own UI under the Canvas when the game starts,
// shows state every frame and forwards clicks to PlayerBuilder; every rule is the
// simulation's. Sections whose rule is switched off in the config are not shown.
public class BenchPanel : MonoBehaviour
{
    public PlayerBuilder playerBuilder;
    public SimHost simHost;

    [Header("Layout, in the Canvas's reference pixels")]
    public float width = 300f;
    public float topInset = 96f;
    public float bottomInset = 196f;

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
        public Image outline, fill, icon;
        public TextMeshProUGUI name, detail;
    }

    private class Action
    {
        public GameObject root;
        public Image outline;
        public TextMeshProUGUI label;
        public CanvasGroup group;
    }

    private RectTransform panel, coreBlock, movesBlock, wallsBlock, benchBlock;
    private TextMeshProUGUI coreText, movesValue, movesNote, wallsValue, wallsNote, benchNote;
    private Action confirmCore, buyMove, buyWalls;
    private readonly List<Slot> slots = new List<Slot>();

    private const float SlotHeight = 58f;

    private SimHost Host => simHost != null ? simHost : (simHost = SimHost.Find());

    void Start()
    {
        if (playerBuilder == null) playerBuilder = FindFirstObjectByType<PlayerBuilder>();
        Build();
    }

    private void Build()
    {
        panel = Rect("BenchPanel", transform);
        panel.anchorMin = new Vector2(0f, 0f);
        panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 0.5f);
        panel.offsetMin = new Vector2(14f, bottomInset);
        panel.offsetMax = new Vector2(14f + width, -topInset);
        panel.gameObject.AddComponent<Image>().color = PanelColor;
        panel.SetAsFirstSibling();

        coreBlock = Block("Core", 120f);
        Label(coreBlock, "YOUR CORE", 20, Bright, TextAlignmentOptions.TopLeft, new Vector2(16f, -10f), new Vector2(width - 32f, 26f), true);
        coreText = Label(coreBlock, "", 15, Dim, TextAlignmentOptions.TopLeft, new Vector2(16f, -36f), new Vector2(width - 32f, 40f), false);
        coreText.textWrappingMode = TextWrappingModes.Normal;
        confirmCore = Button(coreBlock, "KEEP IT HERE", new Vector2(16f, -78f), width - 32f, () => playerBuilder.ConfirmCore());

        movesBlock = Block("Moves", 104f);
        Label(movesBlock, "MOVES", 17, Dim, TextAlignmentOptions.TopLeft, new Vector2(16f, -8f), new Vector2(120f, 24f), true);
        movesValue = Label(movesBlock, "", 28, Bright, TextAlignmentOptions.TopRight, new Vector2(-16f, -2f), new Vector2(140f, 34f), true, true);
        movesNote = Label(movesBlock, "", 14, Dim, TextAlignmentOptions.TopLeft, new Vector2(16f, -34f), new Vector2(width - 32f, 22f), false);
        buyMove = Button(movesBlock, "", new Vector2(16f, -60f), width - 32f, () => playerBuilder.BuyMove());

        wallsBlock = Block("Walls", 104f);
        Label(wallsBlock, "WALL TILES", 17, Dim, TextAlignmentOptions.TopLeft, new Vector2(16f, -8f), new Vector2(140f, 24f), true);
        wallsValue = Label(wallsBlock, "", 28, Bright, TextAlignmentOptions.TopRight, new Vector2(-16f, -2f), new Vector2(140f, 34f), true, true);
        wallsNote = Label(wallsBlock, "", 14, Dim, TextAlignmentOptions.TopLeft, new Vector2(16f, -34f), new Vector2(width - 32f, 22f), false);
        buyWalls = Button(wallsBlock, "", new Vector2(16f, -60f), width - 32f, () => playerBuilder.BuyWalls());

        benchBlock = Block("Bench", 60f);
        Label(benchBlock, "BENCH", 17, Dim, TextAlignmentOptions.TopLeft, new Vector2(16f, -8f), new Vector2(120f, 24f), true);
        benchNote = Label(benchBlock, "", 14, Dim, TextAlignmentOptions.TopLeft, new Vector2(16f, -30f), new Vector2(width - 32f, 22f), false);
    }

    private Slot AddSlot(int index)
    {
        var slot = new Slot();
        RectTransform root = Rect("Bench " + (index + 1), benchBlock);
        root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
        root.pivot = new Vector2(0f, 1f);
        root.anchoredPosition = new Vector2(16f, -56f - index * (SlotHeight + 6f));
        root.sizeDelta = new Vector2(width - 32f, SlotHeight);
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
        RectTransform icon = Rect("Icon", root);
        icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
        icon.pivot = new Vector2(0.5f, 0.5f);
        icon.anchoredPosition = new Vector2(28f, 0f);
        icon.sizeDelta = new Vector2(30f, 30f);
        slot.icon = icon.gameObject.AddComponent<Image>();
        slot.icon.raycastTarget = false;
        slot.name = Label(root, "", 19, Bright, TextAlignmentOptions.TopLeft, new Vector2(54f, -6f), new Vector2(width - 100f, 26f), true);
        slot.detail = Label(root, "", 14, Dim, TextAlignmentOptions.TopLeft, new Vector2(54f, -31f), new Vector2(width - 100f, 22f), false);
        Button button = root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => playerBuilder.ClickBench(index));
        return slot;
    }

    void Update()
    {
        if (panel == null || Host == null || playerBuilder == null) return;
        SimState state = Host.State;
        SimConfig config = Host.Config;
        float y = 6f;

        // The core: only before the first wave, until the player says it stays.
        bool choosing = playerBuilder.ChoosingCore;
        coreBlock.gameObject.SetActive(choosing);
        if (choosing)
        {
            Place(coreBlock, ref y);
            Set(coreText, "Click a tile on the board to move it. Enemies will come from the edges.");
        }

        bool moves = playerBuilder.MovesLimited;
        movesBlock.gameObject.SetActive(moves);
        if (moves)
        {
            Place(movesBlock, ref y);
            bool free = playerBuilder.MovesAreFree;
            Set(movesValue, free ? "free" : state.MovesLeft.ToString());
            movesValue.color = free || state.MovesLeft > 0 ? Bright : Dim;
            Set(movesNote, free ? "before wave 1 nothing is limited" : "move, sell or swap a tower on the board");
            bool sold = !free && config.ExtraMovePrice >= 0f;
            buyMove.root.SetActive(sold);
            if (sold)
            {
                Set(buyMove.label, "+1 MOVE    " + Host.Sim.ExtraMovePrice.ToString("0.#"));
                Enable(buyMove, playerBuilder.CanBuyMove);
            }
        }

        bool walls = playerBuilder.WallsFromStock;
        wallsBlock.gameObject.SetActive(walls);
        if (walls)
        {
            Place(wallsBlock, ref y);
            Set(wallsValue, state.WallsLeft.ToString());
            wallsValue.color = state.WallsLeft > 0 ? Bright : Dim;
            Set(wallsNote, "old walls you may still take up: " + state.RelaysLeft);
            bool sold = config.WallPackSize > 0;
            buyWalls.root.SetActive(sold);
            if (sold)
            {
                Set(buyWalls.label, "+" + config.WallPackSize + " WALLS    " + config.WallPackPrice.ToString("0.#"));
                Enable(buyWalls, playerBuilder.CanBuyWalls);
            }
        }

        int count = state.Bench.Length;
        benchBlock.gameObject.SetActive(count > 0);
        if (count > 0)
        {
            benchBlock.sizeDelta = new Vector2(0f, 60f + count * (SlotHeight + 6f));
            Place(benchBlock, ref y);
            bool card = playerBuilder.Tool == BuildTool.Tower;
            Set(benchNote, card ? "click a slot to keep the card here" : playerBuilder.Tool == BuildTool.Deploy ? "now click a tile on the board" : "towers you own, not on the board");
            while (slots.Count < count) slots.Add(AddSlot(slots.Count));
            for (int i = 0; i < count; i++)
            {
                Slot slot = slots[i];
                TowerState tower = playerBuilder.Benched(i);
                bool inHand = playerBuilder.Tool == BuildTool.Deploy && playerBuilder.SelectedBench == i;
                slot.outline.color = inHand ? Teal : Edge;
                slot.fill.color = inHand ? CardSelected : CardColor;
                slot.icon.enabled = tower != null;
                if (tower == null)
                {
                    Set(slot.name, "");
                    Set(slot.detail, "empty");
                    continue;
                }
                slot.icon.color = TowerView.ColorOf(tower.Def.DamageType);
                Set(slot.name, Title(tower.Def.Id));
                slot.name.color = tower.Level > 1 ? TowerView.ColorOfLevel(tower.Level) : Bright;
                string detail = TowerView.LevelName(tower.Level).ToLowerInvariant();
                for (int g = 0; g < TowerState.MaxGearSlots; g++)
                    if (tower.Gear[g] != TowerState.NoGear) detail += "  +" + Host.Sim.GearLibrary[tower.Gear[g]].Id;
                Set(slot.detail, detail);
            }
        }
    }

    // ------------------------------------------------------------------ small helpers

    private void Place(RectTransform block, ref float fromTop)
    {
        block.anchoredPosition = new Vector2(0f, -fromTop);
        fromTop += block.sizeDelta.y + 6f;
    }

    private RectTransform Block(string name, float height)
    {
        RectTransform rect = Rect(name, panel);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0f, height);
        return rect;
    }

    private Action Button(Transform parent, string text, Vector2 at, float buttonWidth, UnityEngine.Events.UnityAction onClick)
    {
        var action = new Action();
        RectTransform root = Rect("Button", parent);
        root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
        root.pivot = new Vector2(0f, 1f);
        root.anchoredPosition = at;
        root.sizeDelta = new Vector2(buttonWidth, 36f);
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
        action.label = Label(root, text, 17, Gold, TextAlignmentOptions.Center, Vector2.zero, new Vector2(buttonWidth, 36f), true);
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
