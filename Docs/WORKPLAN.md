# WORKPLAN.md — work packages

Each package is sized for **one implementation session**. It lists the files
it creates, the tests that must pass, and a definition of done that can be checked. Read
`CLAUDE.md` and `Docs/ARCHITECTURE.md` first. Section references (§) point to
`ARCHITECTURE.md` unless they say otherwise.

## How to hand a package to an implementer

```
Implement WP<n> from Docs/WORKPLAN.md.
Read CLAUDE.md, then Docs/ARCHITECTURE.md §<sections listed in the WP>.
Work only inside the files the WP lists; if you need to touch anything else, stop and say why.
Finish with: dotnet test green → Unity refresh + read_console zero errors → Unity EditMode tests green
→ append a DEVLOG entry. If a test in "Must pass" cannot pass without breaking an invariant, stop and report.
```

## How a package gets reviewed

1. `dotnet test Tools/dotnet/Thesis.Tests`
2. Read the DEVLOG entry, looking especially at deviations.
3. Run the package's **Review probe**. This is a quick check that exercises the risky part in a way the
   implementer's own tests might not.

## Dependency graph

```
Port the current game faithfully                                  (all done)
WP0 ─┬─ WP1 ── WP2 ── WP3 ── WP4 ── WP5 ── WP-H   (I1 proven on the known game; hardening after the review)
     └─ WP7                                        (Learning: independent; run it in a parallel session; needs DetMath from WP-H)

Then change the rules (towers)
WP-H ── WP-C1 combat core ─┬─ WP-C2 movement classes ───────────────┐
                           ├─ WP-C3 shop + telemetry + wall selling ─┼─ WP-C4 Unity towers + shop UI
                           └────────────────────────────────────────┴─ WP-C5 roster v1 + balance sanity

Director
WP-C1 + WP-C3 ──────── WP8 profile
WP-C2 + WP-C3 ──────── WP9 strategies ── WP10 layers ── WP11 full director ── WP12 harness ── WP13 gates
                                           WP10 + WP-C4 ── WP14 Unity director UX + session setup
```

WP6 (telemetry) no longer exists as its own package: it is part of WP-C3 (D8).

### Revised semester-1 schedule (approved 2026-09-15)

| Week | Original `CLAUDE.md` §9 | Revised |
|---|---|---|
| W5 | wave loop + telemetry | WP0, WP1, WP2, WP3, WP6 |
| W6 | determinism | WP4, WP5 ← critical path, unchanged |
| W7 | build profile: cheap features | **WP-C1** combat core, **WP-C2** movement classes |
| W8 | build profile: expensive (1/2) | **WP-C3** shop, **WP-C4** Unity towers + shop UI (functional, not polished) |
| W9 | build profile: expensive (2/2) | **WP-C5** roster v1 + balance sanity, WP8a (`maze_length`, `tower_concentration`, `damage_type_mix`) |
| W10 | strategy pool | WP8b (`breach_vulnerability`, `chokepoint_reliance`), WP9 |
| W11 | constraint + heuristic layers | WP10, WP14 |
| W12 | learned layer | WP11 |
| W13 | self-play harness | WP12 |
| W14 | gates G1–G3 | WP13 |
| W15 | remediation reserve | reserve, now at real risk |
| W16 | report + session prep | unchanged |

WP7 runs in a **parallel session** during W7–W9. It touches only `Assets/Thesis/Learning`, so it cannot conflict with the combat work.

**What makes this fit, and what it costs:**
- The expensive part of the old W8–W9 was the new per-tile logging. Under towers that
  logging is the `DamageMap`, which WP-C1 builds as part of the tower pass. So WP8b
  is genuinely smaller. **But in calendar terms this still shortens W8–W9,
  which `CLAUDE.md` §9 forbids. The student has to accept that explicitly.**
- Art, VFX, sound and the look of the shop UI move to the S2 W1–W3 UX pass. Real balancing
  stays in the S2 W4 balance pass, which is now larger. In S1, WP-C5 only checks that nothing is broken or absurd.
- If anything slips, the W15 reserve is used up. A new cut candidate to consider
  inserting after cut (1): **`flying_bypass` and the Flying movement class**, replaced by a ground-only strategy.

### Re-plan, 2026-10-02 (D8)

WP0–WP5 are done. By the table above, WP6, WP-C1 and WP-C2 were also due by the end of W7,
and they are not done. **If W5 began on 14 September, the plan is about one week behind,
and that week is the W15 reserve.** (If your W5 started on a different date, shift the
"Week" column below; the order does not change.)

Three changes:

1. **WP6 is folded into WP-C3.** Nothing on the way to WP-C1 or WP-C2 needs telemetry, and a
   schema for the game without towers would never see a participant. Telemetry is built once,
   for the tower game, so `WaveRecord` schema 1 already has towers and the shop.
2. **WP7 runs in a parallel session, starting now.** It needs only WP0 and `DetMath` (WP-H).
3. **A fixed checkpoint for the first cut.** If WP-C3 is not finished by the end of W9, cut
   Flying: the movement class, the anti-air tower, `flying_bypass` and the `Uncounterable`
   veto, replaced by a fourth ground-only strategy. `WORKPLAN` already named this cut; the
   rule only fixes *when*, so it is not decided under pressure.

| Order | Package | Week, if W5 = 14 Sep | Note |
|---|---|---|---|
| done | WP0–WP5 | W5–W7 | |
| done | **WP-H** hardening | W7 | 2026-10-02, after the review |
| done | **WP-C1** combat core | W7 | 2026-10-02; includes "every wave must end" (§4.6) |
| done | **WP-C2** movement classes | W7 | 2026-10-02; Ground, Sapper, Flying |
| 1, parallel | **WP7** learning | W8–W9 | separate session |
| 2 | **WP-C3** shop, telemetry, wall selling | W9 | checkpoint at the end of this week |
| 3 | **WP-C4** Unity towers and shop UI | W10 | |
| 4 | **WP-C5** roster and balance check | W10 | |
| 5 | **WP8** profile | W11 | needs the proposal's §6.2 in the repo |
| 6 | **WP9** strategies | W11 | needs the proposal's §6.3 in the repo; S14 (income per wave shape) |
| 7 | **WP10** layers, recorded plans in the replay | W12 | |
| 8 | **WP14** director UX and session setup | W12 | needed before the pilot |
| 9 | **WP11** full director | W13 | |
| 10 | **WP12** harness and ladder | W14 | |
| 11 | **WP13** gates G1–G3 | W15 | this was the reserve week |
| | report and session prep | W16 | unchanged |

There is no slack left in this table. Cutting Flying at the checkpoint buys back roughly
half a week across WP-C2, WP-C4, WP-C5, WP9 and WP10.

