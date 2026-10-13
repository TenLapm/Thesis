Map SampleScene 24x24, 5 towers, seeds 1 to 10, up to 25 waves.

### The roster

| Tower | Price | Type | Damage | Every | Range | Splash | Slow | Flyers | Damage per second per budget (one target) |
|---|---|---|---|---|---|---|---|---|---|
| archer | 10 | physical | 4 | 0.5 s | 3.5 | - | - | yes | 0.80 |
| ballista | 16 | physical | 22 | 2 s | 5.5 | - | - | yes | 0.69 |
| cannon | 18 | fire | 5 | 1.2 s | 2.5 | 1 | - | no | 0.23 |
| brazier | 12 | fire | 1.2 | 0.1 s | 1.5 | - | - | no | 1.00 |
| frost | 12 | frost | 1 | 0.6 s | 2.5 | - | x0.5 for 1.5 s | no | 0.14 |

### Against `escalation`

Wave each player lost in, by seed (`25+` = still alive after wave 25, with the core's health).

| Policy | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | Median |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sentry | 25+ (10) | 1 | 25+ (10) | 25+ (10) | 5 | 1 | 25+ (10) | 1 | 25+ (10) | 13 | 19.5 |
| mixed | 3 | 1 | 25+ (10) | 25+ (10) | 10 | 1 | 5 | 1 | 25+ (10) | 25+ (10) | 7.5 |
| restless | 2 | 1 | 25+ (10) | 25+ (10) | 5 | 1 | 19 | 1 | 14 | 25+ (10) | 9.5 |
| idle | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 |

Towers, all policies and seeds together. *Value* is damage per budget per minute on the board; the last column is that value over the median of the roster.

| Tower | Bought | Share of purchases | Budget spent | Damage | Kills | Value | Value / median |
|---|---|---|---|---|---|---|---|
| archer | 268 | 19.1 % | 2680 | 243227 | 12342 | 14.6 | 1.00 |
| ballista | 268 | 19.1 % | 4288 | 353701 | 20252 | 14.1 | 0.97 |
| cannon | 315 | 22.4 % | 5670 | 1052137 | 13527 | 29.9 | 2.05 |
| brazier | 256 | 18.2 % | 3072 | 316038 | 9069 | 17.1 | 1.17 |
| frost | 299 | 21.3 % | 3588 | 45704 | 2494 | 2.0 | 0.14 |

### Against `class-cycle`

Wave each player lost in, by seed (`25+` = still alive after wave 25, with the core's health).

| Policy | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | Median |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sentry | 8 | 1 | 3 | 3 | 25+ (4) | 1 | 3 | 1 | 3 | 15 | 3 |
| mixed | 2 | 1 | 3 | 3 | 3 | 1 | 3 | 1 | 3 | 3 | 3 |
| restless | 2 | 1 | 3 | 3 | 3 | 1 | 3 | 1 | 3 | 3 | 3 |
| idle | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 |

Towers, all policies and seeds together. *Value* is damage per budget per minute on the board; the last column is that value over the median of the roster.

| Tower | Bought | Share of purchases | Budget spent | Damage | Kills | Value | Value / median |
|---|---|---|---|---|---|---|---|
| archer | 87 | 23.8 % | 870 | 35861 | 2666 | 13.9 | 1.00 |
| ballista | 46 | 12.6 % | 736 | 16440 | 1440 | 7.9 | 0.57 |
| cannon | 81 | 22.2 % | 1458 | 110240 | 3362 | 31.2 | 2.25 |
| brazier | 74 | 20.3 % | 888 | 35258 | 2209 | 16.3 | 1.17 |
| frost | 77 | 21.1 % | 924 | 4895 | 416 | 2.3 | 0.17 |

What each kind of wave got through, all policies and seeds together.

| Wave of the cycle | Waves played | Spawned | Killed | Reached the core | Share that reached the core |
|---|---|---|---|---|---|
| basic | 47 | 5050 | 3436 | 190 | 3.8 % |
| sappers | 28 | 1633 | 1561 | 20 | 1.2 % |
| flyers | 26 | 1603 | 1075 | 178 | 11.1 % |
| swarm | 9 | 2720 | 2720 | 0 | 0.0 % |
| basic + sappers + flyers | 9 | 1399 | 1301 | 8 | 0.6 % |
