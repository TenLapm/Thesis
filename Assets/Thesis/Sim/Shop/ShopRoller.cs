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
            return Roll(rngSeed, waveIndex, rerollCount, roster, count, null, null);
        }

        // The same, with the cards of 2026-10-12: a slot may hold a coin card or a gear
        // card, and a tower card may be rare or epic, by the weights and chances in
        // `config`. Still a pure function of its arguments (I10). A random number is
        // only drawn for a choice that is switched on, so with every weight and chance
        // at 0 (or no config) the offers are exactly those of the plain roll.
        public static TowerOffer[] Roll(ulong rngSeed, int waveIndex, int rerollCount, TowerDef[] roster, int count, SimConfig config, GearDef[] gear)
        {
            if (roster == null) throw new ArgumentNullException(nameof(roster));

            double coinWeight = config != null && config.CoinCardWeight > 0f ? config.CoinCardWeight : 0.0;
            double gearWeight = config != null && config.GearCardWeight > 0f && gear != null && gear.Length > 0 ? config.GearCardWeight : 0.0;
            double rare = config != null && config.RareTowerChance > 0f ? config.RareTowerChance : 0.0;
            double epic = config != null && config.EpicTowerChance > 0f ? config.EpicTowerChance : 0.0;

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
                if (coinWeight + gearWeight > 0.0)
                {
                    double kind = rng.NextDouble() * (1.0 + coinWeight + gearWeight);
                    if (kind < coinWeight)
                    {
                        offers[slot] = new TowerOffer { Kind = CardKind.Coin, Amount = config.CoinCardAmount };
                        continue;
                    }
                    if (kind < coinWeight + gearWeight)
                    {
                        int piece = Math.Min(gear.Length - 1, (int)(rng.NextDouble() * gear.Length));
                        offers[slot] = new TowerOffer { Kind = CardKind.Gear, GearIndex = piece, Price = gear[piece].Price };
                        continue;
                    }
                }

                int index = Pick(roster, rng.NextDouble() * total);
                int level = 1;
                if (rare + epic > 0.0)
                {
                    double u = rng.NextDouble();
                    level = u < epic ? 3 : u < epic + rare ? 2 : 1;
                }
                offers[slot] = new TowerOffer { TowerIndex = index, Level = level, Price = level == 1 ? roster[index].Cost : TowerUpgrades.CardPrice(roster[index], level, config) };
            }
            return offers;
        }

        // The gear shop's stock number `stock` (0 at game start, then one more each time
        // it is restocked): `count` pieces, each drawn on its own, so a piece can be on
        // sale twice. A pure function of the seed and the stock number.
        public static TowerOffer[] RollGearShop(ulong rngSeed, int stock, GearDef[] gear, int count)
        {
            if (gear == null || gear.Length == 0 || count <= 0) return new TowerOffer[0];

            var h = new Fnv1a64();
            h.Add(rngSeed);
            h.Add("gearshop");
            h.Add(stock);
            var rng = new Pcg32(h.Value, RngStreams.Shop);
            var items = new TowerOffer[count];
            for (int slot = 0; slot < count; slot++)
            {
                int piece = Math.Min(gear.Length - 1, (int)(rng.NextDouble() * gear.Length));
                items[slot] = new TowerOffer { Kind = CardKind.Gear, GearIndex = piece, Price = gear[piece].Price };
            }
            return items;
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
