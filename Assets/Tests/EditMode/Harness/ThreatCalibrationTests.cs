using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Director;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Tests.Harness
{
    // The measurement of the cost table's class factors (WP9, S6). These check the instrument,
    // not the numbers it finds: those are a result (Results/2026-11-16_threat-factors).
    public class ThreatCalibrationTests
    {
        private static SimConfig GameConfig() { return Json.Deserialize<SimConfig>(File.ReadAllText(Path.Combine(TestPaths.Maps, "SimConfig.json"))); }

        private static List<ThreatCalibration.Board> OneBoard(out StrategyKit kit, int wave = 3, ulong seed = 2)
        {
            SimConfig config = GameConfig();
            List<ThreatCalibration.Board> boards = ThreatCalibration.Boards(config, TestSims.GameShapes(), TestSims.GameTowers(), seed, new[] { wave });
            MapData map = boards[0].Snapshot.Map;
            kit = new StrategyKit(config, map, new ThreatCostTable(config, map, 1f, 1f)); // probes are priced as walkers
            return boards;
        }

        [Test]
        public void ABoardIsTakenAtTheBuildPhaseOfTheWaveAskedFor()
        {
            StrategyKit kit;
            List<ThreatCalibration.Board> boards = OneBoard(out kit);
            Assert.AreEqual(1, boards.Count);
            Assert.AreEqual(3, boards[0].Wave);
            Assert.AreEqual(2, boards[0].Snapshot.State.WaveIndex, "two waves played, the third about to start");
            Assert.Greater(boards[0].Snapshot.State.Towers.Count, 0, "the player has built");
        }

        [Test]
        public void TheLeakGrowsWithTheProbesStrength()
        {
            StrategyKit kit;
            ThreatCalibration.Board board = OneBoard(out kit)[0];
            double weak = ThreatCalibration.Run(board, EnemyArchetypes.Basic(), 1f / 16f, kit).LeakShare;
            double strong = ThreatCalibration.Run(board, EnemyArchetypes.Basic(), 16f, kit).LeakShare;
            Assert.Less(weak, strong);
            Assert.Greater(strong, 0.5, "sixteen times the budget overwhelms the board");
        }

        [Test]
        public void AProbeIsRepeatableAndLeavesTheBoardUntouched()
        {
            StrategyKit kit;
            ThreatCalibration.Board board = OneBoard(out kit)[0];
            ulong before = board.Snapshot.ComputeHash();
            ThreatCalibration.Probe a = ThreatCalibration.Run(board, EnemyArchetypes.Flyer(), 1f, kit);
            ThreatCalibration.Probe b = ThreatCalibration.Run(board, EnemyArchetypes.Flyer(), 1f, kit);
            Assert.AreEqual(a.LeakShare, b.LeakShare);
            Assert.AreEqual(a.DamageShare, b.DamageShare);
            Assert.AreEqual(before, board.Snapshot.ComputeHash());
        }

        [Test]
        public void TheSearchFindsTheStrengthAtWhichHalfTheWaveGetsThrough()
        {
            StrategyKit kit;
            List<ThreatCalibration.Board> boards = OneBoard(out kit, wave: 5);
            ThreatCalibration.Search s = ThreatCalibration.Match(boards, EnemyArchetypes.Basic(), ThreatCalibration.HalfLeak, kit, iterations: 10);
            Assert.IsFalse(s.HitBound);
            Assert.AreEqual(ThreatCalibration.HalfLeak, s.LeakAtStrength, 0.2, "one board's leak moves in steps of one enemy");
            Assert.Greater(s.Strength, 1.0, "a player alive at wave 5 needs more than the normal budget to be half overrun");
        }
    }
}
