using System;
using System.Collections.Generic;
using Thesis.Sim;

namespace Thesis.Harness
{
    // Runs a recorded session again and checks it against the hashes it recorded
    // (CLAUDE.md I1; ARCHITECTURE.md §8, "two runs that should match don't").
    //
    // The same class verifies a headless episode and a session played by hand in
    // Unity: both write the same ReplayFile. If a Unity session verifies here, the
    // game rules ran identically in both places, and any bug left is in the view.
    public static class ReplayRunner
    {
        public static Simulation NewSimulation(ReplayFile file)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            IWavePlanner planner = Registry.CreatePlanner(file.Planner, file.Config, file.MapData);
            return new Simulation(file.Config, file.MapData, file.Shapes, file.Towers, file.RngSeed, planner);
        }

        // The simulation as it stood when State.Tick == tick. With
        // includeCommandsAtTick false it is the state before that tick's commands,
        // which is the point the per-tick hashes describe; with true, the commands
        // recorded at that tick are applied too (this is how FinalHash was taken).
        public static Simulation RunToTick(ReplayFile file, int tick, bool includeCommandsAtTick)
        {
            if (tick < 0 || tick > file.FinalTick)
                throw new ArgumentOutOfRangeException(nameof(tick), tick, "The recording covers ticks 0.." + file.FinalTick + ".");

            Simulation sim = NewSimulation(file);
            int cursor = 0;
            while (sim.State.Tick < tick && !sim.State.IsGameOver)
            {
                Feed(file, sim, ref cursor);
                sim.Tick();
            }

            if (includeCommandsAtTick && sim.State.Tick == tick)
            {
                Feed(file, sim, ref cursor);
                sim.FlushInput();
            }
            return sim;
        }

        // Runs the recording again and returns how each of its waves went, in order.
        // A replay stores hashes, not outcomes: this is how to read what happened in
        // one (kills, leaks, breaches, per movement class). It checks nothing; use
        // Verify for that.
        public static List<WaveOutcome> Outcomes(ReplayFile file)
        {
            var outcomes = new List<WaveOutcome>();
            Simulation sim = NewSimulation(file);
            int cursor = 0;
            while (sim.State.Tick < file.FinalTick && !sim.State.IsGameOver)
            {
                Feed(file, sim, ref cursor);
                sim.Tick();

                IReadOnlyList<SimEvent> events = sim.LastTickEvents;
                for (int i = 0; i < events.Count; i++)
                {
                    if (events[i].Kind == SimEventKind.WaveResolved) outcomes.Add(sim.State.LastOutcome.Copy());
                }
            }
            return outcomes;
        }

