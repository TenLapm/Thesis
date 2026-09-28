using Thesis.Sim;
using UnityEngine;
using UnityEngine.Events;

// WP4 adapter. Core HP lives in the simulation (an agent that reaches the core
// costs 1 HP inside AgentSystem); this component turns changes into the same
// UnityEvents the HUD, PlayerBuilder, WaveSpawner and GameSpeedController already
// listen to, and still freezes time on game over.
public class PlayerCore : MonoBehaviour
{
    public SimHost simHost;

    [Header("Match Events")]
    // Broadcasting health updates allows any UI Canvas or VFX manager to listen passively
    public UnityEvent<int> OnHealthChanged;
    public UnityEvent OnGameOver;

    private int lastReportedHp = int.MinValue;
    private bool gameOverRaised;

    private SimHost Host => simHost != null ? simHost : (simHost = SimHost.Find());

    // Now SimConfig.CoreMaxHp (SimConfigAsset). Readable from any Awake - it needs the
    // config, not a running simulation.
    public int maxHealth => Host != null ? Host.Config.CoreMaxHp : 10;

    public int currentHealth => Host.State.CoreHp;

    void OnEnable()
    {
        if (Host == null) return;
        Host.Ticked += ReportHealth;
        Host.SimEventRaised += OnSimEvent;
    }

    void OnDisable()
    {
        if (simHost == null) return;
        simHost.Ticked -= ReportHealth;
        simHost.SimEventRaised -= OnSimEvent;
    }

    void Start()
    {
        ReportHealth();
    }

    private void ReportHealth()
    {
        int hp = Host.State.CoreHp;
        if (hp == lastReportedHp) return;
        if (lastReportedHp != int.MinValue) Debug.Log($"Core took damage! Current Integrity: {hp}/{maxHealth}");
        lastReportedHp = hp;
        OnHealthChanged?.Invoke(hp);
    }

    private void OnSimEvent(SimEvent e)
    {
        if (e.Kind != SimEventKind.GameOver || gameOverRaised) return;
        gameOverRaised = true;
        ReportHealth(); // the fatal hit, before the game-over screen
        Debug.LogWarning("GAME OVER! The core integrity has collapsed. Wave " + Host.State.WaveIndex + ", tick " + Host.State.Tick + ".");

        OnGameOver?.Invoke();

        // Halt everything that runs on scaled time (the simulation has already stopped).
        Time.timeScale = 0f;
    }
}
