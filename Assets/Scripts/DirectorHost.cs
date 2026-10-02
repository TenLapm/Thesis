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

        // NOT a study condition. A development planner that sends sappers and
        // flyers in a fixed four-wave cycle (ground, sappers, flyers, all three), so
        // the movement classes of WP-C2 can be played before the director's
        // strategies exist. See Thesis.Sim.ClassCyclePlanner.
        DevClassCycle = 100,
    }

    public Condition condition = Condition.StaticEscalation;

    public IWavePlanner CreatePlanner(SimConfig config, MapData map)
    {
        IWavePlanner planner;
        switch (condition)
        {
            case Condition.DevClassCycle:
                planner = new ClassCyclePlanner(config, map);
                Debug.Log("[Director] DEVELOPMENT planner 'class-cycle': waves cycle ground, sappers (orange, dig through walls), flyers (blue, fly over everything; "
                          + "only the archer can hit them), then all three. Not a study condition. Set DirectorHost.condition back to StaticEscalation for the baseline.");
                break;
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
