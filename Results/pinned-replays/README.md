# Pinned replays

The standing check that the game runs bit for bit the same on Unity's Mono and on .NET
(`CLAUDE.md` I1). `PinnedReplayTests` replays every `*.replay.json` in this folder, in both
test runners, so each recording is re-run on the runtime that did **not** make it.

| Files | Recorded by | How |
|---|---|---|
| `dotnet-*.replay.json` | .NET | `dotnet run --project Tools/dotnet/Thesis.Cli -- pin` |
| `mono-*.replay.json` | Unity editor (Mono) | menu **Thesis → Replay → Record Pinned Episodes (Mono)** |
| `unity-session-*.replay.json` | Unity editor, through `SimHost`'s real frame loop | played sessions, copied from `Runs/Sessions/` |

The `dotnet-` and `mono-` files are the same scripted episodes (`Thesis.Harness.PinnedEpisodes`),
so besides replaying each one, the test also checks that the two runtimes recorded identical runs:
the same commands from the scripted player and the same hash on every tick.

## When the rules change on purpose

Every WP-C package changes the game, and these recordings then describe the old one.

1. `dotnet run --project Tools/dotnet/Thesis.Cli -- pin`
2. In Unity: **Thesis → Replay → Record Pinned Episodes (Mono)**
3. Delete the `unity-session-*` files, or play a new session and copy its `replay.json` here.
4. Run the tests in both runners, commit the new files, and say so in `Docs/DEVLOG.md`.

If a recording stops verifying and you did **not** mean to change the rules, that is a
determinism bug. `thesis replay <file> --per-tick` names the tick.

## Recorded

2026-10-02, at the WP-C2 commit: the game with movement classes (replay schema 3).

| Episode | What it covers |
|---|---|
| `sentry-6waves` | Towers only, six waves: all three tower types, kills, splash, slow. |
| `mixed-3waves` | Walls, then towers, three waves, with a hash for every tick. |
| `walls-only-to-gameover` | Walls and no towers: nothing is killed, the enemies leak, game over in wave 1, with a hash for every tick. |
| `class-cycle-6waves` | The development planner's cycle: a ground wave, a sapper wave, a flyer wave, all three together, and two more. Walls, then towers; the core has 1,000 HP so all six waves are played. Every class is both killed and let through: sappers breach walls and chew through a tower, flyers are shot down by the one tower type that can hit them and leak past the rest. A hash for every tick. (`thesis replay <file> --outcomes` prints what happened in a recording.) |
| `unity-session-classes` | Played in the Unity editor through `SimHost` with `DirectorHost.condition = DevClassCycle` and a 1,000-HP core: towers placed through the same call the keys make (one refused, on the spawn), wall pieces, two towers and a wall built in the middle of the flyer-and-sapper wave, input while paused, speeds x12, x3, x1 and a pause. This file is the first five waves (23,295 ticks) of a 13-wave session: the recording as `SimHost` had written it when wave 5 resolved. The whole session (57,899 ticks) verified headless too; it is not committed, to keep the test run short. |

Earlier sets were deleted here when the rules changed: the game before towers (schema 1; still in
`Results/2026-10-02_determinism/` as the evidence for that result) and the game with towers but one
movement class (schema 2; in git history at the WP-C1 commit). Both are refused now.
