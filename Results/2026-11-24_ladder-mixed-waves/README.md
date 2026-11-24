# The offline ladder (WP12)

Date: 2026-11-24

Command:

```
dotnet run --project Tools/dotnet/Thesis.Cli -c Release -- ladder --seeds 20 --first-seed 201 --waves 50
```

## Hypothesis

Ranked by how much the player rebuilds, the rungs come out **random < static escalation < authored heuristic < full director** (CLAUDE.md §6).

- **Measure.** The primary metric, the placement entropy of walls and towers on the 6 x 6 partition (`PlacementEntropy`), over a common window: for each player and seed, the fewest waves any rung lasted, so a rung is not credited for a longer game. Beside it, the mean change of the built features from one build phase to the next (`restructure`, what the full director is rewarded for). Waves survived are reported and not ranked (CLAUDE.md §3).
- **Pass.** For the reacting player (`reactive`, the one that answers waves), every rung's mean entropy is above the one below it.
- **Fail, and what follows.** The full director at or below the heuristic: the learner does not add to the authored rules against a player that reacts. Reported as found; before the pilot the reward and the learner's settings (S2 to S4) are revisited, not the measure. `builder` never rebuilds, so for it the rungs are expected to be close: it is the control that shows the metric is not moved by the waves alone.

## Method

Each episode is one game on the game's 48 x 48 board (`GameMaps`), with the game's numbers (`Maps/SimConfig.json`); map seed and rng seed are the episode's seed, 201 to 220 (none of them a study seed). The game has no last wave; an episode stops at 50 waves or when the core falls. Players: `builder` (never rebuilds) and `reactive` (answers the last wave: anti-air after flyers, a longer maze after walkers, fewer walls after sappers, power on the entry side after a split wave). Rungs: `random` (a study strategy at random), `escalation` (the static version), `heuristic` (layers 1 and 2), `full` (with the learner, starting cold). The whole set ran in 5 s on 12 threads; the episodes file is the same on any number of threads (`LadderTests`).

## Result

| player | rung | episodes | entropy (mean ± sd) | restructure | waves survived |
|---|---|---|---|---|---|
| builder | random | 20 | 0.197 ± 0.094 | 0.040 | 6.2 |
| builder | static | 20 | 0.160 ± 0.136 | 0.039 | 5.4 |
| builder | heuristic | 20 | 0.175 ± 0.112 | 0.041 | 7.6 |
| builder | full | 20 | 0.168 ± 0.117 | 0.046 | 7.0 |
| reactive | random | 20 | 0.257 ± 0.147 | 0.053 | 6.0 |
| reactive | static | 20 | 0.365 ± 0.110 | 0.120 | 4.8 |
| reactive | heuristic | 20 | 0.272 ± 0.114 | 0.077 | 6.4 |
| reactive | full | 20 | 0.256 ± 0.131 | 0.080 | 6.3 |

## Verdict

- `builder`: the ladder **does not hold** (random >= static).
- `reactive`: the ladder **does not hold** (static >= heuristic).

**Verdict: FAIL** (the pass rule is the reacting player's ladder).

## The director's time per wave (wall clock, for gate G2)

Over 625 decisions of the two directors: median 0.66 ms, 99th percentile 22.06 ms, most 46.83 ms. Measured with 12 episodes running at once, so these are upper bounds; G2 itself is measured on one thread (`thesis gate g2`).

## Limits

- A scripted player acts only at the start of a build phase; people also build in the middle of a wave.
- The reacting player remembers one wave and has four answers. It is a stand-in to show the directors can be told apart, not a model of a participant.
- Entropy is compared over a common window, which is short when one rung ends a game early.

