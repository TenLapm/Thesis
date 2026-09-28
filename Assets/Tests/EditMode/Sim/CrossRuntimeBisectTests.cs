using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    // Diagnostic for the WP4 cross-runtime divergence: replays the smoke session and
    // writes every tick's state hash to Runs/xrt_<runtime>.txt, plus a per-component
    // dump of the first 3 ticks after the wave starts. Run under Unity and dotnet,
    // then diff the two files to find the first divergent tick and component.
    public class CrossRuntimeBisectTests
    {
        [Test, Explicit("Diagnostic: run under Unity and dotnet, diff Runs/xrt_*.txt")]
        public void WriteTickHashes()
        {
            string runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.StartsWith("Mono") ? "mono" : "coreclr";
            var sb = new StringBuilder();
            Simulation sim = TestSims.SampleScene(seed: 1);

            while (!sim.State.IsGameOver && sim.State.Tick < 6000)
            {
                if (sim.State.Tick == 652)
                {
                    sim.Enqueue(SimCommand.Rotate());
                    sim.Enqueue(SimCommand.Hold());
                    sim.Enqueue(SimCommand.Hold());
                    sim.Enqueue(SimCommand.PlaceShape(16, 17));
                    sim.Enqueue(SimCommand.PlaceShape(19, 15));
                    sim.Enqueue(SimCommand.PlaceShape(13, 20));
                    sim.Enqueue(SimCommand.PlaceShape(21, 12));
                    sim.Enqueue(SimCommand.PlaceShape(10, 23));
                    sim.FlushInput();
                }
                if (sim.State.Tick == 1345)
                {
                    sim.Enqueue(SimCommand.StartWaveNow());
                    sim.FlushInput();
                }
                sim.Tick();
                sb.Append(sim.State.Tick).Append(' ').Append(sim.ComputeHash().ToString("x16")).Append(' ').Append(Components(sim.State)).Append('\n');
            }

            string dir = Path.Combine(TestPaths.ProjectRoot, "Runs");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "xrt_" + runtime + ".txt"), sb.ToString());
        }

        // A few sub-hashes so a divergence can be pinned to a part of the state.
        private static string Components(SimState s)
        {
            var agents = new Fnv1a64();
            for (int i = 0; i < s.LiveAgents.Count; i++)
            {
                AgentState a = s.LiveAgents[i];
                agents.Add(a.Position.X);
                agents.Add(a.Position.Y);
                agents.Add(a.LifeTime);
                agents.Add(a.MinCostSeen);
            }
            var grid = new Fnv1a64();
            for (int i = 0; i < s.Grid.NodeCount; i++)
            {
                SimNode n = s.Grid.ByIndex(i);
                grid.Add(n.TerrainCost);
                grid.Add(n.WallHealth);
                grid.Add(n.BestCost);
                grid.Add(n.NextIndex);
            }
            var bag = new Fnv1a64();
            s.Bag.AddToHash(ref bag);
            var occ = new Fnv1a64();
            s.Occupancy.AddToHash(ref occ);
            string first = s.LiveAgents.Count > 0
                ? "a0=" + BitConverter.SingleToInt32Bits(s.LiveAgents[0].Position.X).ToString("x8") + "," + BitConverter.SingleToInt32Bits(s.LiveAgents[0].Position.Y).ToString("x8") + "," + BitConverter.SingleToInt32Bits(s.LiveAgents[0].LifeTime).ToString("x8")
                : "a0=-";
            return "agents=" + agents.Value.ToString("x16") + " grid=" + grid.Value.ToString("x16") + " bag=" + bag.Value.ToString("x16")
                   + " occ=" + occ.Value.ToString("x16") + " budget=" + BitConverter.SingleToInt32Bits(s.BuildBudget).ToString("x8") + " " + first;
        }
    }
}
