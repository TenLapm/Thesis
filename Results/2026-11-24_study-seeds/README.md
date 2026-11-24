# The study's seeds X and Y, and the practice seed

Date: 2026-11-24

Commands (each seed, each planner):

```
dotnet run --project Tools/dotnet/Thesis.Cli -c Release -- run --game-map <seed> --policy builder --planner escalation --seed <seed> --waves 50 --out Runs/seeds/<seed>
dotnet run --project Tools/dotnet/Thesis.Cli -c Release -- run --game-map <seed> --policy builder --planner heuristic|full --seed <seed> --waves 50 --out Runs/seeds/<seed>_<planner>
```

## Hypothesis

D9 crosses two seed sets with condition and order, so a difference between the two boards would be
averaged out over the groups, but it still adds noise to every within-subject pair. The two seeds should
therefore give boards about equally hard. The scripted player `builder` on seeds 101 to 130 against the
static waves is the measure; the candidates nearest the median are checked against both directors, and
the two whose numbers are closest on all three are taken. The practice seed is a third, ordinary one.

## Result

Waves the scripted player lasted against the static waves (the game has no last wave; 50 at most):

| seed | waves | seed | waves | seed | waves |
|---|---|---|---|---|---|
| 101 | 8 | 111 | 8 | 121 | 17 |
| 102 | 3 | 112 | 12 | 122 | 12 |
| 103 | 11 | 113 | 15 | 123 | 13 |
| 104 | 10 | 114 | 11 | 124 | 8 |
| 105 | 13 | 115 | 8 | 125 | 8 |
| 106 | 8 | 116 | 11 | 126 | 7 |
| 107 | 10 | 117 | 13 | 127 | 8 |
| 108 | 12 | 118 | 13 | 128 | 9 |
| 109 | 10 | 119 | 13 | 129 | 8 |
| 110 | 9 | 120 | 3 | 130 | 15 |

Median 10.5. The six nearest it, against all three planners:

| seed | static | heuristic | full |
|---|---|---|---|
| 103 | 11 | 7 | 7 |
| 104 | 10 | 5 | 5 |
| 107 | 10 | 4 | 4 |
| **109** | **10** | **8** | **8** |
| 114 | 11 | 10 | 6 |
| **116** | **11** | **9** | **9** |

**Chosen:** X = 109, Y = 116 (`StudySessions.SeedX`, `SeedY`); practice = 103 (`PracticeSeed`).

## What it means

- One scripted player and one run per seed is a coarse measure; the pilot shows whether the two boards
  play alike for people. Changing a seed after the pilot would change every later session's board.
- **Found on the way:** against this player both directors end the game sooner than the static waves
  (here 8 to 9 waves against 10 to 11; on the ladder 4.5 against 8.1). The threat budget is the same
  (I2), but the directors' waves are harder for a player who does not answer them. The balance pass has
  to look at this: CLAUDE.md §3 holds difficulty constant on purpose.
