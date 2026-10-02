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
    }
}
