using System;
using System.Collections.Generic;
using Thesis.Core;
using Thesis.Director;
using Thesis.Sim;

namespace Thesis.Harness
{
    // One headless game from tick 0 to its end: a scripted policy builds, the
    // planner sends waves, and everything is recorded as a replay and as telemetry.
    // This loop is the only place headless runs advance a simulation, so the ladder
    // and the gates (WP12, WP13) get recording and the determinism guarantee for free.
    public static class EpisodeRunner
    {
        public static EpisodeResult Run(EpisodeOptions o)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            if (o.Map == null || o.Shapes == null || o.Towers == null || o.Policy == null) throw new ArgumentException("EpisodeOptions needs Map, Shapes, Towers and Policy.");

            IWavePlanner planner = Registry.CreatePlanner(o.Planner, o.Config, o.Map);
            var sim = new Simulation(o.Config, o.Map, o.Shapes, o.Towers, o.Seed, planner);
            var recorder = new ReplayRecorder(sim, o.RecordTickHashes) { Build = o.Build, Session = o.Session, Policy = o.Policy.Name };
            var telemetry = new TelemetryRecorder(sim, o.Session, o.Build, o.Policy.Name);
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
            bool waitedForPlan = false;

            while (!s.IsGameOver && result.WavesResolved < o.MaxWaves)
            {
                s = sim.State; // an Undo, a Redo or a RetryWave puts another state in place
                if (s.Tick >= o.MaxTicks)
                {
                    result.HitTickLimit = true;
                    break;
                }

                if (s.Phase == SimPhase.Prep || s.Phase == SimPhase.Intermission)
                {
                    // With waves planned ahead (SimConfig.PlanAhead) the plan is made on the
                    // first tick of the build phase. The player is told what comes before
                    // building, so the scripted one waits that one tick too.
                    if (!builtThisPhase && sim.Config.PlanAhead && s.NextPlan == null && !waitedForPlan)
                    {
                        waitedForPlan = true;
                    }
                    else if (!builtThisPhase)
                    {
                        o.Policy.OnIntermission(sim, send, policyRng);
                        s = sim.State;
                        builtThisPhase = true;
                        // The player moved the core: the wave is planned again, and the
                        // player gets to answer that plan.
                        if (sim.Config.PlanAhead && s.NextPlan == null && s.WaveIndex == 0 && !s.IsGameOver)
                        {
                            builtThisPhase = false;
                            waitedForPlan = false;
                        }
                        else
                        {
                            // No wave starts while spare gear waits (SimState.GearOverflow): a
                            // policy that left some sells it, so that no episode hangs on it.
                            for (int guard = 0; guard < 64 && s.GearOverflow.Count > 0; guard++) send(SimCommand.SellOverflow(0));
                            if (o.StartWavesEarly) send(SimCommand.StartWaveNow());
                        }
                    }
                }
                else
                {
                    builtThisPhase = false;
                    waitedForPlan = false;
                }

                sim.Tick();
                s = sim.State;
                recorder.AfterTick();
                result.Telemetry.AddRange(telemetry.AfterTick());

                IReadOnlyList<SimEvent> events = sim.LastTickEvents;
                for (int i = 0; i < events.Count; i++)
                {
                    if (events[i].Kind != SimEventKind.WaveResolved) continue;
                    result.WavesResolved++;
                    result.Outcomes.Add(s.LastOutcome.Copy());
                }
            }

            s = sim.State;
            result.Telemetry.AddRange(telemetry.Finish());
            result.Replay = recorder.Snapshot();
            result.GameOver = s.IsGameOver;
            result.Ticks = s.Tick;
            result.CoreHp = s.CoreHp;
            result.BuildBudget = s.BuildBudget;
            result.Placements = s.PlacementLog.Count;
            result.TowersPlaced = s.Towers.Count;
            foreach (TowerState t in s.Towers)
            {
                result.Towers.Add(new TowerSummary
                {
                    Id = t.Def.Id, PricePaid = t.PricePaid, DamageDealt = t.DamageDealt, Shots = t.Shots, Kills = t.Kills,
                    TicksOnBoard = s.Tick - t.PlacedTick, Sold = t.Sold, Destroyed = !t.IsAlive && !t.Sold && !t.Combined,
                });
            }
            return result;
        }
    }
}