---

## WP0 — Scaffolding

**Reads:** §2, §3, §9 · **Depends:** nothing

**Creates**
- `Assets/Thesis/{Core,Sim,Learning,Director,Harness}/Thesis.<Name>.asmdef`: `noEngineReferences: true`,
  references by name, and `overrideReferences: true` with `precompiledReferences: ["Newtonsoft.Json.dll"]`.
- `Assets/Tests/EditMode/Thesis.Tests.EditMode.asmdef`: references all five, `nunit.framework.dll`,
  `defineConstraints: ["UNITY_INCLUDE_TESTS"]`, Editor platform only.
- `Packages/manifest.json`: add `"com.unity.nuget.newtonsoft-json": "3.2.2"` (the version already resolved; it bundles Newtonsoft.Json 13.0.2).
- `Tools/dotnet/`: `Directory.Build.props`, `Thesis.Headless.sln`, one csproj per assembly
  (`<Compile Include="../../../Assets/Thesis/<Name>/**/*.cs" />`, `netstandard2.1`, `LangVersion 9.0`,
  `ProjectReference`s that mirror the asmdefs), `Thesis.Cli` (net9.0, prints `usage`), and
  `Thesis.Tests` (net9.0, NUnit 3.14, NUnit3TestAdapter, Microsoft.NET.Test.Sdk; glob-includes the test sources).
- `.gitignore`: `Tools/dotnet/**/bin/`, `Tools/dotnet/**/obj/`, `Runs/`.
- `Thesis.Core`: `IRandom`, `Pcg32` (PCG-XSH-RR 64/32 with seed and stream, `Save`/`Load` state),
  `RngStreams` constants, `Fnv1a64` (Add int/uint/long/ulong/float via `BitConverter.SingleToInt32Bits`),
  `TileCoord`, `Vec2f` (including the `MoveTowards` port from §4.3), and `Json` settings.
- `Docs/DEVLOG.md` with a header.

**Must pass**
- `Pcg32(seed: 42, stream: 54)` gives first outputs `0xa15c02b7 0x7b47f409 0xba1d3330 0x83d2f293 0xbfa4784b 0xcbed606e`
  (the known-answer values from pcg-c-basic `pcg32-demo`; if they disagree, check the reference source before changing the code).
- `Pcg32` `Save` → draw 100 → `Load` → draw 100 gives identical sequences. Two different streams on the same seed differ.
- `Fnv1a64` of empty input = `0xcbf29ce484222325`; of the single byte `'a'` = `0xaf63dc4c8601ec8c`.
- `Vec2f.MoveTowards` never overshoots, even with `maxDelta` 1000× the distance, and returns the target exactly.

**Done when** `dotnet test` is green, Unity compiles with no console errors, and Unity's Test Runner shows the same tests green.

**Traps**
- In an asmdef, `overrideReferences: true` means every precompiled DLL must be listed explicitly.
- Unity creates `.meta` files on refresh. Do not hand-write them.
- The Unity `.gitignore` ignores every `*.csproj` and `*.sln`. `Tools/dotnet/` needs explicit `!` exceptions, or the headless projects never get committed.

**Review probe:** add `using UnityEngine;` to any file under `Assets/Thesis/Sim` and confirm that **both** Unity and `dotnet build` reject it. Then revert.

---

## WP1 — Grid and flow field port

**Reads:** §4.3 (all porting rules) · **Depends:** WP0

**Creates**
- `Sim/Map/MapData.cs` (width, height, `bool[] Walkable`, `WorldSizeX/Y`, `NodeRadius`, `OriginX/Z`,
  `TileCoord Core`, `TileCoord[] Spawns`, JSON load and save)
- `Sim/Grid/SimNode.cs`, `SimGrid.cs` (`NodeFromPosition`, `GetNeighbors` into a reused buffer, `Clone`), `FlowField.cs`
- `Sim/Debug/AsciiMap.cs` (`Parse` for fixtures, `Render` with a route layer)
- `Assets/Scripts/Editor/MapExporter.cs`: menu `Thesis/Export Map`. It replicates `GridManager.CreateGrid`'s
  `Physics.CheckSphere` scan and writes `Maps/<SceneName>.map.json` (static geometry, spawn, core, world size and origin).
- `Harness/Scenarios/BenchScenarios.cs`: the OpenField, Maze and ChokePoints wall layouts, ported from `ScenarioBenchmark.BuildScenario`
  (wall cost 200, 2-tile clearance around spawn and core).
- `Maps/SampleScene.map.json`, `Maps/Bench_Open.map.json`, `Maps/Bench_Maze.map.json`, `Maps/Bench_Choke.map.json`

**Must pass**
- Corner-cut: in an ASCII fixture with two diagonal walls, no neighbor squeezes between them, while entering a wall head-on is still allowed.
- The goal has `BestCost == 0`. Costs are ×10 (one cardinal step on open ground = 10). Following the `NextIndex` chain from any reachable tile ends at the goal.
- **Parity with recorded results:** spawn path `tiles` and `bestCost/10` equal the values in the committed
  `Benchmark_Open.json` (35 / 35), `Benchmark_Maze.json` (198 / 207.2) and `Benchmark_Choke.json` (59 / 67.8).
- `NodeFromPosition` returns the same index as a hand-computed port of `NodeFromWorldPoint` for 1,000 random positions on a 38×38 map.

**Done when** the parity tests pass.

**Traps**
- `RandomScatter` / `Stress100` used `UnityEngine.Random.InitState(12345)`, which cannot be reproduced outside Unity. Leave them out of parity and say so in the DEVLOG.
- If `Physics.CheckSphere` returns nothing in edit mode, call `Physics.SyncTransforms()` first.
- SampleScene: `gridWorldSize` 75 and `nodeRadius` 1 give 38×38 (`RoundToInt(37.5)` = 38).

**Review probe:** swap the `x` and `y` loops in `GetNeighbors` and confirm that the neighbour-order tests fail.
*(Corrected after running it in WP1: the Maze parity test does **not** fail. Path length and cost are identical whichever equally cheap route wins a tie, so the benchmark files cannot detect tie-break changes. The guard is `NeighbourOrderIsXOuterThenYInner`, which pins the original loop order directly.)*

---

## WP2 — Agents, placement, shape bag, economy

**Reads:** §4.2 step 3, §4.3, §4.5 · **Depends:** WP1

