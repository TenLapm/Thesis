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
| sentry | 24 | 20 | 25+ (10) | 22 | 10 | 1 | 17 | 1 | 21 | 16 | 18.5 |
| mixed | 5 | 24 | 25+ (10) | 3 | 2 | 15 | 25+ (10) | 2 | 16 | 24 | 15.5 |
| restless | 17 | 9 | 18 | 21 | 12 | 16 | 25+ (4) | 19 | 18 | 18 | 18 |
| idle | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 |

Towers, all policies and seeds together. *Value* is damage per budget per minute on the board; the last column is that value over the median of the roster.

| Tower | Bought | Share of purchases | Budget spent | Damage | Kills | Value | Value / median |
|---|---|---|---|---|---|---|---|
| archer | 300 | 21.6 % | 3000 | 321860 | 9616 | 16.7 | 1.00 |
| ballista | 261 | 18.8 % | 4176 | 428682 | 14836 | 18.6 | 1.12 |
| cannon | 267 | 19.2 % | 4806 | 440386 | 4303 | 15.0 | 0.90 |
| brazier | 263 | 18.9 % | 3156 | 404176 | 7381 | 20.1 | 1.20 |
| frost | 297 | 21.4 % | 3564 | 137164 | 3692 | 5.8 | 0.35 |

### Against `class-cycle`

Wave each player lost in, by seed (`25+` = still alive after wave 25, with the core's health).

| Policy | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | Median |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sentry | 13 | 14 | 3 | 15 | 15 | 1 | 5 | 1 | 5 | 16 | 9 |
| mixed | 2 | 9 | 3 | 4 | 15 | 5 | 2 | 5 | 14 | 5 | 5 |
| restless | 4 | 15 | 3 | 16 | 3 | 10 | 5 | 15 | 5 | 4 | 5 |
| idle | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 |

Towers, all policies and seeds together. *Value* is damage per budget per minute on the board; the last column is that value over the median of the roster.

| Tower | Bought | Share of purchases | Budget spent | Damage | Kills | Value | Value / median |
|---|---|---|---|---|---|---|---|
| archer | 159 | 25.1 % | 1590 | 81251 | 4831 | 15.7 | 1.29 |
| ballista | 88 | 13.9 % | 1408 | 47236 | 3618 | 12.2 | 1.00 |
| cannon | 121 | 19.1 % | 2178 | 80408 | 1718 | 11.4 | 0.94 |
| brazier | 132 | 20.9 % | 1584 | 81772 | 3119 | 16.5 | 1.35 |
| frost | 133 | 21.0 % | 1596 | 20507 | 1265 | 4.2 | 0.35 |

What each kind of wave got through, all policies and seeds together.

| Wave of the cycle | Waves played | Spawned | Killed | Reached the core | Share that reached the core |
|---|---|---|---|---|---|
| basic | 64 | 3510 | 3043 | 135 | 3.8 % |
| sappers | 50 | 1506 | 1460 | 41 | 2.7 % |
| flyers | 48 | 1594 | 1504 | 58 | 3.6 % |
| swarm | 43 | 6230 | 6050 | 57 | 0.9 % |
| basic + sappers + flyers | 37 | 2827 | 2494 | 109 | 3.9 % |
