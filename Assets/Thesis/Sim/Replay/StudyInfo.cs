using Newtonsoft.Json;

namespace Thesis.Sim
{
    // Who played a session and which part of the study it was (D9, WP14). The setup screen fills
    // it in a study build; a development session has none (null), and then neither the replay nor
    // any telemetry row writes the field at all, so every file recorded before WP14 is unchanged,
    // byte for byte.
    //
    // It never reaches the simulation: the session's seed and planner are chosen from it before
    // the simulation exists, and from then on it is a label, like ReplayFile.Policy.
    public sealed class StudyInfo
    {
        public const string Static = "static";
        public const string Adaptive = "adaptive";

        [JsonProperty("participant", Order = 1)] public string Participant;  // "P07"
        [JsonProperty("group", Order = 2)] public int Group;                 // 1 to 4 (ARCHITECTURE.md §6), 0 for practice
        [JsonProperty("session", Order = 3)] public int SessionNumber;       // 1 or 2, 0 for practice
        [JsonProperty("condition", Order = 4)] public string Condition;      // Static or Adaptive
        [JsonProperty("seedSet", Order = 5)] public string SeedSet;          // "X", "Y" or "practice"
        [JsonProperty("practice", Order = 6)] public bool Practice;

        public StudyInfo Copy() { return (StudyInfo)MemberwiseClone(); }
    }
}