**Creates**
- `Sim/Agents/AgentState.cs`, `AgentSystem.cs` (the step-3 agent pass)
- `Sim/Build/ShapeDef.cs` (rotation `(x,y) → (y,−x)`, as in `BlockShape.Rotate`), `ShapeBag.cs` (7-bag Fisher–Yates on the
  `Bag` stream, a 3-shape preview queue, hold slot, `hasHeldThisTurn` / `isHoldSlotEmpty` exactly as in `BlockManager`),
  `Placement.cs` (`CanPlace`, a port of `AreTilesPlaceable` that protects **every** spawn and the core; `Place` checks the budget,
  spends it, sets `terrainCost = max(2, digCost)` and `wallHealth = max(0.5, wallHealth)`, rebuilds the field, and logs the placement), `PlacementRecord.cs`
- `Sim/Commands/SimCommand.cs`, `Sim/Events/SimEvent.cs`, `Sim/Stats/OccupancyMap.cs`

**Must pass**
- Two agents on one wall tile breach it in half the time one agent takes. The breach reward is credited **exactly once** with 1, 2 or 8 agents on the tile.
- An agent whose lifetime expires is stalled and adds `deathReward`. An agent reaching `BestCost == 0` is leaked and costs 1 core HP.
- With `dt` = 0.5 s and speed 10, an agent never ends a tick past its target tile centre.
- Placement legality table: off-map, static tile, existing wall, spawn, core, and valid, each tested in all 4 rotations.
- The budget never goes negative. A `PlaceShape` command with too little budget is ignored and emits nothing.
- In any 7 consecutive bag draws that start at a bag boundary, each library shape appears exactly once. Hold works once per piece.

**Done when** the agent, placement and bag tests are green.

**Review probe:** seed 8 agents onto a single wall tile with health 0.01 and check that exactly one `WallBreached` event is emitted and the budget grows by exactly `wallBreakReward`.

---

## WP3 — Wave lifecycle, planner seam, escalation baseline

**Reads:** §4.1, §4.2, §4.4 · **Depends:** WP2, **D4**

**Creates**
- `Sim/SimConfig.cs`, `SimState.cs`, `SimPhase.cs`, `Simulation.cs` (the full tick order, `Clone`, `ComputeHash`, `ValidatePlan`, the PlanWave mutation guard)
- `Sim/Waves/IWavePlanner.cs`, `WaveContext.cs`, `WavePlan.cs`, `AgentGroup.cs`, `WaveOutcome.cs` (including `PressureShare`; the radius comes from config, see S5)
- `Sim/Waves/EscalationPlanner.cs`: the `WaveSpawner.SpawnTrickleRoutine` math ported verbatim, plus the `baseLifeTime = distance(spawn, core) / baseMoveSpeed + margin` derivation
- `Sim/Debug/StateHasher.cs`

**Must pass**
- Escalation table: for waves 1–30, agent count, spawn interval, lifetime and speed equal the `WaveSpawner` formulas evaluated with **SampleScene values**
  (`agentsPerWave` 100, `agentsPerWaveIncrement` 5, `spawnDelay` 0.2, `spawnDelayDecrement` 0.02, `minSpawnDelay` 0.15,
  `lifeTimeIncrementPerWave` 4 capped at `maxLifeTime` 200, `baseMoveSpeed` 1.25 + `moveSpeedIncrementPerWave` 0.0375 capped at 2.25,
  `intermissionDuration` 10, `initialPrepDuration` 45, `budgetStipendPerWave` 20, `lifeTimeSafetyMargin` 5).
  Note: the serialized `baseLifeTime: 60` is overwritten at `Start()` by the distance derivation, so the derivation is what to port.
- Phase sequence: Prep (45 s), then Spawning, Resolving, and Intermission (10 s). The stipend is paid only after wave ≥ 1.
- Determinism: two simulations with the same seed and the same command list have equal hashes on **every** tick for 20,000 ticks.
- `Clone()`, then ticking both copies 5,000 ticks, gives equal hashes. Mutating any one field of a clone (grid cost, agent position, budget, RNG state) changes its hash.
- A planner that mutates `ctx.State` inside `PlanWave` throws.
- `ValidatePlan` throws on a group with count < 0, a spawn index out of range, or a `ThreatSpent` mismatch (when a cost table is present).

**Done when** a headless 25-wave run with `IdlePolicy`-style inaction ends in game over with a plausible wave count, and all tests are green.

**Traps**
- Read every value from `Assets/Scenes/SampleScene.unity` (the `WaveSpawner` block starts near line 1560) and `Assets/Scripts/Prefabs/Agent.prefab`. Do not use the C# field defaults: `agentsPerWave` is 15 in C# but 100 in the scene.
- The agent prefab SampleScene uses is `Assets/Scripts/Prefabs/Agent.prefab` (GUID `c089fd02…`), not `Assets/Resources/Agent.prefab` (which only the benchmarks load).

**Review probe:** run the same 25 waves at tick lengths 0.02 and 0.01 and compare wave-by-wave stall and leak counts. They should be close but not identical. Wildly different counts mean something depends on the tick length where it shouldn't.
*(Refined after running it in WP3: every agent in an escalation wave is identical, so a wave is all-stall or all-leak and the counts come out **identical** at both tick lengths, which says little. The informative measure is continuous: seconds from wave start to the first stall or leak. It is now printed next to the counts by `SampleSceneRunTests.ProbeTickLengthSensitivityOnBenchChoke`.)*

---

## WP4 — Unity host rewire

**Reads:** §6 · **Depends:** WP3, **D2**, **D3**

**Creates / changes**
- New: `SimHost.cs`, `DirectorHost.cs` (escalation only for now), `Config/SimConfigAsset.cs` plus a `.asset` holding the scene values
- Changed to adapters: `WaveSpawner`, `BlockManager`, `PlayerCore`, `PlayerBuilder`, `FlowAgent` (visual only), `Node` (slimmed)
- Ported to read the sim grid: `PathPreviewer`, `FlowFieldVisualizer`, `GhostPreviewer` (it calls `PlayerBuilder.AreTilesPlaceable` as before)
- `Benchmark/`: rebuild timing and A* move to `Thesis.Cli bench`. Frame-time tiers run on `SimHost`.

**Order of work**
1. Copy every serialized value into `SimConfigAsset` **before** deleting any field.
2. Delete the gameplay fields from `Node`, and treat the compiler errors as the checklist of what to migrate.
3. Keep the adapters' public member names so that `CanvasDashboard` compiles unchanged.

**Must pass (manual playtest, logged in DEVLOG)**
- Place every shape, rotate, and hold. The ghost matches the click. Enemies dig through, get stalled, and leak. Budget rewards appear. Game over freezes the game.
- Speeds x1, x2 and x3 all work. Pause blocks building but still allows rotate and hold.
- Bench_Open, Bench_Maze and Bench_Choke report the same path tiles and cost as the committed JSON.
- `CanvasDashboard.cs` has no diff, or its DEVLOG entry explains why.