        // Without perTick: checks every wave hash and the final hash, and stops at
        // the first wave that differs. With perTick (and a file that has them): also
        // checks one hash per tick, so a divergence is pinned to the tick that ran
        // differently; it then runs on to the end of that wave to name the wave too.
        public static ReplayReport Verify(ReplayFile file, bool perTick)
        {
            var report = new ReplayReport { TickHashesInFile = !string.IsNullOrEmpty(file.TickHashes) };

            Simulation sim = NewSimulation(file);
            SimState s = sim.State;

            string initial = ReplayFile.Hex(sim.ComputeHash());
            if (initial != file.InitialHash)
            {
                report.Fail("the starting state differs before any tick ran (hash " + initial + ", recorded " + file.InitialHash
                            + "). The file's config, map and shapes passed their own check, so this is the seed or the simulation code: "
                            + "the rules have changed since this replay was recorded.");
                return report;
            }

            ulong[] tickHashes = perTick ? file.DecodeTickHashes() : null;
            report.TickHashesChecked = tickHashes != null;

            List<WaveHash> waves = file.WaveHashes;
            int cursor = 0;
            int nextWave = 0;

            while (s.Tick < file.FinalTick)
            {
                Feed(file, sim, ref cursor);
                if (s.IsGameOver)
                {
                    report.FirstDivergentWave = s.WaveIndex;
                    report.Fail("the game ended at tick " + s.Tick + " here, but the recording runs on to tick " + file.FinalTick + ".");
                    break;
                }

                int ran = s.Tick;
                sim.Tick();
                report.TicksRun++;

                bool hashed = false;
                ulong hash = 0;
                if (tickHashes != null && report.FirstDivergentTick < 0 && ran < tickHashes.Length)
                {
                    hash = sim.ComputeHash();
                    hashed = true;
                    if (hash != tickHashes[ran])
                    {
                        report.FirstDivergentTick = ran;
                        report.Fail("tick " + ran + " ran differently (hash " + ReplayFile.Hex(hash) + ", recorded " + ReplayFile.Hex(tickHashes[ran]) + ").");
                    }
                }

                bool waveDiverged = false;
                IReadOnlyList<SimEvent> events = sim.LastTickEvents;
                for (int i = 0; i < events.Count; i++)
                {
                    if (events[i].Kind != SimEventKind.WaveResolved) continue;
                    int wave = events[i].IntA;
                    if (!hashed)
                    {
                        hash = sim.ComputeHash();
                        hashed = true;
                    }

                    if (nextWave >= waves.Count)
                    {
                        report.FirstDivergentWave = wave;
                        report.Fail("wave " + wave + " resolved at tick " + s.Tick + " here, but the recording has no wave after " + (waves.Count == 0 ? "the start" : "wave " + waves[waves.Count - 1].Wave) + ".");
                        waveDiverged = true;
                        break;
                    }

                    WaveHash expected = waves[nextWave++];
                    string got = ReplayFile.Hex(hash);
                    if (expected.Wave != wave || expected.Tick != s.Tick || expected.Hash != got)
                    {
                        report.FirstDivergentWave = expected.Wave;
                        report.Fail("wave " + expected.Wave + " does not match: recorded resolving at tick " + expected.Tick + " with hash " + expected.Hash
                                    + "; here wave " + wave + " resolved at tick " + s.Tick + " with hash " + got + ".");
                        waveDiverged = true;
                        break;
                    }
                    if (report.Ok) report.WavesVerified++;
                    else
                    {
                        // Its hash matched, but a tick inside it did not: the runs
                        // parted and met again. Still name the wave it happened in.
                        report.FirstDivergentWave = expected.Wave;
                        waveDiverged = true;
                        break;
                    }
                }

                // A recorded wave that should have resolved by now and has not.
                if (!waveDiverged && nextWave < waves.Count && s.Tick > waves[nextWave].Tick)
                {
                    report.FirstDivergentWave = waves[nextWave].Wave;
                    report.Fail("wave " + waves[nextWave].Wave + " was recorded resolving at tick " + waves[nextWave].Tick + "; here it is still running at tick " + s.Tick + ".");
                    waveDiverged = true;
                }

                if (waveDiverged) return report;
            }

            if (!report.Ok)
            {
                // A tick diverged after the last recorded wave boundary.
                if (report.FirstDivergentWave < 0) report.FirstDivergentWave = s.WaveIndex;
                return report;
            }

            if (nextWave < waves.Count)
            {
                report.FirstDivergentWave = waves[nextWave].Wave;
                report.Fail("the recording has wave " + waves[nextWave].Wave + " resolving at tick " + waves[nextWave].Tick + ", but it never resolved here.");
                return report;
            }

            // Commands recorded at the final tick were applied before FinalHash was taken.
            Feed(file, sim, ref cursor);
            sim.FlushInput();
            string final = ReplayFile.Hex(sim.ComputeHash());
            if (final != file.FinalHash)
            {
                report.FirstDivergentWave = s.WaveIndex;
                report.Fail("every wave hash matched, but the final state at tick " + file.FinalTick + " differs (hash " + final + ", recorded " + file.FinalHash
                            + "). The runs part after the last recorded wave boundary.");
            }
            return report;
        }

        // Enqueues every recorded command stamped with the current tick.
        private static void Feed(ReplayFile file, Simulation sim, ref int cursor)
        {
            List<ReplayCommand> commands = file.Commands;
            int tick = sim.State.Tick;
            while (cursor < commands.Count && commands[cursor].Tick <= tick)
            {
                // "<" cannot happen for a validated file (commands are in tick order
                // and every tick is visited); such a command would be stale, so drop it.
                if (commands[cursor].Tick == tick) sim.Enqueue(commands[cursor].ToCommand());
                cursor++;
            }
        }
    }
}
