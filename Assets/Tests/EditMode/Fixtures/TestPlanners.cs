using System;
using System.Collections.Generic;
using Thesis.Sim;

namespace Thesis.Tests
{
    // Returns the same hand-written plan every wave (with the wave number filled in).
    public sealed class FixedPlanner : IWavePlanner
    {
        private readonly Func<int, AgentGroup[]> groupsFor;
        public readonly List<WaveOutcome> Outcomes = new List<WaveOutcome>();

        public FixedPlanner(params AgentGroup[] groups) : this(_ => groups) { }

        public FixedPlanner(Func<int, AgentGroup[]> groupsFor) { this.groupsFor = groupsFor; }

        public float ThreatSpent;

        public string Name => "fixed";

        public WavePlan PlanWave(WaveContext context)
        {
            AgentGroup[] source = groupsFor(context.WaveIndex);
            var groups = new AgentGroup[source.Length];
            for (int i = 0; i < source.Length; i++) groups[i] = source[i]?.Copy();
            return new WavePlan { WaveIndex = context.WaveIndex, StrategyId = "fixed", Groups = groups, ThreatSpent = ThreatSpent };
        }

        public void OnWaveResolved(WaveOutcome outcome) { Outcomes.Add(outcome); }

        // An enemy must be able to move (PlanValidator), so there is no "stand
        // still" group any more: a test that wants a wave to end quickly gives its
        // agents speed and lets them reach the core, or shoots them.
        public static AgentGroup Group(int count, float hp = 10f, float speed = 5f, int intervalTicks = 1, int spawnIndex = 0, int startDelayTicks = 0, float[] resist = null)
        {
            return new AgentGroup { SpawnIndex = spawnIndex, Count = count, Hp = hp, MoveSpeed = speed, DigRate = 1f, Resist = resist, SpawnIntervalTicks = intervalTicks, StartDelayTicks = startDelayTicks };
        }
    }

    // Breaks the "planners only read" rule, to prove the mutation guard catches it.
    public sealed class MutatingPlanner : IWavePlanner
    {
        public enum When { PlanWave, OnWaveResolved }

        private readonly When when;
        private SimState kept;

        public MutatingPlanner(When when) { this.when = when; }

        public string Name => "mutating";

        public WavePlan PlanWave(WaveContext context)
        {
            kept = context.State;
            if (when == When.PlanWave) context.State.BuildBudget += 1f;
            return new WavePlan { WaveIndex = context.WaveIndex, StrategyId = "mutating", Groups = new[] { FixedPlanner.Group(1, 0.05f) } };
        }

        public void OnWaveResolved(WaveOutcome outcome)
        {
            if (when == When.OnWaveResolved) kept.CoreHp += 1; // reaching back through a reference kept from PlanWave
        }
    }

    // Wraps a well-behaved planner and fails on demand, to prove that neither the
    // simulation nor SafePlanner is broken by a broken director.
    public sealed class FlakyPlanner : IWavePlanner
    {
        private readonly IWavePlanner inner;

        public FlakyPlanner(IWavePlanner inner) { this.inner = inner; }

        // PlanWave / OnWaveResolved throw this many times, then behave.
        public int PlanThrowsLeft;
        public int ResolveThrowsLeft;

        // PlanWave throws for these waves every time it is asked.
        public Func<int, bool> ThrowOnWave = _ => false;

        // PlanWave answers these waves with a plan for the wrong wave, or with null.
        public Func<int, bool> WrongWaveOnWave = _ => false;
        public Func<int, bool> NullOnWave = _ => false;

        public int PlanCalls;
        public int ResolveCalls;

        public string Name => "flaky";

        public WavePlan PlanWave(WaveContext context)
        {
            PlanCalls++;
            if (PlanThrowsLeft > 0)
            {
                PlanThrowsLeft--;
                throw new InvalidOperationException("director bug in PlanWave");
            }
            if (ThrowOnWave(context.WaveIndex)) throw new InvalidOperationException("director bug in PlanWave, wave " + context.WaveIndex);
            if (NullOnWave(context.WaveIndex)) return null;

            WavePlan plan = inner.PlanWave(context);
            if (WrongWaveOnWave(context.WaveIndex)) plan.WaveIndex += 7;
            return plan;
        }

        public void OnWaveResolved(WaveOutcome outcome)
        {
            ResolveCalls++;
            if (ResolveThrowsLeft > 0)
            {
                ResolveThrowsLeft--;
                throw new InvalidOperationException("director bug in OnWaveResolved");
            }
            inner.OnWaveResolved(outcome);
        }
    }

    // A threat cost table for ValidatePlan tests: budget = 10 * wave, price = total agents.
    public sealed class FakePricer : IThreatPricer
    {
        public float BudgetForWave(int waveIndex) { return 10f * waveIndex; }

        public float Price(WavePlan plan) { return plan.TotalAgents(); }
    }
}
