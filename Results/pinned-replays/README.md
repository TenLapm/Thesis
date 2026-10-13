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

**Telemetry (since WP-C3).** Beside every `<name>.replay.json` is `<name>.telemetry.jsonl`: the
telemetry that same session wrote while it ran. Telemetry is made from the simulation alone, so
the test makes each file again from its replay, on the runtime it is running on, and compares
bytes. It also checks that the `dotnet-` and `mono-` telemetry of an episode are the same file but
for the two labels that say who recorded it. `*.jsonl` is kept with LF line ends on every machine
(`.gitattributes`); a file with Windows line ends fails the test.

## When the rules change on purpose

Every WP-C package changes the game, and these recordings then describe the old one.

1. `dotnet run --project Tools/dotnet/Thesis.Cli -- pin` (writes the replays and their telemetry)
2. In Unity: **Thesis → Replay → Record Pinned Episodes (Mono)** (the same, under Mono)
3. Delete the `unity-session-*` files, or play a new session and copy its `replay.json` and its
   `telemetry.jsonl` from `Runs/Sessions/<session>/` here, as `unity-session-<name>.replay.json` and
   `unity-session-<name>.telemetry.jsonl`. Stop the session properly (leave play mode): that is when
   the last row is written.
4. Run the tests in both runners, commit the new files, and say so in `Docs/DEVLOG.md`.

If a recording stops verifying and you did **not** mean to change the rules, that is a
determinism bug. `thesis replay <file> --per-tick` names the tick. If only the telemetry
comparison fails, the row changed and the game did not: `thesis telemetry <replay> --check
<telemetry file>` names the first line that differs.

## Recorded

2026-10-11, at the WP-C3 commit: the game with the shop and selling (replay schema 4,
telemetry schema 1). Eleven recordings, each with its telemetry.

| Episode | What it covers |
|---|---|
| `cards-6waves` | **Cards, levels and gear** (2026-10-12): coin and gear cards, rare and epic tower cards, a scripted player that takes coins, buys gear onto towers and spends the rest on upgrades, against every enemy type. A hash for every tick. |
| `sentry-6waves` | Towers only, six waves: every offer the budget covers is bought, all three tower types, kills, splash, slow. |
| `mixed-3waves` | Walls, then towers, three waves, with a hash for every tick. |
| `walls-only-to-gameover` | Walls and no towers: nothing is killed, the enemies leak, game over in wave 1, with a hash for every tick. Its telemetry ends with a row closed by the game over. |
| `class-cycle-6waves` | The development planner's cycle: a ground wave, a sapper wave, a flyer wave, all three together, and two more. Walls, then towers; the core has 1,000 HP so all six waves are played. A hash for every tick. (`thesis replay <file> --outcomes` prints what happened in a recording; `thesis trace <telemetry file>` does the same from the telemetry.) |
| `restless-5waves` | **Selling.** Every build phase after the first wave the scripted player sells its weakest tower and its oldest wall piece, then builds and buys again: three towers and four wall pieces sold in five waves, with the real 10-HP core. A hash for every tick. |

Earlier sets were deleted here when the rules changed: the game before towers (schema 1; still in
`Results/2026-10-02_determinism/` as the evidence for that result), the game with towers but one
movement class (schema 2; in git history at the WP-C1 commit), and the game with movement classes
but no shop (schema 3, with the session `unity-session-classes`; in git history at the WP-C2
commit). All three are refused now. The session `unity-session-shop` (the development planner's old
four-wave cycle, on the 38 x 38 board) went with WP-C5, which changed that planner; it is in git
history at the WP-C4 commit. The session `unity-session-mouse` (the screen of WP-C4) went with replay schema 5 and has not been replaced; it is in git history at the WP-C4 commit too.
