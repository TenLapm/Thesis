namespace Thesis.Sim
{
    // What the player has built on a tile. Both kinds are the same thing to the
    // flow field and to a digging enemy - expensive terrain with health - and differ
    // only in what happens when the health runs out. The numbers go into state
    // hashes: append, never renumber.
    public enum Occupant
    {
        None = 0,
        Wall = 1,
        Tower = 2,
    }
}
