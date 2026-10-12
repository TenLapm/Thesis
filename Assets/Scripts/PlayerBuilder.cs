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

    public void SelectWall() { Tool = BuildTool.Wall; SelectedOffer = -1; }

    public void SelectSell() { Tool = BuildTool.Sell; SelectedOffer = -1; }

    public void PutDown() { Tool = BuildTool.None; SelectedOffer = -1; }

    // Picks a shop offer up. An offer that is bought, or that the budget does not
    // reach, cannot be picked up; clicking the offer already in the hand puts it down.
    public void SelectOffer(int slot)
    {
        TowerOffer[] offers = Host.State.Shop.Offers;
        if (slot < 0 || slot >= offers.Length) return;
        if (Tool == BuildTool.Tower && SelectedOffer == slot) { SelectWall(); return; }
        if (offers[slot].Bought || Host.State.BuildBudget < offers[slot].Price) return;
        Tool = BuildTool.Tower;
        SelectedOffer = slot;
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

            // The shop's offers change under a tower in the hand when a wave resolves.
            if (Tool == BuildTool.Tower && !CanPickUp(SelectedOffer)) SelectWall();
            if (Tool == BuildTool.Sell && !MaySell) dragging = false;

            // No building while paused - pause is a plan-and-look state, not a
            // build-while-frozen one. A press over the HUD belongs to the HUD:
            // without this, clicking Start would also build on the tile behind it.
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                dragging = !overHud && !IsBuildingBlocked;
                lastDragNode = null;
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
                if (first && TryBuyOffer(SelectedOffer, node.gridX, node.gridY)) SelectWall();
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
        if (price < 0 || Host.State.BuildBudget < price) return false;
        return AreTilesPlaceable(blockManager.GetTargetGridPositions(node.gridX, node.gridY));
    }

    public bool CanPickUp(int slot)
    {
        TowerOffer[] offers = Host.State.Shop.Offers;
        return slot >= 0 && slot < offers.Length && !offers[slot].Bought && Host.State.BuildBudget >= offers[slot].Price;
    }

    public bool CanPlaceTower(Node node, int slot)
    {
        if (node == null || !CanPickUp(slot)) return false;
        oneTile.Clear();
        oneTile.Add(new Vector2Int(node.gridX, node.gridY));
        return AreTilesPlaceable(oneTile);
    }

    // The definition of the offer in a slot, or null.
    public TowerDef OfferDef(int slot)
    {
        TowerOffer[] offers = Host.State.Shop.Offers;
        if (slot < 0 || slot >= offers.Length) return null;
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
            if (!record.IsPlacement) continue; // a shop reroll puts nothing on the board

            if (record.Kind == PlacementKind.Tower)
            {
                TowerState tower = Host.State.Towers[record.TowerId];
                Node node = gridManager.grid[tower.Tile.X, tower.Tile.Y];
                TowerView view = TowerView.Create(tower, node.worldPosition, cellSize);
                towerViews[tower.Id] = view;
                node.visualObject = view.gameObject;
                wallTiles.Add(node);
                continue;
            }

            // A wall is drawn a little lower than it is wide: from straight above
            // only its top shows, and a low block keeps the enemies beside it in view.
            for (int i = 0; i < record.Tiles.Length; i++)
            {
                Node node = gridManager.grid[record.Tiles[i].X, record.Tiles[i].Y];
                GameObject wall = Instantiate(standardWallPrefab, node.worldPosition + new Vector3(0, cellSize / 2f, 0), Quaternion.identity);
                wall.transform.localScale = new Vector3(cellSize * 0.94f, cellSize, cellSize * 0.94f);
                ApplyShapeColor(wall, wallColor);

                // Pop-in flourish. Added AFTER the final scale is set so the animator captures it.
                WallSpawnAnimator anim = wall.AddComponent<WallSpawnAnimator>();
                anim.Play(i * 0.04f);

                node.visualObject = wall;
                wallTiles.Add(node);
            }
        }
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
            case SimEventKind.TowerFired:
                if (towerViews.TryGetValue(e.IntA, out TowerView view) && view != null)
                {
                    AgentState target = Host.State.Agents[e.IntB];
                    float y = Host.GroundY + gridManager.nodeRadius * (target.Movement == MovementClass.Flying ? 2.2f : 0.6f);
                    view.Fire(new Vector3(target.Position.X, y, target.Position.Y));
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
            t.localScale = new Vector3(full * size, cellSize * size, full * size);
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
