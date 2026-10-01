using System;

namespace Thesis.Sim
{
    // Runs another planner and never lets it break a session (ARCHITECTURE.md §6:
    // "a participant session never crashes because of the director").
    //
    // If the inner planner throws, or hands back a plan the simulation would
    // refuse, this returns the fallback planner's plan for that wave instead and
    // reports what happened. The fallback is the static escalation, so the wave
    // still spends the same threat budget (I2).
    //
    // Why a wrapper and not a try/catch around Simulation.Tick(): by the time an
    // exception leaves Tick() the tick is half done. Catching it here means the
    // simulation never sees the failure at all.
    //
    // What it cannot rescue: a planner that WRITES to the simulation state. The
    // state is then already wrong, and Simulation's mutation guard still throws.
    //
    // Name is the inner planner's, so a replay or a telemetry row still names the
    // study condition that was running; fallbacks are reported separately.
    public sealed class SafePlanner : IWavePlanner
    {
        public const string ReasonException = "Exception";
        public const string ReasonInvalidPlan = "InvalidPlan";
        public const string ReasonResolveException = "ExceptionInOnWaveResolved";

        private readonly IWavePlanner inner;
        private readonly IWavePlanner fallback;
        private readonly MapData map;
        private readonly SimConfig config;
        private readonly IThreatPricer pricer;

        // Called once per fallback with a full sentence for the log. The host sets
        // it (SimHost logs with Debug.LogError); it is never required.
        public Action<string> OnFallback;

        public SafePlanner(IWavePlanner inner, IWavePlanner fallback, MapData map, SimConfig config, IThreatPricer pricer = null)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.pricer = pricer;
        }

        public string Name => inner.Name;

        public IWavePlanner Inner => inner;

        // How many waves were planned by the fallback, and the most recent case:
        // telemetry writes LastFallbackReason into that wave's row ("fallback").
        public int FallbackCount { get; private set; }

        public int LastFallbackWave { get; private set; } = -1;

        public string LastFallbackReason { get; private set; }

        public string LastFallbackDetail { get; private set; }

        public WavePlan PlanWave(WaveContext context)
        {
            string reason, detail;
            try
            {
                WavePlan plan = inner.PlanWave(context);
                detail = PlanValidator.Check(plan, context.WaveIndex, map, config, pricer);
                if (detail == null) return plan;
                reason = ReasonInvalidPlan;
            }
            catch (Exception e)
            {
                reason = ReasonException;
                detail = e.GetType().Name + ": " + e.Message;
            }

            FallbackCount++;
            Report(context.WaveIndex, reason, detail, "the wave uses the '" + fallback.Name + "' plan instead.");
            return fallback.PlanWave(context);
        }

        public void OnWaveResolved(WaveOutcome outcome)
        {
            // Both hear about every wave: the fallback may be asked to plan the next one.
            fallback.OnWaveResolved(outcome.Copy());
            try
            {
                inner.OnWaveResolved(outcome);
            }
            catch (Exception e)
            {
                Report(outcome.WaveIndex, ReasonResolveException, e.GetType().Name + ": " + e.Message, "the session continues; the planner may have missed this wave's outcome.");
            }
        }

        private void Report(int wave, string reason, string detail, string consequence)
        {
            LastFallbackWave = wave;
            LastFallbackReason = reason;
            LastFallbackDetail = detail;
            OnFallback?.Invoke("[Director] Planner '" + inner.Name + "' failed at wave " + wave + " (" + reason + "): " + detail + " - " + consequence);
        }
    }
}
