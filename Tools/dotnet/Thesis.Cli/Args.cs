using System;
using System.Collections.Generic;
using System.Globalization;

namespace Thesis.Cli
{
    // A deliberately small argument parser: positionals, `--name value` options and
    // `--name` flags. Anything not declared by the command is an error, so a typo
    // such as --per-tik fails loudly instead of being ignored.
    internal sealed class Args
    {
        private readonly Dictionary<string, string> values = new Dictionary<string, string>();
        private readonly HashSet<string> flags = new HashSet<string>();
        private readonly List<string> positionals = new List<string>();
        private readonly string command;

        public Args(string[] args, string[] valueOptions, string[] flagOptions)
        {
            command = args[0];
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                if (!a.StartsWith("--", StringComparison.Ordinal))
                {
                    positionals.Add(a);
                }
                else if (Array.IndexOf(flagOptions, a) >= 0)
                {
                    flags.Add(a);
                }
                else if (Array.IndexOf(valueOptions, a) >= 0)
                {
                    if (i + 1 >= args.Length) throw new ArgumentException(command + ": " + a + " needs a value");
                    values[a] = args[++i];
                }
                else
                {
                    throw new ArgumentException(command + ": unknown option " + a);
                }
            }
        }

        public bool Has(string name) { return values.ContainsKey(name); }

        public bool Flag(string name) { return flags.Contains(name); }

        public string Get(string name, string fallback)
        {
            string v;
            return values.TryGetValue(name, out v) ? v : fallback;
        }

        public int GetInt(string name, int fallback)
        {
            string v;
            if (!values.TryGetValue(name, out v)) return fallback;
            int parsed;
            if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) throw new ArgumentException(command + ": " + name + " needs a whole number, got '" + v + "'");
            return parsed;
        }

        public ulong GetULong(string name, ulong fallback)
        {
            string v;
            if (!values.TryGetValue(name, out v)) return fallback;
            ulong parsed;
            if (!ulong.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) throw new ArgumentException(command + ": " + name + " needs a non-negative whole number, got '" + v + "'");
            return parsed;
        }

        public string Positional(int index, string what)
        {
            if (index >= positionals.Count) throw new ArgumentException(command + ": missing " + what);
            return positionals[index];
        }

        public void NoMorePositionals(int allowed)
        {
            if (positionals.Count > allowed) throw new ArgumentException(command + ": unexpected argument '" + positionals[allowed] + "'");
        }
    }
}
