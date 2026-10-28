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
    Upgrade,   // click a tower to give it its one upgrade; a choice of buffs then opens
    Target,    // click a tower to change which enemy it shoots
    Collector, // click an ore deposit to build a collector on it (2026-10-14)
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

    public BuildTool Tool { get; private set; } = BuildTool.None;

    // The shop offer in the hand while Tool is Tower; -1 otherwise.
    public int SelectedOffer { get; private set; } = -1;

    // The build menu's tower in the hand (SimConfig.BuildMenu), by its roster index; -1 otherwise.
    public int SelectedMenuTower { get; private set; } = -1;

    // The tile under the mouse, or null when the mouse is over the HUD or off the
    // board. Refreshed every frame; the ghost and the tooltip read it.
    public Node HoverNode { get; private set; }

    // The tower, collector or core whose drawn shape is under the mouse, the nearest if
    // several overlap; null over open ground. Clicks with an empty hand select by this, not by
    // the tile, so any part of what is drawn can be clicked (the student, 2026-10-26: still
    // had to aim low to click a collector).
    public Node HoverSelectable { get; private set; }
    private Vector2 pressScreen;

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

    // The wall tier in the hand (SimConfig.WallTiers): 0 plain, 1 stone, 2 fortified.
    public int WallTier { get; private set; }

    public void SelectWall() { SelectWall(0); }

    public void SelectWall(int tier) { WallTier = Host.Config.WallTiers ? Mathf.Clamp(tier, 0, 2) : 0; Hold(BuildTool.Wall); }

    public void SelectSell() { Hold(BuildTool.Sell); }

    public void PutDown() { Hold(BuildTool.None); }

    // --- undo and redo (SimConfig.UndoSteps, 2026-10-24) ---

    public bool UndoOn => Host.Config.UndoSteps > 0;
    public bool CanUndo => UndoOn && Host.Sim.UndoCount > 0 && IsBuildPhase && !IsBuildingBlocked;
    public bool CanRedo => UndoOn && Host.Sim.RedoCount > 0 && IsBuildPhase && !IsBuildingBlocked;
    public void Undo() { if (!CanUndo) return; PutDown(); Host.Submit(SimCommand.Undo()); }
    public void Redo() { if (!CanRedo) return; PutDown(); Host.Submit(SimCommand.Redo()); }

    // --- the selection (2026-10-24, after RimWorld): with an empty hand a click selects
    // the tower, collector or core under the mouse; the bar shows what it is and what can
    // be done with it. A right click or Esc lets go of it.

    public Node Selected { get; private set; }

    public TowerState SelectedTower => TowerAt(Selected);

    public bool SelectedIsCollector => Selected != null && Host.State.Grid[Selected.gridX, Selected.gridY].Occupant == Occupant.Collector;

    public bool SelectedIsCore => Selected != null && Selected.gridX == Host.State.Core.X && Selected.gridY == Host.State.Core.Y;

    private Node SelectableAt(Node node)
    {
        if (node == null) return null;
        if (TowerAt(node) != null) return node;
        if (Host.State.Grid[node.gridX, node.gridY].Occupant == Occupant.Collector) return node;
        if (node.gridX == Host.State.Core.X && node.gridY == Host.State.Core.Y) return node;
        return null;
    }

    public void Deselect() { Selected = null; }

    // The commands of the selected thing (the inspect pane's buttons).
    public void UpgradeSelected() { if (CanUpgrade(SelectedTower)) { upgradeNode = Selected; upgradeBench = -1; } }

    public void TargetSelected() { TowerState t = SelectedTower; if (t != null && TargetingOn) Host.Submit(SimCommand.SetTargeting(Selected.gridX, Selected.gridY, NextMode(t.Mode))); }

    public void MoveSelected() { if (SelectedTower == null || !MayMoveTower) return; Node from = Selected; Hold(BuildTool.MoveTower); TowerFrom = from; }

    public void SellSelected() { if (Selected == null || !MaySell) return; TrySell(Selected.gridX, Selected.gridY); if (SelectableAt(Selected) == null) Selected = null; }

    public void SelectMoveTower() { Hold(BuildTool.MoveTower); }

    public void SelectMoveGear() { Hold(BuildTool.MoveGear); }

    public void SelectUpgrade() { Hold(BuildTool.Upgrade); }

    public void SelectTarget() { Hold(BuildTool.Target); }

    // --- the one upgrade, with a choice (2026-10-14) ---

    public bool UpgradesOn => Host.Config.ChoiceUpgradeCost >= 0f;

    public float UpgradeCost => Host.Config.ChoiceUpgradeCost;

    // What this tower's next upgrade costs (each one costs more: SimConfig.ChoiceUpgradeCostStep).
    public float UpgradeCostFor(TowerState tower) { return tower != null ? Host.Sim.UpgradePrice(tower) : UpgradeCost; }

    public bool HasUpgradeLeft(TowerState tower) { return tower != null && tower.UpgradeCount < Host.Config.UpgradesPerTower; }

    public bool CanUpgrade(TowerState tower)
    {
        return UpgradesOn && HasUpgradeLeft(tower) && !UpgradeNeedsResearch(tower) && IsBuildPhase && Host.State.BuildBudget >= UpgradeCostFor(tower);
    }

    // Both upgrades a tower has, by name, or "".
    public static string UpgradeNames(TowerState tower)
    {
        TowerLevelMod[] choices = TowerUpgradeChoices.For(tower.Def);
        string text = TowerUpgradeChoices.IsValidFor(tower.Def, tower.Upgrade) ? choices[tower.Upgrade].Label : "";
        if (TowerUpgradeChoices.IsValidFor(tower.Def, tower.Upgrade2)) text += "; " + choices[tower.Upgrade2].Label;
        return text;
    }

    // The extractor (gear that takes a tower's gear off) is in this game: the free Ungear tool is not.
    public bool ExtractorInGame => UsesInventory && Host.Config.ExtractorGear;

    public bool IsExtractor(int piece) { return piece >= 0 && piece < Host.Sim.GearLibrary.Length && Host.Sim.GearLibrary[piece].Extracts; }

    // The tower whose upgrade is being chosen (the panel shows the choices), or null.
    private Node upgradeNode;
    private int upgradeBench = -1;

    public TowerState UpgradePending => upgradeBench >= 0 ? Benched(upgradeBench) : upgradeNode != null ? TowerAt(upgradeNode) : null;

    public void ChooseUpgrade(int choice)
    {
        if (UpgradePending == null) return;
        Host.Submit(upgradeBench >= 0 ? SimCommand.ChooseUpgradeOnBench(upgradeBench, choice) : SimCommand.ChooseUpgrade(upgradeNode.gridX, upgradeNode.gridY, choice));
        CancelUpgrade();
    }

    // The i-th of the choices the pending tower may take next (TowerUpgradeChoices.Options).
    public void ChooseOption(int i)
    {
        int[] options = TowerUpgradeChoices.Options(UpgradePending);
        if (i >= 0 && i < options.Length) ChooseUpgrade(options[i]);
    }

    public void CancelUpgrade()
    {
        upgradeNode = null;
        upgradeBench = -1;
    }

    // --- which enemy a tower shoots ---

    public bool TargetingOn => Host.Config.ChooseTargeting;

    public static string ModeName(TargetingMode mode)
    {
        switch (mode)
        {
            case TargetingMode.Strongest: return "the strongest";
            case TargetingMode.Weakest: return "the weakest";
            case TargetingMode.Nearest: return "the nearest to it";
            case TargetingMode.Raiders: return "raiders first";
            default: return "the one closest to the core";
        }
    }

    public static TargetingMode NextMode(TargetingMode mode) { return (TargetingMode)(((int)mode + 1) % 5); }

    private void Hold(BuildTool tool)
    {
        if (tool != BuildTool.None && tool != BuildTool.MoveTower) Selected = null;
        Tool = tool;
        SelectedOffer = -1;
        SelectedMenuTower = -1;
        SelectedShopGear = -1;
        GearFrom = null;
        TowerFrom = null;
        SelectedBench = -1;
        SelectedInventory = -1;
        SelectedOverflow = -1;
        CancelUpgrade();
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
        if (Tool == BuildTool.EquipGear && SelectedInventory == slot) { PutDown(); return; }
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
        if (Tool == BuildTool.Tower && SelectedMenuTower >= 0) return Host.Sim.TowerLibrary[SelectedMenuTower];
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
        PutDown();
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

    // --- rolling the gear shop again ---

    public bool GearRerollsOn => Host.Config.GearShopSlots > 0 && Host.Config.GearShopRerollCost >= 0f;

    public float GearRerollPrice => Host.Sim.GearRerollPrice;

    public bool CanRerollGear => GearRerollsOn && Host.State.BuildBudget >= GearRerollPrice && !IsBuildingBlocked;

    public void RerollGear()
    {
        if (!CanRerollGear) return;
        if (Tool == BuildTool.Gear && SelectedShopGear >= 0) PutDown();
        Host.Submit(SimCommand.RerollGearShop());
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
        if (Tool == BuildTool.EquipGear && SelectedOverflow >= 0) PutDown();
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
            case BuildTool.Upgrade:
                if (occupied && CanUpgrade(Benched(slot))) { upgradeBench = slot; upgradeNode = null; }
                return;
            case BuildTool.Target:
                if (occupied && TargetingOn) Host.Submit(SimCommand.SetTargetingOnBench(slot, NextMode(Benched(slot).Mode)));
                return;
            case BuildTool.MoveGear:
                if (UsesInventory && occupied && FirstGearSlot(Benched(slot)) >= 0) { Host.Submit(SimCommand.UnequipGearOnBench(slot, FirstGearSlot(Benched(slot)))); return; }
                break;
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
                if (occupied && SelectedOverflow >= 0) { Host.Submit(SimCommand.EquipOverflowOnBench(SelectedOverflow, slot)); PutDown(); }
                else if (occupied) Host.Submit(SimCommand.EquipGearOnBench(SelectedInventory, slot));
                return;
            case BuildTool.Deploy:
                if (SelectedBench == slot) { PutDown(); return; }
                if (SelectedBench >= 0)
                {
                    bool merge = occupied && CanMerge(Benched(SelectedBench), Benched(slot));
                    Host.Submit(merge ? SimCommand.MergeTower(0, 0, SelectedBench, 0, 0, slot) : SimCommand.SwapBench(SelectedBench, slot));
                    PutDown();
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

    // --- the build menu and the ore economy (2026-10-14) ---

    public bool BuildMenu => Host.Config.BuildMenu;

    public bool HasDeposits => Host.Map.Deposits.Length > 0;

    public bool TowerUnlocked(int index) { return Host.Config.ResearchUnlocksUpgrades || Host.Sim.TowerLibrary[index].Tier <= Host.State.ResearchLevel; }

    // The tower may take another upgrade but the research has not opened it yet (SimConfig.ResearchUnlocksUpgrades).
    public bool UpgradeNeedsResearch(TowerState tower) { return HasUpgradeLeft(tower) && tower.UpgradeCount >= Host.Sim.UpgradesAllowed(Host.State); }

    // A tower of the menu into the hand. It stays there after it is placed, for the next.
    public void SelectMenuTower(int index)
    {
        if (index < 0 || index >= Host.Sim.TowerLibrary.Length || IsBuildingBlocked) return;
        if (Tool == BuildTool.Tower && SelectedMenuTower == index) { PutDown(); return; }
        if (!TowerUnlocked(index)) return;
        Hold(BuildTool.Tower);
        SelectedMenuTower = index;
    }

    public void SelectCollector() { if (HasDeposits) Hold(BuildTool.Collector); }

    public bool CanBuildCollector(Node node)
    {
        if (node == null || !IsBuildPhase) return false;
        var tile = new TileCoord(node.gridX, node.gridY);
        return Host.Map.DepositAt(tile) >= 0 && Host.State.Grid[node.gridX, node.gridY].Occupant == Occupant.None
               && tile != Host.State.Core && Host.State.BuildBudget >= Host.Config.CollectorPrice;
    }

    public void Research() { if (!IsBuildingBlocked) Host.Submit(SimCommand.Research()); }

    public bool CanRepair => Host.Config.RepairPrice >= 0f && IsBuildPhase && Host.State.CoreHp < Host.Config.CoreMaxHp && Host.State.BuildBudget >= Host.Config.RepairPrice;

    public void RepairCore() { if (CanRepair && !IsBuildingBlocked) Host.Submit(SimCommand.RepairCore()); }

    public void CraftGear(int piece) { if (!IsBuildingBlocked) Host.Submit(SimCommand.CraftGear(piece)); }

    // What the core and the standing collectors will pay when the next wave is over.
    public int ExpectedIncome => Host.Config.StipendPerWave + Host.Sim.CollectorIncome(Host.State);

    // --- rerolling the cards ---

    public bool RerollsOn => Host.Config.RerollCost >= 0f;

    public float RerollPrice => Host.Sim.RerollPrice;

    public bool CanReroll => RerollsOn && Host.State.Shop.MayPick && Host.State.BuildBudget >= RerollPrice && !IsBuildingBlocked;

    public void Reroll()
    {
        if (!CanReroll) return;
        if (Tool == BuildTool.Tower || (Tool == BuildTool.Gear && SelectedShopGear < 0)) PutDown();
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
        if (Tool == BuildTool.Gear && SelectedShopGear == slot) { PutDown(); return; }
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
        if ((Tool == BuildTool.Tower || Tool == BuildTool.Gear) && SelectedOffer == slot) { PutDown(); return; }
        if (!CanPickUp(slot)) return;

        // A coin, wall or heal card is taken on the spot; a gear card goes into the hand like a tower.
        if (offers[slot].Kind == CardKind.Coin || offers[slot].Kind == CardKind.Wall || offers[slot].Kind == CardKind.Heal)
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
            playerCore.OnRevived.AddListener(() => isGameOver = false);
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
            HoverSelectable = overHud ? null : PickSelectable(Mouse.current.position.ReadValue());

            if (Keyboard.current.escapeKey.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame)
            {
                // The hand first; with an empty hand, the selection.
                if (Tool != BuildTool.None) PutDown(); else Selected = null;
            }
            bool ctrl = Keyboard.current.ctrlKey.isPressed;
            if (ctrl && Keyboard.current.zKey.wasPressedThisFrame) Undo();
            if (ctrl && Keyboard.current.yKey.wasPressedThisFrame) Redo();

            // The tools and the reroll have a key each (the student, 2026-10-13).
            if (Keyboard.current.qKey.wasPressedThisFrame) SelectWall(); // W, A, S and D move the view
            if (Keyboard.current.xKey.wasPressedThisFrame) SelectSell();
            if (Keyboard.current.mKey.wasPressedThisFrame) SelectMoveTower();
            if (Keyboard.current.rKey.wasPressedThisFrame) Reroll();
            if (Keyboard.current.gKey.wasPressedThisFrame && !ExtractorInGame) SelectMoveGear();
            if (Keyboard.current.uKey.wasPressedThisFrame && UpgradesOn) SelectUpgrade();
            if (Keyboard.current.tKey.wasPressedThisFrame && TargetingOn) SelectTarget();
            if (UpgradePending == null && (upgradeNode != null || upgradeBench >= 0)) CancelUpgrade(); // the tower went

            // The shop's offers change under a tower in the hand when a wave resolves.
            if (Keyboard.current.cKey.wasPressedThisFrame) SelectCollector();
            if (Tool == BuildTool.Tower && SelectedMenuTower < 0 && !CanPickUp(SelectedOffer)) PutDown();
            if (Tool == BuildTool.Gear && SelectedShopGear < 0 && !CanPickUp(SelectedOffer)) PutDown();
            if (Tool == BuildTool.Gear && SelectedShopGear >= 0 && (SelectedShopGear >= Host.State.GearShop.Offers.Length || Host.State.GearShop.Offers[SelectedShopGear].Bought)) PutDown();
            if (Tool == BuildTool.Sell && !MaySell) dragging = false;
            if (Tool == BuildTool.Deploy && Benched(SelectedBench) == null) PutDown();
            if (Tool == BuildTool.EquipGear && SelectedOverflow >= 0 && SelectedOverflow >= Host.State.GearOverflow.Count) PutDown();
            if (Tool == BuildTool.EquipGear && SelectedOverflow < 0 && (SelectedInventory < 0 || Host.State.GearInventory[SelectedInventory] == TowerState.NoGear)) PutDown();
            if (Tool == BuildTool.Core && !ChoosingCore) PutDown();

            // No building while paused - pause is a plan-and-look state, not a
            // build-while-frozen one. A press over the HUD belongs to the HUD:
            // without this, clicking Start would also build on the tile behind it.
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                dragging = !overHud && !IsBuildingBlocked;
                lastDragNode = null;

                // With an empty hand a press selects what is under the mouse (or lets go of
                // the selection on empty ground). Dragging a tower off its tile moves it.
                if (Tool == BuildTool.None && !overHud)
                {
                    // With a power source selected, a click on a tower in its reach ties the tower
                    // to it (SetPowerSource) and keeps the source selected (2026-10-26).
                    if (!TiePowerTo(HoverSelectable)) Selected = HoverSelectable;
                    pressNode = TowerAt(HoverSelectable) != null ? HoverSelectable : null;
                    pressScreen = Mouse.current.position.ReadValue();
                    dragging = false;
                }
            }
            // The press on a tower became a drag: the tower is in the hand until let go.
            if (pressNode != null && Tool == BuildTool.None && Mouse.current.leftButton.isPressed && HoverNode != null && HoverNode != pressNode
                && (Mouse.current.position.ReadValue() - pressScreen).sqrMagnitude > 100f
                && deckBar != null && MayMoveTower && TowerAt(pressNode) != null && !IsBuildingBlocked)
            {
                Node from = pressNode;
                Hold(BuildTool.MoveTower);
                TowerFrom = from;
                towerDrag = true;
                pressNode = null;
            }
            if (!Mouse.current.leftButton.isPressed) pressNode = null;
            if (towerDrag && !Mouse.current.leftButton.isPressed)
            {
                towerDrag = false;
                int benchSlot = deckBar != null ? deckBar.BenchSlotUnderPointer() : -1;
                if (benchSlot >= 0) { ClickBench(benchSlot); PutDown(); }
                else if (HoverNode != null && HoverNode != TowerFrom) { UseTool(HoverNode, true); PutDown(); Selected = SelectableAt(HoverNode); }
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
        DrawPower();
        ShowCoreZone();
    }

    // Where a press with an empty hand began (a tower pressed there may be dragged away).
    private Node pressNode;

    // The tool acts on one tile. `first` is the tile the press began on; the rest of
    // a drag only draws walls and sells walls, so a slip of the mouse cannot buy a
    // second tower or sell the tower next to a wall.
    private void UseTool(Node node, bool first)
    {
        switch (Tool)
        {
            case BuildTool.Wall:
                if (CanPlaceWall(node)) Host.Submit(SimCommand.PlaceShape(node.gridX, node.gridY, WallTier));
                break;
            case BuildTool.Sell:
                if (first || Host.State.Grid[node.gridX, node.gridY].Occupant == Occupant.Wall) TrySell(node.gridX, node.gridY);
                break;
            case BuildTool.Collector:
                if (first && CanBuildCollector(node)) Host.Submit(SimCommand.BuildCollector(node.gridX, node.gridY));
                break;
            case BuildTool.Tower:
                if (!first) break;
                if (SelectedMenuTower >= 0)
                {
                    Host.Submit(SimCommand.BuyTower(SelectedMenuTower, node.gridX, node.gridY));
                    break;
                }
                // On a tower of the card's type the card is merged into it; on open ground it is placed.
                if (TowerAt(node) != null) Host.Submit(SimCommand.FeedCard(SelectedOffer, node.gridX, node.gridY));
                else if (TryBuyOffer(SelectedOffer, node.gridX, node.gridY)) PutDown();
                break;
            case BuildTool.Core:
                if (first) Host.Submit(SimCommand.PlaceCore(node.gridX, node.gridY));
                break;
            case BuildTool.Upgrade:
                if (first && CanUpgrade(TowerAt(node))) { upgradeNode = node; upgradeBench = -1; }
                break;
            case BuildTool.Target:
                if (first && TargetingOn && TowerAt(node) != null) Host.Submit(SimCommand.SetTargeting(node.gridX, node.gridY, NextMode(TowerAt(node).Mode)));
                break;
            case BuildTool.EquipGear:
                if (first && SelectedOverflow >= 0)
                {
                    int spareBefore = Host.State.GearOverflow.Count;
                    Host.Submit(SimCommand.EquipOverflow(SelectedOverflow, node.gridX, node.gridY));
                    if (Host.State.GearOverflow.Count != spareBefore) PutDown();
                }
                else if (first && SelectedInventory >= 0) Host.Submit(SimCommand.EquipGear(SelectedInventory, node.gridX, node.gridY));
                break; // Update() puts the hand down once the piece is on the tower
            case BuildTool.Deploy:
                if (!first || SelectedBench < 0) break;
                int logBefore = Host.State.PlacementLog.Count;
                if (CanMerge(Benched(SelectedBench), TowerAt(node))) Host.Submit(SimCommand.MergeTower(0, 0, SelectedBench, node.gridX, node.gridY, -1));
                else
                Host.Submit(SimCommand.DeployTower(SelectedBench, node.gridX, node.gridY));
                if (Host.State.PlacementLog.Count > logBefore) PutDown();
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
                if (UsesInventory)
                {
                    // With a gear inventory the tool simply takes gear OFF: one click, one piece, into the inventory.
                    if (here != null && FirstGearSlot(here) >= 0) Host.Submit(SimCommand.UnequipGear(node.gridX, node.gridY, FirstGearSlot(here)));
                    break;
                }
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

    private Node NodeUnderMouse() { return NodeAtScreen(Mouse.current.position.ReadValue()); }

    // The tile drawn at this point of the screen.
    public Node NodeAtScreen(Vector2 screenPoint)
    {
        Camera cam = Camera.main;
        if (cam == null) return null;

        Ray ray = cam.ScreenPointToRay(screenPoint);
        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, clickLayer)) return null;

        // NodeFromWorldPoint clamps, so a ray that lands on the ground outside the
        // board would otherwise read as its nearest edge tile.
        Vector3 local = hit.point - gridManager.transform.position;
        if (Mathf.Abs(local.x) > gridManager.gridWorldSize.x / 2f || Mathf.Abs(local.z) > gridManager.gridWorldSize.y / 2f) return null;

        // From a tilted camera the top of a tower, a wall or a rock is drawn in front of
        // the ground the ray meets; aiming at the top hit the tile behind it (the student,
        // 2026-10-24: "I need to aim below what I want to click"). The ray is walked from
        // above the tallest thing down to the ground, and the first tile whose drawn top
        // it passes under is the one under the mouse.
        float cell = gridManager.nodeRadius * 2f;
        float ground = gridManager.transform.position.y;
        float top = ground + cell * 1.6f;
        if (ray.direction.y < -0.01f && ray.origin.y > top)
        {
            float tTop = (top - ray.origin.y) / ray.direction.y;
            float tGround = hit.distance;
            float step = cell * 0.2f / Mathf.Max(0.05f, ray.direction.magnitude);
            for (float t = tTop; t < tGround; t += step)
            {
                Vector3 p = ray.origin + ray.direction * t;
                Vector3 lp = p - gridManager.transform.position;
                if (Mathf.Abs(lp.x) > gridManager.gridWorldSize.x / 2f || Mathf.Abs(lp.z) > gridManager.gridWorldSize.y / 2f) continue;
                Node node = gridManager.NodeFromWorldPoint(p);
                if (node != null && p.y <= ground + DrawnHeight(node, cell)) return node;
            }
        }
        return gridManager.NodeFromWorldPoint(hit.point);
    }

    public Node PickSelectable(Vector2 screenPoint)
    {
        Camera cam = Camera.main;
        if (cam == null || Host == null) return null;
        float cell = gridManager.nodeRadius * 2f;
        Node best = null;
        float bestDepth = float.MaxValue;
        void Consider(TileCoord tile)
        {
            if (tile.X < 0 || tile.Y < 0 || tile.X >= gridManager.gridSizeX || tile.Y >= gridManager.gridSizeY) return;
            Node node = gridManager.grid[tile.X, tile.Y];
            float top = Mathf.Max(DrawnHeight(node, cell), cell * 0.3f);
            float h = cell * 0.5f;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            float depth = float.MaxValue;
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = node.worldPosition + new Vector3((c & 1) == 0 ? -h : h, (c & 4) == 0 ? 0f : top, (c & 2) == 0 ? -h : h);
                Vector3 sp = cam.WorldToScreenPoint(corner);
                if (sp.z <= 0f) return;
                min = Vector2.Min(min, sp); max = Vector2.Max(max, sp);
                depth = Mathf.Min(depth, sp.z);
            }
            if (screenPoint.x < min.x || screenPoint.x > max.x || screenPoint.y < min.y || screenPoint.y > max.y) return;
            if (depth < bestDepth) { bestDepth = depth; best = node; }
        }
        foreach (TowerState t in Host.State.Towers) if (t.IsAlive && !t.OnBench) Consider(t.Tile);
        foreach (Deposit d in Host.Map.Deposits) if (Host.State.Grid[d.Tile.X, d.Tile.Y].Occupant == Occupant.Collector) Consider(d.Tile);
        Consider(Host.State.Core);
        return best;
    }

    // The top of the ground, wall or rock a tile shows, under any tower on it: where a mark on
    // the tile is drawn so that nothing hides it.
    public float SurfaceHeight(Node node)
    {
        float cell = gridManager.nodeRadius * 2f;
        if (Host.State.Grid[node.gridX, node.gridY].Occupant == Occupant.Tower) return Plinth(node, cell);
        return DrawnHeight(node, cell);
    }

    // The power source selected (core, collector or pylon): its tile, reach and room.
    private bool SelectedSource(out TileCoord tile, out float radius)
    {
        tile = TowerState.NoTile; radius = 0f;
        if (!PowerRule || Selected == null) return false;
        TowerState pylon = SelectedTower != null && SelectedTower.PowerCapacity > 0 ? SelectedTower : null;
        if (!SelectedIsCore && !SelectedIsCollector && pylon == null) return false;
        tile = new TileCoord(Selected.gridX, Selected.gridY);
        radius = pylon != null ? pylon.PowerRadius : SelectedIsCore ? Host.Config.CorePowerRadius : Host.Config.CollectorPowerRadius;
        return true;
    }

    // A click on a tower while a source is selected: tie it there. False when the click is a
    // plain selection (no source selected, not a tower, out of reach, or already powered from it).
    private bool TiePowerTo(Node clicked)
    {
        TowerState tower = TowerAt(clicked);
        if (tower == null || tower.PowerCapacity > 0 || !SelectedSource(out TileCoord source, out float radius)) return false;
        if (!Simulation.InPowerReach(source, radius, tower.Tile) || tower.PoweredBy.Equals(source)) return false;
        Host.Submit(SimCommand.SetPowerSource(tower.Tile.X, tower.Tile.Y, source.X, source.Y));
        return true;
    }

    // How high what stands on a tile is drawn, above the ground.
    private float DrawnHeight(Node node, float cell)
    {
        SimNode sim = Host.State.Grid[node.gridX, node.gridY];
        switch (sim.Occupant)
        {
            case Occupant.Tower: return Plinth(node, cell) + cell * 0.8f;
            case Occupant.Collector: return cell * Mathf.Max(0.45f, wallHeight * 1.6f);
            case Occupant.Wall: if (sim.PieceId != SimNode.RockPiece) return cell * wallHeight; break;
        }
        if (sim.IsRock)
        {
            RockLayout rocks = gridManager.GetComponent<RockLayout>();
            return cell * (rocks != null ? rocks.heightShare : 0.45f);
        }
        if (node.gridX == Host.State.Core.X && node.gridY == Host.State.Core.Y) return cell * 0.6f;
        return 0f;
    }

    // --- what the hand may do on a tile (the ghost and the click ask the same questions) ---

    // The price of the next wall tile, or -1 when the bag is empty.
    public int WallPrice
    {
        get
        {
            ShapeDef shape = Host.State.Bag.CurrentShape;
            if (shape != null && WallTier > 0) shape = Host.Sim.WallTier(WallTier, shape);
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

    // A tile a tower could stand on (Placement.CanPlaceTower, I9): with SimConfig.TowersOnWalls
    // a wall or a rock, otherwise free ground.
    public bool IsFreeTile(Node node)
    {
        if (node == null) return false;
        return Placement.CanPlaceTower(Host.State.Grid, Host.Map, new TileCoord(node.gridX, node.gridY), Host.Config.TowersOnWalls);
    }

    public bool TowersOnWalls => Host.Config.TowersOnWalls;

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
        string text = tower.Def.Id + (tower.Rarity == 2 ? " (rare)" : tower.Rarity >= 3 ? " (epic)" : "");
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
            refund = state.Towers[sim.TowerId].PricePaid * Host.Sim.TowerRefundShare(state, state.Towers[sim.TowerId]);
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
        refund = piece.Cost * Host.Sim.WallRefundShare(state, new TileCoord(node.gridX, node.gridY)) * tiles.Count / piece.Tiles.Length;
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
                for (int i = 0; i < record.Tiles.Length; i++) ClearTile(gridManager.grid[record.Tiles[i].X, record.Tiles[i].Y], cellSize);
                continue;
            }
            // A merge empties the tiles of the towers it absorbed; the one that stays
            // is redrawn by its TowerChanged event.
            if (record.IsCombine)
            {
                for (int i = 0; i < record.Tiles.Length; i++) ClearTile(gridManager.grid[record.Tiles[i].X, record.Tiles[i].Y], cellSize);
                RefreshTower(record.TowerId);
                continue;
            }
            // The bench: a tower taken off its tile, put on one, or raised by a merged card.
            if (record.Kind == PlacementKind.TowerRecalled)
            {
                ClearTile(gridManager.grid[record.Tiles[0].X, record.Tiles[0].Y], cellSize);
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
            if (record.Kind == PlacementKind.CollectorBuilt)
            {
                Node at = gridManager.grid[record.OriginX, record.OriginY];
                if (Host.State.Grid[at.gridX, at.gridY].Occupant == Occupant.Collector) DrawCollector(at, cellSize);
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
                ClearTile(gridManager.grid[record.Tiles[0].X, record.Tiles[0].Y], cellSize);
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
                // As for towers: a wall that was since taken up or chewed away is not drawn.
                SimNode standing = Host.State.Grid[node.gridX, node.gridY];
                if (standing.Occupant != Occupant.Wall || standing.PieceId != record.PieceId) continue;
                DrawWall(node, cellSize, i * 0.04f);
            }
        }
    }

    // A wall's colour tells its tier (SimConfig.WallTiers): plain, stone grey, fortified slate.
    public static readonly Color StoneColor = new Color(0.62f, 0.66f, 0.72f);
    public static readonly Color FortColor = new Color(0.33f, 0.4f, 0.5f);

    public Color WallColorOf(float maxHealth)
    {
        if (Host.Config.WallTiers && Mathf.Approximately(maxHealth, Host.Config.FortWallHealth)) return FortColor;
        if (Host.Config.WallTiers && Mathf.Approximately(maxHealth, Host.Config.StoneWallHealth)) return StoneColor;
        return wallColor;
    }

    private void DrawWall(Node node, float cellSize, float popDelay)
    {
        RemoveVisual(node);
        GameObject wall = Instantiate(standardWallPrefab, node.worldPosition + new Vector3(0, cellSize * wallHeight / 2f, 0), Quaternion.identity);
        wall.transform.localScale = new Vector3(cellSize * 0.94f, cellSize * wallHeight, cellSize * 0.94f);
        ApplyShapeColor(wall, WallColorOf(Host.State.Grid[node.gridX, node.gridY].MaxWallHealth));

        // Pop-in flourish. Added AFTER the final scale is set so the animator captures it.
        if (popDelay >= 0f) wall.AddComponent<WallSpawnAnimator>().Play(popDelay);

        node.visualObject = wall;
        wallTiles.Add(node);
    }

    // --- power (SimConfig.PowerRule, 2026-10-20) ---

    public bool PowerRule => Host.Config.PowerRule;

    // Every tower shows whether it has power; in a build phase, and with a tower in the
    // hand, faint rings show how far the core and each collector reach.
    // The tiles the selected core or collector powers, each outlined, and a frame round
    // whatever is selected. Shown only while something is selected (the student, 2026-10-24:
    // the rings were not where the rule is, and were always on).
    private readonly List<LineRenderer> squares = new List<LineRenderer>();
    private readonly List<LineRenderer> powerLinks = new List<LineRenderer>();
    private LineRenderer selectionFrame;

    private void DrawPower()
    {
        if (Host == null) return;
        if (PowerRule)
            foreach (KeyValuePair<int, TowerView> pair in towerViews)
                if (pair.Value != null) pair.Value.SetPowered(Host.State.Towers[pair.Key].Powered);
        foreach (KeyValuePair<int, TowerView> pair in towerViews)
            if (pair.Value != null) pair.Value.SetKnockedOut(Host.State.Towers[pair.Key].KnockedOut);

        float cell = gridManager.nodeRadius * 2f;
        int used = 0;
        TowerState pylon = SelectedTower != null && SelectedTower.PowerCapacity > 0 ? SelectedTower : null;
        if (PowerRule && Selected != null && (SelectedIsCore || SelectedIsCollector || pylon != null))
        {
            float radius = pylon != null ? pylon.PowerRadius : SelectedIsCore ? Host.Config.CorePowerRadius : Host.Config.CollectorPowerRadius;
            var centre = new TileCoord(Selected.gridX, Selected.gridY);
            int reach = Mathf.CeilToInt(radius);
            for (int dx = -reach; dx <= reach; dx++)
                for (int dy = -reach; dy <= reach; dy++)
                {
                    var tile = new TileCoord(centre.X + dx, centre.Y + dy);
                    if (tile.X < 0 || tile.Y < 0 || tile.X >= gridManager.gridSizeX || tile.Y >= gridManager.gridSizeY) continue;
                    if (!Simulation.InPowerReach(centre, radius, tile)) continue;
                    Node at = gridManager.grid[tile.X, tile.Y];
                    Square(squares, ref used, at.worldPosition, cell * 0.92f, new Color(1f, 0.82f, 0.25f, 0.55f), 0.06f, SurfaceHeight(at) + cell * 0.04f);
                }
        }
        for (int k = used; k < squares.Count; k++) squares[k].enabled = false;

        // A line from each source to every tower it powers (2026-10-26); the selected source's
        // lines bright, the rest faint.
        int links = 0;
        if (PowerRule)
        {
            bool hasSource = SelectedSource(out TileCoord chosen, out _);
            foreach (TowerState t in Host.State.Towers)
            {
                if (!t.IsAlive || t.OnBench || t.PoweredBy.X < 0) continue;
                // Only for what is selected (2026-10-28): the selected source's towers, or the selected tower's own source.
                if (!(hasSource && t.PoweredBy.Equals(chosen)) && t != SelectedTower) continue;
                if (links >= powerLinks.Count) powerLinks.Add(TowerView.Line(transform, "PowerLink", Color.white, 0.08f, false));
                LineRenderer line = powerLinks[links++];
                bool lit = hasSource && t.PoweredBy.Equals(chosen);
                Color c = lit ? new Color(1f, 0.85f, 0.3f, 0.95f) : new Color(1f, 0.85f, 0.3f, 0.3f);
                line.startColor = line.endColor = c;
                line.startWidth = line.endWidth = lit ? cell * 0.09f : cell * 0.04f;
                Node from = gridManager.grid[t.PoweredBy.X, t.PoweredBy.Y], to = gridManager.grid[t.Tile.X, t.Tile.Y];
                line.positionCount = 2;
                line.SetPosition(0, from.worldPosition + Vector3.up * (DrawnHeight(from, cell) + cell * 0.1f));
                line.SetPosition(1, to.worldPosition + Vector3.up * (Plinth(to, cell) + cell * 0.45f));
                line.enabled = true;
            }
        }
        for (int k = links; k < powerLinks.Count; k++) powerLinks[k].enabled = false;

        if (selectionFrame == null) selectionFrame = TowerView.Line(transform, "Selection", new Color(0.18f, 0.83f, 0.75f, 1f), 0.12f, true);
        if (Selected != null)
        {
            SetSquare(selectionFrame, Selected.worldPosition, cell * 1.08f, 0.2f);
            selectionFrame.enabled = true;
        }
        else selectionFrame.enabled = false;
    }

    public void Square(List<LineRenderer> pool, ref int used, Vector3 centre, float size, Color color, float width, float lift = 0.1f)
    {
        if (used >= pool.Count) pool.Add(TowerView.Line(transform, "PowerTile", color, width, true));
        LineRenderer line = pool[used++];
        line.startColor = line.endColor = color;
        line.startWidth = line.endWidth = width;
        SetSquare(line, centre, size, lift);
        line.enabled = true;
    }

    private static void SetSquare(LineRenderer line, Vector3 centre, float size, float lift)
    {
        float h = size / 2f;
        Vector3 up = Vector3.up * lift;
        if (line.positionCount != 4) line.positionCount = 4;
        line.SetPosition(0, centre + up + new Vector3(-h, 0f, -h));
        line.SetPosition(1, centre + up + new Vector3(h, 0f, -h));
        line.SetPosition(2, centre + up + new Vector3(h, 0f, h));
        line.SetPosition(3, centre + up + new Vector3(-h, 0f, h));
    }

    // What a tower on this tile would get: power, or why not. "" without the rule.
    public string PowerNote(Node node)
    {
        if (!PowerRule || node == null) return "";
        var tile = new TileCoord(node.gridX, node.gridY);
        if (Simulation.InPowerReach(Host.State.Core, Host.Config.CorePowerRadius, tile)) return "  \u00b7  powered by the core";
        foreach (Deposit d in Host.Map.Deposits)
            if (Host.State.Grid[d.Tile.X, d.Tile.Y].Occupant == Occupant.Collector && Simulation.InPowerReach(d.Tile, Host.Config.CollectorPowerRadius, tile))
                return "  \u00b7  powered by a collector";
        foreach (TowerState t in Host.State.Towers)
            if (t.IsAlive && !t.OnBench && t.PowerCapacity > 0 && Simulation.InPowerReach(t.Tile, t.PowerRadius, tile))
                return "  \u00b7  powered by a pylon";
        return "  \u00b7  NO POWER here: it will not fire";
    }

    // Towers with power, and towers on the board.
    public void PowerCount(out int powered, out int standing)
    {
        powered = standing = 0;
        foreach (TowerState t in Host.State.Towers)
        {
            if (!t.IsAlive || t.OnBench) continue;
            standing++;
            if (t.Powered) powered++;
        }
    }

    // A collector: a squat block in the ore's gold, taller than a wall so it reads as a building.
    private void DrawCollector(Node node, float cellSize)
    {
        RemoveVisual(node);
        float tall = cellSize * Mathf.Max(0.45f, wallHeight * 1.6f);
        GameObject block = Instantiate(standardWallPrefab, node.worldPosition + new Vector3(0, tall / 2f, 0), Quaternion.identity);
        block.name = "Collector";
        block.transform.localScale = new Vector3(cellSize * 0.7f, tall, cellSize * 0.7f);
        ApplyShapeColor(block, CollectorColor);
        node.visualObject = block;
        wallTiles.Add(node);
    }

    public static readonly Color CollectorColor = new Color(1f, 0.78f, 0.2f);

    // A tower left this tile alive: the tile shows what the simulation has there now,
    // which is the wall the tower stood on, if it stood on one (SimConfig.TowersOnWalls).
    private void ClearTile(Node node, float cellSize)
    {
        RemoveVisual(node);
        SimNode left = Host.State.Grid[node.gridX, node.gridY];
        if (left.Occupant == Occupant.Wall && left.PieceId != SimNode.RockPiece) DrawWall(node, cellSize, -1f); // a mountain is drawn by RockLayout
    }

    // How high a tower's base stands: on a wall or a rock it stands on top of it.
    private float Plinth(Node node, float cellSize)
    {
        SimNode sim = Host.State.Grid[node.gridX, node.gridY];
        if (sim.FoundationCost > 0 && sim.FoundationPiece != SimNode.RockPiece) return cellSize * wallHeight;
        if (sim.IsRock)
        {
            RockLayout rocks = gridManager.GetComponent<RockLayout>();
            return cellSize * (rocks != null ? rocks.heightShare : 0.45f);
        }
        return 0f;
    }

    private void DrawTower(TowerState tower, Node node, float cellSize)
    {
        // Only what stands there NOW is drawn. Normally a row of the log is drawn the
        // frame it is written; after a lost wave is taken up again the whole log is
        // walked at once, and a tower that was later moved, sold or destroyed is not there.
        if (!tower.IsAlive || tower.OnBench || tower.Tile.X != node.gridX || tower.Tile.Y != node.gridY) return;
        RemoveVisual(node);
        float plinth = Plinth(node, cellSize);
        TowerView view = TowerView.Create(tower, node.worldPosition + Vector3.up * plinth, cellSize, billboards);
        if (Host.State.Grid[node.gridX, node.gridY].FoundationCost > 0 && Host.State.Grid[node.gridX, node.gridY].FoundationPiece != SimNode.RockPiece)
        {
            // The wall it stands on, drawn as part of the tower: it goes when the tower goes.
            GameObject under = Instantiate(standardWallPrefab, view.transform);
            under.transform.position = node.worldPosition + new Vector3(0, plinth / 2f, 0);
            under.transform.localScale = new Vector3(cellSize * 0.94f, plinth, cellSize * 0.94f);
            ApplyShapeColor(under, WallColorOf(Host.State.Grid[node.gridX, node.gridY].FoundationMaxHealth));
        }
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
            // The collectors were paid: the deposits show what they have left (SimConfig.DepositOre).
            case SimEventKind.WaveResolved:
                if (gridManager.GetComponent<RockLayout>() != null) gridManager.GetComponent<RockLayout>().SyncDeposits(Host.Sim, Host.State, Host.Map);
                break;
            // A breached wall and a chewed-through tower both leave an empty tile.
            // WallBreached carries the tile in IntA/IntB, TowerDestroyed in IntB/IntC.
            case SimEventKind.WallBreached:
                RemoveVisual(gridManager.grid[e.IntA, e.IntB]);
                if (gridManager.GetComponent<RockLayout>() != null) gridManager.GetComponent<RockLayout>().Sync(Host.State.Grid);
                break;
            case SimEventKind.TowerDestroyed:
                RemoveVisual(gridManager.grid[e.IntB, e.IntC]);
                break;
            case SimEventKind.StateRestored:
                RedrawBoard();
                if (gridManager.GetComponent<RockLayout>() != null) gridManager.GetComponent<RockLayout>().SyncDeposits(Host.Sim, Host.State, Host.Map);
                if (gridManager.GetComponent<RockLayout>() != null) gridManager.GetComponent<RockLayout>().Sync(Host.State.Grid);
                break;
            case SimEventKind.CollectorDestroyed:
                RemoveVisual(gridManager.grid[e.IntA, e.IntB]);
                break;
            // IntA = tower id, IntB = target agent id. The hit is already resolved;
            // this only shows it. A dead agent keeps its last position in the list.
            // A tower's level or gear changed: its view shows both.
            case SimEventKind.TowerChanged:
                if (towerViews.TryGetValue(e.IntA, out TowerView changed) && changed != null) changed.Show(Host.State.Towers[e.IntA], Host.Sim.GearLibrary.Length);
                break;
            // IntA = the siege enemy, IntB = the tower it aimed at.
            case SimEventKind.TowerSieged:
                if (towerViews.TryGetValue(e.IntB, out TowerView struck) && struck != null)
                {
                    AgentState siege = Host.State.Agents[e.IntA];
                    struck.Bombarded(new Vector3(siege.Position.X, Host.GroundY + gridManager.nodeRadius * 1.2f, siege.Position.Y));
                }
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

    private void RedrawBoard()
    {
        foreach (Node node in new List<Node>(wallTiles)) RemoveVisual(node);
        foreach (TowerView view in new List<TowerView>(towerViews.Values)) if (view != null) Destroy(view.gameObject);
        towerViews.Clear();
        placementsDrawn = 0;
        Selected = SelectableAt(Selected);
        CancelUpgrade();
        Host.ShowCore();
        DrawNewWalls();
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
