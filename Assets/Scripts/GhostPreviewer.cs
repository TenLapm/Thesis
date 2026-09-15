using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

public class GhostPreviewer : MonoBehaviour
{
    [Header("References")]
    public BlockManager blockManager;
    public GridManager gridManager;
    [Tooltip("Shared placement rules live in PlayerBuilder.AreTilesPlaceable - the preview literally cannot disagree with a real click.")]
    public PlayerBuilder playerBuilder;
    public GameObject ghostTilePrefab;
    public Material validMaterial;
    public Material invalidMaterial;

    private List<GameObject> activeGhostTiles = new List<GameObject>();
    private Camera mainCam;
    private Material lastAppliedMaterial;

    void Start()
    {
        mainCam = Camera.main;
        if (mainCam == null)
        {
            Debug.LogError("GhostPreviewer: No camera tagged 'MainCamera' found in scene!");
        }
    }

    void Update()
    {
        if (Mouse.current == null || mainCam == null) return;

        UpdateGhostPosition();

        // Keyboard.current can be null (e.g. no keyboard attached / device
        // reset), which used to throw every frame here.
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            blockManager.RotateCurrentShape();
        }
    }

    private void UpdateGhostPosition()
    {
        // Building disabled (paused or game over): hide the ghost so it never
        // dangles a "valid" preview over a click that would do nothing.
        if (playerBuilder != null && playerBuilder.IsBuildingBlocked)
        {
            SyncGhostTiles(0);
            return;
        }

        // Hovering the HUD, holding no shape, missing the map, or pointing at
        // nothing valid all HIDE the ghosts now - previously they just froze
        // at their last valid position, which lied about where a click would go.
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            SyncGhostTiles(0);
            return;
        }

        if (blockManager == null || blockManager.currentShape == null)
        {
            SyncGhostTiles(0);
            return;
        }

        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Ray ray = mainCam.ScreenPointToRay(mousePosition);

        // Same reach as the actual placement raycast in PlayerBuilder (it uses
        // Mathf.Infinity too), so the ghost never vanishes at zoom levels where
        // clicking still works.
        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, LayerMask.GetMask("Ground")))
        {
            SyncGhostTiles(0);
            return;
        }

        Node hoverNode = gridManager != null ? gridManager.NodeFromWorldPoint(hit.point) : null;
        if (hoverNode == null)
        {
            SyncGhostTiles(0);
            return;
        }

        List<Vector2Int> targetTiles = blockManager.GetTargetGridPositions(hoverNode.gridX, hoverNode.gridY);
        SyncGhostTiles(targetTiles.Count);

        for (int i = 0; i < targetTiles.Count; i++)
        {
            Vector2Int pos = targetTiles[i];
            GameObject ghost = activeGhostTiles[i];

            if (pos.x >= 0 && pos.x < gridManager.gridSizeX && pos.y >= 0 && pos.y < gridManager.gridSizeY)
            {
                ghost.transform.position = gridManager.grid[pos.x, pos.y].worldPosition + Vector3.up * 0.1f;
            }
            else
            {
                // Tile hangs off the map - park its ghost out of sight.
                ghost.transform.position = Vector3.down * 100;
            }
        }

        bool isValid = playerBuilder != null
            && playerBuilder.AreTilesPlaceable(targetTiles)
            && blockManager.buildBudget >= blockManager.currentShape.buildCost;

        // Only touch renderer materials when validity actually flips.
        Material currentMat = isValid ? validMaterial : invalidMaterial;
        if (currentMat != lastAppliedMaterial)
        {
            lastAppliedMaterial = currentMat;
            foreach (GameObject g in activeGhostTiles)
            {
                MeshRenderer r = g.GetComponent<MeshRenderer>();
                if (r != null) r.sharedMaterial = currentMat;
            }
        }
    }

    // Grows/activates the ghost tile pool to exactly `count` visible tiles.
    private void SyncGhostTiles(int count)
    {
        while (activeGhostTiles.Count < count)
        {
            GameObject ghost = Instantiate(ghostTilePrefab, transform);
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
