namespace Thesis.Sim
{
    // One slot of the shop: a tower type the player may buy until the next roll.
    // A PlaceTower command names the slot, not the tower type, so a player can only
    // ever build what the shop is offering (CLAUDE.md §2).
    public sealed class TowerOffer
    {
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
