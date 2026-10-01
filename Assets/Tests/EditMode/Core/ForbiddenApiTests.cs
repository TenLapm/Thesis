using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Thesis.Tests.Core
{
    // Reads the source of every Thesis.* assembly and fails if it calls something
    // that cannot be reproduced bit for bit (ARCHITECTURE.md §9 rule 3). The compiler
    // already rejects UnityEngine in these assemblies; this covers what it cannot.
    //
    // It is a text scan, not a compiler: it knows about // comments and nothing
    // else. That is enough to stop the accidental call, which is the real risk.
    public class ForbiddenApiTests
    {
        private sealed class Rule
        {
            public Regex Pattern;
            public string Why;
        }

        // (?<![A-Za-z0-9_]) = "not in the middle of a longer name", so DetMath.Log is
        // not mistaken for Math.Log, while System.Math.Log is still caught.
        private static readonly Rule[] Rules =
        {
            new Rule
            {
                Pattern = new Regex(@"(?<![A-Za-z0-9_])MathF?\.(Log10|Log2|Log|Exp|Pow|Sinh|Cosh|Tanh|Asin|Acos|Atan2|Atan|Sin|Cos|Tan|Cbrt)\b"),
                Why = "differs in the last bit between runtimes. Use DetMath.Log / DetMath.Exp; raise to a whole power by multiplying.",
            },
            new Rule
            {
                Pattern = new Regex(@"(?<![A-Za-z0-9_])(System\.Random\b|new\s+Random\s*\()"),
                Why = "cannot be saved into a replay. Take an IRandom from a named stream (RngStreams).",
            },
            new Rule
            {
                Pattern = new Regex(@"(?<![A-Za-z0-9_])(DateTime\.(Now|UtcNow|Today)|Environment\.TickCount|Guid\.NewGuid)\b"),
                Why = "is different on every run. Nothing in Thesis.* may depend on the wall clock; a host passes labels in.",
            },
        };

        // Only for reporting code in the harness, never for anything that feeds a
        // decision: a line ending in this marker plus a reason is skipped.
        private const string Waiver = "// determinism-ok:";

        private static string StripLineComment(string line)
        {
            int at = line.IndexOf("//", StringComparison.Ordinal);
            return at < 0 ? line : line.Substring(0, at);
        }

        // Returns "rule text" for a violating line, or null.
        private static string Check(string line, bool waiverAllowed)
        {
            if (waiverAllowed && line.Contains(Waiver)) return null;
            string code = StripLineComment(line);
            foreach (Rule rule in Rules)
            {
                Match m = rule.Pattern.Match(code);
                if (m.Success) return m.Value + " " + rule.Why;
            }
            return null;
        }

        [Test]
        public void ThesisAssembliesCallNothingThatCannotBeReproduced()
        {
            string root = Path.Combine(TestPaths.ProjectRoot, "Assets", "Thesis");
            string[] files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            Assert.Greater(files.Length, 40, "the scan found the source tree");

            var problems = new List<string>();
            foreach (string file in files)
            {
                string relative = file.Substring(TestPaths.ProjectRoot.Length).TrimStart('\\', '/').Replace('\\', '/');
                bool waiverAllowed = relative.StartsWith("Assets/Thesis/Harness/", StringComparison.Ordinal);
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string problem = Check(lines[i], waiverAllowed);
                    if (problem != null) problems.Add(relative + ":" + (i + 1) + "  " + problem);
                }
            }

            if (problems.Count > 0) Assert.Fail(problems.Count + " forbidden call(s) in Thesis.*:\n" + string.Join("\n", problems));
        }

        [TestCase("double y = Math.Log(x);", true)]
        [TestCase("float y = MathF.Exp(x);", true)]
        [TestCase("var p = System.Math.Pow(a, 3);", true)]
        [TestCase("x = Math.Sin(t) + Math.Cos(t);", true)]
        [TestCase("double y = Math.Log10(x);", true)]
        [TestCase("var r = new Random(42);", true)]
        [TestCase("System.Random r;", true)]
        [TestCase("string stamp = DateTime.Now.ToString();", true)]
        [TestCase("int t = Environment.TickCount;", true)]
        [TestCase("double y = DetMath.Log(x);", false)]
        [TestCase("double y = DetMath.Exp(x);", false)]
        [TestCase("double r = Math.Sqrt(x);", false)]
        [TestCase("int n = Math.Max(a, Math.Abs(b));", false)]
        [TestCase("float f = MathF.Round(v);", false)]
        [TestCase("// never Math.Log here: use DetMath", false)]
        [TestCase("x = 1; // Math.Pow would differ between runtimes", false)]
        [TestCase("IRandom rng = new Pcg32(seed, RngStreams.Policy);", false)]
        [TestCase("string s = \"Logarithm\"; int Exponent = 3;", false)]
        public void TheScanCatchesWhatItShouldAndNothingElse(string line, bool forbidden)
        {
            Assert.AreEqual(forbidden, Check(line, waiverAllowed: false) != null, line);
        }

        [Test]
        public void TheWaiverWorksOnlyWhereItIsAllowed()
        {
            const string line = "double shown = Math.Log(x); // determinism-ok: printed in a report, never fed back";
            Assert.IsNull(Check(line, waiverAllowed: true));
            Assert.IsNotNull(Check(line, waiverAllowed: false));
        }
    }
}
