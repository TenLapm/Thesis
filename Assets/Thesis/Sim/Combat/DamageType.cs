using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Thesis.Sim
{
    // The kinds of damage a tower can deal. An enemy has one resistance multiplier
    // per kind (AgentState.Resist), which is what makes "which towers did the
    // player build?" matter to the director (the damage_type_mix feature).
    //
    // The numbers are array indices and are stored in state hashes: never reorder
    // or renumber them, only append. In JSON the names are written, not the numbers.
    //
    // Placeholder set until the roster is designed (spec gap S12, WP-C5).
    [JsonConverter(typeof(StringEnumConverter))]
    public enum DamageType
    {
        Physical = 0,
        Fire = 1,
        Frost = 2,
    }
}
