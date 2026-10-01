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

2026-10-02, the game before towers (lifetime clock, walls only), at the WP-H commit.
The two `unity-session-*` files are the sessions described in `Results/2026-10-02_determinism/README.md`.
