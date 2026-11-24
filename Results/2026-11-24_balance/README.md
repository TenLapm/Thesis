# The balance pass, first round (2026-11-24)

The student asked for a balance pass and left the targets to me ("dive in with your own targets"). The targets
below were written down before any number was changed. Every number in the game is still a first guess, and the
next tester session checks it (CLAUDE.md §1).

## Targets (stated before the first change)

1. An ordinary player first loses core health somewhere around waves 8 to 12, not in the 20s or 30s.
2. A typical game lasts 20 to 30 waves (the game has no last wave; the student wants it to go on for ever).
3. Ore is spent: a player does not sit on a large pile they have nothing to spend on.
4. No tower gives more than about 1.5 times the damage per ore of the others that do the same job.
5. The threat budget stays equal for every kind of wave (I2): the cost table's class factors are measured again
   under the new numbers.

Difficulty is still not the thesis's goal (CLAUDE.md §3). The point of the pass is that the director's waves press
a player at all: a player who is never in danger has no reason to rebuild, whatever the director sends.

## The yardstick: a tester's own boards

The scripted players are far weaker than a person (the strongest, `builder`, lasted 7 to 16 waves against the old
numbers and never rebuilds), so they cannot show whether a person is pressed. The yardstick is instead the tester's
35-wave session against the full director (`20261124-203002_seed41010671`, build `151aff6`; DEVLOG 2026-11-24). In
that session nothing reached the core until wave 29 and the tester held 155 to 175 ore from wave 20 on.

`thesis curve <replay>` (new, `BoardCurve`) plays the tester's session again with their own commands, re-timed by
build phase so that a build never lands in the middle of a wave, and makes the core unbreakable. At each wave asked
for it copies the board as the tester left it and finds two strengths by halving between 1/16 and 16:

