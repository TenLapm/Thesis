using System;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Harness
{
    // A scripted player: the stand-in for a human in headless runs (the offline
    // ladder, gates G1-G3, determinism checks).
    //
    // A policy acts exactly the way a human does - by sending SimCommands - so a
    // policy-driven run records and replays like any other session, and a policy
    // can never do something the real game would refuse.
    public interface IPlayerPolicy
    {
        // Written to the replay's Policy field and, later, to telemetry.
        string Name { get; }

        // Called once at the start of every build phase (Prep and each Intermission).
        //
        // `sim` is for READING: State, Map, Config. Never Tick() it or Enqueue on it
        // directly - input goes through `send`, which records the command and applies
        // it at once, so sim.State is already up to date when `send` returns.
        //
        // `rng` is the run's Policy stream (RngStreams.Policy). A policy that needs
        // randomness draws from it and nowhere else.
        void OnIntermission(Simulation sim, Action<SimCommand> send, IRandom rng);
    }
}
