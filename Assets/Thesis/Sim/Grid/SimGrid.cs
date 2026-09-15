using System;
using Thesis.Core;

namespace Thesis.Sim
{
    // Port of GridManager's data and queries, without the MonoBehaviour. The static
    // walkability comes from MapData (scanned once in the editor by MapExporter)
    // instead of Physics.CheckSphere at Awake.
    public sealed class SimGrid
    {
        public readonly int Width;
        public readonly int Height;

        private readonly float worldSizeX;
        private readonly float worldSizeY;
        private readonly SimNode[] nodes;

        // Bumped by FlowField.Generate every time the field data changes, so views
        // (FlowFieldVisualizer, PathPreviewer) can rebuild only when needed.
        public int FieldVersion;

        public SimGrid(MapData map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            Width = map.Width;
            Height = map.Height;
            worldSizeX = map.WorldSizeX;
            worldSizeY = map.WorldSizeY;
            nodes = new SimNode[Width * Height];

            // Same arithmetic, in the same order, as GridManager.CreateGrid:
            //   worldBottomLeft = position - right * size.x / 2 - forward * size.y / 2
            //   worldPoint      = worldBottomLeft + right * (x * d + r) + forward * (y * d + r)
            float d = map.NodeRadius * 2f;
            float r = map.NodeRadius;
            float bottomLeftX = map.OriginX - map.WorldSizeX / 2f;
            float bottomLeftZ = map.OriginZ - map.WorldSizeY / 2f;

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    var position = new Vec2f(bottomLeftX + (x * d + r), bottomLeftZ + (y * d + r));
                    int index = x * Height + y;
                    nodes[index] = new SimNode(x, y, index, map.IsWalkable(x, y), position);
                }
            }
        }

        private SimGrid(SimGrid source)
        {
            Width = source.Width;
            Height = source.Height;
            worldSizeX = source.worldSizeX;
            worldSizeY = source.worldSizeY;
            FieldVersion = source.FieldVersion;
            nodes = new SimNode[source.nodes.Length];

            for (int i = 0; i < nodes.Length; i++)
            {
                SimNode s = source.nodes[i];
                nodes[i] = new SimNode(s.X, s.Y, s.Index, s.IsWalkable, s.Position)
                {
                    TerrainCost = s.TerrainCost,
                    WallHealth = s.WallHealth,
                    MaxWallHealth = s.MaxWallHealth,
                    BestCost = s.BestCost,
                    NextIndex = s.NextIndex,
                };
            }
        }

        public int NodeCount => nodes.Length;

        public SimNode this[int x, int y]
        {
            get
            {
                if (!InBounds(x, y)) throw new ArgumentOutOfRangeException("(" + x + "," + y + ") is outside " + Width + "x" + Height + ".");
                return nodes[x * Height + y];
            }
        }

        public SimNode Get(TileCoord t) { return this[t.X, t.Y]; }

        public SimNode ByIndex(int index) { return nodes[index]; }

        // The node NextIndex points to, or null at the goal / in unreachable pockets.
        public SimNode NextOf(SimNode node) { return node.NextIndex < 0 ? null : nodes[node.NextIndex]; }

        public bool InBounds(int x, int y) { return x >= 0 && x < Width && y >= 0 && y < Height; }

        public bool InBounds(TileCoord t) { return InBounds(t.X, t.Y); }

        public SimGrid Clone() { return new SimGrid(this); }

        // Line-for-line port of GridManager.NodeFromWorldPoint. Two quirks are kept
        // on purpose (ARCHITECTURE.md §4.3):
        //   * it ignores the grid origin (assumes the grid is centred on world 0,0);
        //   * it scales by (gridSize - 1) against gridWorldSize, which need not equal
        //     gridSize * nodeDiameter, so tile boundaries are skewed away from the
        //     midpoints between centres (strongly near the low-x / low-z edge).
        // The skew decides which tile an agent is "on" mid-hop, and so when digging
        // starts. Fixing it would be a gameplay change; record it as a deviation if
        // anyone ever does.
        public SimNode NodeFromPosition(Vec2f worldXZ)
        {
            float percentX = (worldXZ.X + worldSizeX / 2) / worldSizeX;
            float percentY = (worldXZ.Y + worldSizeY / 2) / worldSizeY;

            percentX = Clamp01(percentX);
            percentY = Clamp01(percentY);

            int x = RoundToInt((Width - 1) * percentX);
            int y = RoundToInt((Height - 1) * percentY);

            return nodes[x * Height + y];
        }

        // Port of GridManager.GetNeighbors into a caller-owned buffer (length >= 8)
        // to avoid a List allocation per call. The iteration ORDER is part of the
        // port and must not change: with SPFA, when two neighbours offer the same
        // cost, the first one relaxed wins and becomes NextIndex. A different order
        // gives a different (equally cheap) path, and the parity tests will fail.
        public int GetNeighbors(SimNode node, SimNode[] buffer)
        {
            if (buffer == null || buffer.Length < 8)
                throw new ArgumentException("Neighbour buffer must hold at least 8 nodes.", nameof(buffer));

            int count = 0;
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    if (x == 0 && y == 0) continue;

                    int checkX = node.X + x;
                    int checkY = node.Y + y;

                    if (checkX < 0 || checkX >= Width || checkY < 0 || checkY >= Height) continue;

                    // Corner-cut rule: never allow a diagonal move that squeezes
                    // between two solid tiles (static geometry OR standing walls).
                    // Entering a wall tile head-on is allowed - that's how digging
                    // starts - but slipping diagonally between two walls is not.
                    if (x != 0 && y != 0)
                    {
                        if (nodes[(node.X + x) * Height + node.Y].BlocksCorner ||
                            nodes[node.X * Height + (node.Y + y)].BlocksCorner)
                        {
                            continue;
                        }
                    }

                    buffer[count++] = nodes[checkX * Height + checkY];
                }
            }
            return count;
        }

        // Turns a tile into player wall terrain. Walls are diggable terrain, not
        // blockers: raise the entry cost and give it chew-through health. The
        // caller is responsible for rebuilding the flow field afterwards.
        public void SetWall(SimNode node, int terrainCost, float wallHealth)
        {
            if (!node.IsWalkable)
                throw new InvalidOperationException("Cannot place a wall on static geometry at (" + node.X + "," + node.Y + ").");
            if (terrainCost < 2)
                throw new ArgumentOutOfRangeException(nameof(terrainCost), terrainCost, "A wall must cost more than open ground (>= 2).");
            if (wallHealth <= 0f)
                throw new ArgumentOutOfRangeException(nameof(wallHealth), wallHealth, "A wall needs positive health, or HasWall reads false.");

            node.TerrainCost = terrainCost;
            node.WallHealth = wallHealth;
            node.MaxWallHealth = wallHealth;
        }

        // Back to open ground (a breach, or clearing a scenario). Caller rebuilds the field.
        public void ClearWall(SimNode node)
        {
            node.TerrainCost = 1;
            node.WallHealth = 0f;
            node.MaxWallHealth = 0f;
        }

        // Mathf.Clamp01 / Mathf.RoundToInt equivalents. RoundToInt is (int)Math.Round,
        // i.e. round-half-to-even, which is what Unity does too.
        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static int RoundToInt(float v)
        {
            return (int)Math.Round(v);
        }
    }
}
