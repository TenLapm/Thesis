using Thesis.Sim;
using UnityEngine;

// Chooses which wave planner - which study condition - the simulation runs.
// WP4 ships only the static baseline; the director conditions and snapshot import
// arrive in WP10/WP11/WP14 (ARCHITECTURE.md §6). The exception fallback is already
// here (SafePlanner), so it is in place before the first planner that can fail.
public class DirectorHost : MonoBehaviour
{
    public enum Condition
    {
        // The game as it exists today: WaveSpawner's escalation, ported verbatim.
        StaticEscalation = 0,
    }

    public Condition condition = Condition.StaticEscalation;

    public IWavePlanner CreatePlanner(SimConfig config, MapData map)
    {
        IWavePlanner planner;
        switch (condition)
        {
            case Condition.StaticEscalation:
            default:
                planner = new EscalationPlanner(config, map);
                break;
        }

        // Every planner runs inside SafePlanner, the baseline included: if it throws
        // or returns a plan the simulation would refuse, that wave falls back to the
        // static escalation and the session goes on. The wrapper keeps the inner
        // planner's name, so replays and telemetry still name the real condition.
        return new SafePlanner(planner, new EscalationPlanner(config, map), map, config) { OnFallback = Debug.LogError };
    }
}