**Done when** the playtest checklist passes with no console errors.

**Review probe:** play one wave at x1 and replay the same inputs at x3 (using the replay file from WP5 once it exists). Stall and leak counts must be identical.

---

## WP5 — Replay, determinism proof, CLI basics  ← closes I1

**Reads:** §4.5, §7, §8 · **Depends:** WP4

**Status (2026-10-02):** built and passing; result in `Results/2026-10-02_determinism/README.md`.
Still open: (a) one session played **by hand** with mouse and keyboard and verified with
`thesis replay` (the sessions so far were driven through the editor by script);
(b) the Player-build check in Traps below. As built, `ReplayFile` and `ReplayRecorder` live in
`Thesis.Sim/Replay` (the harness assembly is editor-only and `SimHost` records in builds), and
the file stores config, map and shapes in full instead of a config hash. See DEVLOG WP5.
Since WP-H the standing recordings live in `Results/pinned-replays/`.

**Creates**
- `Harness/Replay/ReplayFile.cs` (`schema`, `build`, `map`, `mapSeed`, `rngSeed`, `simConfigHash`, `planner`, optional estimator snapshot,
  `commands: [{tick, cmd}]`, `waveHashes: [{wave, hash}]`), `ReplayRecorder.cs`, `ReplayRunner.cs` (verify, first divergent wave,
  `--per-tick`, `--dump-tick`)
- `Harness/EpisodeRunner.cs`, `Policies/IPlayerPolicy.cs`, `IdlePolicy.cs`, `GreedyDetourPolicy.cs`
- `Thesis.Cli`: `run`, `replay`, `ascii`
- `SimHost` records a replay on every session.

**Must pass**
- A headless 25-wave `GreedyDetour` run is recorded, then replayed, with every wave hash matching.
- The per-tick bisect is tested on purpose: take a recorded replay, change one command's tick by +1, and check that `replay` reports the first divergent wave and then the exact tick.
- **Cross-check:** a session played by hand in the Unity Editor at mixed speeds verifies headless under `dotnet`.

**Done when** the cross-check passes. Write it up as `Results/<date>_determinism/README.md`.

