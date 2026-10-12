using UnityEngine;

// The fixed view of the board from straight above (WP-C4, GDD §3.1). The whole board
// is on screen in the build phase and during a wave, and there is nothing to turn,
// move or zoom: one view to learn.
//
// The camera is orthographic and looks straight down, with world +Z up the screen and
// world +X to the right, so the board is drawn the way the map files and the GDD's
// pictures show it. Every frame it fits the board into the part of the screen the HUD
// leaves free (the insets below), which keeps it right when the window changes shape.
//
// View only. The benchmark scenes keep CameraMovement.
[RequireComponent(typeof(Camera))]
public class TopDownCamera : MonoBehaviour
{
    public GridManager gridManager;

    [Header("Share of the screen the HUD covers on each side")]
    [Range(0f, 0.45f)] public float insetLeft = 0.01f;
    [Range(0f, 0.45f)] public float insetRight = 0.25f;
    [Range(0f, 0.45f)] public float insetTop = 0.08f;
    [Range(0f, 0.45f)] public float insetBottom = 0.17f;

    [Tooltip("Free space kept round the board, in tiles.")]
    public float marginTiles = 0.6f;
    public float height = 60f;

    private Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();
        Fit();
    }

    void LateUpdate() { Fit(); }

    private void Fit()
    {
        if (gridManager == null || cam == null) return;

        float free = (gridManager.nodeRadius * 2f) * marginTiles * 2f;
        float boardX = gridManager.gridWorldSize.x + free;
        float boardZ = gridManager.gridWorldSize.y + free;

        float shareX = Mathf.Max(0.1f, 1f - insetLeft - insetRight);
        float shareZ = Mathf.Max(0.1f, 1f - insetTop - insetBottom);
        float aspect = Mathf.Max(0.1f, cam.aspect);

        // Half the height of the view in world units, large enough for both directions.
        float size = Mathf.Max(boardZ / (2f * shareZ), boardX / (2f * aspect * shareX));

        // The free part of the screen is not centred, so neither is the board.
        float offX = (insetLeft + shareX / 2f - 0.5f) * 2f * size * aspect;
        float offZ = (insetBottom + shareZ / 2f - 0.5f) * 2f * size;

        Vector3 centre = gridManager.transform.position;
        cam.orthographic = true;
        cam.orthographicSize = size;
        transform.SetPositionAndRotation(new Vector3(centre.x - offX, centre.y + height, centre.z - offZ), Quaternion.Euler(90f, 0f, 0f));
    }
}
