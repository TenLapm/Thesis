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

        public static AgentGroup Group(int count, float life, float speed = 0f, int intervalTicks = 1, int spawnIndex = 0, int startDelayTicks = 0)
        {
            return new AgentGroup { SpawnIndex = spawnIndex, Count = count, LifeTime = life, MoveSpeed = speed, DigRate = 1f, SpawnIntervalTicks = intervalTicks, StartDelayTicks = startDelayTicks };
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

    // A threat cost table for ValidatePlan tests: budget = 10 * wave, price = total agents.
    public sealed class FakePricer : IThreatPricer
    {
        public float BudgetForWave(int waveIndex) { return 10f * waveIndex; }

        public float Price(WavePlan plan) { return plan.TotalAgents(); }
    }
}
