using Thesis.Core;

namespace Thesis.Sim
{
    // What the shop is showing right now: the offers of the latest roll, and which
    // of them have been bought. Part of SimState, so it is cloned, hashed and
    // replayed with everything else.
    //
    // A roll is named by two numbers, and those two numbers plus the run's seed
    // decide its offers completely (ShopRoller; CLAUDE.md I10):
    //   WaveIndex    how many waves had resolved when it was rolled. 0 is the roll
    //                at game start; N is the roll made the moment wave N resolved.
    //   RerollCount  how many times the player has paid for a new set since.
    public sealed class ShopState
    {
        public int WaveIndex;
        public int RerollCount;
        public TowerOffer[] Offers = new TowerOffer[0];

        // How many of these offers the player may still take. -1 = as many as the
        // budget buys (the shop before the draft of 2026-10-12, SimConfig.PicksPerRoll 0).
        public int PicksLeft = -1;

        public bool MayPick => PicksLeft != 0;

        public ShopState Clone()
        {
            var c = new ShopState { WaveIndex = WaveIndex, RerollCount = RerollCount, PicksLeft = PicksLeft, Offers = new TowerOffer[Offers.Length] };
            for (int i = 0; i < Offers.Length; i++) c.Offers[i] = Offers[i].Clone();
            return c;
        }

        internal void AddToHash(ref Fnv1a64 h)
        {
            h.Add(WaveIndex);
            h.Add(RerollCount);
            h.Add(PicksLeft);
            h.Add(Offers.Length);
            for (int i = 0; i < Offers.Length; i++)
            {
                h.Add(Offers[i].TowerIndex);
                h.Add(Offers[i].Price);
                h.Add(Offers[i].Bought);
                h.Add((int)Offers[i].Kind);
                h.Add(Offers[i].Level);
                h.Add(Offers[i].GearIndex);
                h.Add(Offers[i].Amount);
            }
        }
    }
}
