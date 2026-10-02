using System;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Harness
{
    // Several policies taking turns in the same build phase, in the order given:
    // each one sees the board the previous one left and the budget it did not spend.
    //
    // The "mixed" player is walls first, then towers. The order matters: towers are
    // placed against the route as it is, so they have to come after the walls that
    // move it. (Towers first, then walls, loses in wave 1: the walls reroute the
    // enemies away from the towers that were just bought.)
    public sealed class SequencePolicy : IPlayerPolicy
    {
        public const string MixedId = "mixed";

        private readonly IPlayerPolicy[] steps;

        public SequencePolicy(string name, params IPlayerPolicy[] steps)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("A policy needs a name.", nameof(name));
            if (steps == null || steps.Length == 0) throw new ArgumentException("A sequence needs at least one policy.", nameof(steps));
            foreach (IPlayerPolicy step in steps) if (step == null) throw new ArgumentException("A step is null.", nameof(steps));

            Name = name;
            this.steps = steps;
        }

        public string Name { get; }

        // Walls (a few, to bend the route), then towers along the route that results.
        public static SequencePolicy Mixed(int wallsPerPhase = 2, int towersPerPhase = 6, int wallSearchOrigins = 24)
        {
            return new SequencePolicy(MixedId,
                new GreedyDetourPolicy { MaxPlacementsPerIntermission = wallsPerPhase, MaxOriginsPerPlacement = wallSearchOrigins },
                new SentryPolicy { MaxTowersPerPhase = towersPerPhase });
        }

        public void OnIntermission(Simulation sim, Action<SimCommand> send, IRandom rng)
        {
            for (int i = 0; i < steps.Length; i++) steps[i].OnIntermission(sim, send, rng);
        }
    }
}