**Traps:** repeat the cross-check once on a **Player build**, not just the Editor, **before the pilot** (the pilot's data is recorded by a build, and it comes before the S2 W6 freeze). Decision D5 makes the study build Windows + Mono, the runtime already verified, and `BuildGuard` refuses anything else; the check is still needed once, on the built player.
*(Update from WP4: Mono vs CoreCLR **did** differ, by one bit from tick 2916 of a real session, because Mono evaluated chained float expressions in double. This is fixed by the explicit-cast rule, ARCHITECTURE §9 rule 3, and all 5,036 ticks now match. IL2CPP compiles to C++, where the compiler flags decide contraction/FMA, so it still needs its own check. `CrossRuntimeBisectTests` shows how to bisect a divergence to a tick and component.)*

---

## WP-H — Hardening after the review  ✅ done 2026-10-02

**Why:** the review after WP5 (`Docs/REVIEW-2026-10-02.md`) found problems that are cheap now
and expensive once the director exists. This package fixed the ones that do not depend on towers.

**Built**
- **A planner can no longer break the simulation.** `BeginWave` and `CloseWave` call the planner
  before committing anything; `PlanValidator` (public) holds the plan rules; `SafePlanner` turns a
  throw or an invalid plan into a fallback wave; `DirectorHost` wraps every planner.
- **`Thesis.Core.DetMath`**: `Log` and `Exp` that give the same bits on every runtime, and
  `ForbiddenApiTests`, which fails on `Math.Log/Exp/Pow`, trig, `System.Random` or the wall clock
  anywhere in `Thesis.*`.
- **Replay writes are atomic** (temp file, then swap). `SimHost` runs on a **copy** of the config
  asset and dispatches events through a queue that tolerates a nested `Submit`.
- **Pinned replays are one command and one click to re-record:** `thesis pin` (.NET) and
  **Thesis → Replay → Record Pinned Episodes (Mono)**. `PinnedReplayTests` now replays whatever is in
  `Results/pinned-replays/` and checks the two runtimes recorded identical runs.
- `.gitattributes`, a GitHub Actions job that runs `dotnet test` on Linux and Windows, and
  `BuildGuard` (the study build must be Mono, D5).
- Documents brought up to date (`CLAUDE.md` §1, decisions D5–D9, this re-plan).

**Not in this package** (folded into the packages below): the "every wave must end" rules
(WP-C1); wall selling, telemetry and its float writer (WP-C3); the planner factory and replay
schema 2 (WP10); the director's config and snapshot in the replay (WP11); parallel episodes
(WP12); session setup (WP14); the pre-pilot checklist at the end of this file.

---

## WP6 — Telemetry v1 and placement entropy  → folded into WP-C3 (D8, 2026-10-02)

Its requirements moved to WP-C3 unchanged in substance: an append-only JSONL writer, a
golden-file test, rows that parse with one `WaveRecord` class in every condition,
`PlacementEntropy`, and the CLI command `trace`. Three things changed on the way:

- **The primary metric is now defined** (§5.9, D7), so `PlacementEntropy` implements that table.
- **The golden file needs its own float writer.** The JSON library prints the same `float`
  differently on the two runtimes (`0.20000458` under .NET, `0.200004578` under Mono; both read
  back to the same bits). A golden file that must be byte-identical in both test runners cannot
  use it. Telemetry writes floats through one formatter in `Thesis.Core.Json`.
- **Telemetry is recorded the way replays are:** by one host-agnostic recorder that Unity and
  `EpisodeRunner` both call, never by host code. `thesis telemetry <replay>` regenerates the rows
  from a replay, and "live rows equal regenerated rows" becomes a test.

---

## WP-C1 — Combat core: HP, towers, damage (replaces the lifetime clock)  ✅ done 2026-10-02

**Reads:** §4.6, `CLAUDE.md` §2 and I12 · **Depends:** WP5 (determinism proven on the ported game). S12 can use a placeholder roster.

**Status:** built; every "must pass" item below has a test, in both runners. As built it differs
from the list below in four ways (DEVLOG WP-C1 has the reasons):
- **`PlaceTower` is a real command already**, not an internal helper: `PlaceTower(towerIndex, x, y)`
  buys a tower of the roster by index. Without a recorded way to place towers, no replay could
  contain one, and the cross-runtime check would not have covered any of the new arithmetic until
  WP-C3. WP-C3 turns the index into a shop offer slot.
- **The simulation takes a tower roster** (`TowerDef[]`), stored in the replay like the shapes.
  `TowerRoster.Placeholder()` is three unbalanced towers until WP-C5.
- **Replay schema 2** (the roster and the command's third argument). Older recordings are refused.
- **Unity has a temporary way to play it:** Z / X / C place the three towers at the mouse tile,
  drawn as tinted wall cubes, and the bar over an enemy shows HP. WP-C4 replaces all of it.

**Creates / changes**
- `Sim/Combat/DamageType.cs`, `TowerDef.cs`, `TowerState.cs`, `TowerSystem.cs` (the step-3 tower pass), `Targeting.cs`, `DamageMap.cs`
- `AgentState`: **delete `Life` first**; the resulting compiler errors are the checklist. Add `Hp`, `MaxHp`, `Resist[]`, `SlowTicks`, `SlowFactor`.
- `SimNode`: add `Occupant { None, Wall, Tower }` and `TowerId`. `Placement.CanPlace` covers a single tower tile (I9). `Placement.PlaceTower(state, def, tile)`
  is internal: tests use it now, and the player-facing command arrives in WP-C3.
- `EscalationPlanner` v2: HP escalation replaces lifetime escalation (placeholder numbers until WP-C5).
- `WaveOutcome`: `Killed` replaces `Stalled`; add `DamageByType[]`, `TowersDestroyed` and `TimedOut`.
- **Every wave must end** (§4.6): `PlanValidator` requires `MoveSpeed > 0`; `SimConfig.MinSlowFactor` and
  `SimConfig.MaxWaveSeconds`; `MapData.Validate` checks that every spawn reaches the core.

**Must pass**
- **Termination:** a plan with `MoveSpeed` 0 is refused. A slow never takes an enemy below `MinSlowFactor`.
  A map whose spawn is walled off from the core by static blockers fails `MapData.Validate` with a message
  naming the spawn. With an enemy that can neither move nor die (forced in a test), the wave still
  resolves at `MaxWaveSeconds` with `TimedOut` set. In a normal 25-wave run `TimedOut` is never set.
- Range: a tower with range 3 hits an agent at 2.9 tiles and not at 3.1.
- Cadence: with a target always present, a tower with `FireIntervalTicks` 25 fires exactly 40 times in 1,000 ticks.
- Damage equals `Damage × Resist[type]`, and `Resist` 0 means immune.
- Splash hits every agent within the radius, in id order. Slow keeps the maximum ticks and the minimum factor.
- `Targeting.First` picks the agent with the lowest route cost, with ties going to the lowest id.
- The kill reward is credited **once** even when a direct hit and splash kill the same agent in the same tick.
- An enemy chewing a tower tile destroys the tower and emits `TowerDestroyed` exactly once, and a destroyed tower never fires on a later tick.
- The WP1 parity tests and the WP3 determinism tests still pass.

**Done when** a headless run with a hand-placed tower layout survives clearly longer than the same map with no towers.

**Review probe:** place a range-0 tower (it can never hit) on the only route. Check that enemies dig through it, `TowerDestroyed` fires once, and the flow field rebuilds.

**Traps:** `PinnedReplayTests` will fail here, **on purpose**. It re-runs the recordings in
`Results/pinned-replays/`, and once HP replaces the clock they no longer describe the game.
Re-record both halves: `thesis pin` under .NET, and **Thesis → Replay → Record Pinned Episodes
(Mono)** in Unity. Delete the two `unity-session-*` files there (they are play sessions of the old
game) or replace them with a new played session. Update the episodes in `PinnedEpisodes` if the
policies need different settings, bump `ReplayFile.CurrentSchema` if the command set changed, and
note it in DEVLOG. Do not delete the test: it is the only standing Mono-vs-.NET check.
All tower maths follows §9 rule 3: casts on chained floats, and no `Math.Pow` for a squared distance.

---

## WP-C2 — Movement classes: Ground, Sapper, Flying  ✅ done 2026-10-02

**Reads:** §4.6 · **Depends:** WP-C1

**Status:** built; every "must pass" item below has a test, in both runners. As built it differs
from the list below in five ways (DEVLOG WP-C2 has the reasons):
- **A development planner, `ClassCyclePlanner` (`--planner class-cycle`, `DirectorHost.Condition.DevClassCycle`).**
  The package listed no way to send a sapper or a flyer; the strategies that will are WP9. Without one,
  nothing could be played, recorded or compared across runtimes. It cycles ground, sappers, flyers, all
  three. It is not a study condition, and its archetype numbers are placeholders (S12).
- **The must-pass fixture has a 10-tile detour, not 20.** With the real wall cost of 15 a wall reads
  as a 14-tile detour, so at 20 even a ground enemy digs. A second test pins that case.
- **Extra files:** `MovementClasses.cs` (array size), `RouteCost.cs` (how far an enemy still has to go,
  per class).
- **`WaveOutcome` counts every class** (`SpawnedByClass[]`, `KilledByClass[]`, `LeakedByClass[]`), where
  §4.6 planned `FlyersSpawned` and `FlyersLeaked`.
- **Replay schema 3.** The layout is unchanged; the hashes cover more state. Older recordings are refused.

**Creates** `Sim/Movement/MovementClass.cs`, `FlowFieldSet.cs`, `FlyingMovement.cs`, and the class branches in the agent pass.

**Must pass**
- The Ground field is byte-identical to the WP1 parity results, so moving to `FlowFieldSet` changes nothing for Ground.
- In a fixture where one wall tile blocks a corridor with a 20-tile detour, Ground routes around it and Sapper digs through it.
- Flyers ignore walls and towers, never dig, and leak at the core.
- A tower with `CanHitFlying = false` never targets a flyer, even when it is the only agent in range.

**Review probe:** use `cli ascii --layer route` to render the Ground and Sapper routes on Bench_Maze side by side.
As built: `thesis ascii --map Maps/Bench_Maze.map.json --scenario maze --class ground` and the same with
`--class sapper` (86 tiles against 198; `+` marks where the sapper route goes through a wall).

---

## WP-C3 — Shop, wall selling and telemetry

**Reads:** §4.5 (counter-based shop RNG), §4.6 (shop defaults, wall selling), §5.1, §5.8, §5.9, `CLAUDE.md` I10 and I11 · **Depends:** WP-C1. S11 can start with the §4.6 defaults.

This package now also contains what was WP6 (see the note above WP-C1). If it grows past one
session, split it as **C3a** (shop, `SellTower`, `SellWall`) and **C3b** (telemetry, entropy, `trace`).

**Already there from WP-C1:** the `PlaceTower` command (by roster index), `Placement.TryPlaceTower`,
the tower roster in the simulation and in the replay, `PlacementRecord.Kind` and `TowerId`, and the
events `TowerPlaced` and `TowerDestroyed`. This package changes what `PlaceTower`'s first argument
means (an offer slot, checked against the shop) and adds the rest. That changes the command set, so
bump `ReplayFile.CurrentSchema` and re-record the pinned episodes.

**From WP-C2:** a sale must rebuild **both** flow fields: call `FlowFieldSet.Generate`, as placement does,
never `FlowField.Generate`. Telemetry's `outcome` gets the per-class counts (`SpawnedByClass` and friends),
and `plan.groups[]` gets `movement`. The pinned set has a fourth episode, `class-cycle-6waves`.

**Creates**
- `Sim/Shop/ShopState.cs`, `ShopRoller.cs`, `TowerOffer.cs`; the commands `PlaceTower`, `SellTower`, `SellWall` and `RerollShop`;
  the events `ShopRolled`, `TowerPlaced`, `TowerSold` and `WallSold`.
- **Wall selling (D6):** `SimNode.PieceId`; `SellWall(x, y)` as specified in §4.6; `SimConfig.WallSellRefund`
  and `SimConfig.SellDuringWave`; sales appended to the build log.
- **Telemetry (was WP6):** `Director/Telemetry/WaveRecord.cs` (schema 1, towers and shop included; director
  fields nullable), `TelemetryRecorder.cs` (host-agnostic, driven like `ReplayRecorder`), `TelemetryWriter.cs`
  (append, flush each line, never rewrite), the float formatter in `Thesis.Core.Json`, `PlacementEntropy.cs`
  (exactly §5.9), and the CLI commands `trace` and `telemetry <replay>`.

**Must pass**
- **I10:** with the same seed, offers for waves 1–30 are identical across (a) the Idle and GreedyDetour policies,
  (b) `EscalationPlanner` and a random planner, and (c) buying everything versus buying nothing. A different seed gives different offers.
- Across 100k rolls, each tower's offer frequency is within 1% of its shop weight.
- Buying the same slot twice fails the second time. Buying without enough budget is a no-op and emits nothing.
- A sale refunds `price × SellRefund`, clears the tile, and rebuilds the fields. Selling from an empty tile is a no-op.
- With rerolls off, `RerollShop` is a no-op. With rerolls on, `RerollCount` increases and the offers match the counter-based expectation.
- **Wall selling:** selling a whole piece refunds `cost × WallSellRefund`. After two of its four tiles were
  breached, it refunds half of that. Selling clears only the tiles that still belong to that piece (a tile
  re-built by a later piece is not touched). A sale during a wave is a no-op while `SellDuringWave` is off.
  After a sale the flow field is rebuilt and the route can get shorter. Selling an empty tile is a no-op.
- **Telemetry golden file:** a fixed escalation run produces byte-identical JSONL **in both test runners**.
  Timestamps are excluded or injected through an `IClock`.
- Rows for escalation-condition waves parse with the same `WaveRecord` class as director waves.
- `thesis telemetry <replay>` reproduces the rows a live run wrote, byte for byte.
- **Entropy (§5.9):** tiles spread evenly over all regions give 1; every tile in one region gives 0; nothing
  built gives 0; tiles built before wave 1 are not counted; two sessions of different length are cut to the
  shorter one.

**Review probe:** record a headless replay that includes purchases, tower sales and wall sales, then verify it.

---

## WP-C4 — Unity towers and shop UI (functional, not polished)

**Reads:** §6 · **Depends:** WP4, WP-C2, WP-C3

**Creates** `Towers/TowerView.cs`, `ShopPanel.cs`, `EnemyHealthBar.cs`, `Config/TowerDefAsset.cs`, `EnemyArchetypeAsset.cs`, primitive placeholder prefabs,
the range ring in `GhostPreviewer`, a sell-mode input for towers **and walls** (hovering a wall tile highlights
the whole piece that would be sold and shows the refund), and an HP bar replacing the life bar on `FlowAgent`.

**Must pass (manual playtest, logged in DEVLOG)**
- Buy each offer and place it, with the ghost agreeing with the click (I9) and the range ring matching the real range.
- Towers turn and fire cosmetic projectiles, and enemies lose HP and die. Only anti-air towers shoot flyers.
- Selling a tower or a wall piece refunds the budget; during a wave the sell input is visibly disabled.
  Offers refresh at intermission start and stay unchanged during a wave.
- A Unity session with towers, recorded as a replay, verifies headless.

**Replaces from WP-C1:** the Z / X / C keys and `PlayerBuilder.TryPlaceTower`, the tower drawn as a
tinted wall cube, and the console hint. `FlowAgent`'s bar already shows HP (its field is still
called `lifeBarFill`, because the prefab refers to it).

**Replaces from WP-C2:** `FlowAgent`'s placeholder look for the movement classes (a sapper tinted orange, a
flyer tinted blue and drawn 2.6 units up). `EnemyArchetypeAsset` gives each enemy type its own prefab.
Also worth adding here: the route preview (`PathPreviewer`) draws the ground route only, so nothing on
screen shows where sappers will dig or that flyers ignore the maze.

