# The cost table's class factors, measured (2026-11-24)

Written by `thesis threat` (WORKPLAN WP9, spec gap S6). Reproduce with:

```
dotnet run --project Tools/dotnet/Thesis.Cli -c Release -- threat --seeds 40 --waves 3,5,7 --iterations 14
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
  baseline on the map the game generates from each seed (`GameMaps`, 48 × 48). At the build phase of waves 3, 5, 7, after the player has built, the board is copied. Seeds 1 to 40: 97 boards (wave 3: 40, wave 5: 38, wave 7: 19; a player who has lost gives no later boards).
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
| walker | 0.205 | 1.917 | 0.498 | 1 (reference) |
| sapper | 0.232 | 1.737 | 0.499 | 1.104 |
| flyer | 0.611 | 0.773 | 0.505 | 2.481 |

Bisection steps (strength, mean leak share):

- walker: (0.063, 0.000) (16.000, 0.914) (1.000, 0.205) (4.000, 0.745) (2.000, 0.519) (1.414, 0.364) (1.682, 0.443) (1.834, 0.480) (1.915, 0.497) (1.957, 0.514) (1.936, 0.503) (1.926, 0.502) (1.920, 0.501) (1.918, 0.500) (1.917, 0.497) (1.917, 0.498)
- sapper: (0.063, 0.000) (16.000, 0.944) (1.000, 0.232) (4.000, 0.777) (2.000, 0.562) (1.414, 0.409) (1.682, 0.480) (1.834, 0.525) (1.756, 0.504) (1.719, 0.494) (1.737, 0.501) (1.728, 0.497) (1.733, 0.498) (1.735, 0.498) (1.736, 0.499) (1.737, 0.499)
- flyer: (0.063, 0.000) (16.000, 0.976) (1.000, 0.611) (0.250, 0.095) (0.500, 0.310) (0.707, 0.473) (0.841, 0.538) (0.771, 0.496) (0.805, 0.531) (0.788, 0.510) (0.780, 0.519) (0.775, 0.505) (0.773, 0.505) (0.772, 0.496) (0.773, 0.498) (0.773, 0.505)

Run time 32 s on this machine.

## Deviations

- Factors are compared where half a wave gets through, not at the normal budget: a player still alive at these
  waves lets almost nothing through at strength 1, and a share near 0 says little.
- One scripted player, one entry (the baseline's), three wave numbers. The factors hold for boards like these; a
  human who mazes more would give sappers more to dig through.
