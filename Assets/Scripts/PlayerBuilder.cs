using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

// What the player holds in the hand (WP-C4). One at a time; Esc puts it down.
public enum BuildTool
{
    None,
    Wall,   // click a tile, or drag to draw a line of wall tiles
    Sell,   // click a tower or a wall tile; drag over wall tiles
    Tower,  // a shop offer picked from its card, waiting for a tile
    Gear,     // a gear card picked from the shop, waiting for a tower
    MoveTower, // click a tower to pick it up, then the free tile it goes to
    MoveGear, // click a tower to pick its gear up, then the tower it goes to
    Core,     // before the first wave: click the tile the core goes on
    Deploy,   // a tower picked up from the bench, waiting for a tile (or a tower to change places with)
    EquipGear, // a piece picked up from the gear inventory, waiting for a tower
}

// Player input for building, and the visuals of what is built.
//
// Input never changes the board itself. A click or a drag becomes commands
// (PlaceShape, PlaceTower, SellTower, SellWall); the simulation checks legality and
// budget, spends, sets the terrain and rebuilds the fields (Thesis.Sim.Placement).
// What is on screen is then DRAWN from the simulation's own record - new build-log
// rows and the breach events - so it is always what the simulation holds, whoever
// put it there (the mouse today, a replay tomorrow).
//
// Since WP-C4 a wall is one tile (GDD §4.1). The simulation still deals "pieces" from
// a bag; the scene's shape library simply holds one shape of one tile, so every piece
// is that tile, a drag is one PlaceShape per tile, and a sale is one tile. The keys of
// WP-C1 and WP-C3 (Z X C B to buy, Delete to sell, R and Shift for the piece) are gone:
// the shop's cards and the two tools took their place (TowerShopPanel).
public class PlayerBuilder : MonoBehaviour
{
    [Header("System References")]
    public GridManager gridManager;
    public BlockManager blockManager;
    public PlayerCore playerCore;
    [Tooltip("Used to forbid building while the game is paused - pause is purely a look-and-think state, not a build-while-frozen state.")]
    public GameSpeedController speedController;
    public SimHost simHost;

    [Header("Prefabs & Layer")]
    public GameObject standardWallPrefab;
    public LayerMask clickLayer;

    [Header("Look")]
    public Color wallColor = new Color(0.65f, 0.71f, 0.99f);
    [Tooltip("Height of a wall as a share of a tile's width. Low walls do not hide the enemies behind them when the board is seen at a tilt.")]
    [Range(0.1f, 1f)] public float wallHeight = 1f;
    [Tooltip("Towers are drawn as flat figures that stand upright and face the camera (the tilted trial scene).")]
    public bool billboards = false;

    // The bottom bar of the tilted trial scene, when the scene has one. With it, gear
    // goes through the inventory and towers can be dragged off the board.
    [System.NonSerialized] public DeckBar deckBar;

    // --- the hand ---

    public BuildTool Tool { get; private set; } = BuildTool.Wall;

    // The shop offer in the hand while Tool is Tower; -1 otherwise.
    public int SelectedOffer { get; private set; } = -1;

    // The tile under the mouse, or null when the mouse is over the HUD or off the
    // board. Refreshed every frame; the ghost and the tooltip read it.
    public Node HoverNode { get; private set; }

    // Reused across placements to avoid allocating a new block every wall.
    private MaterialPropertyBlock wallPropertyBlock;
    private bool isGameOver = false;

    private int placementsDrawn;
    private readonly List<Node> wallTiles = new List<Node>();
    private readonly List<TileCoord> tileBuffer = new List<TileCoord>();
    private readonly List<Vector2Int> oneTile = new List<Vector2Int>(1);
    private readonly Dictionary<int, TowerView> towerViews = new Dictionary<int, TowerView>();

    // A drag is one press that began on the board. The last tile it acted on, so a
    // tile is tried once however long the mouse rests on it.
    private bool dragging;
    private bool towerDrag;
    private Node lastDragNode;

    private SimHost Host => simHost != null ? simHost : (simHost = SimHost.Find());

    // True while the player is not allowed to build. Shared with GhostPreviewer
    // so the hover preview hides itself instead of dangling a "valid" ghost over
    // a click that would do nothing.
    public bool IsBuildingBlocked
    {
        get { return isGameOver || (speedController != null && speedController.IsPaused); }
    }

    public bool IsBuildPhase
    {
        get
        {
            SimPhase p = Host.State.Phase;
            return p == SimPhase.Prep || p == SimPhase.Intermission;
        }
    }

    // Selling is for the time between waves (SimConfig.SellDuringWave).
    public bool MaySell => IsBuildPhase || Host.Config.SellDuringWave;

    public void SelectWall() { Hold(BuildTool.Wall); }

    public void SelectSell() { Hold(BuildTool.Sell); }

    public void PutDown() { Hold(BuildTool.None); }

    public void SelectMoveTower() { Hold(BuildTool.MoveTower); }

    public void SelectMoveGear() { Hold(BuildTool.MoveGear); }

    private void Hold(BuildTool tool)
    {
        Tool = tool;
        SelectedOffer = -1;
        SelectedShopGear = -1;
        GearFrom = null;
        TowerFrom = null;
        SelectedBench = -1;
        SelectedInventory = -1;
        SelectedOverflow = -1;
    }

    // --- the core, the bench, moves and the wall stock (2026-10-13) ---