**Out of scope:** art, VFX, sound, and UI styling. Those belong to the S2 W1–W3 UX pass.

---

## WP-C5 — Tower roster v1 and balance sanity check

**Reads:** §4.6 · **Depends:** WP-C1, WP-C2, WP-C3, **S12**

**Creates**
- 4–6 `TowerDef` assets, covering at least single-target, splash, slow and anti-air across at least 3 damage types.
  They replace `TowerRoster.Placeholder()`: export them to `Maps/Towers.json` the way shapes are exported, load
  that file in the CLI and the tests, and delete the placeholder class.
- 3–4 enemy archetypes: basic, sapper, swarm and flyer. The sapper and flyer numbers are placeholders inside
  `ClassCyclePlanner` today (half the count; sapper 1.5× HP, 0.8× speed, 4× dig rate; flyer 0.5× HP, 1.2×
  speed), and `SimConfig.SapperDigCostFactor` is 0.2. `thesis ascii --map … --class sapper --sapper-factor F`
  shows what a factor does to the route.
- HP escalation numbers
- The CLI command `balance`: policies × 10 seeds × 25 waves, reporting damage per budget spent on each tower, how often each policy buys each tower, and the wave each policy reaches

**Done when** `Results/<date>_balance-v1/README.md` exists with its hypothesis written **before** the run (for example: no tower exceeds 2× the median damage per budget; Idle dies before wave 5; no policy clears 25 waves untouched) and the table, whether it passes or not.
This is a sanity check only. The real balancing happens in the S2 W4 balance pass.

---

## WP7 — Learning assembly (independent: can run in a parallel session)

**Reads:** §5.6, `CLAUDE.md` §5 · **Depends:** WP0 only

