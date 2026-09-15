using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using TMPro; // The standard for Unity text

public class CanvasDashboard : MonoBehaviour
{
    [Header("Game Systems")]
    public BlockManager blockManager;

    [Header("Shape Display")]
    public Image holdIconImage;
    public TextMeshProUGUI holdCostLabel;
    public RectTransform holdIconTransform;

    public Image currentIconImage;
    public Image currentHighlightImage;
    public TextMeshProUGUI currentCostLabel;
    public RectTransform currentIconTransform;

    public Image[] nextIconImages;
    public TextMeshProUGUI[] nextCostLabels;
    public RectTransform[] nextIconTransforms;

    public Color emptySlotColor = new Color(1f, 1f, 1f, 0.12f);

    private Dictionary<string, Sprite> shapeIconCache = new Dictionary<string, Sprite>();
    private BlockShape lastCurrentShape;
    private BlockShape lastHoldShape;
    private int lastShapeVersion = -1;

    [Header("Game State References")]
    public PlayerCore playerCore;

    [Header("HUD Elements")]
    public TextMeshProUGUI healthText;
    public TextMeshProUGUI budgetText;
    public TextMeshProUGUI waveText;

    [Header("Game Over UI")]
    public GameObject gameOverPanel;
    public TextMeshProUGUI gameOverTimeText;
    public Button restartButton;

    public GameSpeedController speedController;
    public Button speedToggleButton;
    public TextMeshProUGUI speedButtonLabel;
    public Button pauseButton;
    public TextMeshProUGUI pauseButtonLabel;
    public WaveSpawner waveSpawner;
    public Button startWaveButton;

    void Awake()
    {
        // Subscribe in Awake (not Start) so we never miss PlayerCore's initial
        // OnHealthChanged broadcast, which fires from PlayerCore's own Start() -
        // Start-to-Start ordering between different scripts isn't guaranteed, but
        // all Awake() calls finish before any Start() call begins.
        if (playerCore != null)
        {
            playerCore.OnHealthChanged.AddListener(UpdateHealthDisplay);
            playerCore.OnGameOver.AddListener(HandleGameOver);
            if (healthText != null)
            {
                healthText.text = $"HP: {playerCore.maxHealth} / {playerCore.maxHealth}";
            }
        }
    }

    void Start()
    {
        // Hook up the button click event
        startWaveButton.onClick.AddListener(OnStartWaveClicked);

        if (restartButton != null)
        {
            restartButton.onClick.AddListener(OnRestartClicked);
        }

        if (speedToggleButton != null)
        {
            speedToggleButton.onClick.AddListener(() => { if (speedController != null) speedController.CycleSpeed(); });
        }

        if (pauseButton != null)
        {
            pauseButton.onClick.AddListener(() => { if (speedController != null) speedController.TogglePause(); });
        }

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(false);
        }

