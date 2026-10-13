# Balance sanity check, version 1 (WP-C5)

**Date:** 2026-10-12 · **Board:** SampleScene, 24 × 24 with rocks, one-tile walls (WP-C4)

This is a sanity check, not a balance pass. It asks whether the first real roster
(five towers, four enemy types) is broken or absurd anywhere. The numbers are tuned
properly in the second semester (S2 W4).

## Command

```
thesis balance --seeds 10 --waves 25 --out Results/2026-10-12_balance-v1
```

(`thesis` is `dotnet run -c Release --project Tools/dotnet/Thesis.Cli --`. It reads
`Maps/SampleScene.map.json`, `Maps/Shapes.json` and `Maps/Towers.json`, plays the scripted
players `sentry`, `mixed`, `restless` and `idle` on seeds 1 to 10 against the static
baseline (`escalation`) and against the development planner (`class-cycle`, which sends
every enemy type), and writes `balance.md` here.)

## Hypotheses

Written before the first run of the command, and not changed afterwards.

| # | Hypothesis | Fails when |
|---|---|---|
| H1 | No tower is far better or far worse value than the others. | A tower's damage per budget per minute on the board is more than 2× the median of the five, or less than half of it (static baseline, all policies together). |
| H2 | An idle player loses early. | `idle` resolves five waves or more on any seed. |
| H3 | Nobody walks through the game. | Any policy reaches the end of wave 25 with the core untouched (10 of 10) on any seed. |
| H4 | Every tower is worth buying to somebody. | A tower is under 5 % of all purchases (it cannot be: `sentry` buys every offer it can pay for, so this checks the shop's weights and prices, not taste). |
| H5 | A plain tower player gets well into the game without finishing it. | The median wave `sentry` loses in, over the ten seeds on the static baseline, is below 8, or it survives all 25 on more than half the seeds. |
| H6 | The enemy types are not harmless and not unanswerable. | Under `class-cycle`, a whole enemy type leaks nothing in every run of every policy, or a type is never killed at all. |

What a failure means: the roster's numbers are changed and the command is run again, and
both the failing table and the passing one are kept below.

## Results

Five runs, in the order they were made. Each run's whole output is kept: `run1.md` to
`run4.md`, and `balance.md` for the fifth, which is the roster the game now has.

| Run | What was changed before it | Sentry, static baseline: median wave lost in | Runs that ended wave 25 untouched (of 30) | Tower value over the median: lowest to highest |
|---|---|---|---|---|
| 1 | nothing: the first guess (waves of 100 from wave 1, as before WP-C5) | 19.5, but only "wave 1" or "25+" | 12 | frost 0.14, cannon 2.05 |
| 2 | waves 60 +6, hit points 8 +4 a wave; cannon 5 to 3.5 damage; frost 1 to 2 | 5.5 | 0 | frost 0.39, cannon 2.56 |
| 3 | waves 50 +6, hit points 6 +3; cannon 3 damage and splash 1 to 0.75 | 7.5 | 0 | frost 0.36, cannon 1.32 |
| 4 | waves 40 +5, enemies 0.4 s apart (was 0.2), hit points 8 +3 | 18.5 | 3 | frost 0.35, brazier 1.20 |
| 5 | hit points 8 +3.5 | 11.5 | 1 | frost 0.34, brazier 1.19 |

### The hypotheses against run 5

| # | Verdict | What the table says |
|---|---|---|
| H1 | **Fails, for one tower.** | Archer 1.00, ballista 1.15, cannon 0.94, brazier 1.19: within a fifth of each other. Frost is 0.34. Frost is the slowing tower, and the measure counts damage only: it gives frost nothing for the damage other towers do to an enemy it slowed. So the measure is wrong for frost, and whether frost is worth its price is not answered here. |
| H2 | Holds. | `idle` loses in wave 1 on every seed. |
| H3 | **Fails, once in 30.** | `mixed` on seed 6 ends wave 25 with the core at 10 of 10. (Run 1 had 12 such runs, run 4 three.) |
| H4 | Holds. | Every tower is between 17.5 % and 22.6 % of purchases. |
| H5 | Holds. | `sentry` loses in wave 11.5 at the median and survives all 25 on one seed of ten. |
| H6 | Holds. | Every kind of wave of the development planner gets enemies through (0.9 % to 4.4 % of them) and loses most. |

Two of six fail. I stopped tuning here on purpose: this is a sanity check, the remaining
failures are small or are the measure's own, and five rounds of nudging numbers against
scripted players is already more fitting than those players deserve. The second
semester's balance pass has the pilot's human players to tune against.

### What else the runs showed

- **Before WP-C5 the game was "lose in wave 1 or never".** Run 1: of 30 runs, 12 won
  untouched and 9 lost in wave 1. A first wave of 100 enemies a fifth of a second apart is
  a question of whether 60 budget of single-target towers can kill five enemies a second;
  after that, income outgrew the waves. The fix was fewer, slower-coming enemies early and
  faster-growing hit points later, not weaker towers.
- **The shop's first roll decides too much.** `sentry` on seeds 6 and 8 loses in wave 1 in
  every one of the five runs, with four towers bought. That is the scripted player's
  placement as much as the shop, but a human will meet the same first minute. Worth
  watching in the pilot; a fixed first offer would be a design change (GDD).
- **Flyers end most runs against the development planner in wave 3**, when the shop has
  not offered a tower that can hit them. The director will not do this (it may not send
  flyers a player cannot answer, I5); the development planner has no such rule.
- **Income depends on the number of enemies** (0.2 a kill), so the smaller early waves pay
  less: a cleared wave 1 pays 8 where it paid 20. This is S14 again, and it is still open.
- **The headless numbers and the test numbers are two things now.** `Maps/SimConfig.json`
  holds the game's numbers (exported from the config asset); `SimConfig`'s C# defaults are
  the old numbers, which several hundred test expectations are written against, and the
  pinned replays are recorded with them. `thesis run` and `thesis balance` read the file.
