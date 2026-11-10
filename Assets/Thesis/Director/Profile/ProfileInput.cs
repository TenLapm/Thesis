using System;
using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Director
{
    // What a feature may read (ARCHITECTURE.md §5.3): the grid with its ground field, the
    // candidate entries, the empty board, the towers, and the damage by type of the last
    // wave played. Read-only by convention; a feature never changes the board.
    //
    // There is deliberately no shop here (CLAUDE.md §2): what the roll offered cannot leak
    // into a feature. WP8b adds what breach_vulnerability and chokepoint_reliance read.
    public sealed class ProfileInput
    {
        public readonly SimGrid Grid;
        public readonly TileCoord[] Spawns;
        public readonly EmptyBoard Empty;
        public readonly IReadOnlyList<TowerState> Towers;

        // The last resolved wave's damage per DamageType, or null before the first wave.
        public readonly float[] LastDamageByType;

        public ProfileInput(SimGrid grid, TileCoord[] spawns, EmptyBoard empty, IReadOnlyList<TowerState> towers, float[] lastDamageByType)
        {
            Grid = grid ?? throw new ArgumentNullException(nameof(grid));
            Spawns = spawns ?? throw new ArgumentNullException(nameof(spawns));
            Empty = empty ?? throw new ArgumentNullException(nameof(empty));
            Towers = towers ?? throw new ArgumentNullException(nameof(towers));
            if (empty.SpawnCount != spawns.Length)
                throw new ArgumentException("the empty board has " + empty.SpawnCount + " entries but the map " + spawns.Length + ": build it from the same map", nameof(empty));
            if (lastDamageByType != null && lastDamageByType.Length != DamageTypes.Count)
                throw new ArgumentException("damage by type has " + lastDamageByType.Length + " entries, not " + DamageTypes.Count, nameof(lastDamageByType));
            LastDamageByType = lastDamageByType;
        }

        // The board as it stands in a build phase. The simulation keeps the ground field
        // current (it rebuilds after every placement), so nothing is rebuilt here.
        public static ProfileInput From(SimState state, MapData map, EmptyBoard empty)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (map == null) throw new ArgumentNullException(nameof(map));
            return new ProfileInput(state.Grid, map.Spawns, empty, state.Towers, state.LastOutcome == null ? null : state.LastOutcome.DamageByType);
        }

        // The one place a field cost becomes tiles: costs are integers ×10 (FlowField.CardinalCost).
        public static float Tiles(int cost) { return (float)(cost / 10f); }

        // A tower that takes part in the defence: standing, on the board, and not a pylon.
        // A tower knocked out by siege still counts: it is back next build phase, and it is
        // still where the player chose to put it. The development director reads the same set
        // (RoughDirector.Shoots).
        public static bool CountsAsTower(TowerState t) { return t.IsAlive && !t.OnBench && t.PowerCapacity == 0; }
    }
}
