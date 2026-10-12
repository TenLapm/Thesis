using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Thesis.Director
{
    // Appends telemetry rows to a .jsonl file: one row, one line, flushed at once.
    // A session that crashes or is killed keeps every row written before it; a row
    // is never gone back to and changed (CLAUDE.md §7).
    //
    // The bytes are fixed: UTF-8 without a byte-order mark and "\n" line ends, on
    // every machine, so that two files of the same session can be compared byte
    // for byte (.gitattributes keeps *.jsonl that way in the repository too).
    public sealed class TelemetryWriter : IDisposable
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly StreamWriter writer;

        // Starts a NEW file: a session writes its own, and a headless run that reuses
        // an output folder must not add its rows to the last run's.
        public TelemetryWriter(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("A path is required.", nameof(path));
            Path = System.IO.Path.GetFullPath(path);
            string dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            writer = new StreamWriter(new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.Read), Utf8NoBom);
            writer.NewLine = "\n";
        }

        public string Path { get; }

        public int RowsWritten { get; private set; }

        public void Write(WaveRecord row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            writer.Write(row.ToJsonLine());
            writer.Write('\n');
            writer.Flush();
            RowsWritten++;
        }

        public void Write(IReadOnlyList<WaveRecord> rows)
        {
            for (int i = 0; i < rows.Count; i++) Write(rows[i]);
        }

        public void Dispose() { writer.Dispose(); }

        // ---- whole files, for tools and tests ----

        // The text a writer would have produced for these rows.
        public static string ToText(IReadOnlyList<WaveRecord> rows)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < rows.Count; i++) sb.Append(rows[i].ToJsonLine()).Append('\n');
            return sb.ToString();
        }

        public static List<WaveRecord> Parse(string text)
        {
            var rows = new List<WaveRecord>();
            if (text == null) return rows;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                try
                {
                    WaveRecord row = WaveRecord.FromJsonLine(line);
                    if (row == null) throw new InvalidDataException("empty row");
                    if (row.Schema != WaveRecord.CurrentSchema)
                        throw new InvalidDataException("schema " + row.Schema + ", expected " + WaveRecord.CurrentSchema);
                    rows.Add(row);
                }
                catch (Exception e) when (e is Newtonsoft.Json.JsonException || e is InvalidDataException)
                {
                    throw new InvalidDataException("[Telemetry] Line " + (i + 1) + " is not a telemetry row: " + e.Message, e);
                }
            }
            return rows;
        }

        public static List<WaveRecord> Load(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("[Telemetry] Telemetry file not found.", path);
            return Parse(File.ReadAllText(path, Utf8NoBom));
        }
    }
}
