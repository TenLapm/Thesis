using Thesis.Sim;

namespace Thesis.Harness
{
    // The map the game generates from a session's seed, with the numbers of the StaticTest (once ArknightsTest)
    // scene (SimHost: gridWorldSize 96 at node radius 1, so 48 x 48; mapMargin 2, mapRidges 22,
    // mapDeposits 28, depositsPerCluster 4). Headless runs that stand in for the game use this,
    // so they play on the board a participant would. The world origin is left at 0: it moves
    // every world position by the same amount and changes no rule.
    public static class GameMaps
    {
        public const int Size = 48;
        public const int Margin = 2;
        public const int Ridges = 22;
        public const int Deposits = 28;
        public const int DepositsPerCluster = 4;

        public static MapData Generate(ulong seed)
        {
            return MapGenerator.Generate(seed, Size, Size, 1f, Margin, Ridges, 0f, 0f, Deposits, DepositsPerCluster);
        }
    }
}
