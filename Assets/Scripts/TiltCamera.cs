using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// The board seen at a tilt, the way a stage is seen from the stalls (2026-10-14, the
// "Arknights look" trial scene): towers are flat figures standing upright on a 3D
// board and facing the camera, which only reads when the camera is not straight above.
//
// Orthographic, so tiles stay the same size from the near edge to the far one and a
// tile is still easy to hit with the mouse. Like TopDownCamera it fits the board into
// the part of the screen the HUD leaves free, at any aspect ratio. View only.
[RequireComponent(typeof(Camera))]
public class TiltCamera : MonoBehaviour
{
    public GridManager gridManager;

    [Tooltip("Degrees down from the horizon: 90 is straight above.")]
    [Range(30f, 90f)] public float tilt = 52f;

    [Header("Share of the screen the HUD covers on each side")]
    [Range(0f, 0.45f)] public float insetLeft = 0.01f;
    [Range(0f, 0.45f)] public float insetRight = 0.25f;
    [Range(0f, 0.45f)] public float insetTop = 0.09f;
    [Range(0f, 0.45f)] public float insetBottom = 0.21f;

    [Tooltip("Free space kept round the board, in tiles (the far edge needs some for the figures standing on it).")]
    public float marginTiles = 1.2f;
    public float distance = 90f;

    [Header("Looking closer")]
    [Tooltip("The mouse wheel zooms in up to this many times; the middle mouse button or the arrow keys move the view.")]
    public float maxZoom = 3f;
    public float panSpeed = 30f;

    private Camera cam;
    private float zoom = 1f;
    private Vector2 pan; // in world units along the board's X and Z

    void Awake()
    {
        cam = GetComponent<Camera>();
        Fit();
    }

    void LateUpdate()
    {
        ReadInput();
        Fit();
    }

    // Zoom and pan are for looking only; they change nothing in the game.
    private void ReadInput()
    {
        if (Mouse.current == null || gridManager == null) return;
        bool overHud = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        // The wheel zooms toward the mouse: the spot of ground under it stays under it.
        float wheel = Mouse.current.scroll.ReadValue().y;
        if (!overHud && Mathf.Abs(wheel) > 0.01f)
        {
            Vector2 mouse = Mouse.current.position.ReadValue();
            bool had = GroundUnder(mouse, out Vector3 before);
            zoom = Mathf.Clamp(zoom * (wheel > 0f ? 1.15f : 1f / 1.15f), 1f, maxZoom);
            Fit();
            if (had && GroundUnder(mouse, out Vector3 after)) pan += new Vector2(before.x - after.x, before.z - after.z);
        }

        Vector2 move = Vector2.zero;
        if (Mouse.current.middleButton.isPressed)
        {
            // Drag the board under the mouse: one screen height is two orthographic sizes.
            Vector2 delta = Mouse.current.delta.ReadValue();
            float unitsPerPixel = 2f * cam.orthographicSize / Mathf.Max(1, Screen.height);
            move = new Vector2(-delta.x * unitsPerPixel, -delta.y * unitsPerPixel / Mathf.Sin(tilt * Mathf.Deg2Rad));
        }
        else if (Keyboard.current != null)
        {
            float step = panSpeed * Time.unscaledDeltaTime / zoom;
            // Arrows or WASD (2026-10-28); the wall tool's key moved from W to Q.
            Keyboard k = Keyboard.current;
            if (k.leftArrowKey.isPressed || k.aKey.isPressed) move.x -= step;
            if (k.rightArrowKey.isPressed || k.dKey.isPressed) move.x += step;
            if (k.upArrowKey.isPressed || k.wKey.isPressed) move.y += step;
            if (k.downArrowKey.isPressed || k.sKey.isPressed) move.y -= step;
        }
        pan += move;

        // Never further than the part of the board that zooming pushed off the screen.
        float reach = 1f - 1f / zoom;
        float maxX = gridManager.gridWorldSize.x / 2f * reach, maxZ = gridManager.gridWorldSize.y / 2f * reach;
        pan = new Vector2(Mathf.Clamp(pan.x, -maxX, maxX), Mathf.Clamp(pan.y, -maxZ, maxZ));
    }

    // Where the ray through a screen point meets the board's plane.
    private bool GroundUnder(Vector2 screen, out Vector3 point)
    {
        Ray ray = cam.ScreenPointToRay(screen);
        var ground = new Plane(Vector3.up, gridManager.transform.position);
        bool hit = ground.Raycast(ray, out float along);
        point = hit ? ray.GetPoint(along) : Vector3.zero;
        return hit;
    }

    private void Fit()
    {
        if (gridManager == null || cam == null) return;

        Quaternion rotation = Quaternion.Euler(tilt, 0f, 0f);
        float free = (gridManager.nodeRadius * 2f) * marginTiles * 2f;
        float boardX = gridManager.gridWorldSize.x + free;
        // Depth is foreshortened by the tilt.
        float boardUp = (gridManager.gridWorldSize.y + free) * Mathf.Sin(tilt * Mathf.Deg2Rad);

        float shareX = Mathf.Max(0.1f, 1f - insetLeft - insetRight);
        float shareY = Mathf.Max(0.1f, 1f - insetTop - insetBottom);
        float aspect = Mathf.Max(0.1f, cam.aspect);
        float size = Mathf.Max(boardUp / (2f * shareY), boardX / (2f * aspect * shareX)) / zoom;

        // Shift the camera so the board's centre lands in the middle of the free area.
        float offX = (insetLeft + shareX / 2f - 0.5f) * 2f * size * aspect;
        float offY = (insetBottom + shareY / 2f - 0.5f) * 2f * size;

        Vector3 centre = gridManager.transform.position + new Vector3(pan.x, 0f, pan.y);
        cam.orthographic = true;
        cam.orthographicSize = size;
        transform.SetPositionAndRotation(centre - (rotation * Vector3.right) * offX - (rotation * Vector3.up) * offY - (rotation * Vector3.forward) * distance, rotation);
    }
}
