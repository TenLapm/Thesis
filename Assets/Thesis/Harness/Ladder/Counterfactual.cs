using System;
using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Harness
{
    // "What if this wave had been planned otherwise?" (WP12): play on from a copy of a running
    // game, with a planner of its own, and leave the real game exactly as it was.
    //
    // The copy gets a NEW planner, never the live one (ARCHITECTURE.md §4.4): a director learns in
    // OnWaveResolved, so handing it the copy's waves would teach the real director about waves that
    // never happened. Simulation.Clone copies the whole state, random streams included, so the copy
    // goes on exactly as the original would have under the same planner and the same player.
    public static class Counterfactual
    {
        public static List<WaveOutcome> PlayOn(Simulation live, IWavePlanner planner, IPlayerPolicy policy, int waves, ulong policySeed)
        {
            if (live == null) throw new ArgumentNullException(nameof(live));
            if (planner == null) throw new ArgumentNullException(nameof(planner));
            if (ReferenceEquals(planner, live.Planner) || (live.Planner is SafePlanner safe && ReferenceEquals(planner, safe.Inner)))
                throw new ArgumentException("a counterfactual needs a planner of its own, never the live one (ARCHITECTURE.md §4.4)");

            Simulation copy = live.Clone(planner);
            var rng = new Pcg32(policySeed, RngStreams.Policy);
            Action<SimCommand> send = c => { copy.Enqueue(c); copy.FlushInput(); };
            var outcomes = new List<WaveOutcome>();
            int target = copy.State.WaveIndex + waves;
            bool built = false;
            for (int guard = 0; guard < 2000000 && !copy.State.IsGameOver; guard++)
            {
                SimState s = copy.State;
                bool phase = s.Phase == SimPhase.Prep || s.Phase == SimPhase.Intermission;
                if (phase)
                {
                    if (s.WaveIndex >= target) break;
                    if (!built && (!copy.Config.PlanAhead || s.NextPlan != null))
                    {
                        if (policy != null) policy.OnIntermission(copy, send, rng);
                        built = true;
                        send(SimCommand.StartWaveNow());
                    }
                }
                else built = false;

                WaveOutcome before = copy.State.LastOutcome;
                copy.Tick();
                WaveOutcome after = copy.State.LastOutcome;
                if (after != null && !ReferenceEquals(after, before)) outcomes.Add(after.Copy());
            }
            return outcomes;
        }
    }
}
