using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Thesis.Director;
using Thesis.Harness;
using Thesis.Sim;

namespace Thesis.Cli
{
    // telemetry <replay.json> [--out file] [--check telemetry.jsonl]
    // Makes a session's telemetry again from its replay. A replay holds everything
    // the rows are made from, so the result is the file the session wrote, byte for
    // byte.
    //   (neither)   print the rows
    //   --out f     write them to f
    //   --check f   compare them with an existing file; exit 1 if any byte differs
    internal static class TelemetryCmd
    {
        public static int Run(string[] argv)
        {
            var args = new Args(argv, new[] { "--out", "--check" }, new string[0]);
            string path = args.Positional(0, "the replay file");
            args.NoMorePositionals(1);

            ReplayFile file = ReplayFile.Load(path);
            List<WaveRecord> rows = ReplayRunner.Telemetry(file);
            string text = TelemetryWriter.ToText(rows);

            if (args.Has("--out"))
            {
                string outPath = args.Get("--out", null);
                using (var writer = new TelemetryWriter(outPath)) writer.Write(rows);
                Console.WriteLine("[Telemetry] " + rows.Count + " rows -> " + Path.GetFullPath(outPath));
            }

            if (args.Has("--check"))
            {
                string checkPath = args.Get("--check", null);
                if (!File.Exists(checkPath)) throw new FileNotFoundException("Telemetry file not found.", checkPath);
                string recorded = new UTF8Encoding(false).GetString(File.ReadAllBytes(checkPath));
                if (recorded == text)
                {
                    Console.WriteLine("[Telemetry] OK: the " + rows.Count + " rows made from the replay are the file's, byte for byte.");
                    return 0;
                }

                string[] a = recorded.Split('\n');
                string[] b = text.Split('\n');
                int line = 0;
                while (line < a.Length && line < b.Length && a[line] == b[line]) line++;
                Console.WriteLine("[Telemetry] DIFFERENT at line " + (line + 1) + " (the file has " + (a.Length - 1) + " rows, the replay gives " + (b.Length - 1) + ").");
                Console.WriteLine("  file  : " + (line < a.Length ? Shorten(a[line]) : "(nothing)"));
                Console.WriteLine("  replay: " + (line < b.Length ? Shorten(b[line]) : "(nothing)"));
                return 1;
            }

            if (!args.Has("--out")) Console.Write(text);
            return 0;
        }

        private static string Shorten(string line) { return line.Length <= 300 ? line : line.Substring(0, 300) + " ..."; }
    }
}
