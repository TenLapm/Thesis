using System.Collections.Generic;
using Thesis.Sim;
using UnityEngine;

// Shows, on the tile under the mouse, what a click would do with the tool in the hand
// (WP-C4): the wall tile, the tower with its range ring, or the tiles a sale would
// clear. Green-ish when the click would work, red-ish when it would not.
//
// It asks PlayerBuilder the same questions the click asks (CanPlaceWall,
// CanPlaceTower, SalePreview), and those end in Thesis.Sim.Placement.CanPlace, so the
// preview cannot disagree with a real click (CLAUDE.md I9).
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

    void Update()
    {
        HoverText = "";
        tiles.Clear();
        bool valid = false;
        float ringRadius = 0f;

        Node hover = playerBuilder != null && !playerBuilder.IsBuildingBlocked ? playerBuilder.HoverNode : null;
        if (hover != null && gridManager != null)
        {
            float cell = gridManager.nodeRadius * 2f;
            switch (playerBuilder.Tool)
            {
                case BuildTool.Wall:
                    tiles.AddRange(blockManager.GetTargetGridPositions(hover.gridX, hover.gridY));
                    valid = playerBuilder.CanPlaceWall(hover);
                    HoverText = playerBuilder.AreTilesPlaceable(tiles)
                        ? (valid ? "Wall  " + playerBuilder.WallPrice : "Not enough budget")
                        : "";
                    if (HoverText.Length == 0) tiles.Clear(); // nothing to say over a rock or a built tile
                    break;

                case BuildTool.Tower:
                    TowerDef def = playerBuilder.OfferDef(playerBuilder.SelectedOffer);
                    if (def == null) break;
                    tiles.Add(new Vector2Int(hover.gridX, hover.gridY));
                    valid = playerBuilder.CanPlaceTower(hover, playerBuilder.SelectedOffer);
                    ringRadius = def.RangeTiles * cell;
                    HoverText = valid ? def.Id + "  range " + def.RangeTiles.ToString("0.#") + " tiles" : "Cannot stand here";
                    break;

                case BuildTool.Sell:
                    if (!playerBuilder.SalePreview(hover, tiles, out float refund)) break;
                    valid = playerBuilder.MaySell;
                    HoverText = valid ? "Sell  +" + refund.ToString("0.#") : "Selling is for the time between waves";
                    break;
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
            // A sale is previewed on top of what stands there, so the ghost sits above it.
            float lift = playerBuilder.Tool == BuildTool.Sell ? gridManager.nodeRadius * 2f + 0.1f : 0.1f;
            activeGhostTiles[i].transform.position = onMap ? gridManager.grid[pos.x, pos.y].worldPosition + Vector3.up * lift : Vector3.down * 100;
        }

        // A sale is drawn in the "invalid" (red) material even when it would work:
        // red is what is about to go.
        Material material = valid && playerBuilder.Tool != BuildTool.Sell ? validMaterial : invalidMaterial;
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
