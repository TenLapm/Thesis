# Determinism: a recorded session replays to the same state on every runtime

**Date:** 2026-10-02 · **Work package:** WP5 · **Invariant:** `CLAUDE.md` I1
**Code:** branch `sim-core/wp0-wp1`, the WP5 commit · **Machine:** Windows 11, 12 cores
**Runtimes:** Unity 6000.4.10f1 editor (Mono) and .NET 9.0.10 (CoreCLR)

## Hypothesis (written before the runs)

Given the same map, config, shape library, seed and player input, the simulation
reaches the same state on every tick, whether it runs:

1. headless under .NET, twice;
2. inside the Unity editor's frame loop, at any game speed, and then again headless;
3. headless, and then again under Unity's Mono.

"The same state" means the 64-bit FNV-1a hash of the whole `SimState` (floats by
bit pattern) is equal after every tick. A replay file stores those hashes; replaying
it recomputes them.

**It fails if** any recorded hash differs. The replay tool then names the first
divergent wave and tick.

## Result

**The hypothesis held in all three cases.** One real defect was found on the way
(a negative zero, below) and fixed before these numbers were taken.

| # | Recorded by | Replayed by | Ticks | Waves | Commands | Per-tick hashes | Result |
|---|---|---|---|---|---|---|---|
| 1 | .NET CLI, greedy policy, seed 1, long-lived core | .NET CLI | 111,861 | 25 | 466 | all match | OK |
| 2 | same file | Unity editor (Mono) | 111,861 | 25 | 466 | all match | OK |
| 3 | Unity session A, driven through the editor | .NET CLI | 4,709 | 1 | 22 | all match | OK |
| 4 | Unity session A | Unity editor (Mono) | 4,709 | 1 | 22 | all match | OK |
| 5 | Unity session B, quit mid-wave | .NET CLI | 882 | 0 | 18 | all match | OK |
| 6 | Unity session B | Unity editor (Mono) | 882 | 0 | 18 | all match | OK |
| 7 | .NET CLI, greedy policy, seed 7, 5 waves | .NET CLI and both test runners | 22,914 | 5 | 80 | not recorded; wave hashes match | OK |

Rows 3–7 are committed in this folder and re-checked on every test run by
`PinnedReplayTests`, in both `dotnet test` and Unity's test runner. Each file is
therefore re-run on the runtime that did not record it.

Row 1's final hash is `a1dc9171e8fd33f0`.

### What the Unity sessions covered

Both sessions ran in SampleScene in play mode through the real `SimHost` frame loop.
Input went in through `SimHost.Submit`, the same call the mouse and keyboard handlers make.

- **Session A:** eight placements with rotations and a hold during prep; a refused
  placement (same tile twice); `StartWaveNow`; rotate and hold **while paused**;
  builds in the middle of the wave; speeds x1, x2, x3; then `timeScale` 20, which
  hits `SimHost`'s cap of 20 ticks per frame; played to game over.
- **Session B:** a hold and six placements with rotations, wave started at x3, then at
  tick 882 a mid-wave build, pause, two rotates and a hold, and play stopped **mid-wave**.
  Those last four commands came after the last tick that ran.

### The bisect works

One `PlaceShape` in the 25-wave file was moved from tick 32435 to 32436:

```
> thesis replay Runs/wp5_tampered/replay.json
[Replay] DIVERGED: wave 8 was recorded resolving at tick 36980; here it is still running at tick 36981.
  waves that matched before it: 7
  first divergent wave: 8
  run again with --per-tick to find the exact tick.

> thesis replay Runs/wp5_tampered/replay.json --per-tick
[Replay] DIVERGED: tick 32435 ran differently (hash b32f7b0d968ebbb9, recorded 734f31aae8cf3560).
  waves that matched before it: 7
  first divergent wave: 8
  first divergent tick: 32435  (the runs agree until this tick starts and differ once it has run; --dump-tick 32436 writes the state right after it)
```

`--dump-tick 32436` on both files, then `diff`, shows what differs: the budget
(0.2 vs 23.2), the placement count (59 vs 53) and the bag.
`ReplayRunnerTests.ACommandMovedByOneTickIsPinnedToItsWaveAndItsTick` does the same in the test suite.

## Defect found: negative zero in the map

The first Unity recording was **refused** when read headless: its setup fingerprint
did not match.

