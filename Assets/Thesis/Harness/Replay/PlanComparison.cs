namespace Thesis.Harness
{
    // What ReplayRunner.CompareRebuiltPlanner found: whether the planner, rebuilt from its name and
    // the seed, hands over the same plans as the recording holds.
    public sealed class PlanComparison
    {
        public bool Ok = true;
        public int PlansCompared;
        public int FirstDifferentPlan = -1; // index into ReplayFile.Plans, or -1
        public string Problem;

        internal void Fail(int index, string problem)
        {
            if (!Ok) return; // the first difference; later ones follow from it
            Ok = false;
            FirstDifferentPlan = index;
            Problem = problem;
        }

        public override string ToString()
        {
            return Ok ? "OK: " + PlansCompared + " plans, all the same" : "DIFFERENT: " + Problem;
        }
    }
}
