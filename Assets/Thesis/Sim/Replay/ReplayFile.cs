using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Thesis.Core;

namespace Thesis.Sim
{
    // Everything needed to run a session again and check it came out the same
    // (CLAUDE.md I1): what the run was built from, every player input with its
    // tick, and the state hashes the original produced.
    //
    // The file is self-contained: Config, MapData and Shapes are stored in full, not
    // by name or hash. A replay written by a Unity session therefore verifies
    // headless with nothing else on disk, and still means the same thing after
    // someone retunes SimConfig or re-exports a map. (Deviation from WORKPLAN WP5,
    // which listed only `map` and `simConfigHash`; see DEVLOG.)
    //
    // It lives in Thesis.Sim, not Thesis.Harness, because SimHost writes one in
    // player builds and the harness assembly is editor-only.
    public sealed class ReplayFile
    {
        // 1: the game before towers (WP5). 2: adds the tower roster and the
        // PlaceTower command (WP-C1). 3: movement classes (WP-C2); the file's layout
        // is the same as 2, but the state every hash covers now includes the sapper
        // field and each enemy's class. A file of another schema is refused: its
        // commands and hashes describe a different game.
        public const int CurrentSchema = 3;

        public int Schema = CurrentSchema;

        // --- labels: never read by a replay, only by people ---
        public string Build;    // who recorded it, e.g. "Unity 6000.4.10f1 editor" or "headless .NET 9.0.10"
        public string Session;  // the session folder name
        public string Policy;   // "human", or the scripted policy's name

        // --- what the run was built from ---
        public string Map;      // MapData.Name
        public ulong MapSeed;   // 0 until seeded mazes exist (semester 2 UX pass)
        public ulong RngSeed;
        public string Planner;  // IWavePlanner.Name, e.g. "escalation"
        public SimConfig Config;
        public MapData MapData;
        public ShapeDef[] Shapes;
        public TowerDef[] Towers;

        // ReplaySetup.Hash(Config, MapData, Shapes, Towers) at record time. Validate()
        // recomputes it from the loaded values.
        public string SetupHash;

        // The state hash before the first tick and before any command.
        public string InitialHash;

        // --- what happened ---
        public List<ReplayCommand> Commands = new List<ReplayCommand>();
        public List<WaveHash> WaveHashes = new List<WaveHash>();

        // Where the recording stops: State.Tick and the state hash at that point,
        // after every command up to it was applied. A session that was quit
        // mid-wave is still fully checked up to here.
        public int FinalTick;
        public string FinalHash;

        // Optional. One hash per tick, 8 bytes little-endian each, base64. Entry i is
        // the hash right after tick i ran (State.Tick == i + 1). This is what lets a
        // divergence be pinned to a tick instead of a wave.
        public string TickHashes;

        public static string Hex(ulong hash) { return hash.ToString("x16", CultureInfo.InvariantCulture); }

        public static ulong ParseHex(string hex)
        {
            ulong value;
            if (hex == null || !ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                throw new InvalidDataException("[Replay] '" + hex + "' is not a 64-bit hex hash.");
            return value;
        }

        public static string EncodeTickHashes(List<ulong> hashes)
        {
            var bytes = new byte[hashes.Count * 8];
            for (int i = 0; i < hashes.Count; i++)
            {
                ulong v = hashes[i];
                for (int b = 0; b < 8; b++) bytes[i * 8 + b] = (byte)(v >> (8 * b));
            }
            return Convert.ToBase64String(bytes);
        }

        // Null when the file was recorded without per-tick hashes.
        public ulong[] DecodeTickHashes()
        {
            if (string.IsNullOrEmpty(TickHashes)) return null;

            byte[] bytes = Convert.FromBase64String(TickHashes);
            if (bytes.Length % 8 != 0) throw new InvalidDataException("[Replay] TickHashes holds " + bytes.Length + " bytes, not a multiple of 8.");

            var hashes = new ulong[bytes.Length / 8];
            for (int i = 0; i < hashes.Length; i++)
            {
                ulong v = 0;
                for (int b = 0; b < 8; b++) v |= (ulong)bytes[i * 8 + b] << (8 * b);
                hashes[i] = v;
            }
            return hashes;
        }

        // Throws with a message that says what is wrong, so a damaged or hand-edited
        // file fails at load time rather than as a strange divergence later.
        public void Validate()
        {
            if (Schema != CurrentSchema) throw new InvalidDataException("[Replay] Schema " + Schema + ", expected " + CurrentSchema + ".");
            if (Config == null || MapData == null || Shapes == null || Towers == null) throw new InvalidDataException("[Replay] Config, MapData, Shapes and Towers are all required.");
            if (Commands == null || WaveHashes == null) throw new InvalidDataException("[Replay] Commands and WaveHashes must be present (they may be empty).");
            if (FinalTick < 0) throw new InvalidDataException("[Replay] FinalTick is " + FinalTick + ".");

            Config.Validate();
            MapData.Validate();

            string setup = Hex(ReplaySetup.Hash(Config, MapData, Shapes, Towers));
            if (setup != SetupHash)
                throw new InvalidDataException("[Replay] The config, map, shapes or towers in this file are not the ones it was recorded with (setup hash " + setup
                                               + ", recorded " + SetupHash + "). Either the file was edited or a number did not survive being written as JSON.");

            int last = 0;
            for (int i = 0; i < Commands.Count; i++)
            {
                ReplayCommand c = Commands[i];
                if (c == null) throw new InvalidDataException("[Replay] Command " + i + " is null.");
                if (c.Tick < last) throw new InvalidDataException("[Replay] Command " + i + " (" + c + ") comes after a command at tick " + last + "; commands must be in tick order.");
                if (c.Tick > FinalTick) throw new InvalidDataException("[Replay] Command " + i + " (" + c + ") is after the recording's final tick " + FinalTick + ".");
                c.ToCommand(); // throws on an unknown kind
                last = c.Tick;
            }
        }

        public string ToJson() { return Json.SerializeIndented(this); }

        public static ReplayFile FromJson(string json)
        {
            ReplayFile file;
            try
            {
                file = Json.Deserialize<ReplayFile>(json);
            }
            catch (Newtonsoft.Json.JsonException e)
            {
                // One exception type for "this file is no good", whatever the cause.
                throw new InvalidDataException("[Replay] Not a valid replay file: " + e.Message, e);
            }
            if (file == null) throw new InvalidDataException("[Replay] Replay JSON was empty.");
            file.Validate();
            return file;
        }

        // Writes to a temporary file next to the target and then swaps it in. A
        // session's replay is rewritten at every wave boundary; writing straight over
        // the old file would leave a truncated one - the whole session lost - if the
        // game or the machine died halfway through.
        public void Save(string path)
        {
            string full = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string temp = full + ".tmp";
            File.WriteAllText(temp, ToJson());
            if (File.Exists(full)) File.Replace(temp, full, null);
            else File.Move(temp, full);
        }

        public static ReplayFile Load(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("[Replay] Replay file not found.", path);
            return FromJson(File.ReadAllText(path));
        }
    }
}
