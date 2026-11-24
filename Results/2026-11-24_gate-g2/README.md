# Gate G2: the director costs nothing visible

Date: 2026-11-24

Command:

```
dotnet run --project Tools/dotnet/Thesis.Cli -c Release -- gate g2
```

## Hypothesis

The full director's work at a wave boundary (profile, vetoes, scores, the learner, the reward of the wave before; timed inside `WaveDirector`) takes **at most 16 ms** at every boundary: the full director against the reacting player on the game's 48 x 48 board, seeds 201 to 220, up to 50 waves, one episode at a time. **Fail:** any boundary above 33 ms (a visible frame spike): make the director's work incremental. Between: reported.

## Result

| decisions | median | 99th percentile | most |
|---|---|---|---|
| 109 | 0.33 ms | 0.99 ms | 2.36 ms |

## Verdict

**PASS**.

**Limit.** Measured headless under .NET on the development machine. The study build runs on Mono (D5), whose code is slower; the same director in the Unity editor has not been timed yet. The F9 overlay shows each decision's time while playing.

