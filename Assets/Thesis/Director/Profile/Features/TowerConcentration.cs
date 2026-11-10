using System;
using Thesis.Sim;

namespace Thesis.Director
{
    // tower_concentration (ARCHITECTURE.md §5.3): do the towers stand together or spread out?
    //
    //   1 − (mean tower distance from the towers' centroid ÷ the diagonal of the built area)
    //
    // The built area is the bounding box of every tile the player has built on: walls (not
    // rocks), towers and collectors (the student, 2026-11-10: the area built in, not the whole
    // board). Tiles count whole, so the box of one tile is 1 × 1 and never has a zero diagonal.
    // Building further out widens the box and makes the same towers more concentrated; that is
    // a real change in the base.
    //
    // Which towers: ProfileInput.CountsAsTower (knocked-out ones yes, pylons no).
    public sealed class TowerConcentration : IBuildFeature
    {
        public const string FeatureName = "tower_concentration";

        public string Name => FeatureName;

        public FeatureValue Compute(ProfileInput input)
        {
            int n = 0;
            float cx = 0f, cy = 0f;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int t = 0; t < input.Towers.Count; t++)
            {
                TowerState tower = input.Towers[t];
                if (!ProfileInput.CountsAsTower(tower)) continue;
                n++;
                cx = (float)(cx + tower.Tile.X);
                cy = (float)(cy + tower.Tile.Y);
                Widen(tower.Tile.X, tower.Tile.Y, ref minX, ref minY, ref maxX, ref maxY);
            }
            if (n == 0) return FeatureValue.NoData;

            SimGrid grid = input.Grid;
            for (int i = 0; i < grid.NodeCount; i++)
            {
                SimNode node = grid.ByIndex(i);
                bool built = node.Occupant == Occupant.Tower || node.Occupant == Occupant.Collector
                          || (node.Occupant == Occupant.Wall && node.PieceId != SimNode.RockPiece);
                if (built) Widen(node.X, node.Y, ref minX, ref minY, ref maxX, ref maxY);
            }

            cx = (float)(cx / n);
            cy = (float)(cy / n);
            float distance = 0f;
            for (int t = 0; t < input.Towers.Count; t++)
            {
                TowerState tower = input.Towers[t];
                if (!ProfileInput.CountsAsTower(tower)) continue;
                float dx = (float)(tower.Tile.X - cx), dy = (float)(tower.Tile.Y - cy);
                distance = (float)(distance + (float)Math.Sqrt((float)((float)(dx * dx) + (float)(dy * dy)))); // sqrt is exact under IEEE 754
            }
            distance = (float)(distance / n);

            int w = maxX - minX + 1, h = maxY - minY + 1;
            float diagonal = (float)Math.Sqrt((float)(w * w + h * h));
            float v = (float)(1f - (float)(distance / diagonal));
            return FeatureValue.Of(Math.Max(0f, Math.Min(1f, v)));
        }

        private static void Widen(int x, int y, ref int minX, ref int minY, ref int maxX, ref int maxY)
        {
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }
    }
}
