# A tester's 77 waves, 2026-10-28

**Session** `20261028-091845_seed1084692328`, played in the Unity editor on commit `071db3f`
(planner `rough-director`). The telemetry is here; the replay (5.6 MB) is not committed: it only
replays on `071db3f`'s rules.

**How it was read.** A script over `telemetry.jsonl`: each wave's plan, outcome, ore and the build log.

## What it showed

| Finding | Evidence |
|---|---|
| No pressure for 66 waves | core HP 9-10 from wave 1 to wave 66; every enemy killed until wave 67 |
| A runaway economy | 28 collectors built, none lost; income about +95 ore a wave by wave 30; up to 877 ore banked; 110 towers bought |
| A late cliff | enemy HP compounding 8 % a wave: 29 at wave 10, 625 at 50, 2,914 at 77; lost at wave 77 |
| A director that answered nothing | raids on waves 7, 9 and 11 only (1-2 raiders); "baseline" from wave 12 on. It looked only at towers next to the core, and this player spread them |
| Fire did ~90 % of the damage from wave 25 | 101 keen edges crafted (+3 damage a hit then), mostly on braziers (ten hits a second) and flak |

## What was changed (DEVLOG 2026-10-28)

- Damage gear adds damage a second, not a hit (already done that morning).
- Deposits run out: 50 ore each (`SimConfig.DepositOre`).
- Enemy HP: 30 + 4.5 a wave, then +4 % a wave compounded (was 24, +8 %): about twice as hard
  from wave 10 to 40, the same by wave 70, a little gentler after.
- The rough director scores each weakness on its own: bare collectors (raid), the densest cluster
  anywhere (siege), one damage type relied on (resistant enemies), weak anti-air (flyers; never
  when nothing can hit them).

Not calibrated against a player yet: the next tester session is the check.
