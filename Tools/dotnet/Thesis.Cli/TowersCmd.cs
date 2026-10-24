using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Thesis.Core;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Cli
{
    // `thesis towers`: the tower balance check of 2026-10-14. For each tower type it plays
    // a scripted player whose shop holds that type ONLY, on generated maps against the
    // development planner that sends every enemy type (RaidPlanner), and reports
    //
    //   * the wave that player lost in, map by map,
    //   * what one copy of the tower did: damage per minute on the board, kills,
    //   * the same for the whole roster together ("all").
    //
    // The game's cards are a free draft, so a tower costs a pick and a place under the
    // tower cap, not budget: the comparison is per tower, not per gold. A type that gets
    // far on its own is strong; one that cannot get far alone may still be a good second
    // tower (frost), which the pairs (--pairs a+b,c+d) are for.
    //
    // It prints Markdown and writes it to <out>/towers.md. It judges nothing.
    public static class TowersCmd
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static int Run(string[] argv)
        {
            var args = new Args(argv, new[] { "--out", "--shapes", "--towers", "--config", "--seeds", "--waves", "--policy", "--planner", "--pairs", "--only", "--core-hp", "--copies", "--upgrade" }, new[] { "--gauntlet" });
            args.NoMorePositionals(0);

            ShapeDef[] shapes = ShapeLibraryFile.Load(args.Get("--shapes", Path.Combine("Maps", "Shapes.json")));
            TowerDef[] roster = TowerLibraryFile.Load(args.Get("--towers", Path.Combine("Maps", "Towers.json")));
            SimConfig config = RunCmd.LoadConfig(args.Get("--config", Path.Combine("Maps", "SimConfig.json")));
            if (args.Flag("--gauntlet")) return Gauntlet(args, roster, shapes, config);
            // --core-hp: a core that does not fall, so every set plays all the waves and the
            // share of enemies that got through says how well it held, decade by decade.
            int coreHp = args.GetInt("--core-hp", 0);
            if (coreHp > 0) config.CoreMaxHp = coreHp;
            int seeds = args.GetInt("--seeds", 10);
            int waves = args.GetInt("--waves", 40);
            string policy = args.Get("--policy", "raider");
            string planner = args.Get("--planner", RaidPlanner.Id);

            // The sets to play: every type alone, the pairs asked for, and the whole roster.
            var sets = new List<KeyValuePair<string, TowerDef[]>>();
            string only = args.Get("--only", null);
            foreach (TowerDef t in roster)
                if (only == null || Array.IndexOf(only.Split(','), t.Id) >= 0) sets.Add(new KeyValuePair<string, TowerDef[]>(t.Id, new[] { t }));
            string pairs = args.Get("--pairs", null);
            if (pairs != null)
            {
                foreach (string pair in pairs.Split(','))
                {
                    var members = new List<TowerDef>();
                    foreach (string id in pair.Split('+')) members.Add(Find(roster, id.Trim()));
                    sets.Add(new KeyValuePair<string, TowerDef[]>(pair, members.ToArray()));
                }
            }
            if (only == null) sets.Add(new KeyValuePair<string, TowerDef[]>("all", roster));

            var md = new StringBuilder();
            md.Append("Generated maps 1 to ").Append(seeds).Append(" (the run's seed is the map's), planner `").Append(planner).Append("`, policy `").Append(policy)
              .Append("`, up to ").Append(waves).Append(" waves. The shop holds only the set's towers.\n\n");
            int decades = (waves + 9) / 10;
            md.Append("| Set | Lost in wave, map by map | Median | Towers built | Damage per tower-minute | Kills per tower-minute |");
            for (int d = 0; d < decades; d++) md.Append(" Got through, waves ").Append(d * 10 + 1).Append("-").Append(Math.Min(waves, d * 10 + 10)).Append(" |");
            md.Append("\n|---|---|---|---|---|---|");
            for (int d = 0; d < decades; d++) md.Append("---|");
            md.Append("\n");

            foreach (KeyValuePair<string, TowerDef[]> set in sets)
            {
                var reached = new List<int>();
                var row = new StringBuilder();
                int built = 0;
                double damage = 0, kills = 0, minutes = 0;
                var spawned = new long[decades];
                var leaked = new long[decades];
                for (int seed = 1; seed <= seeds; seed++)
                {
                    EpisodeResult run = EpisodeRunner.Run(new EpisodeOptions
                    {
                        Config = config.Clone(),
                        Map = MapGenerator.Generate((ulong)seed),
                        Shapes = shapes,
                        Towers = set.Value,
                        Seed = (ulong)seed,
                        Planner = planner,
                        Policy = Registry.CreatePolicy(policy),
                        MaxWaves = waves,
                    });
                    bool survived = !run.GameOver;
                    int lostIn = survived ? waves + 1 : run.Outcomes.Count;
                    reached.Add(lostIn);
                    row.Append(seed > 1 ? " " : "").Append(survived ? waves + "+" : lostIn.ToString(Inv));
                    foreach (WaveOutcome o in run.Outcomes)
                    {
                        int d = Math.Min(decades - 1, (o.WaveIndex - 1) / 10);
                        spawned[d] += o.Spawned;
                        leaked[d] += o.Leaked;
                    }
                    foreach (TowerSummary t in run.Towers)
                    {
                        built++;
                        damage += t.DamageDealt;
                        kills += t.Kills;
                        minutes += t.TicksOnBoard * config.TickSeconds / 60.0;
                    }
                }
                reached.Sort();
                double median = (reached[(reached.Count - 1) / 2] + reached[reached.Count / 2]) / 2.0;
                md.Append("| ").Append(set.Key).Append(" | ").Append(row).Append(" | ").Append(median > waves ? waves + "+" : F(median, "0.#"))
                  .Append(" | ").Append(built).Append(" | ").Append(minutes > 0 ? F(damage / minutes, "0") : "-")
                  .Append(" | ").Append(minutes > 0 ? F(kills / minutes, "0.0") : "-").Append(" |");
                for (int d = 0; d < decades; d++) md.Append(' ').Append(spawned[d] > 0 ? F(100.0 * leaked[d] / spawned[d], "0") + " %" : "-").Append(" |");
                md.Append("\n");
                Console.Error.WriteLine("[Towers] " + set.Key + ": " + row);
            }

            string text = md.ToString();
            Console.Write(text);
            string outDir = args.Get("--out", null);
            if (outDir != null)
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "towers.md"), text);
                Console.WriteLine("\n[Towers] written to " + Path.Combine(outDir, "towers.md"));
            }
            return 0;
        }

        // ------------------------------------------------------------------ the gauntlet

        // `thesis towers --gauntlet`: the towers without a player. A lane one tile wide
        // with rock on both sides; `--copies` towers of one type (4) stand on the rock
        // beside the last stretch before the core, the way a player stacks them there.
        // (or, by default, as many as the tower cap allows at that wave). One wave of one enemy type at the strength of wave w walks the lane, and the
        // table gives the share it killed. No upgrades or gear unless --upgrade <choice>
        // gives every tower that one upgrade (power, rapid, reach, volley).
        //
        // It measures what a tower does per tower, which is what the draft makes scarce,
        // and nothing about a player's choices. The scripted players are no yardstick
        // here: they lose their towers to digging and die in wave 2.
        private const string Lane =
            "X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X\n" +
            "X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X\n" +
            "S . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . C\n" +
            "X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X\n" +
            "X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X X\n";

        private sealed class FixedPlanner : IWavePlanner
        {
            public AgentGroup Group;
            public string Name => "gauntlet";
            public WavePlan PlanWave(WaveContext context) { return new WavePlan { WaveIndex = context.WaveIndex, StrategyId = "gauntlet", Groups = new[] { Group.Copy() } }; }
            public void OnWaveResolved(WaveOutcome outcome) { }
        }

        private static int Gauntlet(Args args, TowerDef[] roster, ShapeDef[] shapes, SimConfig baseConfig)
        {
            int copies = args.GetInt("--copies", 0); // 0: as many as the tower cap allows at that wave
            int[] waves = { 5, 15, 30, 45, 60 };
            int detailWave = 30;
            string upgrade = args.Get("--upgrade", null);
            // --upgrade <n>: every tower takes its own n-th choice; <id>: the choice of that name, where a tower has one.
            int choice = TowerUpgradeChoices.None;
            if (upgrade != null && !int.TryParse(upgrade, out choice)) choice = -2;
            EnemyArchetype[] types = EnemyArchetypes.Everything();

            var sets = new List<KeyValuePair<string, TowerDef[]>>();
            foreach (TowerDef t in roster) sets.Add(new KeyValuePair<string, TowerDef[]>(t.Id, new[] { t }));
            string pairs = args.Get("--pairs", null);
            if (pairs != null)
                foreach (string pair in pairs.Split(','))
                {
                    var members = new List<TowerDef>();
                    foreach (string id in pair.Split('+')) members.Add(Find(roster, id.Trim()));
                    sets.Add(new KeyValuePair<string, TowerDef[]>(pair, members.ToArray()));
                }

            var md = new StringBuilder();
            md.Append(copies > 0 ? copies.ToString(Inv) : "As many").Append(" towers of a set on the rock beside the last tiles of a 40-tile lane (for a pair, alternating; \"as many\" is the tower cap at the wave), ")
              .Append(upgrade == null ? "no upgrades, no gear" : "each with the upgrade " + upgrade).Append(". One wave of one enemy type at the strength of wave w; the share of it killed.\n\n");
            md.Append("Average over the ten enemy types:\n\n| Set |");
            foreach (int w in waves) md.Append(" wave ").Append(w).Append(" |");
            md.Append("\n|---|");
            foreach (int w in waves) md.Append("---|");
            md.Append("\n");

            var detail = new StringBuilder();
            detail.Append("\nWave ").Append(detailWave).Append(", by enemy type:\n\n| Set |");
            foreach (EnemyArchetype e in types) detail.Append(' ').Append(e.Id).Append(" |");
            detail.Append("\n|---|");
            foreach (EnemyArchetype e in types) detail.Append("---|");
            detail.Append("\n");

            foreach (KeyValuePair<string, TowerDef[]> set in sets)
            {
                md.Append("| ").Append(set.Key).Append(" |");
                foreach (int w in waves)
                {
                    double sum = 0;
                    var row = new StringBuilder();
                    foreach (EnemyArchetype e in types)
                    {
                        int n = copies > 0 ? copies : Math.Max(1, TowerCombine.CapAt(baseConfig, w - 1));
                        double share = KilledShare(baseConfig, roster, shapes, set.Value, n, choice, upgrade, e, w);
                        sum += share;
                        if (w == detailWave) row.Append(' ').Append(F(100 * share, "0")).Append(" |");
                    }
                    md.Append(' ').Append(F(100 * sum / types.Length, "0")).Append(" % |");
                    if (w == detailWave) detail.Append("| ").Append(set.Key).Append(" |").Append(row).Append("\n");
                }
                md.Append("\n");
                Console.Error.WriteLine("[Towers] " + set.Key + " done");
            }

            string text = md.ToString() + detail;
            Console.Write(text);
            string outDir = args.Get("--out", null);
            if (outDir != null)
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "gauntlet.md"), text);
                Console.WriteLine("\n[Towers] written to " + Path.Combine(outDir, "gauntlet.md"));
            }
            return 0;
        }

        private static double KilledShare(SimConfig baseConfig, TowerDef[] roster, ShapeDef[] shapes, TowerDef[] set, int copies, int choice, string upgradeId, EnemyArchetype type, int wave)
        {
            SimConfig config = baseConfig.Clone();
            config.CoreMaxHp = 1000000;
            config.WaitForStart = true;
            config.PlanAhead = false;
            config.CorePlacement = false;
            MapData map = AsciiMap.Parse(Lane, "gauntlet").Map;

            AgentGroup basic = new EscalationPlanner(config, map).PlanFor(wave).Groups[0];
            var planner = new FixedPlanner { Group = type.Apply(basic, 1f) };
            planner.Group.SpawnIndex = 0;
            var sim = new Simulation(config, map, shapes, roster, 1, planner);
            SimState s = sim.State;

            // Towers on the rock above and below the lane, from two tiles before the core
            // backwards, two to a column.
            int core = map.Core.X;
            for (int i = 0; i < copies; i++)
            {
                var tile = new TileCoord(core - 2 - i / 2, i % 2 == 0 ? 3 : 1);
                TowerDef def = set[i % set.Length];
                SimNode node = s.Grid.Get(tile);
                int own = choice;
                if (choice == -2) { own = TowerUpgradeChoices.None; TowerLevelMod[] list = TowerUpgradeChoices.For(def); for (int u = 0; u < list.Length; u++) if (TowerUpgradeChoices.IdOf(def, u) == upgradeId) own = u; }
                var tower = new TowerState(s.Towers.Count, def, tile, node.Position, map.NodeDiameter, 0) { Upgrade = TowerUpgradeChoices.IsValidFor(def, own) ? own : TowerUpgradeChoices.None };
                tower.Recompute(sim.GearLibrary);
                s.Towers.Add(tower);
                s.Grid.SetTower(node, Math.Max(2, def.DigCost), Math.Max(0.5f, def.TowerHealth), tower.Id);
            }
            sim.RebuildFieldForSetup();

            sim.Enqueue(SimCommand.StartWaveNow());
            for (int tick = 0; tick < 400000 && s.LastOutcome == null; tick++) sim.Tick();
            WaveOutcome o = s.LastOutcome;
            return o == null || o.Spawned == 0 ? 0 : (double)o.Killed / o.Spawned;
        }

        private static TowerDef Find(TowerDef[] roster, string id)
        {
            foreach (TowerDef t in roster) if (t.Id == id) return t;
            throw new ArgumentException("No tower '" + id + "' in the roster.");
        }

        private static string F(double value, string format) { return value.ToString(format, Inv); }
    }
}
