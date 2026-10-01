using System;
using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Harness
{
    // One headless game from tick 0 to its end: a scripted policy builds, the
    // planner sends waves, and everything is recorded as a replay. This loop is the
    // only place headless runs advance a simulation, so the ladder and the gates
    // (WP12, WP13) get recording and the determinism guarantee for free.
    public static class EpisodeRunner
    {
        public static EpisodeResult Run(EpisodeOptions o)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            if (o.Map == null || o.Shapes == null || o.Policy == null) throw new ArgumentException("EpisodeOptions needs Map, Shapes and Policy.");

            IWavePlanner planner = Registry.CreatePlanner(o.Planner, o.Config, o.Map);
            var sim = new Simulation(o.Config, o.Map, o.Shapes, o.Seed, planner);
            var recorder = new ReplayRecorder(sim, o.RecordTickHashes) { Build = o.Build, Session = o.Session, Policy = o.Policy.Name };
            var policyRng = new Pcg32(o.Seed, RngStreams.Policy);

            Action<SimCommand> send = command =>
            {
                recorder.OnCommand(command);
                sim.Enqueue(command);
                sim.FlushInput();
            };

            var result = new EpisodeResult();
            SimState s = sim.State;
            bool builtThisPhase = false;

            while (!s.IsGameOver && result.WavesResolved < o.MaxWaves)
            {
                if (s.Tick >= o.MaxTicks)
                {
                    result.HitTickLimit = true;
                    break;
                }

                if (s.Phase == SimPhase.Prep || s.Phase == SimPhase.Intermission)
                {
                    if (!builtThisPhase)
                    {
                        o.Policy.OnIntermission(sim, send, policyRng);
                        if (o.StartWavesEarly) send(SimCommand.StartWaveNow());
                        builtThisPhase = true;
                    }
                }
                else
                {
                    builtThisPhase = false;
                }

                sim.Tick();
                recorder.AfterTick();

                IReadOnlyList<SimEvent> events = sim.LastTickEvents;
                for (int i = 0; i < events.Count; i++)
                {
                    if (events[i].Kind != SimEventKind.WaveResolved) continue;
                    result.WavesResolved++;
                    result.Outcomes.Add(s.LastOutcome.Copy());
                }
            }

            result.Replay = recorder.Snapshot();
            result.GameOver = s.IsGameOver;
            result.Ticks = s.Tick;
            result.CoreHp = s.CoreHp;
            result.BuildBudget = s.BuildBudget;
            result.Placements = s.PlacementLog.Count;
            return result;
        }
    }
}
