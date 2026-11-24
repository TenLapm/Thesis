# Gate G3: the graduated reward beats the binary one

Date: 2026-11-24

Command:

```
dotnet run --project Tools/dotnet/Thesis.Cli -c Release -- gate g3
```

## Hypothesis

With the graduated reward (0.45 restructure + 0.25 survived + 0.20 pressure + 0.10 novelty), the learner reaches G1's accuracy (85 % best-strategy picks over 50 episodes in a row) **in fewer episodes** than with the binary control (`restructure >= tau && survived`), comparing the medians over 20 seeds.

**The problem** (fixed before the run): the threshold problem of G1 decides which strategy is best; a pull's reward is built like the director's: the player's rebuild is drawn evenly between 0 and twice its mean, 0.14 for the best strategy and 0.08 for the others (the ladder's reacting player rebuilt 0.11 a wave against the directors); survival (0.85), pressure and novelty are the same noise for every strategy; tau = 0.2. Runs stop at 3000 episodes.

**Fail** (no difference, or the binary control is faster): ship the binary reward, and report it (CLAUDE.md §6).

## Result, per seed (episodes to reach the accuracy)

| seed | graduated | binary |
|---|---|---|
| 1 | not reached | 604 |
| 2 | 2059 | 502 |
| 3 | not reached | 738 |
| 4 | not reached | 651 |
| 5 | not reached | 183 |
| 6 | 1804 | 395 |
| 7 | 2236 | 518 |
| 8 | not reached | 452 |
| 9 | not reached | 341 |
| 10 | 2030 | 409 |
| 11 | not reached | 241 |
| 12 | not reached | 519 |
| 13 | 1096 | 420 |
| 14 | not reached | 392 |
| 15 | not reached | 594 |
| 16 | not reached | 559 |
| 17 | not reached | 740 |
| 18 | 1885 | 499 |
| 19 | not reached | 562 |
| 20 | not reached | 489 |

## Summary

Median: graduated 3000, binary 500.5. Reached within 3000: graduated 6 of 20, binary 20 of 20.

**Verdict: FAIL**. By the rule fixed beforehand, the director ships with the binary reward. **Not switched yet:** `DirectorConfig.Provisional()` still uses the graduated one; the switch changes the study and is the student's and the supervisor's to make, with this result in hand.

## Limits

The result is about this problem. How far a rebuild depends on the strategy, and how noisy the other terms are, decide how much the extra information in the graduated reward helps; both are guesses until the pilot's telemetry measures them.

