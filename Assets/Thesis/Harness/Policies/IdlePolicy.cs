using System;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Harness
{
    // Builds nothing. The floor of every comparison: on an open board each agent
    // out-runs its lifetime clock, so this player loses in wave 1 on SampleScene.
    public sealed class IdlePolicy : IPlayerPolicy
    {
        public const string Id = "idle";

        public string Name => Id;

        public void OnIntermission(Simulation sim, Action<SimCommand> send, IRandom rng) { }
    }
}
