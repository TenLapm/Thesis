using Thesis.Sim;
using UnityEngine;

// Chooses which wave planner - which study condition - the simulation runs.
// WP4 ships only the static baseline; the director conditions, the exception
// fallback and snapshot import arrive in WP10/WP11/WP14 (ARCHITECTURE.md §6).
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
        switch (condition)
        {
            case Condition.StaticEscalation:
            default:
                return new EscalationPlanner(config, map);
        }
    }
}
