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
| sentry | 18 | 12 | 25+ (3) | 22 | 10 | 1 | 11 | 1 | 11 | 16 | 11.5 |
| mixed | 5 | 15 | 16 | 3 | 2 | 25+ (10) | 10 | 2 | 25 | 16 | 12.5 |
| restless | 5 | 8 | 16 | 21 | 3 | 20 | 4 | 11 | 18 | 11 | 11 |
| idle | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 |

Towers, all policies and seeds together. *Value* is damage per budget per minute on the board; the last column is that value over the median of the roster.

| Tower | Bought | Share of purchases | Budget spent | Damage | Kills | Value | Value / median |
|---|---|---|---|---|---|---|---|
| archer | 219 | 21.3 % | 2190 | 219405 | 6653 | 17.9 | 1.00 |
| ballista | 180 | 17.5 % | 2880 | 321188 | 10392 | 20.6 | 1.15 |
| cannon | 212 | 20.6 % | 3816 | 326372 | 3104 | 16.9 | 0.94 |
| brazier | 185 | 18.0 % | 2220 | 272848 | 5203 | 21.3 | 1.19 |
| frost | 232 | 22.6 % | 2784 | 91024 | 2572 | 6.1 | 0.34 |

### Against `class-cycle`

Wave each player lost in, by seed (`25+` = still alive after wave 25, with the core's health).

| Policy | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | Median |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sentry | 7 | 10 | 3 | 12 | 12 | 1 | 5 | 1 | 5 | 16 | 6 |
| mixed | 2 | 9 | 3 | 4 | 14 | 5 | 2 | 16 | 10 | 5 | 5 |
| restless | 4 | 10 | 3 | 15 | 3 | 5 | 4 | 15 | 5 | 4 | 4.5 |
| idle | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 |

Towers, all policies and seeds together. *Value* is damage per budget per minute on the board; the last column is that value over the median of the roster.

| Tower | Bought | Share of purchases | Budget spent | Damage | Kills | Value | Value / median |
|---|---|---|---|---|---|---|---|
| archer | 143 | 25.0 % | 1430 | 77118 | 4314 | 17.3 | 1.28 |
| ballista | 75 | 13.1 % | 1200 | 41786 | 2867 | 12.7 | 0.94 |
| cannon | 109 | 19.1 % | 1962 | 72489 | 1350 | 13.5 | 1.00 |
| brazier | 124 | 21.7 % | 1488 | 68914 | 2745 | 15.7 | 1.17 |
| frost | 120 | 21.0 % | 1440 | 16662 | 996 | 4.6 | 0.34 |

What each kind of wave got through, all policies and seeds together.

| Wave of the cycle | Waves played | Spawned | Killed | Reached the core | Share that reached the core |
|---|---|---|---|---|---|
| basic | 61 | 3240 | 2702 | 142 | 4.4 % |
| sappers | 47 | 1365 | 1295 | 50 | 3.7 % |
| flyers | 42 | 1307 | 1226 | 51 | 3.9 % |
| swarm | 38 | 5230 | 4950 | 46 | 0.9 % |
| basic + sappers + flyers | 32 | 2356 | 2099 | 112 | 4.8 % |
