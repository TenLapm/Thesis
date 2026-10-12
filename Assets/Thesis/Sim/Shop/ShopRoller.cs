using System;
using Thesis.Core;

namespace Thesis.Sim
{
    // Rolls the shop's offers. A pure function of (run seed, wave, reroll count):
    // nothing about the board, the budget, the planner or what was bought earlier
    // goes in, so nothing about them can come out (CLAUDE.md I10). Two sessions on
    // the same seed are offered the same towers in the same waves, whatever the
    // player or the director does. That is what stops "this participant had better
    // shop luck" from explaining a difference between the study's two conditions.
    //
    // It is counter-based, not a running stream: each roll seeds its own generator
    // from the three numbers (ARCHITECTURE.md §4.5). A running stream would make
    // wave 7's offers depend on how many rolls came before, and so on whether the
    // player ever paid for a reroll.
    public static class ShopRoller
    {
        public static ulong SeedFor(ulong rngSeed, int waveIndex, int rerollCount)
        {
            var h = new Fnv1a64();
            h.Add(rngSeed);
            h.Add("shop");
            h.Add(waveIndex);
            h.Add(rerollCount);
            return h.Value;
        }

        // `count` offers, each drawn on its own with a chance in proportion to the
        // tower's ShopWeight. The draws are independent, so the same tower can fill
        // two slots: with a small roster that is the only way a roll can be
        // anything but "one of each".
        //
        // Returns no offers when there is nothing to offer (an empty roster, or
        // every weight zero).
        public static TowerOffer[] Roll(ulong rngSeed, int waveIndex, int rerollCount, TowerDef[] roster, int count)
        {
            if (roster == null) throw new ArgumentNullException(nameof(roster));

            // Doubles throughout: the weights are floats, and a sum and a product of
            // doubles come out the same on every runtime (ARCHITECTURE.md §9 rule 3
            // is about float intermediates).
            double total = 0.0;
            for (int i = 0; i < roster.Length; i++) total += Weight(roster[i]);
            if (count <= 0 || !(total > 0.0)) return new TowerOffer[0];

            var rng = new Pcg32(SeedFor(rngSeed, waveIndex, rerollCount), RngStreams.Shop);
            var offers = new TowerOffer[count];
            for (int slot = 0; slot < count; slot++)
            {
                int index = Pick(roster, rng.NextDouble() * total);
                offers[slot] = new TowerOffer { TowerIndex = index, Price = roster[index].Cost };
            }
            return offers;
        }

        // The first tower whose running total of weight passes `point`.
        private static int Pick(TowerDef[] roster, double point)
        {
            double running = 0.0;
            int last = -1;
            for (int i = 0; i < roster.Length; i++)
            {
                double w = Weight(roster[i]);
                if (!(w > 0.0)) continue;
                running += w;
                last = i;
                if (point < running) return i;
            }
            // `point` is below the total by construction; this is only reached if
            // rounding left it a hair above the last running sum.
            return last;
        }

        private static double Weight(TowerDef def)
        {
            return def.ShopWeight > 0f ? def.ShopWeight : 0.0;
        }
    }
}
