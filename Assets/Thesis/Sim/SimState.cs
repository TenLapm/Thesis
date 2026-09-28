using System;
using System.Collections.Generic;
using Thesis.Core;

namespace Thesis.Sim
{
    // Everything that changes during a run, in one object (ARCHITECTURE.md §4.1).
    // Nothing that affects the outcome lives outside it: that is what makes
    // Simulation.Clone() a deep copy of this and ComputeHash() a hash of this.
    //
    // Public fields so views, telemetry and planners can read cheaply. Only
    // Simulation writes; a planner that writes is caught by the mutation guard.
    public sealed class SimState
    {
        public int Tick;
        public SimPhase Phase;

        // Ticks left in Prep/Intermission; 0 while a wave is running.
        public int PhaseTicksRemaining;

        // 0 before the first wave; then the 1-based number of the current/last wave.
        public int WaveIndex;

        public SimGrid Grid;

        // Every agent ever spawned. Agent id == index, so views and replays can
        // address an agent by id forever, even after it died.
        public List<AgentState> Agents = new List<AgentState>();

        public ShapeBag Bag;
        public float BuildBudget;
        public int CoreHp;

        // The validated copy of the plan for the current/last wave.
        public WavePlan CurrentPlan;

        // Open while a wave runs, null otherwise. LastOutcome is the most recent closed one.
        public WaveOutcome CurrentOutcome;
        public WaveOutcome LastOutcome;

        public OccupancyMap Occupancy;
        public List<PlacementRecord> PlacementLog = new List<PlacementRecord>();

        // Randomness, one stream per subsystem (RngStreams). Bag is the same
        // instance the ShapeBag draws from, so its state here is the bag's state.
        public Pcg32 BagRng;
        public Pcg32 SpawnRng;

        // Live agents in ascending id - the order AgentSystem.Step must see them in.
        internal List<AgentState> Live = new List<AgentState>();

        // The current wave's spawn order, fixed at BeginWave.
        internal SpawnSlot[] Schedule = new SpawnSlot[0];
        internal int ScheduleCursor;

        // Commands enqueued for the next Tick(). Part of the state so a Clone()
        // taken between Enqueue and Tick still applies them.
        internal List<SimCommand> Pending = new List<SimCommand>();

        // Set by a StartWaveNow command, consumed by the next tick's phase step. It
        // lives here rather than as a local in Tick() so that input applied early by
        // Simulation.FlushInput() is not lost before the tick that acts on it.
        internal bool StartWaveRequested;

        public bool IsGameOver => Phase == SimPhase.GameOver;

        public IReadOnlyList<AgentState> LiveAgents => Live;

        public int LiveAgentCount => Live.Count;

        internal SimState Clone()
        {
            var c = (SimState)MemberwiseClone();

            c.Grid = Grid.Clone();
            c.BagRng = Pcg32.FromState(BagRng.Save());
            c.SpawnRng = Pcg32.FromState(SpawnRng.Save());
            c.Bag = Bag.CloneWith(c.BagRng);

            c.Agents = new List<AgentState>(Agents.Count);
            for (int i = 0; i < Agents.Count; i++) c.Agents.Add(Agents[i].Clone());
            c.Live = new List<AgentState>(Live.Count);
            for (int i = 0; i < Live.Count; i++) c.Live.Add(c.Agents[Live[i].Id]);

            c.CurrentPlan = CurrentPlan?.Copy();
            c.CurrentOutcome = CurrentOutcome?.Copy();
            c.LastOutcome = LastOutcome?.Copy();
            c.Occupancy = Occupancy.Clone();
            c.PlacementLog = new List<PlacementRecord>(PlacementLog); // records are never mutated after logging
            c.Schedule = (SpawnSlot[])Schedule.Clone();
            c.Pending = new List<SimCommand>(Pending);
            return c;
        }
    }

    // One scheduled spawn: agent Slot of plan group Group enters on Tick.
    internal readonly struct SpawnSlot
    {
        public readonly int Tick;
        public readonly int Group;
        public readonly int Slot;

        public SpawnSlot(int tick, int group, int slot)
        {
            Tick = tick;
            Group = group;
            Slot = slot;
        }

        // Groups interleave by tick; ties go to the lower group, then the lower slot
        // (ARCHITECTURE.md §4.2 step 2). Keys are unique, so an unstable sort is
        // still deterministic.
        public static int Compare(SpawnSlot a, SpawnSlot b)
        {
            if (a.Tick != b.Tick) return a.Tick.CompareTo(b.Tick);
            if (a.Group != b.Group) return a.Group.CompareTo(b.Group);
            return a.Slot.CompareTo(b.Slot);
        }
    }
}
