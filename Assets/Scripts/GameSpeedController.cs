using UnityEngine;
using UnityEngine.InputSystem;

// Two small meta-controls, both driven purely by Time.timeScale (which already
// correctly scales everything in the game - agent movement/life timers via
// Time.deltaTime, wave spawn/intermission coroutines via WaitForSeconds):
//
// - Speed control (x1/x2/x3): one button that cycles up through the speed
//   levels, plus number-row keys 1/2/3 (deliberately NOT the numpad) that jump
//   straight to a level. Lets the player skip through slower moments faster.
// - Pause: NOT a menu, no options - just freezes the simulation so the player
//   has a moment to look and think. Bound to Space in addition to a UI button.
//   Building is disabled while paused (PlayerBuilder checks IsPaused), but
//   rotating/holding the piece still works so the player can line up their NEXT
//   move - "pause to read the board and plan," not "pause to freeze-build."
public class GameSpeedController : MonoBehaviour
{
    [Header("System References")]
    public PlayerCore playerCore;

    [Header("Speed Levels")]
    [Tooltip("Selectable time-scale multipliers. The speed button cycles up through these and wraps; number-row keys 1..N jump straight to the matching entry.")]
    public float[] speedLevels = { 1f, 2f, 3f };

    [Header("Pause")]
    public Key pauseKey = Key.Space;

    // Which speedLevels entry applies while NOT paused.
    private int speedIndex = 0;

    public bool IsPaused { get; private set; }
    public float CurrentSpeed => speedLevels[Mathf.Clamp(speedIndex, 0, speedLevels.Length - 1)];
    // "x1" / "x2" / "x3" for the HUD button label.
    public string CurrentSpeedLabel => "x" + Mathf.RoundToInt(CurrentSpeed);

    private bool isGameOver = false;

    void Awake()
    {
        // PlayerCore.ExecuteGameOver() already freezes Time.timeScale = 0 on its
        // own - once that happens, speed/pause toggling should stop doing
        // anything rather than fight with it (e.g. unpausing must not resume
        // time after the run is already over).
        if (playerCore != null)
        {
            playerCore.OnGameOver.AddListener(HandleGameOver);
        }
    }

    void Start()
    {
        ApplyTimeScale();
    }

    void Update()
    {
        if (isGameOver) return;

        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (kb[pauseKey].wasPressedThisFrame) TogglePause();

        // Number ROW keys (digitNKey), explicitly not the numpad (numpadNKey),
        // pick a speed level directly. 1 -> first level, 2 -> second, etc.
        if (kb.digit1Key.wasPressedThisFrame) SetSpeedIndex(0);
        else if (kb.digit2Key.wasPressedThisFrame) SetSpeedIndex(1);
        else if (kb.digit3Key.wasPressedThisFrame) SetSpeedIndex(2);
    }

    // Wired to the on-screen speed button: cycles x1 -> x2 -> x3 -> x1.
    public void CycleSpeed()
    {
        if (isGameOver || IsPaused) return;
        SetSpeedIndex((speedIndex + 1) % speedLevels.Length);
    }

    // Jump straight to a level. Shared by the number keys. Blocked while paused
    // (same as the button) rather than silently changing the speed that'll apply
    // on unpause - the button is greyed out then too, so this stays consistent.
    public void SetSpeedIndex(int index)
    {
        if (isGameOver || IsPaused) return;
        if (index < 0 || index >= speedLevels.Length) return;

        speedIndex = index;
        ApplyTimeScale();
    }

    public void TogglePause()
    {
        if (isGameOver) return;

        IsPaused = !IsPaused;
        // On resume, re-apply the currently selected level rather than a
        // remembered raw timeScale - single source of truth is speedIndex.
        Time.timeScale = IsPaused ? 0f : CurrentSpeed;
    }

    private void ApplyTimeScale()
    {
        // While paused we stay frozen; TogglePause() re-applies on resume.
        if (IsPaused) return;
        Time.timeScale = CurrentSpeed;
    }

    private void HandleGameOver()
    {
        isGameOver = true;
    }
}
