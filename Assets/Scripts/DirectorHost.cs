using Thesis.Director;
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

        // The director without learning (WP10): layers 1 and 2 over the study's four strategies.
        // The ablation condition. The student plays it before it replaces the development director.
        HeuristicDirector = 1,

        // The full director (WP11): the heuristic plus the learner's bounded correction, learning
        // from how much the player rebuilds. Starts cold each session.
        FullDirector = 2,

        // The bottom rungs of the offline ladder, playable for a look: a strategy at random, or
        // the four in turn. Not study conditions.
        LadderRandom = 200,
        LadderRoundRobin = 201,

        // NOT a study condition. A development planner that sends sappers and
        // flyers in a fixed four-wave cycle (ground, sappers, flyers, all three), so
        // the movement classes of WP-C2 can be played before the director's
        // strategies exist. See Thesis.Sim.ClassCyclePlanner.
        DevClassCycle = 100,
        DevRaid = 101,

        // NOT a study condition either: a rough authored director (Thesis.Sim.RoughDirector)
        // that answers towers stacked at the core with outpost raids or siege enemies.
        DevRoughDirector = 102,
    }

    public Condition condition = Condition.StaticEscalation;

    // seed: the session's rngSeed; the director and the ladder planners draw their streams from it.
    public IWavePlanner CreatePlanner(SimConfig config, MapData map, ulong seed)
    {
        IWavePlanner planner;
        switch (condition)
        {
            case Condition.HeuristicDirector:
                planner = PlannerFactory.Create(WaveDirector.HeuristicId, config, map, seed);
                Debug.Log("[Director] the heuristic director (WP10): the study's four strategies, chosen by authored scores. No learning yet.");
                break;
            case Condition.FullDirector:
                planner = PlannerFactory.Create(WaveDirector.FullId, config, map, seed);
                Debug.Log("[Director] the full director (WP11): authored scores plus a learned correction of at most one point either way.");
                break;
            case Condition.LadderRandom:
                planner = PlannerFactory.Create(RandomPlanner.Id, config, map, seed);
                break;
            case Condition.LadderRoundRobin:
                planner = PlannerFactory.Create(RoundRobinPlanner.Id, config, map, seed);
                break;
            case Condition.DevClassCycle:
                planner = new ClassCyclePlanner(config, map);
                Debug.Log("[Director] DEVELOPMENT planner 'class-cycle': waves cycle ground, sappers (orange, dig through walls), flyers (blue, fly over everything; "
                          + "only the archer can hit them), then all three. Not a study condition. Set DirectorHost.condition back to StaticEscalation for the baseline.");
                break;
            case Condition.DevRaid:
                planner = new RaidPlanner(config, map);
                Debug.Log("[Director] DEVELOPMENT planner 'raid': the class cycle's enemies, coming in at one to three entry points chosen wave by wave. Not a study condition.");
                break;
            case Condition.DevRoughDirector:
                planner = new RoughDirector(config, map);
                Debug.Log("[Director] DEVELOPMENT planner 'rough-director': raids your outposts or sends siege enemies when your towers stand by the core. Not a study condition.");
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
