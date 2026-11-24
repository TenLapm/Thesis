# The director's ranges and board groups, widened after the first play-test

Date: 2026-11-24

Commands:

```
dotnet run --project Tools/dotnet/Thesis.Cli -c Release -- ladder --seeds 20 --waves 50 --out Runs/ladder-profiles --profiles Runs/ladder-profiles/profiles.csv
python Results/2026-11-24_director-ranges/ranges.py
```

## Hypothesis

The student played the heuristic director for 11 waves (`DirectorTest`). The log
(`student-playtest-telemetry.jsonl`) shows it alternating between swarm and split. The reason is that three of the four
scores sat at their maximum of 4. The ranges they are scaled over were measured on the scripted player against the
static waves only, and a person's board lies outside them. Ranges measured over all three kinds of player should
spread a person's board over the scale.

## Method

For each feature, take its 5 % and 95 % points for `builder`, for `reactive` (both on the ladder, all four rungs,
seeds 201 to 220, up to 50 waves) and for the student's session (waves 2 to 10). The new range runs from the lowest
5 % point to the highest 95 % point, plus a tenth of its span at the top, so that no board sits fixed at 4;
`tower_concentration` is capped at 1, its largest value. The board groups (the learner's context, cut on
`chokepoint_reliance`) are the 20/40/60/80 % points with each of the three players weighted alike.

## Result

| feature | builder 5–95 % | reactive 5–95 % | student 5–95 % | old range | new range |
|---|---|---|---|---|---|
| maze_length | 1.048–1.262 | 1.049–1.281 | 1.354 (every wave) | 1.056–1.309 | **1.048–1.385** |
| breach_vulnerability | 0–0 | 0–0.047 | 0 | 0–0.067 | **0–0.052** |
| chokepoint_reliance | 0.033–0.637 | 0.039–0.620 | 0.206–0.810 | 0.017–0.339 | **0.033–0.888** |
| tower_concentration | 0.740–0.908 | 0.740–0.925 | 0.969–0.985 | 0.763–0.933 | **0.740–1.000** |

Board groups: **0.125 / 0.224 / 0.352 / 0.484** (were 0.06 / 0.10 / 0.14 / 0.22).

The student also changed two bans at the same time:
- flyers are banned without anti-air up to wave 5 only (B3)
- a wave like the last one is no longer banned (B4)

Together, on the ladder (`Results/2026-11-24_ladder-new-rules`), the full director now comes out above the
heuristic for the reacting player (0.253 against 0.239; before, 0.249 against 0.252). Random now comes out above the
static version (0.221 against 0.215), so the ladder still fails, one rung lower. In four sample runs of the full
director against `reactive`, every strategy was chosen at least once and repeats occurred.

## Limits

- One play-test of one person, 9 readings. The threshold freeze replaces all of this with the pilot's data (I6).
- `breach_vulnerability` is 0 on nearly every board: the breach strategy scores 0 unless the route depends on walls.
