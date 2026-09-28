using System.Collections.Generic;
using Thesis.Sim;
using UnityEngine;

// WP4 adapter. The wave loop - escalation, spawn trickle, intermission, stipend -
// is now Thesis.Sim (Simulation + EscalationPlanner, numbers ported verbatim, and
// the tuning values moved to SimConfigAsset). This component keeps two jobs:
//   * the wave read-outs CanvasDashboard already uses (same member names), and
//   * the pool of agent GameObjects that draw the simulation's agents.
//
// One behaviour change (decision D4): the intermission now starts when the last
// agent of a wave is resolved, not when it spawns, so "still roaming" during an
// intermission no longer happens.
public class WaveSpawner : MonoBehaviour
{
    [Header("Scene References")]
    [Tooltip("Where agents enter. Read once by SimHost when the simulation starts.")]
    public Transform spawnPoint;
    public SimHost simHost;

    [Header("Agent Views")]
    public GameObject agentPrefab;
    public List<GameObject> agentPool = new List<GameObject>();

    private readonly Dictionary<int, FlowAgent> active = new Dictionary<int, FlowAgent>();
    private readonly List<FlowAgent> activeList = new List<FlowAgent>();
    private readonly Stack<FlowAgent> free = new Stack<FlowAgent>();

    private SimHost Host => simHost != null ? simHost : (simHost = SimHost.Find());

    // --- read-outs (CanvasDashboard) ---

    public int currentWave => Host.State.WaveIndex;

    // True in the pre-game prep as well as between waves, as before.
    public bool isIntermission
    {
        get
        {
            SimPhase p = Host.State.Phase;
            return p == SimPhase.Prep || p == SimPhase.Intermission;
        }
    }

    public float intermissionTimeRemaining => isIntermission ? Host.State.PhaseTicksRemaining * Host.Config.TickSeconds : 0f;

    // Live enemies still on the field.
    public int ActiveAgentCount => Host.State.LiveAgentCount;

    // The Start Wave button. A no-op while a wave is running.
    public void StartWave()
    {
        Host.Submit(SimCommand.StartWaveNow());
    }

    // --- agent views ---

    void OnEnable()
    {
        if (Host == null) return;
        Host.SimEventRaised += OnSimEvent;
        Host.Ticked += OnTicked;
    }

    void OnDisable()
    {
        if (simHost == null) return;
        simHost.SimEventRaised -= OnSimEvent;
        simHost.Ticked -= OnTicked;
    }

    private void OnSimEvent(SimEvent e)
    {
        switch (e.Kind)
        {
            case SimEventKind.AgentSpawned:
                FlowAgent view = Acquire();
                if (view == null) return;
                view.Bind(Host.State.Agents[e.IntA]);
                active[e.IntA] = view;
                activeList.Add(view);
                break;
            case SimEventKind.AgentStalled:
            case SimEventKind.AgentLeaked:
                if (active.TryGetValue(e.IntA, out FlowAgent done))
                {
                    active.Remove(e.IntA);
                    activeList.Remove(done);
                    done.Release();
                    free.Push(done);
                }
                break;
        }
    }

    private void OnTicked()
    {
        var agents = Host.State.Agents;
        for (int i = 0; i < activeList.Count; i++) activeList[i].OnSimTick(agents[activeList[i].AgentId]);
    }

    void Update()
    {
        float alpha = Host.TickAlpha;
        for (int i = 0; i < activeList.Count; i++) activeList[i].Render(alpha);
    }

    private FlowAgent Acquire()
    {
        if (free.Count > 0) return free.Pop();

        if (agentPrefab == null)
        {
            Debug.LogError("WaveSpawner: agentPrefab is not assigned in the Inspector!");
            return null;
        }

        GameObject go = Instantiate(agentPrefab);
        go.SetActive(false);
        agentPool.Add(go);
        FlowAgent view = go.GetComponent<FlowAgent>();
        if (view == null) Debug.LogError("WaveSpawner: agent prefab has no FlowAgent component!");
        return view;
    }
}
