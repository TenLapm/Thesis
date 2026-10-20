using System.Collections.Generic;
using Thesis.Sim;
using UnityEngine;

// Shows, on the tile under the mouse, what a click would do with what is in the hand:
// the wall tile, the tower with its range ring, the tiles a sale would clear, the
// tower a piece of gear or an upgrade would go to. Green-ish when the click would
// work, red-ish when it would not, and one line of text beside the mouse (HoverText).
//
// It asks PlayerBuilder the same questions the click asks, and those end in the
// simulation's own rules (Thesis.Sim.Placement.CanPlace for anything that is built),
// so the preview cannot disagree with a real click (CLAUDE.md I9).
public class GhostPreviewer : MonoBehaviour
{
    [Header("References")]
    public BlockManager blockManager;
    public GridManager gridManager;
    [Tooltip("Shared placement rules live in PlayerBuilder - the preview literally cannot disagree with a real click.")]
    public PlayerBuilder playerBuilder;
    public GameObject ghostTilePrefab;
    public Material validMaterial;
    public Material invalidMaterial;

    [Header("Range ring")]
    public Color ringColor = new Color(1f, 1f, 1f, 0.85f);
    public Color ringBlockedColor = new Color(1f, 0.4f, 0.4f, 0.85f);

    // What the tooltip beside the mouse says about the tile under it ("" = nothing).
    public string HoverText { get; private set; } = "";

    private readonly List<GameObject> activeGhostTiles = new List<GameObject>();
    private readonly List<Vector2Int> tiles = new List<Vector2Int>();
    private Material lastAppliedMaterial;
    private LineRenderer ring;
    private bool redGhost;

