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
| builder | random | 20 | 0.206 ± 0.124 | 0.055 | 5.6 |
| builder | escalation | 20 | 0.210 ± 0.131 | 0.047 | 9.3 |
| builder | heuristic | 20 | 0.159 ± 0.125 | 0.051 | 5.2 |
| builder | full | 20 | 0.149 ± 0.116 | 0.051 | 5.2 |
| reactive | random | 20 | 0.219 ± 0.138 | 0.086 | 5.3 |
| reactive | escalation | 20 | 0.230 ± 0.170 | 0.059 | 8.1 |
| reactive | heuristic | 20 | 0.252 ± 0.139 | 0.113 | 4.5 |
| reactive | full | 20 | 0.249 ± 0.134 | 0.116 | 4.5 |

## Verdict

- `builder`: the ladder **does not hold** (escalation >= heuristic).
- `reactive`: the ladder **does not hold** (heuristic >= full).

**Verdict: FAIL** (the pass rule is the reacting player's ladder).

## The director's time per wave (wall clock, for gate G2)

Over 465 decisions of the two directors: median 0.42 ms, 99th percentile 11.08 ms, most 17.92 ms. Measured with 12 episodes running at once, so these are upper bounds; G2 itself is measured on one thread (`thesis gate g2`).

## Limits

- A scripted player acts only at the start of a build phase; people also build in the middle of a wave.
- The reacting player remembers one wave and has four answers. It is a stand-in to show the directors can be told apart, not a model of a participant.
- Entropy is compared over a common window, which is short when one rung ends a game early.


## What this means (written after the run)

- **The ladder does not hold for the reacting player at the top: the full director (0.249) is level with the heuristic (0.252).** Below that it holds: random < escalation < heuristic in entropy, and both directors make the reacting player rebuild about twice as much per wave as the static waves (restructure 0.11 against 0.06). For `builder`, which never answers a wave, the directors come out *lower* than the static waves, as expected of a control: its entropy only tracks how long it lives.
- **Why the top rung is flat.** The games are short: against the two directors the reacting player lasts 4.5 waves, so the full director makes about four decisions, all of them on cold or thin cells, where its correction is 0 or borrowed from the kernel. It cannot differ from the heuristic yet. The window over which entropy is compared is then also only about four waves.
- **The games are short because the directors are harder.** Every rung spends the same threat budget (I2), yet both directors end the game in about half the waves of the static version. The study holds difficulty constant (CLAUDE.md §3), so this matters more than the ladder's top rung: the balance pass must look at the strategies' prices (the cost table's factors) before the pilot.
- **By the rule fixed beforehand** this is a FAIL, and what follows is to revisit the reward and the learner's settings (S2 to S4), not the measure. The first thing to try is not a setting at all: a longer game (the balance pass), so the learner has decisions to learn from.
