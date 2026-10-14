# Tower balance, 2026-10-14

**Why.** A tester reached wave 60 and reported that a board of ballistas beat three fully
upgraded and geared towers of any other kind, and that the powder gear (+0.5 splash) made
every single-target tower too strong. The student asked for a measured rebalance.

**Hypothesis, stated before the tables.** With the numbers of commit `04871e1` the ballista
is the strongest tower at every stage, and cannon, brazier and frost are far behind; numbers
can be found that bring the single towers within a narrow band without any one leading at
every stage.

**Method.** `thesis towers --gauntlet` (Tools/dotnet/Thesis.Cli/TowersCmd.cs). A lane one
tile wide and 40 long, rock on both sides. As many towers of one type as the tower cap
allows at that wave (4, plus 1 every 3 waves) stand on the rock beside the last tiles before
the core, two to a column, the way a player stacks them there. One wave of ONE enemy type at
the strength of wave *w* (`EscalationPlanner` × `EnemyArchetype`) walks the lane, and the
table gives the share of it killed, averaged over the ten enemy types. No upgrades and no
gear, except in the two runs that give every tower one upgrade.

The game's cards are a free draft, so a tower costs a pick and a place under the cap, not
budget: the comparison is per tower, not per gold.

**What it does not measure.** Mazes (the lane is straight), the player's choices, gear,
wave mixes. The scripted players were tried first (`thesis towers` without `--gauntlet`) and
are no yardstick: they put towers on walls in the enemies' path, lose them to digging, and
die in wave 2 on every map. Absolute numbers are low (a real board has upgrades, gear and a
maze); only the comparison between rows means anything.

**Rule change made on the way.** Only the first bolt of a shot splashes
(`TowerSystem.Step`). Enemies walk in file, so a splash hits several; with one splash per
bolt the Volley upgrade made a splash tower about twice what it was (cannon 58 % and
brazier 62 % at wave 30 with Volley, against 14 % and 15 % without).

## Commands

```
dotnet run -c Release --project Tools/dotnet/Thesis.Cli -- towers --gauntlet --towers Results/2026-10-14_tower-balance/before/Towers-before.json --pairs cannon+frost,ballista+frost --out Results/2026-10-14_tower-balance/before
dotnet run -c Release --project Tools/dotnet/Thesis.Cli -- towers --gauntlet --pairs cannon+frost,ballista+frost,sniper+frost --out Results/2026-10-14_tower-balance
dotnet run -c Release --project Tools/dotnet/Thesis.Cli -- towers --gauntlet --upgrade power --out Results/2026-10-14_tower-balance/power
dotnet run -c Release --project Tools/dotnet/Thesis.Cli -- towers --gauntlet --upgrade volley --out Results/2026-10-14_tower-balance/volley
```

## Before (the roster of `04871e1`)

| Set | wave 5 | wave 15 | wave 30 | wave 45 | wave 60 |
|---|---|---|---|---|---|
| archer | 71 % | 26 % | 8 % | 8 % | 3 % |
| ballista | 82 % | 40 % | 27 % | 20 % | 15 % |
| cannon | 46 % | 7 % | 2 % | 1 % | 1 % |
| brazier | 52 % | 4 % | 1 % | 1 % | 0 % |
| frost | 15 % | 0 % | 0 % | 0 % | 0 % |
| sniper | 70 % | 38 % | 15 % | 14 % | 10 % |
| shard | 70 % | 17 % | 4 % | 2 % | 2 % |
| flak | 71 % | 32 % | 20 % | 16 % | 14 % |
| cannon+frost | 39 % | 7 % | 1 % | 0 % | 0 % |
| ballista+frost | 66 % | 21 % | 13 % | 6 % | 7 % |

The hypothesis's first half holds: the ballista leads at every wave, cannon, brazier and frost
are near zero from wave 15, and frost in a pair makes the pair worse.

## After

| Tower | Was | Now |
|---|---|---|
| ballista | 22 dmg, range 5.5, hits flyers | 32 dmg, range 5, **ground only** |
| sniper | 45 dmg | 40 dmg |
| archer | 4 dmg, range 3.5 | 6 dmg, range 4 |
| cannon | 3 dmg, range 2.5 | 6 dmg, range 3 (splash 0.75) |
| brazier | 1.2 dmg, range 1.5, no splash | 0.8 dmg, range 2, splash 0.5 |
| shard | 3 dmg, range 3 | 5 dmg, range 3.5 |
| frost | 2 dmg, slow to 50 %, one target | 0.5 dmg, slow to 40 %, splash 1 (slows an area) |
| flak | 2 dmg | 2.5 dmg |

Fire intervals and everything else unchanged. The tables are in `gauntlet.md`,
`power/gauntlet.md` and `volley/gauntlet.md`.

| Set | wave 5 | wave 15 | wave 30 | wave 45 | wave 60 |
|---|---|---|---|---|---|
| archer | 93 % | 37 % | 19 % | 13 % | 10 % |
| ballista | 72 % | 41 % | 16 % | 13 % | 8 % |
| cannon | 70 % | 41 % | 14 % | 12 % | 11 % |
| brazier | 64 % | 55 % | 15 % | 13 % | 12 % |
| frost | 66 % | 28 % | 10 % | 5 % | 3 % |
| sniper | 67 % | 37 % | 13 % | 14 % | 10 % |
| shard | 70 % | 54 % | 19 % | 11 % | 5 % |
| flak | 85 % | 37 % | 26 % | 22 % | 21 % |

**Reading.** The damage towers now sit within 37–55 % at wave 15 and 13–26 % at wave 30,
where before the spread was 4–40 % and 1–27 %. No tower leads everywhere: shard and brazier
lead early, flak late (it is the one with splash that also hits flyers). Frost is a support:
weak alone, and with a splash tower the slow bunches enemies into the blast (cannon+frost
44 % at wave 30 against 14 % for cannon alone); with single-target towers it adds nothing.
The enemy types now counter towers: armored takes 2–4 % from physical towers, ember nothing
from fire towers, frostborn nothing from frost towers (`gauntlet.md`, the per-type table).

**Open.** These are first numbers from a straight lane. They say nothing about the late game
a real player reaches with mazes, upgrades and gear, and the first play test with them should
say whether any tower is still the obvious pick. Flak may now be the strongest late; it is the
next one to watch.
