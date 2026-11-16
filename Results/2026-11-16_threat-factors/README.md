# The cost table's class factors, measured (2026-11-16)

Written by `thesis threat` (WORKPLAN WP9, spec gap S6). Reproduce with:

```
dotnet run --project Tools/dotnet/Thesis.Cli -c Release -- threat --seeds 20 --waves 3,5,7 --iterations 14
```

## Hypothesis (stated before the first run)

The student decided on 2026-11-16 that an enemy costs hit points × speed × a factor for its movement class, and that
the flyer and sapper factors are measured, not guessed.

- **Flyers are harder to stop than walkers** per unit of hit points × speed: they fly over every wall, take the
  straight line, and only towers that hit flyers can shoot them. Expected: flyer factor above 1.
- **Sappers are about as hard to stop as walkers** on these boards: the scripted player builds walls only under
  its towers, so there is little for a sapper to dig through, and sappers are slower. Expected: sapper factor
  between 0.67 and 1.5.
- **Inconclusive** if any search hits its bound (a strength outside 1/16 to 16): then the factor is not measured.

## Method

- **Boards.** `BuilderPolicy` plays the game's own rules (`Maps/SimConfig.json`, `Maps/Towers.json`) against the static
  baseline on the map the game generates from each seed (`GameMaps`, 48 × 48). At the build phase of waves 3, 5, 7, after the player has built, the board is copied. Seeds 1 to 20: 59 boards (wave 3: 20, wave 5: 20, wave 7: 19; a player who has lost gives no later boards).
- **Probe.** On a copy, the planned wave is dropped and one wave of a single type is sent from the baseline's entry,
  priced at the wave's budget × a strength (walkers, sappers and flyers are made from the baseline's walker with
  their `EnemyArchetype`, fitted to the price as the strategies are). The copy's core cannot fall, so every enemy
  that gets through is counted. Leak share = enemies that reached the core ÷ enemies sent.
- **Half-leak strength.** For each type, bisection on log₂ strength in [−4, 4] (14 steps) for the strength at which the mean leak share over all boards is 0.5.
- **Factor** = the walkers' half-leak strength ÷ the type's. A type that gets half through on half the walkers'
  budget has factor 2.

## Result

| Type | Mean leak share at strength 1 | Half-leak strength | Leak at that strength | Factor |
|---|---|---|---|---|
| walker | 0.065 | 3.447 | 0.503 | 1 (reference) |
| sapper | 0.105 | 2.644 | 0.500 | 1.304 |
| flyer | 0.381 | 1.467 | 0.501 | 2.350 |

Bisection steps (strength, mean leak share):

- walker: (0.063, 0.000) (16.000, 0.808) (1.000, 0.065) (4.000, 0.556) (2.000, 0.283) (2.828, 0.417) (3.364, 0.487) (3.668, 0.521) (3.513, 0.504) (3.437, 0.496) (3.475, 0.505) (3.456, 0.505) (3.447, 0.496) (3.451, 0.505) (3.449, 0.503) (3.448, 0.503)
- sapper: (0.063, 0.000) (16.000, 0.901) (1.000, 0.105) (4.000, 0.657) (2.000, 0.376) (2.828, 0.530) (2.378, 0.453) (2.594, 0.495) (2.709, 0.513) (2.650, 0.503) (2.622, 0.498) (2.636, 0.500) (2.643, 0.500) (2.647, 0.500) (2.645, 0.500) (2.644, 0.500)
- flyer: (0.063, 0.000) (16.000, 0.830) (1.000, 0.381) (4.000, 0.726) (2.000, 0.589) (1.414, 0.492) (1.682, 0.540) (1.542, 0.515) (1.477, 0.501) (1.445, 0.497) (1.461, 0.498) (1.469, 0.501) (1.465, 0.499) (1.467, 0.499) (1.468, 0.501) (1.467, 0.501)

Run time 15 s on this machine.

## Deviations

- Factors are compared where half a wave gets through, not at the normal budget: a player still alive at these
  waves lets almost nothing through at strength 1, and a share near 0 says little.
- One scripted player, one entry (the baseline's), three wave numbers. The factors hold for boards like these; a
  human who mazes more would give sappers more to dig through.

## Stability check (run after the result above, not part of the plan)

The same command with `--seeds 40` (116 boards): walker half-leak strength 3.293, sapper 2.634, flyer 1.513, so a
sapper factor of **1.250** and a flyer factor of **2.177**. The 20-seed factors are within 4 % (sapper) and 8 % (flyer)
of these. The table takes the planned 20-seed numbers, rounded: **flyer 2.35, sapper 1.30**. Both hypotheses hold on
either seed set. The balance pass (S2 W4) measures again on the final game.
