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
    // `thesis curve <replay>` (the balance pass, 2026-11-24): a real player's boards under the config
    // being tuned, and for each wave asked for, the strength of plain walkers (a multiple of the
    // static wave's) at which --target of them get through (BoardCurve). Below 1 the game already
    // presses that board at that wave; above 1, by that much it does not.
    //
    // The replay is read WITHOUT its fingerprint check: its commands are re-timed by build phase, so
    // this is a study of the player's boards, not an exact re-run (use `replay` for that).
    public static class CurveCmd
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static int Run(string[] argv)
        {
            var args = new Args(argv, new[] { "--config", "--waves", "--target", "--out" }, new string[0]);
            string path = args.Positional(0, "the replay file");
            args.NoMorePositionals(1);
            ReplayFile file = Json.Deserialize<ReplayFile>(File.ReadAllText(path));
            if (file == null || file.Plans == null) throw new ArgumentException("curve: a replay with recorded plans (schema 16) is needed");
            SimConfig config = RunCmd.LoadConfig(args.Get("--config", null));
            double target = double.Parse(args.Get("--target", "0.1"), Inv);
            string[] waveText = args.Get("--waves", "3,5,8,10,12,15,18,20,25,30").Split(',');
            var waves = new int[waveText.Length];
            for (int i = 0; i < waves.Length; i++) waves[i] = int.Parse(waveText[i], Inv);

            List<ThreatCalibration.Board> boards = BoardCurve.Boards(file, config, waves);
            List<BoardCurve.Point> points = BoardCurve.Measure(boards, config, target);
            var met = new Dictionary<int, double>();
            foreach (ThreatCalibration.Board b in boards) met[b.Wave] = BoardCurve.MetStrength(b, target);

            var md = new StringBuilder();
            md.Append("Boards of ").Append(file.Session).Append(" under ").Append(args.Get("--config", "Maps/SimConfig.json"))
              .Append("; plain walkers, the strength at which ").Append((target * 100).ToString("0", Inv)).Append(" % get through.\n\n");
            md.Append("| wave | walkers strength | met wave strength | towers | ore in hand |\n|---|---|---|---|---|\n");
            foreach (BoardCurve.Point p in points)
                md.Append("| ").Append(p.Wave).Append(" | ").Append(p.HitBound ? "beyond 16 (or under 1/16)" : p.StrengthAtTarget.ToString("0.00", Inv))
                  .Append(" | ").Append(double.IsNaN(met[p.Wave]) ? "outside 1/16 to 16" : met[p.Wave].ToString("0.00", Inv)).Append(" | ").Append(p.Towers).Append(" | ").Append(p.Ore.ToString("0", Inv)).Append(" |\n");
            int lastWave = 0;
            foreach (int w in waves) lastWave = Math.Max(lastWave, w);
            md.Append("\nWhat each tower type did up to wave ").Append(lastWave).Append(" (towers sold or destroyed on the way are not counted):\n\n| tower | copies | damage | ore paid | damage per ore |\n|---|---|---|---|---|\n");
            foreach (var (id, copies, damage, paid) in BoardCurve.TowerValue(file, config, lastWave))
                md.Append("| ").Append(id).Append(" | ").Append(copies).Append(" | ").Append(damage.ToString("0", Inv)).Append(" | ").Append(paid.ToString("0", Inv))
                  .Append(" | ").Append(paid > 0 ? (damage / paid).ToString("0.0", Inv) : "-").Append(" |\n");
            Console.Write(md.ToString());
            string outDir = args.Get("--out", null);
            if (outDir != null)
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "curve.md"), md.ToString());
            }
            return 0;
        }
    }
}