        RefreshUI();
    }

    // Called every frame from Update(). Icons are only touched (and animated)
    // when the shape reference OR its version changes - the version counter is
    // what makes rotation visible here (rotating mutates the clone in place, so
    // the reference alone never changed and the panel used to show stale art).
    public void RefreshUI()
    {
        if (blockManager == null) return;

        bool versionChanged = blockManager.shapeVersion != lastShapeVersion;
        lastShapeVersion = blockManager.shapeVersion;

        if (blockManager.holdShape != lastHoldShape)
        {
            lastHoldShape = blockManager.holdShape;
            ApplyIcon(holdIconImage, blockManager.holdShape);
            ApplyCostLabel(holdCostLabel, blockManager.holdShape);
            if (holdIconTransform != null) StartCoroutine(PunchScale(holdIconTransform, 0f));
        }

        // A reference change here means a piece was just placed (or swapped into
        // hold) - this doubles as our animation trigger.
        if (blockManager.currentShape != lastCurrentShape)
        {
            lastCurrentShape = blockManager.currentShape;
            ApplyIcon(currentIconImage, blockManager.currentShape);
            ApplyCostLabel(currentCostLabel, blockManager.currentShape);

            if (currentHighlightImage != null && blockManager.currentShape != null)
            {
                Color hl = blockManager.currentShape.shapeColor;
                hl.a = 0.35f;
                currentHighlightImage.color = hl;
            }

            BlockShape[] upcoming = blockManager.nextShapes.ToArray();
            for (int i = 0; i < nextIconImages.Length; i++)
            {
                BlockShape shape = i < upcoming.Length ? upcoming[i] : null;
                ApplyIcon(nextIconImages[i], shape);
                if (nextCostLabels != null && i < nextCostLabels.Length) ApplyCostLabel(nextCostLabels[i], shape);
            }

            // Bouncy pop for the new current piece, then a staggered pop across the
            // queue so it reads as the whole line advancing rather than a flat swap.
            if (currentIconTransform != null) StartCoroutine(PunchScale(currentIconTransform, 0f));
            for (int i = 0; i < nextIconTransforms.Length; i++)
            {
                if (nextIconTransforms[i] != null) StartCoroutine(PunchScale(nextIconTransforms[i], 0.05f * (i + 1)));
            }
        }
        else if (versionChanged)
        {
            // Same piece, new orientation (R was pressed): quietly redraw the
            // current icon so the panel matches the ghost preview - no pop, a
            // rotation isn't a new piece.
            ApplyIcon(currentIconImage, blockManager.currentShape);
            ApplyCostLabel(currentCostLabel, blockManager.currentShape);
        }
    }

    private void ApplyIcon(Image target, BlockShape shape)
    {
        if (target == null) return;
        Sprite icon = GetIconForShape(shape);
        target.sprite = icon;
        target.color = icon != null ? Color.white : emptySlotColor;
        target.enabled = true;
    }

    private void ApplyCostLabel(TextMeshProUGUI label, BlockShape shape)
    {
        if (label == null) return;
        label.text = shape != null ? shape.buildCost.ToString() : "";
    }

    // Cache key includes the tile layout, not just the name - each of a shape's
    // four rotations gets (and keeps) its own icon. Keying by name alone is what
    // froze the panel art on the spawn orientation forever.
    private string GetShapeCacheKey(BlockShape shape)
    {
        var sb = new System.Text.StringBuilder(shape.shapeName);
        if (shape.localTiles != null)
        {
            foreach (var t in shape.localTiles)
            {
                sb.Append('|').Append(t.x).Append(',').Append(t.y);
            }
        }
        return sb.ToString();
    }

    private Sprite GetIconForShape(BlockShape shape)
    {
        if (shape == null) return null;
        string key = GetShapeCacheKey(shape);
        if (!shapeIconCache.TryGetValue(key, out Sprite icon))
        {
            icon = GenerateShapeIcon(shape);
            shapeIconCache[key] = icon;
        }
        return icon;
    }

    // Builds a small pixel-art sprite of the shape's actual cell layout, colored
    // with its assigned shapeColor - a picture of the piece instead of its name.
    private Sprite GenerateShapeIcon(BlockShape shape, int cellSize = 28)
    {
        if (shape == null || shape.localTiles == null || shape.localTiles.Length == 0) return null;

        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (var t in shape.localTiles)
        {
            minX = Mathf.Min(minX, t.x); maxX = Mathf.Max(maxX, t.x);
            minY = Mathf.Min(minY, t.y); maxY = Mathf.Max(maxY, t.y);
        }
        int cols = maxX - minX + 1;
        int rows = maxY - minY + 1;
        int texW = cols * cellSize;
        int texH = rows * cellSize;

        Texture2D tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color[] pixels = new Color[texW * texH];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;
        tex.SetPixels(pixels);

        Color fill = shape.shapeColor;
        Color border = fill * 0.55f; border.a = 1f;
        Color highlight = Color.Lerp(fill, Color.white, 0.4f); highlight.a = 1f;
        int borderPx = Mathf.Max(1, cellSize / 12);

        foreach (var t in shape.localTiles)
        {
            int cellX = (t.x - minX) * cellSize;
            int cellY = (t.y - minY) * cellSize;

            for (int px = 0; px < cellSize; px++)
            {
                for (int py = 0; py < cellSize; py++)
                {
                    Color c;
                    if (px < borderPx || py < borderPx || px >= cellSize - borderPx || py >= cellSize - borderPx)
                    {
                        c = border;
                    }
                    else if (px < cellSize / 2 && py >= cellSize / 2)
                    {
                        c = highlight;
                    }
                    else
                    {
                        c = fill;
                    }
                    tex.SetPixel(cellX + px, cellY + py, c);
                }
            }
        }
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, texW, texH), new Vector2(0.5f, 0.5f), cellSize);
    }

    // Punchy "pop": scales from 0 up past 1x (a slight overshoot) before settling,
    // using a back-out ease - the bounce the player asked for on placement. Uses
    // unscaled time so placing a piece while paused (Time.timeScale = 0) still
    // pops smoothly instead of freezing mid-animation.
    private IEnumerator PunchScale(RectTransform target, float delay, float duration = 0.3f, float overshoot = 1.7f)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / duration);
            float eased = BackOut(p, overshoot);
            target.localScale = Vector3.one * eased;
            yield return null;
        }
        target.localScale = Vector3.one;
    }

    private float BackOut(float t, float overshoot)
    {
        t -= 1f;
        return t * t * ((overshoot + 1f) * t + overshoot) + 1f;
    }

    void Update()
    {
        // Polling is simple and cheap here (a 3-item queue + one label) and keeps
        // BlockManager itself free of any UI dependency.
        RefreshUI();

        if (waveText != null && waveSpawner != null)
        {
            // "Cleared" is only claimed when it's true: the old text celebrated
            // the moment the last enemy SPAWNED, while survivors were still
            // marching at the core.
            int alive = waveSpawner.ActiveAgentCount;

            if (waveSpawner.isIntermission)
            {
                int secs = Mathf.CeilToInt(waveSpawner.intermissionTimeRemaining);
                if (waveSpawner.currentWave == 0)
                {
                    waveText.text = $"Wave 1 begins in {secs}s\n(or press Start)";
                }
                else if (alive > 0)
                {
                    waveText.text = $"Wave {waveSpawner.currentWave}: {alive} still roaming\nNext wave in {secs}s";
                }
                else
                {
                    waveText.text = $"Wave {waveSpawner.currentWave} cleared!\nNext wave in {secs}s";
                }
            }
            else if (waveSpawner.currentWave > 0)
            {
                waveText.text = alive > 0
                    ? $"Wave {waveSpawner.currentWave}: {alive} roaming"
                    : $"Wave {waveSpawner.currentWave}";
            }
            else
            {
                waveText.text = "Get ready...";
            }
        }

        if (budgetText != null && blockManager != null)
        {
            budgetText.text = $"Build Budget: {blockManager.buildBudget:0.#}";
        }

        if (speedController != null)
        {
            if (speedButtonLabel != null) speedButtonLabel.text = speedController.CurrentSpeedLabel;
            if (speedToggleButton != null) speedToggleButton.interactable = !speedController.IsPaused;
            if (pauseButtonLabel != null) pauseButtonLabel.text = speedController.IsPaused ? "Resume" : "Pause";
        }
    }

    private void OnStartWaveClicked()
    {
        // No longer disabled after one click - StartWave() itself is a safe no-op
        // while a wave is active, and this button now doubles as a "skip the rest
        // of the countdown" action for every intermission, not just the first one.
        if (waveSpawner != null) waveSpawner.StartWave();
    }

    private void UpdateHealthDisplay(int currentHealth)
    {
        if (healthText != null && playerCore != null)
        {
            healthText.text = $"HP: {Mathf.Max(0, currentHealth)} / {playerCore.maxHealth}";
        }
    }

    private void HandleGameOver()
    {
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
        }
        if (gameOverTimeText != null)
        {
            // A wave number reached is a far more meaningful result than wall-clock
            // time survived, which mostly just measured how long the fixed-length
            // intermissions happened to run for.
            int waveReached = waveSpawner != null ? waveSpawner.currentWave : 0;
            gameOverTimeText.text = $"You reached Wave {waveReached}";
        }
    }

    private void OnRestartClicked()
    {
        // ExecuteGameOver() froze Time.timeScale at 0 - restore it before reloading.
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
