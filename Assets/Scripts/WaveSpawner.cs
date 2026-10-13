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

    [Tooltip("How much bigger than the prefab an enemy is drawn. The prefab is small for a tile of two units.")]
    public float agentScale = 1f;
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

    // True when a build phase has no clock (SimConfig.WaitForStart).
    public bool WaitsForStart => Host.Config.WaitForStart;

    // What the next wave is made of, when it is already decided (SimConfig.PlanAhead):
    // "26 sapper", or "30 basic + 15 sapper + 15 flyer". Empty when it is not known.
    public string NextWaveText
    {
        get
        {
            WavePlan plan = Host.State.NextPlan;
            if (plan == null || plan.Groups == null) return "";
            // One entry per enemy type, however many places it comes in at.
            var names = new System.Collections.Generic.List<string>();
            var counts = new System.Collections.Generic.List<int>();
            var entries = new System.Collections.Generic.List<int>();
            foreach (AgentGroup group in plan.Groups)
            {
                int at = names.IndexOf(group.Archetype);
                if (at < 0) { names.Add(group.Archetype); counts.Add(group.Count); }
                else counts[at] += group.Count;
                if (!entries.Contains(group.SpawnIndex)) entries.Add(group.SpawnIndex);
            }
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < names.Count; i++)
            {
                if (text.Length > 0) text.Append(" + ");
                text.Append(counts[i]).Append(' ').Append(names[i]);
            }
            if (entries.Count > 1) text.Append(", from ").Append(entries.Count).Append(" sides");
            return text.ToString();
        }
    }

    // One short line about the unusual enemy types of the next wave, so that a name the
    // player has not met says what it means. Empty when the wave holds none of them.
    public string NextWaveHint
    {
        get
        {
            WavePlan plan = Host.State.NextPlan;
            if (plan == null || plan.Groups == null) return "";
            var said = new System.Collections.Generic.List<string>();
            var text = new System.Text.StringBuilder();
            foreach (AgentGroup group in plan.Groups)
            {
                string hint = HintFor(group.Archetype);
                if (hint == null || said.Contains(group.Archetype)) continue;
                said.Add(group.Archetype);
                if (text.Length > 0) text.Append("   ");
                text.Append(group.Archetype).Append(": ").Append(hint);
            }
            return text.ToString();
        }
    }

    private static string HintFor(string archetype)
    {
        switch (archetype)
        {
            case "sapper": return "digs through walls";
            case "flyer": return "flies over everything";
            case "swarm": return "many, weak";
            case "brute": return "few, very tough";
            case "runner": return "fast";
            case "armored": return "shrugs off physical damage";
            case "ember": return "shrugs off fire";
            case "frostborn": return "immune to frost and slow";
            case "bats": return "many small flyers";
            default: return null;
        }
    }

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
            case SimEventKind.AgentKilled:
            case SimEventKind.AgentLeaked:
            case SimEventKind.AgentRemoved:
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
        // Drawn bigger than the prefab (view only: an enemy is a point to the simulation).
        go.transform.localScale *= agentScale;
        if (view != null) view.groundHeight *= agentScale;
        if (view == null) Debug.LogError("WaveSpawner: agent prefab has no FlowAgent component!");
        return view;
    }
}