- **met wave strength:** how many times stronger the wave the tester actually met (the director's, as recorded)
  would have to be for 10 % of it to get through. 1 means that wave already let 10 % through.
- **walkers strength:** the same for a wave of plain walkers sized like the static version's wave at that number.

Commands:

```
dotnet run --project Tools/dotnet/Thesis.Cli -c Release -- curve <replay> --config Results/2026-11-24_balance/SimConfig-before.json --waves 3,5,8,10,12,15,20,25,30 --out <dir>
dotnet run --project Tools/dotnet/Thesis.Cli -c Release -- curve <replay> --waves 3,5,8,10,12,15,20,25,30 --out <dir>
```

The tester's replay is not committed (it is a participant-like recording outside `Results/`); the two outputs are,
as `curve-before.md` and `curve-after.md`. The recording stops at wave 27 (the save failure fixed the same day), so
wave 30 has no board.

## What the old numbers did

The met-wave strength, with the old curve (hit points 30 + 4.5 a wave, then 4 % more a wave on top):

| wave | 3 | 5 | 8 | 10 | 12 | 15 | 20 | 25 |
|---|---|---|---|---|---|---|---|---|
| met wave strength | 1.54 | 3.89 | 5.10 | 7.38 | 3.86 | 3.39 | 1.20 | 1.22 |

From wave 5 to wave 15 the tester's board would have held waves three to seven times as strong as the ones they met.
The middle of the game asked nothing of them, and that is where the director's waves have to work. The 4 %
compounding then catches up late, which is why the margin falls back to about 1.2 by wave 20.

## The change

`HpIncrementPerWave` 4.5 → **14**, `HpGrowthPerWave` 0.04 → **0.01** (`Maps/SimConfig.json` and `SimConfig.asset`;
the C# defaults, which the tests are written against, are unchanged). Hit points of the static wave's walker:

| wave | 1 | 3 | 5 | 8 | 10 | 12 | 15 | 20 | 25 | 30 | 35 | 40 | 50 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| before | 30 | 42 | 56 | 81 | 100 | 122 | 161 | 243 | 354 | 501 | 694 | 949 | 1712 |
| after | 30 | 59 | 89 | 137 | 171 | 205 | 260 | 358 | 465 | 582 | 710 | 849 | 1166 |
| after ÷ before | 1.00 | 1.40 | 1.59 | 1.70 | 1.70 | 1.68 | 1.61 | 1.47 | 1.31 | 1.16 | 1.02 | 0.90 | 0.68 |

The met-wave strength divided by that ratio is the tester's margin under the new numbers (the waves' shapes stay as
the director chose them; only their hit points grow):

| wave | 3 | 5 | 8 | 10 | 12 | 15 | 20 | 25 |
|---|---|---|---|---|---|---|---|---|
| margin, before | 1.54 | 3.89 | 5.10 | 7.38 | 3.86 | 3.39 | 1.20 | 1.22 |
| margin, after | 1.10 | 2.44 | 3.01 | 4.34 | 2.30 | 2.10 | 0.82 | 0.93 |

So this tester would have started losing core health around wave 20 instead of wave 29, and wave 3 becomes close
(1.1). The walkers column (`curve-after.md`) shows the same tightening: their wave-8 to wave-12 boards, which held
walkers beyond 16 times the static wave's strength before, now let 10 % through at 3 to 4 times.

## Tower value

Damage per ore paid (tower, upgrades and gear) over the tester's session up to wave 27, towers that were sold or
destroyed on the way not counted. It is the same under both configs: the replay keeps the roster it was recorded
with.

| tower | copies | damage per ore |
|---|---|---|
| cannon | 1 | 222 |
| sniper | 1 | 218 |
| archer | 3 | 196 |
| brazier | 6 | 67 |
| flak | 5 | 37 |
| frost | 1 | 16 |

The three single-target damage towers are within 15 % of each other. The brazier gives a third as much for its ore.
The tester bought six and geared them with the old flat damage gear, which was the problem the tester reported and
which `PercentDamageGear` already changed the same day. Flak (anti-air) and frost (slowing) are not damage towers and
are not held to target 4. **No tower number was changed:** one session, one copy each of cannon and sniper, and a
gear rule that has just changed are too little to tune eight towers on.

## Threat factors measured again (target 5)

`thesis threat --seeds 40 --waves 3,5,7 --iterations 14` under the new numbers
(`Results/2026-11-24_threat-factors-balance`): flyers **2.48** (was 2.35), sappers **1.10** (was 1.30). Both are in
`ThreatCostTable.MeasuredFlyingFactor` and `MeasuredSapperFactor`. Why the sapper factor fell was not looked into;
both are measured on `builder`'s boards, which have few walls to dig through.

## The ladder under the new numbers

`thesis ladder --seeds 20 --first-seed 201 --waves 50` (`Results/2026-11-24_ladder-balance`): both scripted players
now lose in 3.4 to 5.8 waves, and the ladder does not hold for either. The verdict was FAIL before the change too
(`Results/2026-11-24_ladder`). With games this short the common window is three or four waves, too short for the
entropy to tell the rungs apart. **The ladder now needs a stronger scripted player**, not easier waves: weakening
the game so that a bot survives would undo the change for the people it is meant for.

`BuilderPolicyTests` asked `builder` to survive six waves on three of three seeds. It now loses wave 6 on seeds 2
and 3, so the bar is four waves (it still survives four on every seed). The bar was lowered, not the waves.

## Against the targets

| target | met? |
|---|---|
| 1. first core loss around waves 8 to 12 | **Not shown.** For this tester it moves from wave 29 to about 20. They are a strong player (ten towers by wave 8, nothing through until wave 29); a weaker one, whose margin is smaller, would lose health earlier. A session by a less practised tester is needed to check it. |
| 2. a game lasts 20 to 30 waves | **Not shown,** for the same reason. Past wave 35 the new curve is gentler than the old one (0.68 of it at wave 50), so a player who gets that far lasts longer than before. |
| 3. ore is spent | **Not met; not changed.** Hit points do not change income. The tester's pile (175 at wave 20) needs something to spend it on, which is a design question for the student (a money sink; the tester's research-tree idea). |
| 4. tower value within 1.5× | Met among the single-target damage towers (1.13×). The brazier was not; its gear changed the same day, so it is to be measured again. |
| 5. equal threat budget | Met: both factors measured again and in the table. |

## Limits

- One player, one seed, one session. The margins hold for boards like theirs.
- The tester built against the old waves. Under harder waves they would have built differently and probably sooner.
  The curve shows how far their boards were from being pressed, not how they would have played.
- The waves are re-timed by build phase. A command the tester gave in the middle of a wave is applied at the next
  build phase, and a retry is dropped.
- The margins under the new numbers come from dividing by the hit-point ratio. That assumes a wave's danger grows
  with its hit points alone; speed and count did not change, so the assumption only concerns how towers' damage adds
  up, which is close to linear for a board that does not leak.
