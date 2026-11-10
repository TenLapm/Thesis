namespace Thesis.Director
{
    // One of the proposal's five features (ARCHITECTURE.md §5.3). A feature reads the board
    // and nothing else: never the shop's offers, the study condition or the director's
    // choice (the shop confound rule, CLAUDE.md §2). ProfileInput holds no shop for that reason.
    public interface IBuildFeature
    {
        string Name { get; }
        FeatureValue Compute(ProfileInput input);
    }
}
