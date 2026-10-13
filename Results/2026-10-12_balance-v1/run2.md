Map SampleScene 24x24, 5 towers, seeds 1 to 10, up to 25 waves.

### The roster

| Tower | Price | Type | Damage | Every | Range | Splash | Slow | Flyers | Damage per second per budget (one target) |
|---|---|---|---|---|---|---|---|---|---|
| archer | 10 | physical | 4 | 0.5 s | 3.5 | - | - | yes | 0.80 |
| ballista | 16 | physical | 22 | 2 s | 5.5 | - | - | yes | 0.69 |
| cannon | 18 | fire | 3.5 | 1.2 s | 2.5 | 1 | - | no | 0.16 |
| brazier | 12 | fire | 1.2 | 0.1 s | 1.5 | - | - | no | 1.00 |
| frost | 12 | frost | 2 | 0.6 s | 2.5 | - | x0.5 for 1.5 s | no | 0.28 |

### Against `escalation`

Wave each player lost in, by seed (`25+` = still alive after wave 25, with the core's health).

| Policy | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | Median |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sentry | 7 | 1 | 18 | 9 | 6 | 1 | 5 | 1 | 5 | 12 | 5.5 |
| mixed | 3 | 4 | 4 | 12 | 4 | 1 | 3 | 1 | 8 | 3 | 3.5 |
| restless | 2 | 6 | 4 | 7 | 2 | 1 | 3 | 1 | 6 | 3 | 3 |
| idle | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 |

Towers, all policies and seeds together. *Value* is damage per budget per minute on the board; the last column is that value over the median of the roster.

| Tower | Bought | Share of purchases | Budget spent | Damage | Kills | Value | Value / median |
|---|---|---|---|---|---|---|---|
| archer | 100 | 23.4 % | 1000 | 35816 | 2691 | 15.1 | 1.00 |
| ballista | 66 | 15.4 % | 1056 | 31346 | 2054 | 12.2 | 0.81 |
| cannon | 79 | 18.5 % | 1422 | 146017 | 2701 | 38.8 | 2.56 |
| brazier | 89 | 20.8 % | 1068 | 56147 | 2072 | 21.6 | 1.43 |
| frost | 94 | 22.0 % | 1128 | 15944 | 1090 | 5.9 | 0.39 |

### Against `class-cycle`

Wave each player lost in, by seed (`25+` = still alive after wave 25, with the core's health).

| Policy | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | Median |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sentry | 7 | 1 | 3 | 3 | 5 | 1 | 3 | 1 | 3 | 14 | 3 |
| mixed | 2 | 4 | 3 | 3 | 4 | 1 | 2 | 1 | 3 | 3 | 3 |
| restless | 3 | 4 | 3 | 3 | 3 | 1 | 3 | 1 | 3 | 6 | 3 |
| idle | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 |

Towers, all policies and seeds together. *Value* is damage per budget per minute on the board; the last column is that value over the median of the roster.

| Tower | Bought | Share of purchases | Budget spent | Damage | Kills | Value | Value / median |
|---|---|---|---|---|---|---|---|
| archer | 70 | 24.6 % | 700 | 14218 | 1366 | 12.7 | 1.00 |
| ballista | 31 | 10.9 % | 496 | 7412 | 713 | 8.4 | 0.66 |
| cannon | 59 | 20.7 % | 1062 | 39538 | 1535 | 25.0 | 1.97 |
| brazier | 63 | 22.1 % | 756 | 18089 | 1009 | 15.9 | 1.25 |
| frost | 62 | 21.8 % | 744 | 4702 | 393 | 4.5 | 0.35 |

What each kind of wave got through, all policies and seeds together.

| Wave of the cycle | Waves played | Spawned | Killed | Reached the core | Share that reached the core |
|---|---|---|---|---|---|
| basic | 44 | 2790 | 1982 | 178 | 6.4 % |
| sappers | 26 | 918 | 834 | 53 | 5.8 % |
| flyers | 23 | 873 | 587 | 122 | 14.0 % |
| swarm | 9 | 1584 | 1209 | 35 | 2.2 % |
| basic + sappers + flyers | 5 | 449 | 404 | 12 | 2.7 % |
