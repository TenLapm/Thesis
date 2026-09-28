using System;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Core
{
    // Diagnostic for the cross-runtime divergence found in WP4 (Unity/Mono and
    // .NET/CoreCLR give different state hashes for the same run). Prints the exact
    // bits of the float expressions the simulation relies on; run under both
    // runtimes and diff the output to see which kind of expression differs.
    public class FloatFingerprintTests
    {
        [Test, Explicit("Diagnostic: run under Unity and dotnet, diff the output")]
        public void PrintFloatFingerprint()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("runtime: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);

            // 1. Chained float ops without casts (the BaseLifeTime shape).
            float dist = (float)Math.Sqrt(64f * 64f + 64f * 64f);
            float speed = 1.25f, margin = 5f;
            float chained = dist / speed + margin;
            float castStepwise = (float)((float)(dist / speed) + margin);
            sb.AppendLine("dist=" + Bits(dist) + " chained=" + Bits(chained) + " stepwise=" + Bits(castStepwise) + " equal=" + (chained == castStepwise));

            // 2. The real planner value.
            var planner = new EscalationPlanner(new SimConfig(), TestSims.SampleSceneMap());
            sb.AppendLine("BaseLifeTime=" + Bits(planner.BaseLifeTime));

            // 3. Repeated subtraction (lifetime clock) and multiply-subtract (digging).
            float life = planner.BaseLifeTime, wall = 6f, dig = 1f, dt = 0.02f;
            for (int i = 0; i < 1000; i++) { life -= dt; wall -= dig * dt; }
            sb.AppendLine("life after 1000 ticks=" + Bits(life) + " wall=" + Bits(wall));

            // 4. MoveTowards chain.
            Vec2f p = new Vec2f(-32f, -32f);
            Vec2f target = new Vec2f(-35.5f, -35.5f);
            for (int i = 0; i < 50; i++) p = Vec2f.MoveTowards(p, new Vec2f(target.X + i * 2f, target.Y + i * 2f), 1.25f * 0.02f);
            sb.AppendLine("moveTowards x=" + Bits(p.X) + " y=" + Bits(p.Y));

            // 5. NodeFromPosition's percent arithmetic.
            float wx = -36.4865f, size = 75f;
            float percent = (wx + size / 2) / size;
            float scaled = (38 - 1) * percent;
            sb.AppendLine("percent=" + Bits(percent) + " scaled=" + Bits(scaled) + " rounded=" + (int)Math.Round(scaled));

            TestContext.WriteLine(sb.ToString());
        }

        private static string Bits(float f)
        {
            return f.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "[" + BitConverter.SingleToInt32Bits(f).ToString("x8") + "]";
        }
    }
}
