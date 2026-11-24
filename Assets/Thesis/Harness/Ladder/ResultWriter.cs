using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Thesis.Harness
{
    // One result file in Results/<date>_<name>/README.md (CLAUDE.md §7): the date, the exact command
    // that makes it again, the hypothesis stated BEFORE the table, then the tables, then what the
    // numbers mean, including when they say the hypothesis failed. This file set becomes the
    // methodology chapter, so a table without its hypothesis is refused rather than written.
    public sealed class ResultWriter
    {
        public string Title;
        public string Date;          // yyyy-MM-dd, given by the caller (no clock in Thesis.*)
        public string Command;
        public string Hypothesis;
        public readonly List<string> Sections = new List<string>();

        public void Table(string heading, string[] header, IEnumerable<string[]> rows)
        {
            var b = new StringBuilder();
            if (!string.IsNullOrEmpty(heading)) b.Append("## ").Append(heading).Append("\n\n");
            b.Append("| ").Append(string.Join(" | ", header)).Append(" |\n|");
            for (int i = 0; i < header.Length; i++) b.Append("---|");
            b.Append('\n');
            foreach (string[] row in rows) b.Append("| ").Append(string.Join(" | ", row)).Append(" |\n");
            Sections.Add(b.ToString());
        }

        public void Text(string heading, string body)
        {
            Sections.Add((string.IsNullOrEmpty(heading) ? "" : "## " + heading + "\n\n") + body.TrimEnd() + "\n");
        }

        public string Render()
        {
            if (string.IsNullOrWhiteSpace(Hypothesis))
                throw new InvalidOperationException("[Results] '" + Title + "': the hypothesis is empty. State it before the table (CLAUDE.md §7).");
            if (string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(Date) || string.IsNullOrWhiteSpace(Command))
                throw new InvalidOperationException("[Results] A result needs a title, a date and the command that makes it again.");
            var b = new StringBuilder();
            b.Append("# ").Append(Title).Append("\n\n");
            b.Append("Date: ").Append(Date).Append("\n\n");
            b.Append("Command:\n\n```\n").Append(Command).Append("\n```\n\n");
            b.Append("## Hypothesis\n\n").Append(Hypothesis.TrimEnd()).Append("\n\n");
            foreach (string s in Sections) b.Append(s).Append('\n');
            return b.ToString();
        }

        public void Write(string path)
        {
            string text = Render(); // refuses before anything touches the disk
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, text.Replace("\r\n", "\n"));
        }
    }
}
