using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Thesis.Sim
{
    // How a tower chooses among the enemies in its range. One mode for now; the
    // roster (WP-C5) adds more only if a tower needs one. Append, never renumber.
    [JsonConverter(typeof(StringEnumConverter))]
    public enum TargetingMode
    {
        // The enemy closest to the core by route cost: the one about to leak.
        First = 0,

        // 2026-10-14: the player may choose, tower by tower (TowerState.Mode).
        Strongest = 1,  // the most hit points left
        Weakest = 2,    // the fewest hit points left
        Nearest = 3,    // closest to the tower itself
        Raiders = 4,    // raiders first (2026-10-24), the one closest to the core; others as First
    }
}
