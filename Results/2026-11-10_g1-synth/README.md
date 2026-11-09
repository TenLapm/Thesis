# G1-synthetic: does the learner find the best strategy? (2026-11-10)

Written by `thesis synth` (WORKPLAN WP7). Reproduce with:

```
dotnet run --project Tools/dotnet/Thesis.Cli -c Release -- synth --seeds 20 --speed-episodes 1000000
```

## Hypothesis (stated before the table)

Gate G1 (`CLAUDE.md` §6): on a problem whose best arm flips at a threshold in the context, `BinnedPosterior` picks the best arm
at least 85 % of the time within 300 episodes, averaged over 20 seeds, and beats uniform-random and round-robin.
Below 70 %, or no separation from the baselines, is the fail number: an implementation defect, or the context carries no information.

## The problem (fixed on 2026-11-10, before the first run)

No spec gives these numbers; they were chosen once and not tuned against results.

- 4 arms, context x ~ U[0, 1), 5 equal bins.
- x < 0.600: arm 0 pays 1 with p = 0.800; x >= 0.600: arm 1 does. Every other arm pays with p = 0.400.
- The threshold is a bin edge, so the bins can represent it exactly. A threshold inside a bin would cap the best possible share in that bin.
- Learner: Thompson sampling over the 20 Beta posteriors (largest θ), discount G = 0.950.
- **"Within 300 episodes" is read as the best-arm share over episodes 251-300**, per seed, then averaged. The spec does not say which episodes count.

## Result: PASS

| Policy | Mean best-arm share, episodes 251-300 | Lowest seed |
|---|---|---|
| Thompson | 0.851 | 0.740 |
| DirectorCorrection | 0.848 | 0.540 |
| UniformRandom | 0.256 | 0.160 |
| RoundRobin | 0.250 | 0.180 |

`DirectorCorrection` is not part of G1. It is the director's rule with every authored score at 0: the largest `Correction.Value`,
where a cold cell counts exactly 0. It shows spec gap S2 (WP11): a cold arm loses to any warm arm whose θ is above 0.5 and may never be tried.

Learning curve of `Thompson` (mean best-arm share per block of 25 episodes):

| Episodes | 1-25 | 26-50 | 51-75 | 76-100 | 101-125 | 126-150 | 151-175 | 176-200 | 201-225 | 226-250 | 251-275 | 276-300 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| share | 0.310 | 0.472 | 0.502 | 0.628 | 0.648 | 0.720 | 0.760 | 0.756 | 0.796 | 0.806 | 0.844 | 0.858 |

## Calibration (a diagnostic, not a gate)

Pairs of (posterior mean of the chosen arm before the update, the 0/1 reward), all 300 episodes of every `Thompson` seed.

Brier 0.207, ECE (10 bins) 0.040.

| Predicted | Pairs | Mean predicted | Mean realised |
|---|---|---|---|
| 0.100-0.200 | 2 | 0.175 | 0.000 |
| 0.200-0.300 | 82 | 0.250 | 0.451 |
| 0.300-0.400 | 312 | 0.345 | 0.487 |
| 0.400-0.500 | 423 | 0.458 | 0.449 |
| 0.500-0.600 | 985 | 0.529 | 0.489 |
| 0.600-0.700 | 1044 | 0.659 | 0.683 |
| 0.700-0.800 | 1892 | 0.753 | 0.770 |
| 0.800-0.900 | 1206 | 0.839 | 0.780 |
| 0.900-1.000 | 54 | 0.914 | 0.796 |

## Review probe: is the discount doing its job?

The two best arms swap at episode 300 of 600. Expected: without the discount (G = 1) the learner adapts measurably worse.

| G | Episodes 251-300 | 301-400 (just after the flip) | 501-600 |
|---|---|---|---|
| 0.950 | 0.851 | 0.213 | 0.713 |
| 1.000 | 0.870 | 0.091 | 0.397 |

## Speed

1000000 episodes of one `Thompson` run took 0.50 s on this machine (wall clock, a single run); best-arm share over its last 10,000 episodes 0.992.

## Deviations

- The measurement window (episodes 251-300) and the problem's numbers were chosen here, because the spec gives neither.
