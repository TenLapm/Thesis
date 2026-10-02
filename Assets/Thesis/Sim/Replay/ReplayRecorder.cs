using System;
using System.Collections.Generic;

namespace Thesis.Sim
{
    // Watches one Simulation from its first tick and builds the ReplayFile for it.
    // The host (SimHost in Unity, EpisodeRunner headless) makes three calls:
    //
    //     recorder.OnCommand(c);   // just before sim.Enqueue(c)
    //     sim.Tick();
    //     recorder.AfterTick();    // straight after every sim.Tick()
    //     ...
    //     recorder.Snapshot();     // the file so far; safe to call any number of times
    //
    // A command is stamped with State.Tick at the moment it is handed over. That is
    // exact whether the host then applies it with FlushInput() or leaves it for the
    // next Tick(): nothing else happens between ticks, so both give the same state
    // (InputTests.FlushInputEqualsApplyingAtTheNextTick).
    public sealed class ReplayRecorder
    {
        private readonly Simulation sim;
        private readonly bool recordTickHashes;
        private readonly string initialHash;
        private readonly string setupHash;
        private readonly List<ReplayCommand> commands = new List<ReplayCommand>();
        private readonly List<WaveHash> waveHashes = new List<WaveHash>();
        private readonly List<ulong> tickHashes = new List<ulong>();
        private int ticksSeen;

        // Labels copied into the file; see ReplayFile.
        public string Build;
        public string Session;
        public string Policy;

        public ReplayRecorder(Simulation sim, bool recordTickHashes)
        {
            this.sim = sim ?? throw new ArgumentNullException(nameof(sim));
            if (sim.State.Tick != 0 || sim.State.PlacementLog.Count != 0)
                throw new InvalidOperationException("[Replay] A recorder must be attached before the first tick and the first command (tick is " + sim.State.Tick + ").");

            this.recordTickHashes = recordTickHashes;
            initialHash = ReplayFile.Hex(sim.ComputeHash());

            // Taken now, not at Snapshot(): the file stores the simulation's live
            // config object, and in the Unity editor that object can be edited in
            // the inspector while the game runs. Such a session cannot be
            // reproduced, and this is how it gets caught - the stale hash no longer
            // matches the stored values, so ReplayFile.Validate() refuses the file.
            setupHash = ReplayFile.Hex(ReplaySetup.Hash(sim.Config, sim.Map, sim.ShapeLibrary, sim.TowerLibrary));
        }

        public int CommandCount => commands.Count;

        public int WaveCount => waveHashes.Count;

        public void OnCommand(SimCommand command)
        {
            // The simulation drops input once the game is over; so does the record.
            if (sim.State.IsGameOver) return;
            commands.Add(ReplayCommand.From(sim.State.Tick, command));
        }

        // Returns true when this tick closed a wave (or ended the game): the moments
        // a host may want to write the file to disk.
        public bool AfterTick()
        {
            SimState s = sim.State;
            if (s.Tick == ticksSeen) return false; // Tick() after game over does not advance
            if (s.Tick != ticksSeen + 1)
                throw new InvalidOperationException("[Replay] AfterTick must follow every Tick(): saw tick " + ticksSeen + ", simulation is at " + s.Tick + ".");
            ticksSeen = s.Tick;

            bool boundary = false;
            IReadOnlyList<SimEvent> events = sim.LastTickEvents;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].Kind == SimEventKind.WaveResolved || events[i].Kind == SimEventKind.GameOver) boundary = true;
            }

            if (!recordTickHashes && !boundary) return false;

            ulong hash = sim.ComputeHash();
            if (recordTickHashes) tickHashes.Add(hash);
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].Kind == SimEventKind.WaveResolved)
                    waveHashes.Add(new WaveHash { Wave = events[i].IntA, Tick = s.Tick, Hash = ReplayFile.Hex(hash) });
            }
            return boundary;
        }

        // The recording up to now. Lists are copied, so the returned file does not
        // change as the session continues.
        public ReplayFile Snapshot()
        {
            return new ReplayFile
            {
                Build = Build,
                Session = Session,
                Policy = Policy,
                Map = sim.Map.Name,
                MapSeed = 0,
                RngSeed = sim.RngSeed,
                Planner = sim.Planner.Name,
                Config = sim.Config,
                MapData = sim.Map,
                Shapes = sim.ShapeLibrary,
                Towers = sim.TowerLibrary,
                SetupHash = setupHash,
                InitialHash = initialHash,
                Commands = new List<ReplayCommand>(commands),
                WaveHashes = new List<WaveHash>(waveHashes),
                FinalTick = sim.State.Tick,
                FinalHash = ReplayFile.Hex(sim.ComputeHash()),
                TickHashes = recordTickHashes ? ReplayFile.EncodeTickHashes(tickHashes) : null,
            };
        }
    }
}