    void Update()
    {
        HoverText = "";
        tiles.Clear();
        bool valid = false;
        float ringRadius = 0f;
        redGhost = false;

        Node hover = playerBuilder != null && !playerBuilder.IsBuildingBlocked ? playerBuilder.HoverNode : null;
        if (hover != null && gridManager != null)
        {
            float cell = gridManager.nodeRadius * 2f;
            SimHost host = playerBuilder.simHost != null ? playerBuilder.simHost : SimHost.Find();
            TowerState tower = playerBuilder.TowerAt(hover);

            switch (playerBuilder.Tool)
            {
                case BuildTool.Wall:
                    tiles.AddRange(blockManager.GetTargetGridPositions(hover.gridX, hover.gridY));
                    valid = playerBuilder.CanPlaceWall(hover);
                    if (playerBuilder.WallsFromStock)
                        HoverText = playerBuilder.AreTilesPlaceable(tiles) ? (valid ? "Wall  (" + host.State.WallsLeft + " left)" : "No wall tiles left")
                                  : host.Map.InBuildMargin(new Thesis.Core.TileCoord(hover.gridX, hover.gridY)) ? "Nothing is built on the border" : "";
                    else
                    HoverText = playerBuilder.AreTilesPlaceable(tiles) ? (valid ? "Wall  " + playerBuilder.WallPrice : "Not enough budget") : "";
                    if (HoverText.Length == 0) tiles.Clear(); // nothing to build on a rock or a built tile
                    break;

                case BuildTool.Tower:
                    if (playerBuilder.SelectedMenuTower >= 0)
                    {
                        // The build menu: any free wall or rock, for the tower's price.
                        TowerDef menu = host.Sim.TowerLibrary[playerBuilder.SelectedMenuTower];
                        tiles.Add(new Vector2Int(hover.gridX, hover.gridY));
                        bool afford = host.State.BuildBudget >= menu.Cost;
                        bool spot = playerBuilder.IsFreeTile(hover);
                        valid = afford && spot;
                        ringRadius = menu.RangeTiles * cell;
                        HoverText = !spot ? (playerBuilder.TowersOnWalls ? "Towers stand on a wall or a rock" : "Cannot stand here")
                                  : !afford ? "Not enough ore: " + menu.Cost
                                  : menu.Id + "  " + menu.Cost + " ore" + playerBuilder.PowerNote(hover);
                        break;
                    }
                    TowerDef def = playerBuilder.OfferDef(playerBuilder.SelectedOffer);
                    if (def == null) break;
                    TowerOffer card = host.State.Shop.Offers[playerBuilder.SelectedOffer];
                    tiles.Add(new Vector2Int(hover.gridX, hover.gridY));
                    if (tower != null && host.Config.FeedDuplicates)
                    {
                        // On a tower: the card is merged into it, if it is the same tower.
                        valid = playerBuilder.CanFeed(card, tower);
                        ringRadius = tower.RangeTiles * cell;
                        HoverText = valid ? "Merge into " + playerBuilder.Describe(tower) + ": becomes " + TowerView.LevelName(playerBuilder.FedLevel(card, tower))
                                  : tower.Def != def ? "Only a " + def.Id + " card merges into a " + def.Id
                                  : tower.Level >= TowerState.MaxLevel ? "Already at the top level"
                                  : "A merge needs " + playerBuilder.MergeCount + " of the same level (this card counts as one)";
                        break;
                    }
                    valid = playerBuilder.CanPlaceTower(hover, playerBuilder.SelectedOffer);
                    float range = def.RangeTiles;
                    for (int level = 2; level <= card.Level; level++) range += def.LevelMod(level).RangeAdd;
                    ringRadius = range * cell;
                    bool room = playerBuilder.HasRoomFor(card);
                    HoverText = !room ? "Tower limit reached: sell one, or complete a set of " + host.Config.CombineCount
                              : !valid ? (playerBuilder.TowersOnWalls ? "Towers stand on a wall or a rock" : "Cannot stand here")
                              : playerBuilder.WouldCombine(card) ? def.Id + ": completes a set, becomes " + TowerView.LevelName(card.Level + 1)
                              : def.Id + "  range " + range.ToString("0.#") + " tiles";
                    break;

                case BuildTool.Sell:
                    if (!playerBuilder.SalePreview(hover, tiles, out float refund)) break;
                    valid = playerBuilder.MaySell;
                    redGhost = true; // red is what is about to go
                    HoverText = valid ? "Sell  +" + refund.ToString("0.#") : "Selling is for the time between waves";
                    if (valid && tower != null && !playerBuilder.HasMove) { valid = false; HoverText = "No moves left this wave"; }
                    if (valid && tower == null && playerBuilder.WallsFromStock)
                    {
                        bool fresh = playerBuilder.IsFreshWall(hover);
                        valid = fresh || host.Config.FreeWallPickup || host.State.RelaysLeft > 0;
                        HoverText = fresh || host.Config.FreeWallPickup ? "Take up: back to your stock" : valid ? "Take up an old wall  (" + host.State.RelaysLeft + " left this wave)" : "No more old walls can be taken up this wave";
                    }
                    break;

                case BuildTool.Gear:
                    tiles.Add(new Vector2Int(hover.gridX, hover.gridY));
                    if (tower == null) { HoverText = "Gear goes on a tower"; break; }
                    valid = tower.FreeGearSlot() >= 0;
                    HoverText = valid ? "Put it on " + playerBuilder.Describe(tower) : "No free gear slot (a rare tower has two)";
                    ringRadius = tower.RangeTiles * cell;
                    break;

                case BuildTool.Upgrade:
                    if (tower == null) break;
                    tiles.Add(new Vector2Int(hover.gridX, hover.gridY));
                    ringRadius = tower.RangeTiles * cell;
                    valid = playerBuilder.CanUpgrade(tower);
                    HoverText = !playerBuilder.HasUpgradeLeft(tower) ? "Fully upgraded: " + PlayerBuilder.UpgradeNames(tower)
                              : !playerBuilder.IsBuildPhase ? "Upgrades are for the time between waves"
                              : valid ? "Upgrade " + tower.Def.Id + " to " + TowerView.LevelName(tower.Rarity + 1).ToLowerInvariant() + " for " + playerBuilder.UpgradeCostFor(tower).ToString("0.#") + ": click, then choose"
                              : "This upgrade costs " + playerBuilder.UpgradeCostFor(tower).ToString("0.#");
                    break;

                case BuildTool.Collector:
                {
                    int deposit = host.Map.DepositAt(new Thesis.Core.TileCoord(hover.gridX, hover.gridY));
                    if (deposit < 0) { HoverText = "Collectors go on ore deposits"; break; }
                    tiles.Add(new Vector2Int(hover.gridX, hover.gridY));
                    valid = playerBuilder.CanBuildCollector(hover);
                    int yield = host.Map.Deposits[deposit].Yield;
                    HoverText = host.State.Grid[hover.gridX, hover.gridY].Occupant == Occupant.Collector ? "A collector: +" + yield + " ore after every wave it survives"
                              : !playerBuilder.IsBuildPhase ? "Collectors are built between waves"
                              : valid ? "Collector: " + host.Config.CollectorPrice.ToString("0.#") + " ore, then +" + yield + " after every wave it survives"
                              : "Not enough ore: " + host.Config.CollectorPrice.ToString("0.#");
                    break;
                }

                case BuildTool.Target:
                    if (tower == null) break;
                    tiles.Add(new Vector2Int(hover.gridX, hover.gridY));
                    ringRadius = tower.RangeTiles * cell;
                    valid = true;
                    HoverText = "Shoots " + PlayerBuilder.ModeName(tower.Mode) + ".  Click: " + PlayerBuilder.ModeName(PlayerBuilder.NextMode(tower.Mode));
                    break;

                case BuildTool.EquipGear:
                    tiles.Add(new Vector2Int(hover.gridX, hover.gridY));
                    if (tower == null) { HoverText = "Gear goes on a tower"; break; }
                    if (playerBuilder.SelectedOverflow < 0 && playerBuilder.SelectedInventory >= 0
                        && playerBuilder.IsExtractor(host.State.GearInventory[playerBuilder.SelectedInventory]))
                    {
                        valid = PlayerBuilder.FirstGearSlot(tower) >= 0;
                        HoverText = valid ? "Extract: " + playerBuilder.Describe(tower) + " loses its gear (to your inventory)" : "This tower has no gear";
                        ringRadius = tower.RangeTiles * cell;
                        break;
                    }
                    valid = tower.FreeGearSlot() >= 0;
                    HoverText = valid ? "Put it on " + playerBuilder.Describe(tower) : "No free gear slot (a rare tower has two)";
                    ringRadius = tower.RangeTiles * cell;
                    break;

                case BuildTool.Core:
                    tiles.Add(new Vector2Int(hover.gridX, hover.gridY));
                    valid = playerBuilder.CanPlaceCore(hover);
                    HoverText = valid ? "Put the core here" : "The core needs open ground, away from the edge";
                    break;

                case BuildTool.Deploy:
                    TowerState benched = playerBuilder.Benched(playerBuilder.SelectedBench);
                    if (benched == null) break;
                    tiles.Add(new Vector2Int(hover.gridX, hover.gridY));
                    ringRadius = benched.RangeTiles * cell;
                    if (!playerBuilder.IsBuildPhase) { HoverText = "The bench is for the time between waves"; break; }
                    if (!playerBuilder.HasMove) { HoverText = "No moves left this wave"; break; }
                    if (tower != null && playerBuilder.CanMerge(benched, tower)) { valid = true; HoverText = "Merge: this " + tower.Def.Id + " becomes " + TowerView.LevelName(tower.Level + 1) + "  (uses " + playerBuilder.MergeCount + ", 1 move)"; break; }
                    if (tower != null) { valid = true; HoverText = "Change places with " + playerBuilder.Describe(tower); break; }
                    bool space = playerBuilder.TowerCap <= 0 || playerBuilder.TowersStanding < playerBuilder.TowerCap;
                    valid = space && playerBuilder.IsFreeTile(hover);
                    HoverText = !space ? "Tower limit reached: change places with a tower instead" : valid ? "Put " + playerBuilder.Describe(benched) + " here"
                              : playerBuilder.TowersOnWalls ? "Towers stand on a wall or a rock" : "Cannot stand here";
                    break;

                case BuildTool.MoveTower:
                    if (!playerBuilder.MayMoveTower) { if (tower != null) HoverText = "Towers move between waves"; break; }
                    if (!playerBuilder.HasMove) { if (tower != null) HoverText = "No moves left this wave"; break; }
                    TowerState carried = playerBuilder.TowerFrom != null ? playerBuilder.TowerAt(playerBuilder.TowerFrom) : null;
                    if (tower != null)
                    {
                        tiles.Add(new Vector2Int(hover.gridX, hover.gridY));
                        ringRadius = tower.RangeTiles * cell;
                        valid = true;
                        HoverText = tower == carried ? "Now click a free tile"
                                  : playerBuilder.CanMerge(carried, tower) ? "Merge: this " + tower.Def.Id + " becomes " + TowerView.LevelName(tower.Level + 1) + "  (uses " + playerBuilder.MergeCount + ", 1 move)"
                                  : "Pick up " + playerBuilder.Describe(tower);
                    }
                    else if (carried != null)
                    {
                        tiles.Add(new Vector2Int(hover.gridX, hover.gridY));
                        ringRadius = carried.RangeTiles * cell;
                        valid = playerBuilder.IsFreeTile(hover);
                        HoverText = valid ? "Move " + playerBuilder.Describe(carried) + " here" + playerBuilder.PowerNote(hover) : playerBuilder.TowersOnWalls ? "Towers stand on a wall or a rock" : "Cannot stand here";
                    }
                    break;

                case BuildTool.MoveGear:
                    if (tower == null) break;
                    if (playerBuilder.UsesInventory)
                    {
                        tiles.Add(new Vector2Int(hover.gridX, hover.gridY));
                        ringRadius = tower.RangeTiles * cell;
                        if (!playerBuilder.MayMoveGear) { HoverText = "Gear comes off between waves"; break; }
                        valid = PlayerBuilder.FirstGearSlot(tower) >= 0;
                        HoverText = valid ? "Take gear off " + playerBuilder.Describe(tower) + "  (to your inventory)" : "This tower has no gear";
                        break;
                    }
                    tiles.Add(new Vector2Int(hover.gridX, hover.gridY));
                    ringRadius = tower.RangeTiles * cell;
                    if (!playerBuilder.MayMoveGear) { HoverText = "Gear moves between waves"; break; }
                    if (playerBuilder.GearFrom == null)
                    {
                        valid = PlayerBuilder.FirstGearSlot(tower) >= 0;
                        HoverText = valid ? "Pick up gear from " + playerBuilder.Describe(tower) : "This tower has no gear";
                    }
                    else
                    {
                        valid = hover != playerBuilder.GearFrom && tower.FreeGearSlot() >= 0;
                        HoverText = valid ? "Move it to " + playerBuilder.Describe(tower) : hover == playerBuilder.GearFrom ? "Click another tower" : "No free gear slot";
                    }
                    break;
            }

            // With nothing else to say about a tower, say what it is.
            if (HoverText.Length == 0 && tower != null)
            {
                HoverText = playerBuilder.Describe(tower);
                ringRadius = tower.RangeTiles * cell;
                valid = true;
            }
        }

        ShowTiles(valid);
        ShowRing(hover, ringRadius, valid);
        ShowFigure(hover, valid);
    }

