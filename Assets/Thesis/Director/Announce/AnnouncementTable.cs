namespace Thesis.Director
{
    // The banner's line for a chosen strategy, keyed by (bin, strategy) as CLAUDE.md §5 asks.
    // Templated, never generated. For now one plain line per strategy, the same in every bin (the
    // student's provisional choice, 2026-11-16); per-bin lines can be added without changing the
    // ids. The wording matters for gates G6 (fairness) and G7 (players notice): edit it here.
    public static class AnnouncementTable
    {
        // What a WavePlan carries in AnnouncementId: "<strategy>@<bin>".
        public static string Id(string strategy, int bin) { return strategy + "@" + bin; }

        // The static version's line (GDD question 7: both versions show one). Not yet shown by the
        // HUD; that waits for the supervisor's answer to question 7.
        public const string StaticLine = "The next wave is on its way.";

        public static string Line(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            int at = id.IndexOf('@');
            string strategy = at < 0 ? id : id.Substring(0, at);
            switch (strategy)
            {
                case BreachThinWall.StrategyName: return "Sappers are coming for your thinnest wall.";
                case SwarmChokepoint.StrategyName: return "A swarm is coming through the spot your towers hold.";
                case SplitGroups.StrategyName: return "They are coming from three sides at once.";
                case FlyingBypass.StrategyName: return "Flyers are coming over your maze.";
                case RaidOutposts.StrategyName: return "Raiders are coming for your collectors.";
                default: return null;
            }
        }
    }
}
