using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests
{
    // Tower definitions for tests. Roster() is a fixed roster of three (it was the
    // game's placeholder roster until WP-C5, and many tests are written against its
    // numbers); TestSims.GameTowers() is the roster the game sells from today. The
    // builders below make one-purpose towers that cost nothing, so a test can place
    // them without caring about the budget.
    public static class TestTowers
    {
        public static TowerDef[] Roster()
        {
            return new[]
            {
                new TowerDef { Id = "archer", Cost = 10, DamageType = DamageType.Physical, Damage = 4f, RangeTiles = 3.5f, FireIntervalTicks = 25, CanHitFlying = true },
                new TowerDef { Id = "cannon", Cost = 18, DamageType = DamageType.Fire, Damage = 3f, RangeTiles = 2.5f, FireIntervalTicks = 60, SplashRadiusTiles = 0.75f },
                new TowerDef { Id = "frost", Cost = 12, DamageType = DamageType.Frost, Damage = 1f, RangeTiles = 2.5f, FireIntervalTicks = 30, SlowTicks = 75, SlowFactor = 0.5f },
            };
        }

        public static TowerDef Gun(float damage = 1f, float range = 3f, int interval = 25, DamageType type = DamageType.Physical, int cost = 0, string id = "gun", bool canHitFlying = false)
        {
            return new TowerDef { Id = id, Cost = cost, DamageType = type, Damage = damage, RangeTiles = range, FireIntervalTicks = interval, CanHitFlying = canHitFlying };
        }

        public static TowerDef Splash(float damage, float range, float splashTiles, int interval = 25, bool canHitFlying = false)
        {
            return new TowerDef { Id = "splash", Cost = 0, DamageType = DamageType.Fire, Damage = damage, RangeTiles = range, FireIntervalTicks = interval, SplashRadiusTiles = splashTiles, CanHitFlying = canHitFlying };
        }

        public static TowerDef Slower(int slowTicks, float slowFactor, float range = 3f, float damage = 0f, int interval = 25)
        {
            return new TowerDef { Id = "slower", Cost = 0, DamageType = DamageType.Frost, Damage = damage, RangeTiles = range, FireIntervalTicks = interval, SlowTicks = slowTicks, SlowFactor = slowFactor };
        }

        // A tower as TowerSystem sees it, without going through a Simulation: for
        // unit tests that call TowerSystem.Step directly.
        public static TowerState StateOn(AsciiFixture f, int id, TowerDef def, int x, int y)
        {
            return new TowerState(id, def, new TileCoord(x, y), f.Grid[x, y].Position, f.Map.NodeDiameter, 0);
        }

        public static AgentState Agent(int id, Vec2f position, float hp = 10f, float speed = 1f, float[] resist = null, float killReward = 0.2f,
                                       MovementClass movement = MovementClass.Ground)
        {
            return new AgentState(id, position, speed, hp, resist ?? DamageTypes.AllOnes(), 1f, killReward, 1f, movement);
        }

        // ---- buying from the shop (WP-C3): a PlaceTower command names an offer slot ----

        // The first slot not yet bought that offers this tower type, or -1.
        public static int SlotOf(Simulation sim, string defId)
        {
            TowerOffer[] offers = sim.State.Shop.Offers;
            for (int i = 0; i < offers.Length; i++)
            {
                if (!offers[i].Bought && sim.TowerLibrary[offers[i].TowerIndex].Id == defId) return i;
            }
            return -1;
        }

        // The first slot not yet bought, or -1.
        public static int FreeSlot(Simulation sim)
        {
            TowerOffer[] offers = sim.State.Shop.Offers;
            for (int i = 0; i < offers.Length; i++) if (!offers[i].Bought) return i;
            return -1;
        }

        // Buys this tower type and puts it on (x, y). Throws when the shop is not
        // offering it or the purchase is refused: a test that needs a particular
        // tower must not carry on quietly without it.
        public static TowerState Buy(Simulation sim, string defId, int x, int y)
        {
            int slot = SlotOf(sim, defId);
            if (slot < 0) throw new System.InvalidOperationException("rig: the shop is not offering '" + defId + "' (seed " + sim.RngSeed + ", roll " + sim.State.Shop.WaveIndex + "): " + Offered(sim));
            return BuySlot(sim, slot, x, y);
        }

        // Buys whatever the first free slot offers.
        public static TowerState BuyAny(Simulation sim, int x, int y)
        {
            int slot = FreeSlot(sim);
            if (slot < 0) throw new System.InvalidOperationException("rig: every offer is already bought: " + Offered(sim));
            return BuySlot(sim, slot, x, y);
        }

        public static TowerState BuySlot(Simulation sim, int slot, int x, int y)
        {
            int before = sim.State.Towers.Count;
            TestSims.Send(sim, SimCommand.PlaceTower(slot, x, y));
            if (sim.State.Towers.Count != before + 1)
                throw new System.InvalidOperationException("rig: buying slot " + slot + " at (" + x + "," + y + ") was refused (budget " + sim.State.BuildBudget + "): " + Offered(sim));
            return sim.State.Towers[before];
        }

        public static string Offered(Simulation sim)
        {
            var parts = new List<string>();
            foreach (TowerOffer o in sim.State.Shop.Offers) parts.Add(sim.TowerLibrary[o.TowerIndex].Id + " " + o.Price + (o.Bought ? " (bought)" : ""));
            return "[" + string.Join(", ", parts) + "]";
        }

        public static int Count(IList<SimEvent> events, SimEventKind kind)
        {
            int n = 0;
            for (int i = 0; i < events.Count; i++) if (events[i].Kind == kind) n++;
            return n;
        }
    }
}