**Creates** everything under `Assets/Thesis/Learning/`, plus the CLI command `synth`.

**Maths rule (WP-H):** every `log` and `exp` is `Thesis.Core.DetMath`; no `Math.Log`, `Math.Exp`, `Math.Pow`
or trig (`ForbiddenApiTests` fails the build's tests otherwise). Normal draws use the polar method (`DetMath.Log`
and `Math.Sqrt` only). Compute in `double`, round to `float` once at the end.

**Must pass**
- `BetaSampler`: at 200k draws, the sample mean and variance for (1,1), (2,5) and (30,10) are within 1% of the analytic values. It throws when either shape is below 1.
- `BinnedPosterior`: after 10⁵ random updates with G = 0.95, every α and β is ≥ 1. A strategy with no updates reports `Cold` and a correction of exactly 0.
  `Value` stays within [−1, +1] for any θ.
- `Export` → `Import` → the same RNG seed gives identical `SampleCorrection` sequences.
- **Cross-runtime fingerprint:** a fixed sequence of 10⁵ `SampleCorrection` and `Update` calls hashes to one
  pinned value in both test runners (the same idea as `DetMathTests.AMillionResultsHaveTheSameBitsOnEveryRuntime`).
- `KernelEstimator`: a query at a stored point with σ → small returns that point's reward. The ring buffer overwrites the oldest entry at 129.
- **G1-synthetic:** in `ThresholdBanditEnv` (the best arm flips at a context threshold), `BinnedPosterior` reaches ≥ 85% best-arm selection within 300 episodes, averaged over 20 seeds, and beats uniform-random and round-robin.
- **Calibration diagnostic** *(added 2026-09-29, borrowed from the Jev review; a diagnostic, not a new gate)*: `CalibrationReport` takes
  `(predicted p = posterior mean α/(α+β), realised reward)` pairs and returns the Brier score and a 10-bin ECE. Known-answer tests:
  perfectly calibrated synthetic pairs give ECE ≈ 0 (≤ 0.02 over 10⁵ pairs); pairs where the prediction is always 0.9 but the true rate is 0.5 give ECE ≈ 0.4.
  On `ThresholdBanditEnv`, report ECE after 300 episodes alongside the G1 number. It is written into the G1 results folder and never used to decide pass or fail,
  because the gates were fixed before implementation (`CLAUDE.md` §6).

**Done when** everything above is green and the `synth` command writes `Results/<date>_g1-synth/`.

**Review probe:** set G = 1.0 (no discount), make the environment flip its best arm halfway through, and confirm the learner adapts measurably worse. This shows the discount is actually doing its job.

---

## WP8 — Build profile

**Reads:** §5.3, `CLAUDE.md` §2 · **Depends:** WP-C1, WP-C3 (shop log, telemetry), **S9** (needs the proposal in the repo)

- **WP8a (W9):** `IBuildFeature`, `ProfileInput`, `BuildProfile`, `BuildProfiler`, `MazeLength`, `TowerConcentration`, `DamageTypeMix`
- **WP8b (W10):** `BreachVulnerability`, `ChokepointReliance` (uses the `DamageMap` from WP-C1)

**Must pass (ASCII fixtures for each feature)**
- On an open board, `maze_length` = 1.0 and `breach_vulnerability` = 0.
- A wall across the only corridor gives `breach_vulnerability` > 0. A detour around a wall gives `breach_vulnerability` = 0 and `maze_length` > 1.
- Adjacent towers have higher `tower_concentration` than towers at opposite corners.
- `damage_type_mix`: all damage of one type gives 0, and equal damage across all types gives 1.
- `chokepoint_reliance`: damage spread evenly over the route gives 0, and all damage on one tile gives 1.
- Every feature that uses cost divides by 10 (a regression test fails if that division is removed).
- **Shop confound rule:** on the same board, every feature is identical no matter which unbought offers were rolled.

---

## WP9 — Strategy pool, threat cost table, multiple spawns

**Reads:** §5.4 · **Depends:** WP-C2, WP-C3, **S6**, **S8**, **S10**

**Creates** `Director/Strategies/` (`BreachThinWall`, `SwarmChokepoint`, `SplitGroups`, `FlyingBypass`), `Config/ThreatCostTableAsset.cs`,
multi-spawn support in `MapData` / `Placement` / `SimHost`, and the extra spawn points in SampleScene.

**From WP-C2:** the movement classes these strategies need exist (`AgentGroup.Movement`). Once a strategy sends
sappers and one sends flyers, `ClassCyclePlanner` has done its job: delete it and its `DevClassCycle` condition,
or keep it as a test planner only, and re-record the `class-cycle-6waves` pinned episode with a real strategy.
**Close S14 first:** `KillReward` is per enemy, so strategies that buy fewer, stronger enemies with the same
threat budget pay the player less (measured in WP-C2). The cost table and the kill reward have to be designed together.

**Must pass**
- **I2:** for waves 1–40 and every strategy, `|plan.ThreatSpent − BudgetForWave(w)| ≤ ε`.
- `BudgetForWave(w)` equals the price of the `EscalationPlanner` plan for wave w.
- Plans are deterministic for a given seed. `split_groups` uses at least 2 spawns. `flying_bypass` plans contain flyers. Placement protects every spawn.
- **I11:** a test runs every strategy and fails if the shop state, tower definitions, prices or budget change.

---

## WP10 — Constraints, heuristic, bucketizer, heuristic-only director

**Reads:** §5.2, §5.5 · **Depends:** WP8a, WP9, **S1**

**Creates** `Director/Layers/*`, `Director/Context/ContextBucketizer.cs`, `Director/Decision/*` (with `DirectorMode.HeuristicOnly`),
`Baselines/RandomPlanner.cs`, `RoundRobinPlanner.cs`, `Announce/AnnouncementTable.cs`, `DirectorConfig.cs`

Also, from the 2026-10-02 review:
- **One planner factory.** `Director/PlannerFactory.cs` builds every planner from a name and its config.
  `DirectorHost` (Unity) and `Harness.Registry` (headless) both call it; neither keeps its own list.
