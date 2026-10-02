using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Thesis.Sim
{
    // How an enemy gets to the core (WP-C2).
    //
    //   Ground   follows the flow field: it walks around what the player built when
    //            the detour is cheap and chews through it when it is not. Every
    //            enemy of the original game is this.
    //   Sapper   the same, but on a second flow field in which a built tile costs
    //            only SimConfig.SapperDigCostFactor of its price. It therefore
    //            prefers to go THROUGH walls and towers. How fast it chews is its
    //            DigRate, as for everyone.
    //   Flying   uses no field at all: a straight line to the core, over walls,
    //            towers and static blockers alike. It never digs, and only a tower
    //            with CanHitFlying can shoot it.
    //
    // The numbers are stored in state hashes and index the per-class arrays
    // (WaveOutcome): never reorder or renumber them, only append. In JSON the names
    // are written, not the numbers.
    [JsonConverter(typeof(StringEnumConverter))]
    public enum MovementClass
    {
        Ground = 0,
        Sapper = 1,
        Flying = 2,
    }
}
