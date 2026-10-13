Map SampleScene 24x24, 5 towers, seeds 1 to 10, up to 25 waves.

### The roster

| Tower | Price | Type | Damage | Every | Range | Splash | Slow | Flyers | Damage per second per budget (one target) |
|---|---|---|---|---|---|---|---|---|---|
| archer | 10 | physical | 4 | 0.5 s | 3.5 | - | - | yes | 0.80 |
| ballista | 16 | physical | 22 | 2 s | 5.5 | - | - | yes | 0.69 |
| cannon | 18 | fire | 3 | 1.2 s | 2.5 | 0.75 | - | no | 0.14 |
| brazier | 12 | fire | 1.2 | 0.1 s | 1.5 | - | - | no | 1.00 |
| frost | 12 | frost | 2 | 0.6 s | 2.5 | - | x0.5 for 1.5 s | no | 0.28 |

### Against `escalation`

Wave each player lost in, by seed (`25+` = still alive after wave 25, with the core's health).

| Policy | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | Median |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sentry | 6 | 7 | 18 | 10 | 5 | 1 | 11 | 1 | 8 | 15 | 7.5 |
| mixed | 3 | 4 | 4 | 5 | 4 | 2 | 3 | 2 | 9 | 5 | 4 |
| restless | 3 | 10 | 4 | 25+ (10) | 4 | 9 | 11 | 12 | 5 | 3 | 7 |
| idle | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 |

Towers, all policies and seeds together. *Value* is damage per budget per minute on the board; the last column is that value over the median of the roster.

| Tower | Bought | Share of purchases | Budget spent | Damage | Kills | Value | Value / median |
|---|---|---|---|---|---|---|---|
| archer | 135 | 21.7 % | 1350 | 75741 | 4324 | 16.5 | 1.00 |
| ballista | 103 | 16.6 % | 1648 | 81467 | 4091 | 15.0 | 0.91 |
| cannon | 118 | 19.0 % | 2124 | 160348 | 2830 | 21.8 | 1.32 |
| brazier | 126 | 20.3 % | 1512 | 93508 | 3232 | 18.2 | 1.11 |
| frost | 139 | 22.4 % | 1668 | 25739 | 1540 | 5.2 | 0.32 |

### Against `class-cycle`

Wave each player lost in, by seed (`25+` = still alive after wave 25, with the core's health).

| Policy | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | Median |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sentry | 8 | 10 | 3 | 9 | 5 | 1 | 3 | 1 | 3 | 15 | 4 |
| mixed | 2 | 4 | 3 | 4 | 4 | 14 | 2 | 15 | 3 | 5 | 4 |
| restless | 5 | 4 | 3 | 12 | 3 | 10 | 5 | 14 | 3 | 4 | 4.5 |
| idle | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 |

Towers, all policies and seeds together. *Value* is damage per budget per minute on the board; the last column is that value over the median of the roster.

| Tower | Bought | Share of purchases | Budget spent | Damage | Kills | Value | Value / median |
|---|---|---|---|---|---|---|---|
| archer | 127 | 25.0 % | 1270 | 42731 | 3828 | 13.5 | 1.09 |
| ballista | 67 | 13.2 % | 1072 | 24182 | 2145 | 9.5 | 0.77 |
| cannon | 94 | 18.5 % | 1692 | 79800 | 2644 | 22.0 | 1.78 |
| brazier | 113 | 22.2 % | 1356 | 37328 | 2129 | 12.3 | 1.00 |
| frost | 108 | 21.2 % | 1296 | 9997 | 853 | 4.1 | 0.33 |

What each kind of wave got through, all policies and seeds together.

| Wave of the cycle | Waves played | Spawned | Killed | Reached the core | Share that reached the core |
|---|---|---|---|---|---|
| basic | 54 | 3270 | 2717 | 123 | 3.8 % |
| sappers | 42 | 1461 | 1414 | 21 | 1.4 % |
| flyers | 39 | 1464 | 1227 | 138 | 9.4 % |
| swarm | 30 | 5040 | 4616 | 50 | 1.0 % |
| basic + sappers + flyers | 22 | 1943 | 1625 | 68 | 3.5 % |
