namespace Thesis.Sim
{
    // The shared threat-budget cost table seen from the simulation (CLAUDE.md I2:
    // every strategy spends an identical threat budget at a given wave number). The
    // real table is WP9; WP3 only defines the hook so ValidatePlan can enforce I2
    // from the moment one exists. Null means "no cost table in use" and skips the check.
    public interface IThreatPricer
    {
        float BudgetForWave(int waveIndex);

        float Price(WavePlan plan);
    }
}
