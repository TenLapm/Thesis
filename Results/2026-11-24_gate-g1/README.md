# Gate G1: the learner works

Date: 2026-11-24

Command:

```
dotnet run --project Tools/dotnet/Thesis.Cli -c Release -- gate g1
```

## Hypothesis

On the synthetic threshold problem (`ThresholdBanditEnv`: 4 strategies, 5 bins, the best strategy flips at a context threshold of 0.6), Thompson sampling over `BinnedPosterior` picks the best strategy in **at least 85 %** of episodes 251 to 300, averaged over 20 seeds, **and** more often than uniform random and round-robin. **Fail:** below 70 %, or no better than either baseline: an implementation defect, or the context carries no information; revisit the features. Between the two numbers: neither; reported.

## Result

| policy | best-strategy share, episodes 251-300 |
|---|---|
| Thompson (the learner) | 0.851 |
| uniform random | 0.256 |
| round-robin | 0.250 |

## Verdict

**PASS** (20 seeds). The same number as `Results/2026-11-10_g1-synth` (0.851), a narrow pass. G1 is run on the synthetic problem because against a real board the best strategy is not known (the student, 2026-11-24); it shows the learner can learn a threshold, not that it learns from people. CLAUDE.md §6: G1 passing does not license claiming G5.