- **Cause:** SampleScene's `GridManager` sits at Z = −0.0. The simulation ran with
  −0.0, but the JSON writer puts −0.0 in the file as `0.0`, on both runtimes. The
  file therefore described a slightly different map from the one that was played.
- **Effect on gameplay:** none. −0.0 and +0.0 give the same result in every sum the
  grid does, and the WP4 cross-runtime check had passed with it.
- **Why it still matters:** the file is supposed to say exactly what the run was
  built from. The setup check exists to catch a number that does not survive JSON,
  and this is one.
- **Fix:** `SceneMapBuilder` strips negative zeros from every position it reads from a
  transform. `ReplayFileTests.ANegativeZeroIsEitherKeptOrRefusedNeverSilentlyChanged`
  pins the rule that the sign is never lost silently.

## Deviations and limits

- **The Unity sessions were driven through the editor by script, not by hand.** The
  commands took the same path as real input (`SimHost.Submit`), and the frame loop,
  speeds and pause were real. A session played with mouse and keyboard is still to do;
  it uses the same recorder, so it needs only `thesis replay` on the file it writes.
- **Both Unity sessions ended in wave 1** (one by game over, one by quitting). The
  wave-to-intermission path under Unity's frame loop is covered by the test suite
  and by row 2, not by a Unity-recorded file.
- **The 25-wave run needs a core that cannot die.** With SampleScene's real settings the
  greedy policy loses in wave 1 or 2, so `long-core.config.json` sets `CoreMaxHp` to 100,000.
  The rules are otherwise SampleScene's. All 25 waves leak; the run exercises
  spawning, digging, breaches, leaks and building, not good play.
- **IL2CPP is not covered.** Only the editor (Mono) and .NET were run. A player build
  compiles to C++, where compiler flags decide float contraction. Repeat rows 3–6 on a
  player build before the build freeze (`WORKPLAN.md` WP5 trap).
- **Timings are not a result here.** For orientation only: replaying 111,861 ticks with
  a hash per tick took 9 to 10 s under both runtimes; without per-tick hashes, under 2 s on .NET.

## Commands

From the project root.

```bash
# Row 1: record, then replay
dotnet run --project Tools/dotnet/Thesis.Cli -- run --policy greedy --seed 1 --waves 25 --per-tick --config Results/2026-10-02_determinism/long-core.config.json --out Runs/wp5_greedy_25waves
dotnet run --project Tools/dotnet/Thesis.Cli -- replay Runs/wp5_greedy_25waves/replay.json --per-tick

# Rows 3, 5, 7: the committed recordings
dotnet run --project Tools/dotnet/Thesis.Cli -- replay Results/2026-10-02_determinism/unity-session-a.replay.json --per-tick
dotnet run --project Tools/dotnet/Thesis.Cli -- replay Results/2026-10-02_determinism/unity-session-b.replay.json --per-tick
dotnet run --project Tools/dotnet/Thesis.Cli -- replay Results/2026-10-02_determinism/headless-greedy-5waves-seed7.replay.json

# Row 7 was recorded with
dotnet run --project Tools/dotnet/Thesis.Cli -- run --policy greedy --seed 7 --waves 5 --config Results/2026-10-02_determinism/long-core.config.json --out Runs/wp5_greedy_5waves_seed7

# Every row from 3 on, on both runtimes
dotnet test Tools/dotnet/Thesis.Headless.sln --filter FullyQualifiedName~PinnedReplay
#   and in Unity: Window > General > Test Runner > EditMode > PinnedReplayTests
```

Rows 2, 4 and 6 (replaying under Mono) were run in the editor with
`ReplayRunner.Verify(file, perTick: true)`, which is what the menu
**Thesis → Replay → Verify File…** calls.

To record a new Unity session: press Play in SampleScene and play. The console prints
`[Replay] Recording to …/Runs/Sessions/<time>_seed<seed>/replay.json`; the file is
written at every wave boundary and when play stops.

## Files

| File | What |
|---|---|
| `unity-session-a.replay.json` | Row 3/4. Recorded by the Unity editor. |
| `unity-session-b.replay.json` | Row 5/6. Recorded by the Unity editor. |
| `headless-greedy-5waves-seed7.replay.json` | Row 7. Recorded by the .NET CLI. |
| `long-core.config.json` | The config override used for rows 1 and 7. |
