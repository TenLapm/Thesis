using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

// WP4: player input for building, plus the visuals of what is built.
//
// A click no longer places anything itself. It becomes a PlaceShape command; the
// simulation checks legality and budget, spends, sets the terrain and rebuilds the
// field (Thesis.Sim.Placement). Walls are then DRAWN from the simulation's own
// record - new PlacementLog rows and WallBreached events - so what is on screen is
// always what the simulation holds, whoever placed it (a click today, a replay in WP5).
//
// Towers (WP-C1) are drawn the same way, as a cube tinted by damage type. That is a
// placeholder, and so is the way to buy one: Z / X / C put the roster's first three
// towers on the tile under the mouse. The shop, its panel and real tower visuals
// replace both in WP-C3 and WP-C4.
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

    // Reused across placements to avoid allocating a new block every wall.
    private MaterialPropertyBlock wallPropertyBlock;
    private bool isGameOver = false;

    private int placementsDrawn;
    private readonly List<Node> wallTiles = new List<Node>();
    private readonly List<TileCoord> tileBuffer = new List<TileCoord>();

    private SimHost Host => simHost != null ? simHost : (simHost = SimHost.Find());

    // True while the player is not allowed to build. Shared with GhostPreviewer
    // so the hover preview hides itself instead of dangling a "valid" ghost over
    // a click that would do nothing.
    public bool IsBuildingBlocked
    {
        get { return isGameOver || (speedController != null && speedController.IsPaused); }
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

    void Start()
    {
        // Until the shop UI exists nothing on screen says towers can be bought at all.
        if (Host == null) return;
        TowerDef[] roster = Host.Sim.TowerLibrary;
        string[] keys = { "Z", "X", "C" };
        string hint = "";
        for (int i = 0; i < roster.Length && i < keys.Length; i++) hint += (i > 0 ? ", " : "") + keys[i] + " = " + roster[i].Id + " (" + roster[i].Cost + ")";
        if (hint != "") Debug.Log("[Sim] Towers (temporary keys, the shop arrives in WP-C3/C4): hover a tile and press " + hint + ".");
    }

    void OnDisable()
    {
        if (simHost != null) simHost.SimEventRaised -= OnSimEvent;
    }

    void Update()
    {
        if (!isGameOver && Mouse.current != null && Keyboard.current != null)
        {
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                // No building while paused - pause is a plan-and-look state now, not a
                // build-while-frozen one. (Rotating/holding the piece still works, so
                // the player can still line up their NEXT move while paused.)
                bool paused = speedController != null && speedController.IsPaused;

                // Without the UI check, clicking Start/Restart (or any other HUD button)
                // would also raycast into the scene underneath and place a wall.
                if (!paused && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
                {
                    HandlePlacement();
                }
            }

            if (Keyboard.current.leftShiftKey.wasPressedThisFrame)
            {
                blockManager.SwapHoldShape();
            }

            HandleDevTowerKeys();
        }

        DrawNewWalls();
        DrawChewProgress();
    }

    private void HandlePlacement()
    {
        if (standardWallPrefab == null)
        {
            Debug.LogError("PlayerBuilder: standardWallPrefab is not assigned in the Inspector!");
            return;
        }
        if (blockManager == null || blockManager.currentShape == null) return;

        Node originNode = NodeUnderMouse();
        if (originNode == null) return;

        // Same messages as before. The simulation re-checks both and has the final
        // say; these are only here to explain a click that does nothing.
        List<Vector2Int> targetTiles = blockManager.GetTargetGridPositions(originNode.gridX, originNode.gridY);
        if (!AreTilesPlaceable(targetTiles))
        {
            Debug.LogWarning("Placement blocked: out of bounds, overlapping a wall, or on a protected tile (spawn/core).");
            return;
        }
        int cost = blockManager.currentShape.buildCost;
        if (blockManager.buildBudget < cost)
        {
            Debug.LogWarning($"Placement blocked: not enough build budget ({blockManager.buildBudget}/{cost}).");
            return;
        }

        Host.Submit(SimCommand.PlaceShape(originNode.gridX, originNode.gridY));
    }

    private Node NodeUnderMouse()
    {
        Camera cam = Camera.main;
        if (cam == null) return null;

        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Ray ray = cam.ScreenPointToRay(mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, clickLayer)) return null;
        return gridManager.NodeFromWorldPoint(hit.point);
    }

    // TEMPORARY (WP-C1). There is no shop yet, so every tower type is always for
    // sale: Z, X and C buy the roster's first three and put them on the tile under
    // the mouse. Same rules as a wall click: not while paused, not through the HUD.
    private void HandleDevTowerKeys()
    {
        Keyboard kb = Keyboard.current;
        int index = kb.zKey.wasPressedThisFrame ? 0 : kb.xKey.wasPressedThisFrame ? 1 : kb.cKey.wasPressedThisFrame ? 2 : -1;
        if (index < 0) return;
        if (speedController != null && speedController.IsPaused) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        Node node = NodeUnderMouse();
        if (node != null) TryPlaceTower(index, node.gridX, node.gridY);
    }

    // Sends the PlaceTower command and reports whether the simulation accepted it.
    // The simulation is the only judge of legality and budget (I9).
    public bool TryPlaceTower(int towerIndex, int x, int y)
    {
        TowerDef[] roster = Host.Sim.TowerLibrary;
        if (towerIndex < 0 || towerIndex >= roster.Length) return false;

        int before = Host.State.Towers.Count;
        Host.Submit(SimCommand.PlaceTower(towerIndex, x, y));
        bool placed = Host.State.Towers.Count > before;
        if (!placed)
        {
            Debug.LogWarning("Tower '" + roster[towerIndex].Id + "' not placed at (" + x + "," + y + "): the tile is not free, or the budget ("
                             + Host.State.BuildBudget + ") is below its cost (" + roster[towerIndex].Cost + ").");
        }
        return placed;
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

    // --- wall visuals ---

    private void DrawNewWalls()
    {
        List<PlacementRecord> log = Host.State.PlacementLog;
        float cellSize = gridManager.nodeRadius * 2f;

        for (; placementsDrawn < log.Count; placementsDrawn++)
        {
            PlacementRecord record = log[placementsDrawn];
            Color color;
            if (record.Kind == PlacementKind.Tower)
            {
                color = TowerColor(Host.State.Towers[record.TowerId].Def.DamageType);
            }
            else
            {
                BlockShape master = blockManager.MasterFor(record.ShapeName);
                color = master != null ? master.shapeColor : Color.white;
            }

            for (int i = 0; i < record.Tiles.Length; i++)
            {
                Node node = gridManager.grid[record.Tiles[i].X, record.Tiles[i].Y];
                GameObject wall = Instantiate(standardWallPrefab, node.worldPosition + new Vector3(0, cellSize / 2f, 0), Quaternion.identity);
                wall.transform.localScale = new Vector3(cellSize, cellSize, cellSize);
                ApplyShapeColor(wall, color);

                // Pop-in flourish, staggered per tile so a multi-tile shape ripples in.
                // Added AFTER the final scale is set so the animator captures it.
                WallSpawnAnimator anim = wall.AddComponent<WallSpawnAnimator>();
                anim.Play(i * 0.04f);

                node.visualObject = wall;
                wallTiles.Add(node);
            }
        }
    }

    // Placeholder look for a tower: the wall cube in a colour per damage type.
    private static Color TowerColor(DamageType type)
    {
        switch (type)
        {
            case DamageType.Fire: return new Color(1f, 0.45f, 0.1f);
            case DamageType.Frost: return new Color(0.3f, 0.8f, 1f);
            default: return new Color(0.85f, 0.85f, 0.9f);
        }
    }

    private void OnSimEvent(SimEvent e)
    {
        // A breached wall and a chewed-through tower both leave an empty tile.
        // WallBreached carries the tile in IntA/IntB, TowerDestroyed in IntB/IntC.
        Node node;
        if (e.Kind == SimEventKind.WallBreached) node = gridManager.grid[e.IntA, e.IntB];
        else if (e.Kind == SimEventKind.TowerDestroyed) node = gridManager.grid[e.IntB, e.IntC];
        else return;

        if (node.visualObject != null)
        {
            Destroy(node.visualObject);
            node.visualObject = null;
        }
        wallTiles.Remove(node);
    }

    // Shrink damaged walls so chew progress is readable at a glance. Untouched walls
    // are skipped, which also leaves the placement pop animation alone.
    private void DrawChewProgress()
    {
        SimGrid grid = Host.State.Grid;
        for (int i = 0; i < wallTiles.Count; i++)
        {
            Node node = wallTiles[i];
            SimNode sim = grid[node.gridX, node.gridY];
            if (node.visualObject == null || sim.MaxWallHealth <= 0f || sim.WallHealth >= sim.MaxWallHealth) continue;

            float frac = Mathf.Clamp01(sim.WallHealth / sim.MaxWallHealth);
            Transform t = node.visualObject.transform;
            Vector3 s = t.localScale;
            float full = s.x; // walls are uniform cubes; x keeps the original size
            s.y = full * Mathf.Lerp(0.15f, 1f, frac);
            t.localScale = s;

            Vector3 p = t.position;
            p.y = node.worldPosition.y + s.y / 2f;
            t.position = p;
        }
    }

    // Tints a placed obstacle with its shape's color (base color for normal
    // lighting, boosted emission so it glows / triggers Bloom). Uses a
    // MaterialPropertyBlock rather than a per-instance material so every wall
    // can have its own color without breaking batching.
    private void ApplyShapeColor(GameObject wall, Color shapeColor)
    {
        MeshRenderer renderer = wall.GetComponent<MeshRenderer>();
        if (renderer == null) return;

        if (wallPropertyBlock == null) wallPropertyBlock = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(wallPropertyBlock);

        Color emission = shapeColor * 2.5f;
        emission.a = 1f;

        wallPropertyBlock.SetColor("_BaseColor", shapeColor);
        wallPropertyBlock.SetColor("_EmissionColor", emission);
        renderer.SetPropertyBlock(wallPropertyBlock);
    }
}
