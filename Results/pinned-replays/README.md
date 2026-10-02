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

2026-10-02, at the WP-C1 commit: the game with hit points and towers (replay schema 2).

| Episode | What it covers |
|---|---|
| `sentry-6waves` | Towers only, six waves: all three tower types, kills, splash, slow. |
| `mixed-3waves` | Walls, then towers, three waves, with a hash for every tick. |
| `walls-only-to-gameover` | Walls and no towers: nothing is killed, the enemies leak, game over in wave 1, with a hash for every tick. |
| `unity-session-towers` | Played in the Unity editor through `SimHost`: towers placed through the same call the keys make (one refused, on the spawn), a wall piece, input while paused, a build in the middle of a wave, speeds x3 and `timeScale` 12, three waves, game over. |

The recordings of the game before towers were deleted here when the rules changed. They are still in
`Results/2026-10-02_determinism/`, as the evidence for that result; they are schema 1 and are refused now.
