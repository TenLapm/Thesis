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
                    HoverText = playerBuilder.AreTilesPlaceable(tiles) ? (valid ? "Wall  " + playerBuilder.WallPrice : "Not enough budget") : "";
                    if (HoverText.Length == 0) tiles.Clear(); // nothing to build on a rock or a built tile
                    break;

                case BuildTool.Tower:
                    TowerDef def = playerBuilder.OfferDef(playerBuilder.SelectedOffer);
                    if (def == null) break;
                    TowerOffer card = host.State.Shop.Offers[playerBuilder.SelectedOffer];
                    tiles.Add(new Vector2Int(hover.gridX, hover.gridY));
                    valid = playerBuilder.CanPlaceTower(hover, playerBuilder.SelectedOffer);
                    float range = def.RangeTiles;
                    for (int level = 2; level <= card.Level; level++) range += def.LevelMod(level).RangeAdd;
                    ringRadius = range * cell;
                    bool room = playerBuilder.HasRoomFor(card);
                    HoverText = !room ? "Tower limit reached: sell one, or complete a set of " + host.Config.CombineCount
                              : !valid ? "Cannot stand here"
                              : playerBuilder.WouldCombine(card) ? def.Id + ": completes a set, becomes " + TowerView.LevelName(card.Level + 1)
                              : def.Id + "  range " + range.ToString("0.#") + " tiles";
                    break;

                case BuildTool.Sell:
                    if (!playerBuilder.SalePreview(hover, tiles, out float refund)) break;
                    valid = playerBuilder.MaySell;
                    redGhost = true; // red is what is about to go
                    HoverText = valid ? "Sell  +" + refund.ToString("0.#") : "Selling is for the time between waves";
                    break;

                case BuildTool.Gear:
                    tiles.Add(new Vector2Int(hover.gridX, hover.gridY));
                    if (tower == null) { HoverText = "Gear goes on a tower"; break; }
                    valid = tower.FreeGearSlot() >= 0;
                    HoverText = valid ? "Put it on " + playerBuilder.Describe(tower) : "No free gear slot (a rare tower has two)";
                    ringRadius = tower.RangeTiles * cell;
                    break;

                case BuildTool.MoveTower:
                    if (!playerBuilder.MayMoveTower) { if (tower != null) HoverText = "Towers move between waves"; break; }
                    TowerState carried = playerBuilder.TowerFrom != null ? playerBuilder.TowerAt(playerBuilder.TowerFrom) : null;
                    if (tower != null)
                    {
                        tiles.Add(new Vector2Int(hover.gridX, hover.gridY));
                        ringRadius = tower.RangeTiles * cell;
                        valid = true;
                        HoverText = tower == carried ? "Now click a free tile" : "Pick up " + playerBuilder.Describe(tower);
                    }
                    else if (carried != null)
                    {
                        tiles.Add(new Vector2Int(hover.gridX, hover.gridY));
                        ringRadius = carried.RangeTiles * cell;
                        valid = playerBuilder.IsFreeTile(hover);
                        HoverText = valid ? "Move " + playerBuilder.Describe(carried) + " here" : "Cannot stand here";
                    }
                    break;

                case BuildTool.MoveGear:
                    if (tower == null) break;
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
    }

    private void ShowTiles(bool valid)
    {
        SyncGhostTiles(tiles.Count);
        for (int i = 0; i < tiles.Count; i++)
        {
            Vector2Int pos = tiles[i];
            bool onMap = pos.x >= 0 && pos.x < gridManager.gridSizeX && pos.y >= 0 && pos.y < gridManager.gridSizeY;
            // Over something that stands there, the ghost sits on top of it.
            bool built = playerBuilder.Tool != BuildTool.Wall && playerBuilder.Tool != BuildTool.Tower
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
