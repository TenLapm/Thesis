using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WaveSpawner : MonoBehaviour
{
    [Header("System References")]
    public FlowFieldManager flowManager;
    public Transform spawnPoint;
    public BlockManager blockManager;
    public PlayerCore playerCore;

    [Header("Agent Pool")]
    public GameObject agentPrefab;
    public List<GameObject> agentPool = new List<GameObject>();

    [Header("Wave Settings (Wave 1 baseline)")]
    public int agentsPerWave = 15;
    public float spawnDelay = 0.5f;

    [Header("Wave Escalation (per wave beyond #1)")]
    public int agentsPerWaveIncrement = 5;
    public float spawnDelayDecrement = 0.02f;
    public float minSpawnDelay = 0.15f;
    public float baseLifeTime = 60f;
    [Tooltip("Enemies get TOUGHER each wave: their stall timer (their 'HP') grows by this many seconds per wave, so the player's maze has to buy proportionally more detour/dig time to keep stalling them.")]
    public float lifeTimeIncrementPerWave = 4f;
    [Tooltip("Upper bound on the per-wave-grown lifetime so very late waves stay finite.")]
    public float maxLifeTime = 200f;
    public float baseMoveSpeed = 1.25f;
    public float moveSpeedIncrementPerWave = 0.0375f;
    public float maxMoveSpeed = 2.25f;

    [Header("Pacing")]
    public float intermissionDuration = 10f;
    public float initialPrepDuration = 45f;
    public int budgetStipendPerWave = 20;

    [Tooltip("baseLifeTime is auto-derived at Start() as the fastest possible unobstructed spawn-to-core travel time (straight-line distance / baseMoveSpeed) plus this margin - the extra detour/dig time the player's maze must force before an enemy expires.")]
    public float lifeTimeSafetyMargin = 5f;

    [Header("Wave State (read-only at runtime)")]
    public int currentWave = 0;
    public bool isIntermission = false;
    public float intermissionTimeRemaining = 0f;

    private bool isWaveActive = false;
    private bool isGameOver = false;
    private Coroutine activeIntermissionCoroutine;

    // Live enemies still on the field - lets the HUD say "cleared" only when
    // it's actually true (previously it congratulated you the moment the last
    // enemy SPAWNED, while a dozen were still marching at the core).
    public int ActiveAgentCount
    {
        get
        {
            int count = 0;
            if (agentPool != null)
            {
                foreach (GameObject agent in agentPool)
                {
                    if (agent != null && agent.activeInHierarchy) count++;
                }
            }
            return count;
        }
    }

    void Awake()
    {
        if (playerCore != null)
        {
            playerCore.OnGameOver.AddListener(HandleGameOver);
        }
    }

    void Start()
    {
        // baseLifeTime = the fastest an enemy could possibly reach the core
        // (straight line, nothing built yet) plus a safety margin - so a totally
        // unobstructed run always reaches the core, and the player's maze has to
        // force at least `lifeTimeSafetyMargin` seconds of extra detour/dig time
        // to stall one out. Derived from live spawn/core positions and the
        // current baseMoveSpeed so rebalancing either later can't desync this.
        if (spawnPoint != null && playerCore != null)
        {
            float fastestPossibleTime = Vector3.Distance(spawnPoint.position, playerCore.transform.position) / baseMoveSpeed;
            baseLifeTime = fastestPossibleTime + lifeTimeSafetyMargin;
        }

        // Give the player a build-up phase before the first assault.
        activeIntermissionCoroutine = StartCoroutine(IntermissionRoutine(initialPrepDuration));
    }

    private void HandleGameOver()
    {
        isGameOver = true;
        StopAllCoroutines();
    }

    // Called by the Start Wave button, or automatically when intermission ends.
    public void StartWave()
    {
        if (isWaveActive || isGameOver) return;

        if (activeIntermissionCoroutine != null)
        {
            StopCoroutine(activeIntermissionCoroutine);
            activeIntermissionCoroutine = null;
        }

        currentWave++;
        isWaveActive = true;
        isIntermission = false;
        StartCoroutine(SpawnTrickleRoutine());
    }

    private IEnumerator SpawnTrickleRoutine()
    {
        // Scale difficulty with the wave number.
        int waveIndex = currentWave - 1;
        int waveAgentCount = agentsPerWave + waveIndex * agentsPerWaveIncrement;
        float waveSpawnDelay = Mathf.Max(minSpawnDelay, spawnDelay - waveIndex * spawnDelayDecrement);
        // Enemies grow tougher each wave: lifetime (their effective HP) climbs
        // with the wave number, capped so it stays finite.
        float waveLifeTime = Mathf.Min(maxLifeTime, baseLifeTime + waveIndex * lifeTimeIncrementPerWave);
        float waveMoveSpeed = Mathf.Min(maxMoveSpeed, baseMoveSpeed + waveIndex * moveSpeedIncrementPerWave);

        Debug.Log($"Wave {currentWave}: {waveAgentCount} agents, {waveLifeTime}s life, {waveMoveSpeed} speed, {waveSpawnDelay}s spawn delay");

        for (int i = 0; i < waveAgentCount; i++)
        {
            SpawnSingleAgent(waveLifeTime, waveMoveSpeed);
            yield return new WaitForSeconds(waveSpawnDelay);
        }

        isWaveActive = false;
        if (!isGameOver)
        {
            activeIntermissionCoroutine = StartCoroutine(IntermissionRoutine(intermissionDuration));
        }
    }

    private IEnumerator IntermissionRoutine(float duration)
    {
        isIntermission = true;
        intermissionTimeRemaining = duration;

        // Fresh building funds between waves (not before the first one - the
        // starting budget covers the prep phase).
        if (blockManager != null && currentWave > 0)
        {
            blockManager.buildBudget += budgetStipendPerWave;
        }

        while (intermissionTimeRemaining > 0f)
        {
            if (isGameOver) yield break;
            intermissionTimeRemaining -= Time.deltaTime;
            yield return null;
        }

        isIntermission = false;
        if (!isGameOver) StartWave();
    }

    private void SpawnSingleAgent(float lifeTime, float speed)
    {
        GameObject newAgent = GetAgentFromPool();
        if (newAgent == null) return;

        newAgent.transform.position = spawnPoint != null ? spawnPoint.position : transform.position;
        newAgent.SetActive(true);

        FlowAgent agentScript = newAgent.GetComponent<FlowAgent>();
        if (agentScript != null)
        {
            agentScript.Initialize(flowManager, playerCore, lifeTime, speed, blockManager);
        }
        else
        {
            Debug.LogError("WaveSpawner: agent prefab has no FlowAgent component!");
            newAgent.SetActive(false);
        }
    }

    private GameObject GetAgentFromPool()
    {
        if (agentPool == null) agentPool = new List<GameObject>();

        // Reuse a sleeping agent if one is available.
        foreach (GameObject agent in agentPool)
        {
            if (agent != null && !agent.activeInHierarchy) return agent;
        }

        // Pool exhausted - grow it.
        if (agentPrefab == null)
        {
            Debug.LogError("WaveSpawner: agentPrefab is not assigned in the Inspector!");
            return null;
        }

        GameObject overflowAgent = Instantiate(agentPrefab);
        overflowAgent.SetActive(false);
        agentPool.Add(overflowAgent);
        return overflowAgent;
    }
}
