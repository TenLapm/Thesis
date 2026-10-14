namespace Thesis.Sim
{
    // One slot of the shop: a tower type the player may buy until the next roll.
    // A PlaceTower command names the slot, not the tower type, so a player can only
    // ever build what the shop is offering (CLAUDE.md §2).
    //
    // Since 2026-10-12 a slot holds a CARD, and a card is one of three things: a
    // tower (possibly rare or epic), a handful of budget, or a piece of gear. The
    // class keeps its name: most cards are towers, and every caller that only knows
    // towers still reads the right fields.
    public enum CardKind
    {
        Tower = 0,
        Coin = 1,   // PlaceTower does nothing with it; TakeCoin pays Amount into the budget
        Gear = 2,   // BuyGear puts GearIndex onto a tower for Price
        Wall = 3,   // TakeCoin adds Amount wall tiles to the wall stock (2026-10-13)
        Heal = 4,   // TakeCoin heals the core by Amount, up to its maximum (2026-10-14)
    }

    public sealed class TowerOffer
    {
        public CardKind Kind;

        // A tower card's level: 1 common, 2 rare, 3 epic.
        public int Level = 1;

        // A gear card's piece: its index in the gear roster. -1 otherwise.
        public int GearIndex = -1;

        // A coin card's budget. 0 otherwise.
        public int Amount;

        // Which tower: its index in the simulation's tower roster.
        public int TowerIndex;

        // What buying it costs. Today this is the roster's Cost; it is kept on the
        // offer so that a later rule (a discount, a mark-up) cannot change what a
        // tower already bought was sold for.
        public int Price;

        // Each slot can be bought once. A bought slot stays in the list, so the
        // slot numbers of the others do not shift.
        public bool Bought;

        public TowerOffer Clone() { return (TowerOffer)MemberwiseClone(); }

        public override string ToString() { return "tower #" + TowerIndex + " for " + Price + (Bought ? " (bought)" : ""); }
    }
}
