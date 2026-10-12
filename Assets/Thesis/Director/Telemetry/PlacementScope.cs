namespace Thesis.Director
{
    // Which of the player's building the placement entropy counts. The primary
    // metric counts both (ARCHITECTURE.md §5.9); the other two are reported next
    // to it.
    public enum PlacementScope
    {
        WallsAndTowers = 0,
        WallsOnly = 1,
        TowersOnly = 2,
    }
}