- **The next replay schema (§4.5):** `ReplayRecorder` stores each wave's `WavePlan`; `RecordedPlanner` plays them back;
  `ReplayRunner` uses it by default. `replay --rerun-director` rebuilds the planner and compares its plans.
  *(This was written as "schema 2" before WP-C1 and WP-C2 each took a number; it is whatever comes after WP-C3's.)*

**Must pass**
- A vetoed strategy scores `NegativeInfinity`, and a spy estimator that **throws when called** is never called for it (I5).
- When everything is vetoed, the director falls back to escalation and the trace records `fallback: "AllVetoed"`.
- Ties go to the lowest id. The trace contains one row per strategy, in id order.
- `TooSimilarToPrevious` vetoes a plan identical to the previous wave's and allows a plan above the threshold.
- `Uncounterable` vetoes `flying_bypass` when the player owns no anti-air tower and none is on offer, and allows it as soon as one is on offer.
- A session run with the random planner replays exactly from its recorded plans **with the planner removed
  from `PlannerFactory`** (the simulation replay must not need the planner). Replays of the schema before
  it still load, with their planner rebuilt by name as today: that step changes the file, not the rules.

---

## WP11 — Reward, credit, full director

**Reads:** §5.1 (the timeline), §5.6, §5.7 · **Depends:** WP7, WP10, **S2**, **S3**, **S4**, **S5**

**Must pass**
- **Sequencing:** a scripted fake timeline shows that the reward for wave N is computed inside `PlanWave(N+1)`, *before* the decision
  for N+1, and never uses an outcome that has not resolved yet.
- Credit weights are 1, 0.5 and 0.25 for lags 0, 1 and 2. Nothing is credited beyond the configured window.
- The `binary` reward equals `(restructure >= tau) && survived` in all four combinations.
- The final correction is always within [−1, +1]. Cold strategies get 0.
- `DirectorMode.HeuristicOnly` and `DirectorMode.Full` with an always-cold estimator choose identically (I4 ablation sanity check).
- The replay stores the `DirectorConfig` and the estimator snapshot the session started from, and
  `replay --rerun-director` reproduces every recorded plan from them on the same runtime.
- A director run recorded under .NET is re-run under Mono (and the reverse) with identical plans: the
  pinned-episode set gains one full-director episode.

**Review probe:** use the CLI `trace` on a 25-wave headless run and read three rows by hand. Check that the numbers in the rows are consistent with each other.

---

## WP12 — Harness: policies, counterfactual, ladder

**Reads:** §7 · **Depends:** WP5, WP11

**Creates** the rest of `Harness/Policies/*`, `Counterfactual.cs`, `Ladder.cs`, `ResultWriter.cs`, and the CLI command `ladder`.

*(From WP-C2: none of the stand-in players knows about movement classes. Against the development planner's
cycle, `Sentry` clears the sapper, flyer and mixed waves (its towers stand beside the straight line, which on
an open board is also the flyers' line) and then loses in wave 5, five waves earlier than against the
escalation, because half-size waves pay half the kill rewards (S14). `mixed` loses to the first sapper wave.
A policy for the ladder needs to buy anti-air when flyers come and to stop relying on a wall when sappers do.)*

*(From WP-C1: three stand-in players exist. `Sentry` (towers beside the route) clears nine waves of the
placeholder numbers; `GreedyDetour` (walls only) and `Idle` lose in wave 1, because walls alone no longer
win anything; `mixed` (walls, then towers) loses in wave 2, weaker than towers alone because its walls eat
the budget. Towers placed first and walls second loses in wave 1: the walls reroute the enemies away from
the towers just bought. None of them sells, reacts to damage types, or looks more than one step ahead.)*

*(From WP5: `GreedyDetourPolicy` as built is a weak player. On SampleScene's real settings it loses
in wave 1 or 2, because it maximises flow-field **cost** and a swarm chews through one wall tile
almost at once, so cost is a poor stand-in for time. The ladder needs policies that actually
survive; judge them after WP-C1, when damage, not time, decides a wave. `Registry` is where new
policies and planners get their names.)*

*(From the 2026-10-02 review.)*
- **Episodes run in parallel.** They are independent and `Thesis.*` has no static mutable state, so the
  ladder runs one episode per core. A search-based policy is slow (the greedy one took about 15 s per
  25-wave episode), and the ladder is policies × rungs × seeds.
- **Each clone gets its own planner.** Never pass the live director to `Simulation.Clone` (§4.4).
- **A limit to state in the results:** a scripted policy acts only at the start of a build phase. People
  also build and sell in the middle of a wave.

**Must pass**
- A counterfactual on a cloned state leaves the original simulation's hash unchanged, **and leaves the real
  director's estimator digest unchanged**.
- Running the ladder on 1 core and on all cores gives identical result files.
- `ResultWriter` refuses to write a table when the hypothesis section is still empty.

**Done when** `ladder` writes `Results/<date>_ladder/README.md` with the rungs random, escalation, heuristic and full, for every policy.
**Record the result whether or not the ladder holds** (`CLAUDE.md` §6).

---

## WP13 — Gates G1–G3

**Depends:** WP12 · Adds `Harness/Gates/*` and the CLI command `gate`. Each gate writes its own `Results/` folder, recording the pass and fail numbers
from `CLAUDE.md` §6 **before** the run and the decision the result implies afterward.

---

## WP14 — Director UX in Unity

**Depends:** WP-C4, WP10 · The announcement line shown before each wave (`AnnouncementTableAsset`), the `DirectorOverlay` on F9
(the latest `DirectorTrace` as a table), estimator snapshot import, and **session setup (D9, §6)**: a start screen that takes a
participant code and looks up that participant's two sessions (condition and seed set for each) in an assignment table
shipped with the build; participant, session number, condition and seed are written into the replay and telemetry.
The exception fallback itself exists since WP-H (`SafePlanner`); this package surfaces it.
**Must pass:** throwing inside `WaveDirector` during play shows the fallback in telemetry and does not crash the session.
The four groups of §6 each give the two sessions the table says. A development run with no participant code still
uses seed 1.

---

## Before the pilot — checklist

None of these is a package; each is small, and each protects the pilot's data.

- [ ] **Player-build replay check (D5).** Build the Windows player, play a session to at least wave 3, copy
      its `replay.json` out of `persistentDataPath/Sessions/`, and run `thesis replay <file> --per-tick`. Also
      open the file and check that every field has a value: a stripped build would write empty objects.
- [ ] **S7 frozen (D7).** The metric in §5.9 confirmed with the supervisor and marked frozen.
- [ ] **A session played by hand** in the editor verifies headless (the WP4 playtest checklist in DEVLOG).
- [ ] **Company and product name set** (still `DefaultCompany/InternProj`): they decide where the data is written.
- [ ] **Exports are current.** `Maps/*.map.json`, `Maps/Shapes.json` and the config the harness uses match
      the scene and the `SimConfig` asset. (Today the game reads the asset and headless reads the C# defaults;
      they are equal, but nothing checks it. Add an export for the config and a check before the S2 balance pass.)
- [ ] **Session setup works** for all four groups (WP14).
- [ ] **Telemetry and the replay agree:** `thesis telemetry <replay>` reproduces the pilot build's own rows.
