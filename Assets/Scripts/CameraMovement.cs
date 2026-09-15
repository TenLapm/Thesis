using UnityEngine;
using UnityEngine.InputSystem;

public class CameraMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    public float panSpeed = 20f;
    public float panBorderThickness = 15f;
    [Tooltip("Half-extents of the allowed camera area. Keep this close to the actual map size so the camera can't wander into the void.")]
    public Vector2 panLimit;

    [Header("Zoom Settings")]
    public float scrollSpeed = 5f;
    public float minY = 5f;
    public float maxY = 30f;

    [Header("Rotation Settings (hold Right Mouse Button)")]
    public float yawSpeed = 0.15f;
    public float pitchSpeed = 0.15f;
    public float rollSpeed = 60f;

    [Tooltip("Instantly restores the camera to its starting orientation - the escape hatch for ending up rolled upside down in free-look.")]
    public Key resetOrientationKey = Key.C;

    private Quaternion initialRotation;

    void Start()
    {
        initialRotation = transform.rotation;
    }

    void Update()
    {
        if (Keyboard.current == null || Mouse.current == null) return;

        // Unscaled so the camera stays responsive while the game is paused
        // (building during pause is an intended part of the game).
        float dt = Time.unscaledDeltaTime;
        Vector3 pos = transform.position;

        // Move relative to where the camera is actually facing (flattened onto
        // the ground plane) instead of fixed world axes - otherwise WASD stays
        // locked to world Z/X and stops matching "forward" once free-look (RMB)
        // has yawed the camera away from its start orientation.
        Vector3 forward = transform.forward;
        forward.y = 0f;
        forward.Normalize();
        Vector3 right = transform.right;
        right.y = 0f;
        right.Normalize();

        Vector3 move = Vector3.zero;
        if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) move += forward;
        if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) move -= forward;
        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) move += right;
        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) move -= right;
        if (move.sqrMagnitude > 1f) move.Normalize();

        pos += move * panSpeed * dt;

        float scroll = Mouse.current.scroll.y.ReadValue();
        pos.y -= scroll * scrollSpeed * dt * 0.01f;

        pos.x = Mathf.Clamp(pos.x, -panLimit.x, panLimit.x);
        pos.y = Mathf.Clamp(pos.y, minY, maxY);
        pos.z = Mathf.Clamp(pos.z, -panLimit.y, panLimit.y);

        // Free-look while holding RMB (deliberate: the full 6DOF tumble is a
        // feature, and the reset key below is the safety net).
        if (Mouse.current.rightButton.isPressed)
        {
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            float yaw = mouseDelta.x * yawSpeed;
            float pitch = -mouseDelta.y * pitchSpeed;
            transform.Rotate(Vector3.up, yaw, Space.World);
            transform.Rotate(Vector3.right, pitch, Space.Self);

            float roll = 0f;
            if (Keyboard.current.qKey.isPressed) roll -= rollSpeed * dt;
            if (Keyboard.current.eKey.isPressed) roll += rollSpeed * dt;
            if (roll != 0f) transform.Rotate(Vector3.forward, roll, Space.Self);
        }

        if (Keyboard.current[resetOrientationKey].wasPressedThisFrame)
        {
            transform.rotation = initialRotation;
        }

        transform.position = pos;
    }
}