    // --- the gear inventory (2026-10-14) ---

    public bool UsesInventory => deckBar != null && Host.Config.GearInventorySlots > 0;

    // The inventory slot in the hand while Tool is EquipGear; -1 otherwise.
    public int SelectedInventory { get; private set; } = -1;

    // A click on an inventory slot: pick the piece up; or, with a tower's gear in the
    // hand (the Gear tool), take that piece off the tower into the inventory.
    public void ClickInventory(int slot)
    {
        int[] inventory = Host.State.GearInventory;
        if (slot < 0 || slot >= inventory.Length || IsBuildingBlocked) return;
        if (Tool == BuildTool.MoveGear && GearFrom != null && TowerAt(GearFrom) != null)
        {
            Host.Submit(SimCommand.UnequipGear(GearFrom.gridX, GearFrom.gridY, FirstGearSlot(TowerAt(GearFrom))));
            GearFrom = null;
            return;
        }
        if (Tool == BuildTool.EquipGear && SelectedInventory == slot) { SelectWall(); return; }
        if (inventory[slot] == TowerState.NoGear) return;
        Hold(BuildTool.EquipGear);
        SelectedInventory = slot;
    }

    // What a drag let go over a tile does: the tool in the hand, used once on that tile.
    public void UseToolAt(Node node) { if (node != null && !IsBuildingBlocked) UseTool(node, true); }

    // The tile under the mouse whether or not the HUD is in the way.
    public Node NodeUnderPointer() { return Mouse.current != null ? NodeUnderMouse() : null; }

    // The tower type and level of what is in the hand, for the figure the preview shows; null when it is no tower.
    public TowerDef HandTower(out int level)
    {
        level = 1;
        if (Tool == BuildTool.Tower && OfferDef(SelectedOffer) != null) { level = Host.State.Shop.Offers[SelectedOffer].Level; return OfferDef(SelectedOffer); }
        TowerState held = Tool == BuildTool.Deploy ? Benched(SelectedBench) : Tool == BuildTool.MoveTower && TowerFrom != null ? TowerAt(TowerFrom) : null;
        if (held == null) return null;
        level = held.Level;
        return held.Def;
    }

    // The bench slot in the hand while Tool is Deploy; -1 otherwise.
    public int SelectedBench { get; private set; } = -1;

    // Before the first wave the player may put the core somewhere else. Until they
    // say they are done, that is the one thing the screen asks for.
    public bool CoreConfirmed { get; private set; }

    public bool ChoosingCore => Host.Config.CorePlacement && Host.State.WaveIndex == 0 && IsBuildPhase && !CoreConfirmed;

    public void ConfirmCore()
    {
        CoreConfirmed = true;
        SelectWall();
    }

    // The simulation's rule for a core tile (Simulation.PlaceCore), for the preview.
    public bool CanPlaceCore(Node node)
    {
        if (node == null || !ChoosingCore) return false;
        SimNode sim = Host.State.Grid[node.gridX, node.gridY];
        MapData map = Host.Map;
        return sim.IsWalkable && sim.Occupant == Occupant.None
               && map.EdgeDistance(node.gridX, node.gridY) >= Mathf.Max(map.BuildMargin, Host.Config.CoreEdgeMargin);
    }

    public bool MovesLimited => Host.Config.MovesPerWave > 0;

    public bool HasMove => !MovesLimited || MovesAreFree || Host.State.MovesLeft > 0;

    // Before the first wave nothing is limited (SimConfig.FreeFirstBuild).
    public bool MovesAreFree => Host.Sim.MovesAreFree;

    // Until the first wave starts the core can be picked up again.
    public bool MayReopenCore => Host.Config.CorePlacement && Host.State.WaveIndex == 0 && IsBuildPhase && CoreConfirmed && !IsBuildingBlocked;

    public void ReopenCore()
    {
        if (!MayReopenCore) return;
        CoreConfirmed = false;
        Hold(BuildTool.Core);
    }

    // While the core is being placed: a gold frame round the part of the board it may
    // stand on, and the core itself pulsing, so it is plain what is being asked.
    private LineRenderer coreZone;
    private Vector3 coreScale;