    // In the tilted scene the tower in the hand is seen standing on the tile under the
    // mouse, faintly, before it is put down.
    private GameObject figure;
    private TowerDef figureDef;
    private int figureLevel;

    private void ShowFigure(Node hover, bool valid)
    {
        TowerDef def = null;
        int level = 1;
        if (playerBuilder != null && playerBuilder.billboards && hover != null && !playerBuilder.IsBuildingBlocked) def = playerBuilder.HandTower(out level);
        // Over a tower the hand's figure would hide the one that stands there.
        if (def != null && playerBuilder.TowerAt(hover) != null) def = null;

        if (def != figureDef || level != figureLevel)
        {
            if (figure != null) Destroy(figure);
            figure = def != null ? TowerView.Figure(def, level, gridManager.nodeRadius * 2f, 0.6f) : null;
            figureDef = def;
            figureLevel = level;
        }
        if (figure != null) figure.transform.position = hover.worldPosition + Vector3.up * 0.1f;
    }

    private void ShowTiles(bool valid)
    {
        SyncGhostTiles(tiles.Count);
        for (int i = 0; i < tiles.Count; i++)
        {
            Vector2Int pos = tiles[i];
            bool onMap = pos.x >= 0 && pos.x < gridManager.gridSizeX && pos.y >= 0 && pos.y < gridManager.gridSizeY;
            // Over something that stands there, the ghost sits on top of it.
            bool built = playerBuilder.Tool != BuildTool.Wall && playerBuilder.Tool != BuildTool.Tower && playerBuilder.Tool != BuildTool.Core && playerBuilder.Tool != BuildTool.Deploy
                         && !(playerBuilder.Tool == BuildTool.MoveTower && onMap && playerBuilder.TowerAt(gridManager.grid[pos.x, pos.y]) == null);
            float lift = built ? gridManager.nodeRadius * 2f + 0.1f : 0.1f;
            activeGhostTiles[i].transform.position = onMap ? gridManager.grid[pos.x, pos.y].worldPosition + Vector3.up * lift : Vector3.down * 100;
        }

        Material material = valid && !redGhost ? validMaterial : invalidMaterial;
        if (material == lastAppliedMaterial) return;
        lastAppliedMaterial = material;
        foreach (GameObject g in activeGhostTiles)
        {
            MeshRenderer r = g.GetComponent<MeshRenderer>();
            if (r != null) r.sharedMaterial = material;
        }
    }

