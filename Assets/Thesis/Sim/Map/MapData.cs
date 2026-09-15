using System;
using System.IO;
using Thesis.Core;

namespace Thesis.Sim
{
    // Static map geometry exported from a Unity scene (Assets/Scripts/Editor/MapExporter.cs)
    // or parsed from an ASCII fixture. Everything that changes during a run (walls,
    // costs) lives in SimGrid, never here.
    //
    // Rows is the stored form so a map file can be read and diffed by eye:
    //   Rows[0] is the TOP row = y = Height - 1, Rows[Height - 1] is y = 0.
    //   '.' walkable, 'X' static blocker (the scene's unwalkableMask).
    // The same orientation is used by AsciiMap, so a map file and a test fixture
    // look the same.
    public sealed class MapData
    {
        public const int CurrentSchema = 1;

        public int Schema = CurrentSchema;
        public string Name;

        // GridManager.gridSizeX / gridSizeY.
        public int Width;
        public int Height;

        // GridManager.gridWorldSize, nodeRadius and transform position (x, z). Kept
        // verbatim because NodeFromPosition must reproduce NodeFromWorldPoint's
        // arithmetic exactly, including the fact that WorldSize * 1 need not equal
        // Width * NodeDiameter (SampleScene: 75 vs 76).
        public float WorldSizeX;
        public float WorldSizeY;
        public float NodeRadius;
        public float OriginX;
        public float OriginZ;

        public string[] Rows;

        public TileCoord Core;
        public TileCoord[] Spawns = new TileCoord[0];
        public WorldPoint CoreWorld;
        public WorldPoint[] SpawnWorlds = new WorldPoint[0];

        public float NodeDiameter => NodeRadius * 2f;

        public bool IsWalkable(int x, int y)
        {
            return Rows[Height - 1 - y][x] == '.';
        }

        // Throws with a message that says what is wrong and where, so a bad export
        // fails at load time rather than as a strange path three systems later.
        public void Validate()
        {
            if (Schema != CurrentSchema)
                throw new InvalidDataException("Map '" + Name + "': schema " + Schema + ", expected " + CurrentSchema + ".");
            if (Width <= 0 || Height <= 0)
                throw new InvalidDataException("Map '" + Name + "': size " + Width + "x" + Height + " is not positive.");
            if (NodeRadius <= 0f)
                throw new InvalidDataException("Map '" + Name + "': NodeRadius " + NodeRadius + " is not positive.");
            if (WorldSizeX <= 0f || WorldSizeY <= 0f)
                throw new InvalidDataException("Map '" + Name + "': world size " + WorldSizeX + "x" + WorldSizeY + " is not positive.");
            if (Rows == null || Rows.Length != Height)
                throw new InvalidDataException("Map '" + Name + "': expected " + Height + " rows, found " + (Rows == null ? 0 : Rows.Length) + ".");

            for (int r = 0; r < Rows.Length; r++)
            {
                string row = Rows[r];
                if (row == null || row.Length != Width)
                    throw new InvalidDataException("Map '" + Name + "': row " + r + " (y=" + (Height - 1 - r) + ") has length " + (row == null ? 0 : row.Length) + ", expected " + Width + ".");
                for (int c = 0; c < row.Length; c++)
                {
                    if (row[c] != '.' && row[c] != 'X')
                        throw new InvalidDataException("Map '" + Name + "': row " + r + " column " + c + " has '" + row[c] + "'; only '.' and 'X' are allowed.");
                }
            }

            CheckTile(Core, "Core");
            if (Spawns == null) throw new InvalidDataException("Map '" + Name + "': Spawns is null.");
            for (int i = 0; i < Spawns.Length; i++) CheckTile(Spawns[i], "Spawns[" + i + "]");
            if (SpawnWorlds == null || SpawnWorlds.Length != Spawns.Length)
                throw new InvalidDataException("Map '" + Name + "': " + Spawns.Length + " spawns but " + (SpawnWorlds == null ? 0 : SpawnWorlds.Length) + " spawn world positions.");
            if (CoreWorld == null)
                throw new InvalidDataException("Map '" + Name + "': CoreWorld is missing.");
        }

        private void CheckTile(TileCoord t, string what)
        {
            if (t.X < 0 || t.X >= Width || t.Y < 0 || t.Y >= Height)
                throw new InvalidDataException("Map '" + Name + "': " + what + " " + t + " is outside " + Width + "x" + Height + ".");
            if (!IsWalkable(t.X, t.Y))
                throw new InvalidDataException("Map '" + Name + "': " + what + " " + t + " is on a static blocker.");
        }

        public string ToJson()
        {
            return Json.SerializeIndented(this);
        }

        public static MapData FromJson(string json)
        {
            MapData map = Json.Deserialize<MapData>(json);
            if (map == null) throw new InvalidDataException("Map JSON was empty.");
            map.Validate();
            return map;
        }

        public static MapData Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Map file not found. Export it from Unity with the menu Thesis/Export All Maps.", path);
            return FromJson(File.ReadAllText(path));
        }
    }
}