    private void ShowCoreZone()
    {
        bool on = !isGameOver && ChoosingCore;
        Transform goal = Host.flowManager != null ? Host.flowManager.targetGoal : null;
        if (goal != null)
        {
            if (coreScale == Vector3.zero) coreScale = goal.localScale;
            goal.localScale = on ? coreScale * (1.25f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f)) : coreScale;
        }
        if (coreZone == null)
        {
            if (!on) return;
            float cell = gridManager.nodeRadius * 2f;
            int inset = Mathf.Max(Host.Map.BuildMargin, Host.Config.CoreEdgeMargin);
            float halfX = gridManager.gridWorldSize.x / 2f - inset * cell, halfZ = gridManager.gridWorldSize.y / 2f - inset * cell;
            Vector3 c = gridManager.transform.position + Vector3.up * 0.12f;
            coreZone = TowerView.Line(transform, "CoreZone", new Color(0.98f, 0.75f, 0.14f, 0.9f), 0.3f, true);
            coreZone.positionCount = 4;
            coreZone.SetPositions(new[] { c + new Vector3(-halfX, 0f, -halfZ), c + new Vector3(halfX, 0f, -halfZ), c + new Vector3(halfX, 0f, halfZ), c + new Vector3(-halfX, 0f, halfZ) });
        }
        coreZone.enabled = on;
    }

    public bool CanBuyMove => MovesLimited && Host.Config.ExtraMovePrice >= 0f && IsBuildPhase && Host.State.BuildBudget >= Host.Sim.ExtraMovePrice && !IsBuildingBlocked;

    public void BuyMove() { if (CanBuyMove) Host.Submit(SimCommand.BuyMove()); }

    public bool WallsFromStock => Host.Config.WallStock > 0;

    public bool CanBuyWalls => WallsFromStock && Host.Config.WallPackSize > 0 && Host.State.BuildBudget >= Host.Config.WallPackPrice && !IsBuildingBlocked;

    public void BuyWalls() { if (CanBuyWalls) Host.Submit(SimCommand.BuyWalls()); }

    // A wall laid in this build phase comes up again for nothing; an older one uses a pick-up.
    public bool IsFreshWall(Node node)
    {
        SimNode sim = Host.State.Grid[node.gridX, node.gridY];
        return sim.Occupant == Occupant.Wall && sim.PieceId >= Host.State.PhaseLogStart;
    }

    // The tower in a bench slot, or null.
    public TowerState Benched(int slot)
    {
        int[] bench = Host.State.Bench;
        return slot >= 0 && slot < bench.Length && bench[slot] != TowerBench.Empty ? Host.State.Towers[bench[slot]] : null;
    }

    // Whether the player owns a tower this card could be merged into.
    public bool CanFeedSomewhere(TowerOffer card)
    {
        if (!Host.Config.FeedDuplicates || card.Kind != CardKind.Tower) return false;
        TowerDef def = Host.Sim.TowerLibrary[card.TowerIndex];
        foreach (TowerState t in Host.State.Towers) if (t.IsAlive && t.Def == def && CanFeed(card, t)) return true;
        return false;
    }

    public bool CanFeed(TowerOffer card, TowerState tower)
    {
        return tower != null && card.Kind == CardKind.Tower && FedLevel(card, tower) > 0;
    }

    // The level the tower would have after this card is merged into it; 0 when it cannot be (TowerMerge).
    public int FedLevel(TowerOffer card, TowerState tower)
    {
        return TowerMerge.LevelAfterCard(Host.State, Host.Config, Host.Sim.TowerLibrary[card.TowerIndex], card.Level, tower);
    }

    // Whether dropping one of the player's towers on another merges them (TowerMerge).
    public bool CanMerge(TowerState source, TowerState target) { return TowerMerge.CanMerge(Host.State, Host.Config, source, target); }

    public int MergeCount => Host.Config.MergeCount;

    // --- spare gear: pieces that came back from a sold or merged tower and found the inventory full ---

    // The spare piece in the hand while Tool is EquipGear; -1 when the piece is from the inventory.
    public int SelectedOverflow { get; private set; } = -1;

    public void ClickOverflow(int index)
    {
        if (index < 0 || index >= Host.State.GearOverflow.Count || IsBuildingBlocked) return;
        Hold(BuildTool.EquipGear);
        SelectedOverflow = index;
    }

    public void SellOverflow(int index)
    {
        if (index < 0 || index >= Host.State.GearOverflow.Count || IsBuildingBlocked) return;
        if (Tool == BuildTool.EquipGear && SelectedOverflow >= 0) SelectWall();
        Host.Submit(SimCommand.SellOverflow(index));
    }

    // A click on a bench slot. What it does depends on what is in the hand.
    public void ClickBench(int slot)
    {
        SimState s = Host.State;
        if (slot < 0 || slot >= s.Bench.Length || IsBuildingBlocked) return;
        bool occupied = s.Bench[slot] != TowerBench.Empty;
        switch (Tool)
        {
            case BuildTool.Tower: // a card: onto the bench, or merged into the tower there
                Host.Submit(occupied ? SimCommand.FeedCardOnBench(SelectedOffer, slot) : SimCommand.BenchCard(SelectedOffer, slot));
                return;
            case BuildTool.Sell:
                if (occupied) Host.Submit(SimCommand.SellBenched(slot));
                return;
            case BuildTool.MoveTower:
                if (TowerFrom != null && TowerAt(TowerFrom) != null)
                {
                    if (occupied && CanMerge(TowerAt(TowerFrom), Benched(slot))) Host.Submit(SimCommand.MergeTower(TowerFrom.gridX, TowerFrom.gridY, -1, 0, 0, slot));
                    else
                    Host.Submit(occupied ? SimCommand.DeployTower(slot, TowerFrom.gridX, TowerFrom.gridY) : SimCommand.RecallTower(TowerFrom.gridX, TowerFrom.gridY, slot));
                    TowerFrom = null;
                    return;
                }
                break;
            case BuildTool.EquipGear:
                if (occupied && SelectedOverflow >= 0) { Host.Submit(SimCommand.EquipOverflowOnBench(SelectedOverflow, slot)); SelectWall(); }
                else if (occupied) Host.Submit(SimCommand.EquipGearOnBench(SelectedInventory, slot));
                return;
            case BuildTool.Deploy:
                if (SelectedBench == slot) { SelectWall(); return; }
                if (SelectedBench >= 0)
                {
                    bool merge = occupied && CanMerge(Benched(SelectedBench), Benched(slot));
                    Host.Submit(merge ? SimCommand.MergeTower(0, 0, SelectedBench, 0, 0, slot) : SimCommand.SwapBench(SelectedBench, slot));
                    SelectWall();
                    return;
                }
                break;
        }
        if (!occupied) return;
        Hold(BuildTool.Deploy);
        SelectedBench = slot;
    }

    // While moving a tower: the tile it was picked up from, or null before the first click.
    public Node TowerFrom { get; private set; }

    public bool MayMoveTower => Host.Config.MoveTowers && IsBuildPhase;

    // --- rerolling the cards ---

    public bool RerollsOn => Host.Config.RerollCost >= 0f;

    public float RerollPrice => Host.Sim.RerollPrice;

    public bool CanReroll => RerollsOn && Host.State.Shop.MayPick && Host.State.BuildBudget >= RerollPrice && !IsBuildingBlocked;

    public void Reroll()
    {
        if (!CanReroll) return;
        if (Tool == BuildTool.Tower || (Tool == BuildTool.Gear && SelectedShopGear < 0)) SelectWall();
        Host.Submit(SimCommand.RerollShop());
    }

    // --- how many towers stand, and how many may ---

    public int TowerCap => Host.Sim.TowerCap;

    public int TowersStanding => TowerCombine.Standing(Host.State.Towers);

    // Standing towers like this card's (same type, same level).
    public int CopiesOf(TowerOffer card) { return TowerCombine.Copies(Host.State.Towers, Host.Sim.TowerLibrary[card.TowerIndex], card.Level); }

    public bool WouldCombine(TowerOffer card) { return TowerCombine.WouldCombine(Host.State.Towers, Host.Sim.TowerLibrary[card.TowerIndex], card.Level, Host.Config); }

    public bool HasRoomFor(TowerOffer card) { return TowerCombine.HasRoomFor(Host.State.Towers, Host.Sim.TowerLibrary[card.TowerIndex], card.Level, Host.Config, Host.State.WaveIndex); }

    // The gear shop's slot in the hand while Tool is Gear and the piece comes from the
    // gear shop, not from a card; -1 otherwise.
    public int SelectedShopGear { get; private set; } = -1;

    // Picks a piece up from the gear shop. It goes onto the next tower clicked.
    public void SelectShopGear(int slot)
    {
        TowerOffer[] stock = Host.State.GearShop.Offers;
        if (slot < 0 || slot >= stock.Length) return;
        if (Tool == BuildTool.Gear && SelectedShopGear == slot) { SelectWall(); return; }
        if (stock[slot].Bought || Host.State.BuildBudget < stock[slot].Price) return;
        if (UsesInventory) { Host.Submit(SimCommand.StoreShopGear(slot)); return; } // bought into the inventory
        Tool = BuildTool.Gear;
        SelectedOffer = -1;
        SelectedShopGear = slot;
        GearFrom = null;
    }

    // While moving gear: the tower it was picked up from, or null before the first click.
    public Node GearFrom { get; private set; }

    // Picks a shop offer up. An offer that is bought, or that the budget does not
    // reach, cannot be picked up; clicking the offer already in the hand puts it down.
    public void SelectOffer(int slot)
    {
        TowerOffer[] offers = Host.State.Shop.Offers;
        if (slot < 0 || slot >= offers.Length) return;
        if ((Tool == BuildTool.Tower || Tool == BuildTool.Gear) && SelectedOffer == slot) { SelectWall(); return; }
        if (!CanPickUp(slot)) return;

        // A coin card is taken on the spot; a gear card goes into the hand like a tower.
        if (offers[slot].Kind == CardKind.Coin)
        {
            Host.Submit(SimCommand.TakeCoin(slot));
            return;
        }
        if (offers[slot].Kind == CardKind.Gear && UsesInventory)
        {
            Host.Submit(SimCommand.StoreGearCard(slot));
            return;
        }
        Tool = offers[slot].Kind == CardKind.Gear ? BuildTool.Gear : BuildTool.Tower;
        SelectedOffer = slot;
        SelectedShopGear = -1;
        GearFrom = null;
    }

    void Start()
    {
        if (Host != null && Host.Config.CorePlacement) Hold(BuildTool.Core);
    }

    void Awake()
    {
        if (playerCore != null)
        {
            playerCore.OnGameOver.AddListener(() => isGameOver = true);
        }
    }

    void OnEnable()
    {
        if (Host != null) Host.SimEventRaised += OnSimEvent;
    }

    void OnDisable()
    {
        if (simHost != null) simHost.SimEventRaised -= OnSimEvent;
    }

    void Update()
    {
        HoverNode = null;
        if (!isGameOver && Mouse.current != null && Keyboard.current != null)
        {
            bool overHud = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (!overHud) HoverNode = NodeUnderMouse();

            if (Keyboard.current.escapeKey.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame) PutDown();

            // The tools and the reroll have a key each (the student, 2026-10-13).
            if (Keyboard.current.wKey.wasPressedThisFrame) SelectWall();
            if (Keyboard.current.xKey.wasPressedThisFrame) SelectSell();
            if (Keyboard.current.mKey.wasPressedThisFrame) SelectMoveTower();
            if (Keyboard.current.dKey.wasPressedThisFrame) Reroll();

            // The shop's offers change under a tower in the hand when a wave resolves.
            if (Tool == BuildTool.Tower && !CanPickUp(SelectedOffer)) SelectWall();
            if (Tool == BuildTool.Gear && SelectedShopGear < 0 && !CanPickUp(SelectedOffer)) SelectWall();
            if (Tool == BuildTool.Gear && SelectedShopGear >= 0 && (SelectedShopGear >= Host.State.GearShop.Offers.Length || Host.State.GearShop.Offers[SelectedShopGear].Bought)) SelectWall();
            if (Tool == BuildTool.Sell && !MaySell) dragging = false;
            if (Tool == BuildTool.Deploy && Benched(SelectedBench) == null) SelectWall();
            if (Tool == BuildTool.EquipGear && SelectedOverflow >= 0 && SelectedOverflow >= Host.State.GearOverflow.Count) SelectWall();
            if (Tool == BuildTool.EquipGear && SelectedOverflow < 0 && (SelectedInventory < 0 || Host.State.GearInventory[SelectedInventory] == TowerState.NoGear)) SelectWall();
            if (Tool == BuildTool.Core && !ChoosingCore) SelectWall();

            // No building while paused - pause is a plan-and-look state, not a
            // build-while-frozen one. A press over the HUD belongs to the HUD:
            // without this, clicking Start would also build on the tile behind it.
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                dragging = !overHud && !IsBuildingBlocked;
                lastDragNode = null;

                // With the bottom bar, a press on a tower picks it up: it is dragged to
                // another tile, or to the bench.
                bool free = Tool == BuildTool.Wall || Tool == BuildTool.None || Tool == BuildTool.MoveTower;
                if (dragging && deckBar != null && free && MayMoveTower && TowerAt(HoverNode) != null)
                {
                    Hold(BuildTool.MoveTower);
                    TowerFrom = HoverNode;
                    towerDrag = true;
                    dragging = false;
                }
            }
            if (towerDrag && !Mouse.current.leftButton.isPressed)
            {
                towerDrag = false;
                int benchSlot = deckBar != null ? deckBar.BenchSlotUnderPointer() : -1;
                if (benchSlot >= 0) { ClickBench(benchSlot); SelectWall(); }
                else if (HoverNode != null && HoverNode != TowerFrom) { UseTool(HoverNode, true); SelectWall(); }
                // Let go on its own tile: it stays in the hand, as after a click.
            }
            if (!Mouse.current.leftButton.isPressed || IsBuildingBlocked) dragging = false;

            if (dragging && HoverNode != null && HoverNode != lastDragNode)
            {
                bool first = lastDragNode == null;
                lastDragNode = HoverNode;
                UseTool(HoverNode, first);
            }
        }

        DrawNewWalls();
        DrawChewProgress();
        ShowCoreZone();
    }

    // The tool acts on one tile. `first` is the tile the press began on; the rest of
    // a drag only draws walls and sells walls, so a slip of the mouse cannot buy a
    // second tower or sell the tower next to a wall.
    private void UseTool(Node node, bool first)
    {
        switch (Tool)
        {
            case BuildTool.Wall:
                if (CanPlaceWall(node)) Host.Submit(SimCommand.PlaceShape(node.gridX, node.gridY));
                break;
            case BuildTool.Sell:
                if (first || Host.State.Grid[node.gridX, node.gridY].Occupant == Occupant.Wall) TrySell(node.gridX, node.gridY);
                break;
            case BuildTool.Tower:
                if (!first) break;
                // On a tower of the card's type the card is merged into it; on open ground it is placed.
                if (TowerAt(node) != null) Host.Submit(SimCommand.FeedCard(SelectedOffer, node.gridX, node.gridY));
                else if (TryBuyOffer(SelectedOffer, node.gridX, node.gridY)) SelectWall();
                break;
            case BuildTool.Core:
                if (first) Host.Submit(SimCommand.PlaceCore(node.gridX, node.gridY));
                break;
            case BuildTool.EquipGear:
                if (first && SelectedOverflow >= 0)
                {
                    int spareBefore = Host.State.GearOverflow.Count;
                    Host.Submit(SimCommand.EquipOverflow(SelectedOverflow, node.gridX, node.gridY));
                    if (Host.State.GearOverflow.Count != spareBefore) SelectWall();
                }
                else if (first && SelectedInventory >= 0) Host.Submit(SimCommand.EquipGear(SelectedInventory, node.gridX, node.gridY));
                break; // Update() puts the hand down once the piece is on the tower
            case BuildTool.Deploy:
                if (!first || SelectedBench < 0) break;
                int logBefore = Host.State.PlacementLog.Count;
                if (CanMerge(Benched(SelectedBench), TowerAt(node))) Host.Submit(SimCommand.MergeTower(0, 0, SelectedBench, node.gridX, node.gridY, -1));
                else
                Host.Submit(SimCommand.DeployTower(SelectedBench, node.gridX, node.gridY));
                if (Host.State.PlacementLog.Count > logBefore) SelectWall();
                break;
            case BuildTool.Gear:
                if (!first) break;
                if (SelectedShopGear >= 0) Host.Submit(SimCommand.BuyShopGear(SelectedShopGear, node.gridX, node.gridY));
                else Host.Submit(SimCommand.BuyGear(SelectedOffer, node.gridX, node.gridY));
                // Update() puts the hand down once the piece is sold.
                break;
            case BuildTool.MoveTower:
                if (!first || !MayMoveTower) break;
                if (TowerFrom == null || TowerAt(TowerFrom) == null)
                {
                    TowerFrom = TowerAt(node) != null ? node : null;
                }
                else if (TowerAt(node) != null && node != TowerFrom && CanMerge(TowerAt(TowerFrom), TowerAt(node)))
                {
                    Host.Submit(SimCommand.MergeTower(TowerFrom.gridX, TowerFrom.gridY, -1, node.gridX, node.gridY, -1));
                    TowerFrom = null;
                }
                else if (TowerAt(node) != null)
                {
                    TowerFrom = node == TowerFrom ? null : node; // another tower: pick that one up
                }
                else
                {
                    Host.Submit(SimCommand.MoveTower(TowerFrom.gridX, TowerFrom.gridY, node.gridX, node.gridY));
                    if (TowerAt(TowerFrom) == null) TowerFrom = null; // it went
                }
                break;
            case BuildTool.MoveGear:
                if (!first) break;
                TowerState here = TowerAt(node);
                if (GearFrom == null)
                {
                    if (here != null && FirstGearSlot(here) >= 0) GearFrom = node;
                }
                else
                {
                    TowerState source = TowerAt(GearFrom);
                    if (source != null && here != null && here != source)
                        Host.Submit(SimCommand.MoveGear(GearFrom.gridX, GearFrom.gridY, FirstGearSlot(source), node.gridX, node.gridY));
                    GearFrom = null;
                }
                break;
        }
    }

    private Node NodeUnderMouse()
    {
        Camera cam = Camera.main;
        if (cam == null) return null;

        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Ray ray = cam.ScreenPointToRay(mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, clickLayer)) return null;

        // NodeFromWorldPoint clamps, so a ray that lands on the ground outside the
        // board would otherwise read as its nearest edge tile.
        Vector3 local = hit.point - gridManager.transform.position;
        if (Mathf.Abs(local.x) > gridManager.gridWorldSize.x / 2f || Mathf.Abs(local.z) > gridManager.gridWorldSize.y / 2f) return null;
        return gridManager.NodeFromWorldPoint(hit.point);
    }

    // --- what the hand may do on a tile (the ghost and the click ask the same questions) ---

    // The price of the next wall tile, or -1 when the bag is empty.
    public int WallPrice
    {
        get
        {
            ShapeDef shape = Host.State.Bag.CurrentShape;
            return shape != null ? shape.BuildCost : -1;
        }
    }

    // CLAUDE.md I9: the single source of truth for placement legality is
    // Thesis.Sim.Placement.CanPlace; this only wraps it. GhostPreviewer calls this
    // too, so the preview can never disagree with an actual click.
    public bool AreTilesPlaceable(List<Vector2Int> tiles)
    {
        if (Host == null) return false;
        tileBuffer.Clear();
        foreach (Vector2Int t in tiles) tileBuffer.Add(new TileCoord(t.x, t.y));
        return Placement.CanPlace(Host.State.Grid, Host.Map, tileBuffer);
    }

    public bool CanPlaceWall(Node node)
    {
        if (node == null || blockManager == null) return false;
        int price = WallPrice;
        if (WallsFromStock) { if (Host.State.WallsLeft <= 0) return false; }
        else if (price < 0 || Host.State.BuildBudget < price) return false;
        return AreTilesPlaceable(blockManager.GetTargetGridPositions(node.gridX, node.gridY));
    }

    public bool CanPickUp(int slot)
    {
        TowerOffer[] offers = Host.State.Shop.Offers;
        return slot >= 0 && slot < offers.Length && !offers[slot].Bought && Host.State.Shop.MayPick && Host.State.BuildBudget >= offers[slot].Price;
    }

    public bool CanPlaceTower(Node node, int slot)
    {
        if (node == null || !CanPickUp(slot)) return false;
        TowerOffer card = Host.State.Shop.Offers[slot];
        if (card.Kind == CardKind.Tower && !HasRoomFor(card)) return false;
        return IsFreeTile(node);
    }

    // A tile a tower could stand on (Placement.CanPlace, I9).
    public bool IsFreeTile(Node node)
    {
        if (node == null) return false;
        oneTile.Clear();
        oneTile.Add(new Vector2Int(node.gridX, node.gridY));
        return AreTilesPlaceable(oneTile);
    }

    // The living tower on a tile, or null.
    public TowerState TowerAt(Node node)
    {
        if (node == null) return null;
        SimNode sim = Host.State.Grid[node.gridX, node.gridY];
        if (sim.Occupant != Occupant.Tower) return null;
        TowerState tower = Host.State.Towers[sim.TowerId];
        return tower.IsAlive ? tower : null;
    }

    public static int FirstGearSlot(TowerState tower)
    {
        for (int i = 0; i < TowerState.MaxGearSlots; i++) if (tower.Gear[i] != TowerState.NoGear) return i;
        return -1;
    }

    public bool MayMoveGear => IsBuildPhase || Host.Config.MoveGearDuringWave;

    // One line about a tower: its name, its level, its gear.
    public string Describe(TowerState tower)
    {
        string text = tower.Def.Id + (tower.Level == 2 ? " (rare)" : tower.Level >= 3 ? " (epic)" : "");
        for (int i = 0; i < TowerState.MaxGearSlots; i++)
            if (tower.Gear[i] != TowerState.NoGear) text += "  +" + Host.Sim.GearLibrary[tower.Gear[i]].Id;
        return text;
    }

    // The definition of the offer in a slot, or null.
    public TowerDef OfferDef(int slot)
    {
        TowerOffer[] offers = Host.State.Shop.Offers;
        if (slot < 0 || slot >= offers.Length || offers[slot].Kind != CardKind.Tower) return null;
        return Host.Sim.TowerLibrary[offers[slot].TowerIndex];
    }

    // What a sale on this tile would clear and pay: the tower, or the wall piece the
    // tile belongs to (one tile, since WP-C4). Returns false when there is nothing to
    // sell there. This only PREVIEWS; the amount paid is the simulation's
    // (Placement.TrySellTower / TrySellWall), whose arithmetic it repeats.
    public bool SalePreview(Node node, List<Vector2Int> tiles, out float refund)
    {
        tiles.Clear();
        refund = 0f;
        if (node == null) return false;

        SimState state = Host.State;
        SimNode sim = state.Grid[node.gridX, node.gridY];
        if (sim.Occupant == Occupant.Tower)
        {
            tiles.Add(new Vector2Int(sim.X, sim.Y));
            refund = state.Towers[sim.TowerId].PricePaid * Host.Config.SellRefund;
            return true;
        }
        if (sim.Occupant != Occupant.Wall || sim.PieceId < 0 || sim.PieceId >= state.PlacementLog.Count) return false;

        PlacementRecord piece = state.PlacementLog[sim.PieceId];
        if (piece.Kind != PlacementKind.Wall) return false;
        for (int i = 0; i < piece.Tiles.Length; i++)
        {
            SimNode tile = state.Grid.Get(piece.Tiles[i]);
            if (tile.Occupant == Occupant.Wall && tile.PieceId == sim.PieceId) tiles.Add(new Vector2Int(tile.X, tile.Y));
        }
        refund = piece.Cost * Host.Config.WallSellRefund * tiles.Count / piece.Tiles.Length;
        return tiles.Count > 0;
    }

    // Sends the PlaceTower command for one of the shop's offers and reports whether
    // the simulation accepted it. The simulation is the only judge of legality and
    // budget (I9).
    public bool TryBuyOffer(int slot, int x, int y)
    {
        TowerOffer[] offers = Host.State.Shop.Offers;
        if (slot < 0 || slot >= offers.Length || offers[slot].Bought) return false;

        int before = Host.State.Towers.Count;
        Host.Submit(SimCommand.PlaceTower(slot, x, y));
        return Host.State.Towers.Count > before;
    }

    // Sells the tower on this tile, or the wall piece the tile belongs to.
    public bool TrySell(int x, int y)
    {
        SimState state = Host.State;
        SimNode node = state.Grid[x, y];
        if (node.Occupant == Occupant.None || !MaySell) return false;

        int before = state.PlacementLog.Count;
        Host.Submit(node.Occupant == Occupant.Tower ? SimCommand.SellTower(x, y) : SimCommand.SellWall(x, y));
        return state.PlacementLog.Count > before;
    }

    // --- visuals of what is built ---

    private void DrawNewWalls()
    {
        List<PlacementRecord> log = Host.State.PlacementLog;
        float cellSize = gridManager.nodeRadius * 2f;

        // The build log is the one record of what was built AND of what was sold, in
        // order, so walking it draws both: a sale row lists the tiles it cleared.
        for (; placementsDrawn < log.Count; placementsDrawn++)
        {
            PlacementRecord record = log[placementsDrawn];
            if (record.IsSale)
            {
                for (int i = 0; i < record.Tiles.Length; i++) RemoveVisual(gridManager.grid[record.Tiles[i].X, record.Tiles[i].Y]);
                continue;
            }
            // A merge empties the tiles of the towers it absorbed; the one that stays
            // is redrawn by its TowerChanged event.
            if (record.IsCombine)
            {
                for (int i = 0; i < record.Tiles.Length; i++) RemoveVisual(gridManager.grid[record.Tiles[i].X, record.Tiles[i].Y]);
                RefreshTower(record.TowerId);
                continue;
            }
            // The bench: a tower taken off its tile, put on one, or raised by a merged card.
            if (record.Kind == PlacementKind.TowerRecalled)
            {
                RemoveVisual(gridManager.grid[record.Tiles[0].X, record.Tiles[0].Y]);
                continue;
            }
            if (record.Kind == PlacementKind.TowerDeployed)
            {
                DrawTower(Host.State.Towers[record.TowerId], gridManager.grid[record.Tiles[0].X, record.Tiles[0].Y], cellSize);
                continue;
            }
            if (record.Kind == PlacementKind.TowerFed)
            {
                RefreshTower(record.TowerId);
                continue;
            }
            if (record.Kind == PlacementKind.CorePlaced)
            {
                Host.ShowCore();
                continue;
            }
            // A moved tower: its view goes with it. Tiles are { from, to }.
            if (record.Kind == PlacementKind.TowerMoved)
            {
                RemoveVisual(gridManager.grid[record.Tiles[0].X, record.Tiles[0].Y]);
                DrawTower(Host.State.Towers[record.TowerId], gridManager.grid[record.Tiles[1].X, record.Tiles[1].Y], cellSize);
                continue;
            }
            if (!record.IsPlacement) continue; // a shop reroll puts nothing on the board

            if (record.Kind == PlacementKind.Tower)
            {
                DrawTower(Host.State.Towers[record.TowerId], gridManager.grid[record.OriginX, record.OriginY], cellSize);
                continue;
            }
            // A wall is drawn a little lower than it is wide: from straight above
            // only its top shows, and a low block keeps the enemies beside it in view.
            for (int i = 0; i < record.Tiles.Length; i++)
            {
                Node node = gridManager.grid[record.Tiles[i].X, record.Tiles[i].Y];
                GameObject wall = Instantiate(standardWallPrefab, node.worldPosition + new Vector3(0, cellSize * wallHeight / 2f, 0), Quaternion.identity);
                wall.transform.localScale = new Vector3(cellSize * 0.94f, cellSize * wallHeight, cellSize * 0.94f);
                ApplyShapeColor(wall, wallColor);

                // Pop-in flourish. Added AFTER the final scale is set so the animator captures it.
                WallSpawnAnimator anim = wall.AddComponent<WallSpawnAnimator>();
                anim.Play(i * 0.04f);

                node.visualObject = wall;
                wallTiles.Add(node);
            }
        }
    }

    private void DrawTower(TowerState tower, Node node, float cellSize)
    {
        RemoveVisual(node);
        TowerView view = TowerView.Create(tower, node.worldPosition, cellSize, billboards);
        view.Show(tower, Host.Sim.GearLibrary.Length);
        towerViews[tower.Id] = view;
        node.visualObject = view.gameObject;
        wallTiles.Add(node);
    }

    private void RefreshTower(int id)
    {
        if (towerViews.TryGetValue(id, out TowerView view) && view != null) view.Show(Host.State.Towers[id], Host.Sim.GearLibrary.Length);
    }

    private void OnSimEvent(SimEvent e)
    {
        switch (e.Kind)
        {
            // A breached wall and a chewed-through tower both leave an empty tile.
            // WallBreached carries the tile in IntA/IntB, TowerDestroyed in IntB/IntC.
            case SimEventKind.WallBreached:
                RemoveVisual(gridManager.grid[e.IntA, e.IntB]);
                break;
            case SimEventKind.TowerDestroyed:
                RemoveVisual(gridManager.grid[e.IntB, e.IntC]);
                break;
            // IntA = tower id, IntB = target agent id. The hit is already resolved;
            // this only shows it. A dead agent keeps its last position in the list.
            // A tower's level or gear changed: its view shows both.
            case SimEventKind.TowerChanged:
                if (towerViews.TryGetValue(e.IntA, out TowerView changed) && changed != null) changed.Show(Host.State.Towers[e.IntA], Host.Sim.GearLibrary.Length);
                break;
            case SimEventKind.TowerFired:
                if (towerViews.TryGetValue(e.IntA, out TowerView view) && view != null)
                {
                    AgentState target = Host.State.Agents[e.IntB];
                    float y = Host.GroundY + gridManager.nodeRadius * (target.Movement == MovementClass.Flying ? 2.2f : 0.6f);
                    view.Fire(new Vector3(target.Position.X, y, target.Position.Y), Mathf.Sqrt(Host.State.Towers[e.IntA].SplashSquared));
                }
                break;
        }
    }

    private void RemoveVisual(Node node)
    {
        if (node.visualObject != null)
        {
            TowerView view = node.visualObject.GetComponent<TowerView>();
            if (view != null) towerViews.Remove(view.TowerId);
            Destroy(node.visualObject);
            node.visualObject = null;
        }
        wallTiles.Remove(node);
    }

    // Shrink damaged walls and towers so chew progress is readable at a glance. From
    // above, height does not show, so the footprint shrinks with it. Untouched tiles
    // are skipped, which also leaves the placement pop animation alone.
    private void DrawChewProgress()
    {
        SimGrid grid = Host.State.Grid;
        float cellSize = gridManager.nodeRadius * 2f;
        for (int i = 0; i < wallTiles.Count; i++)
        {
            Node node = wallTiles[i];
            SimNode sim = grid[node.gridX, node.gridY];
            if (node.visualObject == null || sim.MaxWallHealth <= 0f || sim.WallHealth >= sim.MaxWallHealth) continue;

            float frac = Mathf.Clamp01(sim.WallHealth / sim.MaxWallHealth);
            float size = Mathf.Lerp(0.35f, 1f, frac);
            Transform t = node.visualObject.transform;
            if (t.GetComponent<TowerView>() != null)
            {
                t.localScale = new Vector3(size, size, size);
                continue;
            }
            float full = cellSize * 0.94f;
            t.localScale = new Vector3(full * size, cellSize * wallHeight * size, full * size);
            Vector3 p = t.position;
            p.y = node.worldPosition.y + t.localScale.y / 2f;
            t.position = p;
        }
    }

    // Tints a placed wall (base color for normal lighting, boosted emission so it
    // glows / triggers Bloom). Uses a MaterialPropertyBlock rather than a
    // per-instance material so the walls still batch.
    private void ApplyShapeColor(GameObject wall, Color shapeColor)
    {
        MeshRenderer renderer = wall.GetComponent<MeshRenderer>();
        if (renderer == null) return;

        if (wallPropertyBlock == null) wallPropertyBlock = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(wallPropertyBlock);

        Color emission = shapeColor * 0.35f;
        emission.a = 1f;

        wallPropertyBlock.SetColor("_BaseColor", shapeColor);
        wallPropertyBlock.SetColor("_EmissionColor", emission);
        renderer.SetPropertyBlock(wallPropertyBlock);
    }
}