    private void ShowRing(Node hover, float radius, bool valid)
    {
        if (ring == null)
        {
            ring = TowerView.Line(transform, "RangeRing", ringColor, 0.18f, true);
        }
        if (hover == null || radius <= 0f)
        {
            ring.enabled = false;
            return;
        }
        ring.startColor = ring.endColor = valid ? ringColor : ringBlockedColor;
        TowerView.DrawCircle(ring, hover.worldPosition + Vector3.up * 0.4f, radius, 64);
        ring.enabled = true;
    }

    // Grows/activates the ghost tile pool to exactly `count` visible tiles.
    private void SyncGhostTiles(int count)
    {
        while (activeGhostTiles.Count < count)
        {
            GameObject ghost = Instantiate(ghostTilePrefab, transform);
            // One ghost covers one tile, whatever size the prefab was made for.
            float cell = gridManager.nodeRadius * 2f * 0.9f;
            ghost.transform.localScale = new Vector3(cell, ghost.transform.localScale.y, cell);
            MeshRenderer r = ghost.GetComponent<MeshRenderer>();
            if (r != null && lastAppliedMaterial != null) r.sharedMaterial = lastAppliedMaterial;
            activeGhostTiles.Add(ghost);
        }

        for (int i = 0; i < activeGhostTiles.Count; i++)
        {
            activeGhostTiles[i].SetActive(i < count);
        }
    }
}
