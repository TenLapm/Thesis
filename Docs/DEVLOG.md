# DEVLOG.md

Append-only. One entry per work package (see `Docs/WORKPLAN.md`). Record what
changed, **deviations** from the plan (these become the limitations chapter), the tests
added, the exact commands run, and known issues. Newest entry at the bottom.

---

## 2026-09-15 — WP0 Scaffolding

**Changed**
- Assembly definitions: `Assets/Thesis/{Core,Sim,Learning,Director,Harness}/Thesis.<Name>.asmdef`.
  Every one has `noEngineReferences: true`, references by name, and `overrideReferences` with
  `Newtonsoft.Json.dll`. `Thesis.Harness` is Editor-only. Each assembly has an
  `AssemblyInfo.cs` that grants internals to `Thesis.Tests.EditMode` (Unity) and
  `Thesis.Tests` (dotnet). This also gives the otherwise-empty assemblies one source file each.
- `Assets/Tests/EditMode/Thesis.Tests.EditMode.asmdef`: references the test runner and all five assemblies, with `UNITY_INCLUDE_TESTS`.
- `Thesis.Core`: `IRandom`, `Pcg32` + `Pcg32State`, `RngStreams` (with `Shop = 7` reserved
  for the counter-based shop RNG), `Fnv1a64`, `TileCoord`, `Vec2f` (including a Unity
  `MoveTowards` port), and `Json` (shared Newtonsoft settings).
- `Packages/manifest.json`: added `com.unity.nuget.newtonsoft-json` 3.2.2, the version already
  resolved as a transitive dependency. `packages-lock.json` changed as a result.
- `Tools/dotnet/`: `Directory.Build.props` (LangVersion 9.0, netstandard2.1 for the libraries),
  `Thesis.Headless.sln`, one csproj per assembly that glob-includes `Assets/Thesis/<Name>`,
  `Thesis.Tests` (net9.0, NUnit 3.14), and `Thesis.Cli` (net9.0; prints usage only).
- `.gitignore`: added `!` exceptions for `Tools/dotnet/**/*.csproj` and `*.sln` (the Unity
  template ignores all of them), ignored `Tools/dotnet/**/bin|obj`, and ignored `/Runs/`.

**Deviations**
- Docs had said Newtonsoft package **3.2.1**. The version actually resolved was **3.2.2**
  (still Newtonsoft.Json 13.0.2). The docs are updated.
- `Fnv1a64` stores `hash XOR offsetBasis` internally so that `new Fnv1a64()` is a valid empty
  hash. C# 9 structs cannot have a parameterless constructor, and a zero-initialised FNV
  state would silently produce wrong hashes. This is not in the architecture doc, but it
  does not change the public API.
- `Vec2f.MoveTowards` / `Magnitude` use `(float)Math.Sqrt`, as Unity's `Vector3` does, rather
  than `MathF.Sqrt`, so the port matches Unity's rounding.

**Tests added:** 24, in `Assets/Tests/EditMode/Core/`
- `Pcg32Tests` (9): the pcg-c-basic `pcg32-demo` known-answer values for seed 42 / stream 54
  (**matched on the first run**), Save/Load and FromState round-trips, stream independence,
  determinism, bounded draws (range, uniformity, invalid arguments), and unit-interval draws.
- `Fnv1a64Tests` (6): empty = offset basis, `"a"` = `0xaf63dc4c8601ec8c`, little-endian ints,
  bit-pattern floats (+0 and −0 differ), length-prefixed strings, order sensitivity.
- `Vec2fTests` (5): MoveTowards lands exactly on the target, never overshoots, takes exact partial steps, is stable at the target; TileCoord equality.
- `JsonTests` (4): `-Infinity` round-trips as a string, floats round-trip bit-exactly, `Pcg32State` round-trips, and nulls are written.

**Commands run**
```
dotnet test Tools/dotnet/Thesis.Headless.sln                  → Passed 24/24 (≈0.5 s)
dotnet run --project Tools/dotnet/Thesis.Cli -- help          → usage, exit 0
Unity MCP: refresh_unity(force, compile) + read_console        → 0 errors, 0 warnings
Unity MCP: run_tests EditMode, assembly Thesis.Tests.EditMode → Passed 24/24
```

**Review probe (done):** added `Assets/Thesis/Sim/ProbeEngineReference.cs` containing
`using UnityEngine;`. **Both** toolchains rejected it with the same error:
`dotnet build` (CS0246 `UnityEngine`) and Unity (CS0246 in the console). The probe was
then deleted, Unity recompiled cleanly, and the tests passed again (24/24).

**Known issues**
- The first build found a missing `Vec2f ==/!=` operator. `dotnet` caught it before Unity
  did, as designed, and it was fixed in the same package.
- `dotnet` prints "Workload updates are available". This is harmless and unrelated.
- Nothing is committed yet.

---

## 2026-09-15 — WP1 Grid and flow-field port

**Changed**
- `Thesis.Sim`: `Map/MapData.cs` (validated; rows stored top-down as readable strings) and `Map/WorldPoint.cs`;
  `Grid/SimNode.cs`, `Grid/SimGrid.cs` (`NodeFromPosition`, `GetNeighbors` into a reused buffer, `SetWall`/`ClearWall`, `Clone`),
  `Grid/FlowField.cs` (SPFA ported verbatim), `Grid/Route.cs` (`CountHops`, `Collect`, `CostInTiles`), and `Debug/AsciiMap.cs` (parse and render).
- `Thesis.Harness`: `Scenarios/BenchScenarios.cs` (OpenField, Maze and ChokePoints from `ScenarioBenchmark.BuildScenario`).
- `Assets/Scripts/Editor/MapExporter.cs`: menus `Thesis/Export Map (Active Scene)` and `Thesis/Export All Maps`.
  It refuses to run when any open scene has unsaved changes, and restores the open scenes when done.
- `Maps/SampleScene.map.json` (38×38, core (34,34), spawn (3,3)) and `Maps/Bench_{Open,Maze,Choke}.map.json`
  (38×38, core (18,36), spawn (18,1)).
- `Thesis.Core`: `Json.SerializeIndented`, and `[JsonIgnore]` on `Vec2f.SqrMagnitude/Magnitude` so computed values don't leak into files.

**Deviations**
- WP1 asked for "`NodeFromPosition` equals a hand-computed port for 1,000 random positions". Test assemblies must build
  without UnityEngine, so this is done **inside `MapExporter`** against Unity's real `Mathf.Clamp01`/`RoundToInt`
  instead: 4,000 samples per scene (including off-map clamping), plus every tile centre compared with `CreateGrid`'s
  `worldPoint`. All 4 scenes had 0 mismatches, and the exporter refuses to write a map if there are any. The pure tests cover
  centres, the boundary skew (hand-computed values) and clamping.
- **Review probe expectation was wrong** (WORKPLAN corrected). Swapping the `GetNeighbors` loops is caught by the 4
  neighbour-order tests, but the Maze parity test still passes: path length and cost don't change under a different
  tie-break. The benchmark files therefore cannot confirm Unity's exact tie choices. The port matches them only by
  copying the loop order, which `NeighbourOrderIsXOuterThenYInner` pins.
- `SimNode` has no `bestDirection`; views can derive it from `NextIndex` (it was only used for gizmos and arrows).
- `Route.CountHops` keeps the benchmark's quirk: an unreachable start counts as 1 hop.
- `BenchScenario` has no `RandomScatter`, because its layout came from `UnityEngine.Random.InitState(12345)`, which can't be reproduced. `Benchmark_Stress100.json` therefore has no parity check.

**Findings**
- SampleScene exports with **0 static blockers** even though `unwalkableMask` is layer 6 ("Obstacle"). This is correct: the only
  two layer-6 objects (`Cube`, `Cube (1)`) are inactive, so the game's own `Awake` scan sees none either.
- `GridManager` sits at world (0,0) in all 4 scenes (the exporter warns otherwise), so `NodeFromWorldPoint` ignoring the origin causes no offset today.
- SampleScene's `gridWorldSize` of 75 does not equal 38 × diameter 2 = 76, so tile boundaries are skewed. For example, tile 1 starts only 0.0135
  world units past tile 0's centre. This is kept verbatim and documented in `SimGridTests.NodeFromPositionKeepsTheOriginalBoundarySkew`.

**Tests added:** 37 (61 total)
- `SimGridTests` (11): neighbour order; corner-cut against a wall and against static geometry; open diagonal; buffer size; tile centres; every centre maps back to its tile; boundary skew; clamping; `SetWall` validation; clone independence.
- `FlowFieldTests` (10): goal is 0, cardinal step 10, diagonal 14; terrain multiplier; detour beats digging with a hand-traced tie-break; every chain reaches the goal with strictly decreasing cost; unreachable pocket clears stale pointers; static blockers never entered; unwalkable goal; rebuild after a breach; hop count.
- `MapDataTests` (8): JSON round-trip; readable rows and no computed fields; top-down orientation; 4 validation failures; a missing file names the export menu.
- `AsciiMapTests` (5): parse orientation; render → parse round-trip; route layer; rulers; bad input.
- `BenchParityTests` (3): **path tiles, cost and wall count equal the committed `Benchmark_Open/Maze/Choke.json`**. The expected values are read from those files, not copied into the test.

**Commands run**
```
dotnet test Tools/dotnet/Thesis.Headless.sln                  → 58/61 before export (parity: no maps yet), 61/61 after
Unity MCP: execute_menu_item "Thesis/Export All Maps"         → 4/4 exported, 0 cross-check mismatches
Unity MCP: run_tests EditMode, Thesis.Tests.EditMode          → Passed 61/61, console clean
Probe: GetNeighbors loops swapped                             → 4 failed (order tests), parity still passed; reverted → 61/61
```

**Known issues:** none. Nothing is committed yet.

---

## 2026-09-16 — WP2 Agents, placement, shape bag, economy

**Changed**
- `Thesis.Sim`: `Commands/SimCommand.cs` and `Events/SimEvent.cs` (the full command/event
  enums from ARCHITECTURE §4.5, though WP2 only raises `WallPlaced`, `WallBreached`,
  `AgentStalled`, `AgentLeaked` — dispatch and the rest of the events are WP3's job);
  `Stats/OccupancyMap.cs`; `Agents/AgentState.cs` and `Agents/AgentSystem.cs` (the
  step-3 agent pass, ported from `FlowAgent.Update`); `Build/ShapeDef.cs` (mutable,
  `Rotate()` ported verbatim, `Clone()` plays the role `Instantiate()` played),
  `Build/ShapeBag.cs` (7-bag, preview queue, hold, ported from `BlockManager`),
  `Build/PlacementRecord.cs`, `Build/Placement.cs` (`CanPlace`/`TryPlace`, ported
  from `PlayerBuilder`, generalised from one spawn to `map.Spawns`).

**Deviations**
- `ShapeDef` has no separate "rotation count"; like `BlockShape`, only `LocalTiles`
  itself encodes the current orientation. `ShapeBag` tracks `CurrentRotationTurns`/
  `HoldRotationTurns` alongside `CurrentShape`/`HoldShape` purely so a
  `PlacementRecord` can log a `rot` field — the original never needed this because
  nothing in it wrote that kind of telemetry.
- A breach used to call `GenerateFlowField()` **immediately** inside `ChewWall`, so
  later agents in the same Unity frame already saw the new field. `AgentSystem.Step`
  only reports `fieldDirty`; the rebuild is the caller's job, once, after every
  agent has moved. This was already flagged as an intended change in
  `Docs/ARCHITECTURE.md` §4.2, not a new deviation — recorded here because WP2 is
  where it first has an observable effect (the eight-agents-on-one-tile test would
  give a different, still-correct, credited-once result under either rule).
- `Placement.CanPlace` protects **every** entry in `map.Spawns`, not one `spawnPoint`
  — this was WP2's explicit brief (multi-spawn support lands properly in WP9, but
  the legality check already had to be spawn-count-agnostic since `MapData` is).

**Findings**
- `Assets/Scripts/BlockS/` has 9 `.asset` files (`I J L O S T Z Cube Wall`), but
  `SampleScene`'s `BlockManager.shapeLibrary` only wires up 7 of them — the
  standard tetrominoes (`I J L O S T Z`). `Cube` and `Wall` exist as assets but are
  not part of the live game. WP2's "7-bag" tests use a 7-shape library to match
  what SampleScene actually plays.

**Tests added:** 29 test results (61 → 90 total; both runners report 90). *(Corrected
2026-09-29: this line originally said "33 (94 total …)", which was a miscount.)*
- `OccupancyMapTests` (3): increment per node, reset, rejects a negative size.
- `AgentSystemTests` (9 results, 7 methods): **two agents breach in exactly half the ticks of one**
  (6 vs 3, hand-computed); **breach reward credited exactly once for 1, 2 and 8
  agents sharing a tile** (this is also WP2's review probe — see below); a stalled
  agent credits `deathReward` and never touches core HP; a leaked agent costs 1
  core HP and pays no reward; movement with `speed*dt` more than double the tile
  spacing still lands exactly on the next tile, never past it; a dead agent is
  fully skipped; occupancy and `MinCostSeen` update for live agents and are left
  untouched for one that leaks on its first tick.
- `ShapeBagTests` (6): rotation matches `(x,y) -> (y,-x)` over all 4 turns; a
  clone's rotation never touches its master; **every consecutive run of 7 draws is
  a permutation of a 7-shape library** (verified across 4 bags, i.e. 28 draws);
  rotating a drawn instance never leaks into a later draw of the same shape; hold
  works once per piece and preserves the held piece's rotation across a swap back;
  rejects an empty library.
- `PlacementTests` (11 results: 6 methods, one of them a 6-case `TestCaseSource`): the legality table
  (off-map, static blocker, existing wall, spawn, core, valid) across all 4
  rotations of a 2-tile shape on a hand-verified map; every spawn is protected,
  not just the first; a successful placement spends the budget, sets
  `terrainCost`/`wallHealth`, rebuilds the field, logs one `PlacementRecord`, and
  emits one `WallPlaced` event per tile; `digCost`/`wallHealth` are clamped to
  their floors (2 and 0.5); an illegal or unaffordable placement changes nothing;
  budget never goes negative across repeated placements.

**Commands run**
```
dotnet test Tools/dotnet/Thesis.Headless.sln                  → Passed 90/90 (~0.7 s)
Unity MCP: refresh_unity(force, compile) + read_console       → 0 errors, 0 warnings
Unity MCP: run_tests EditMode, Thesis.Tests.EditMode           → Passed 90/90
```

**Review probe (done, as a real test rather than a throwaway check):**
`AgentSystemTests.BreachRewardIsCreditedExactlyOnceRegardlessOfAgentCount(8)` seeds
8 agents onto one wall tile with health 0.01 and asserts exactly one
`WallBreached` event and a budget increase of exactly one `wallBreakReward`. Passed.

**Known issues:** none. Nothing from this session is committed — the user asked
for WP2's implementation only, not a PR this time.

---

## 2026-09-29 — Docs: Jev review

- `CLAUDE.md` §8: new "explicitly rejected" row for Jev (TypeSafe AI) and any
  hosted/pretrained System-One decision model inside the director. It breaks I1 and
  I3, doesn't learn per player, and would change what the I4 ablation measures.
  §10: katgpt-rs's "10⁵–10⁷× faster than Jev" scoreboard added to the not-citable
  list; the TypeSafe blog added as a related-work contrast case; Guo et al. 2017 and
  Brier 1950 added for the calibration check.
- `WORKPLAN` WP7 and `ARCHITECTURE` §3/§5.6: `CalibrationReport` (Brier score and 10-bin ECE of
  posterior mean vs realised reward). It is reported next to G1 as a **diagnostic, not a
  gate**, because the gates were fixed before implementation.
- Corrected the WP2 test counts in this log: 29 results, 61 → 90 (it said 33/94).
- WP0–WP2 are committed (`717ca5e`, then the student's `de350b1` "WP0-2"), so the
  "nothing committed" notes in the entries above are out of date.

---

## 2026-09-29 — WP3 Wave lifecycle, planner seam, escalation baseline

**Changed**
- `Thesis.Sim`: `SimConfig.cs` (its field initialisers are SampleScene's serialized values,
  not the scripts' C# defaults), `SimPhase.cs`, `SimState.cs` (+ internal `SpawnSlot`),
  `Simulation.cs` (the §4.2 tick loop, `Enqueue`, `Clone`, `ComputeHash`, the mutation
  guard, `ValidatePlan`), `Debug/StateHasher.cs`, and `Waves/`: `IWavePlanner`,
  `IThreatPricer`, `WaveContext`, `WavePlan`, `AgentGroup`, `WaveOutcome`, `EscalationPlanner`.
- Small additions to WP2 types: `AgentState.WaveIndex`, `AgentState.Leaked` (set by
  `AgentSystem`), `AgentState.Clone()`; `ShapeBag.CloneWith()` / `AddToHash()`;
  `OccupancyMap.Clone()` / `AddToHash()`.
- `Fnv1a64.Add(uint)` now does its four bytes in one pass instead of four `AddByte`
  calls. The output is byte-identical (the WP0 known-answer and little-endian tests
  still pass). It is the hot path of per-tick hashing.

**Deviations / decisions (all intentional)**
- **D4 implemented**: the intermission starts when the wave's last agent resolves. The
  stipend is paid at that moment, *after* `WaveOutcome.BudgetAfter` is recorded, so the
  outcome measures only the wave.
- **Core destroyed mid-wave**: the wave is closed with `CoreDestroyed = true`, the
  planner gets `OnWaveResolved`, `WaveResolved` is emitted, and then `GameOver`. Step 5
  (normal resolution) is skipped when `CoreHp <= 0`. After game over, `Tick()` is a
  no-op and discards queued input, so the hash stays constant.
- **The mutation guard is always on**, not DEBUG-only as ARCHITECTURE §4.4 first said. It
  also wraps `OnWaveResolved`, because a planner can keep the `SimState` reference from
  `PlanWave`. The cost is two state hashes per wave boundary, far inside G2's 16 ms.
- **`IThreatPricer`**: an optional constructor argument (null in WP3). With a pricer,
  `ValidatePlan` checks both `|ThreatSpent − budget| ≤ ε` and `|price − ThreatSpent| ≤ ε`,
  so a plan can't misreport its own spend. The real cost table is WP9.
- **`RebuildFieldForSetup()`**: a public hook so benchmark layouts and fixtures can put
  walls on the grid before tick 0. It throws after the first tick, so commands remain
  the only way in during play.
- **Spawn timing**: `WaitForSeconds(delay)` becomes `round(delay / dt)` ticks, e.g.
  0.15 s → 8 ticks = 0.16 s; the original was frame-quantised too. Agents enter at the
  spawn transform's XZ, not the tile centre, exactly where `WaveSpawner` put them. They
  act on the tick they spawn; life and movement start together, so there is no bias.
- **`StateHasher` skips dead agents.** A dead agent is frozen (`AgentSystem` never
  touches it again) and was hashed while it was alive. Hashing the ever-growing list every tick
  would make the 20k-tick tests quadratic. `Agents.Count` is still hashed.
- **Rewards (`DeathReward`, `WallBreakReward`) and `DigRate` live in `SimConfig`, not in the
  plan**: rewards pay the player, and I11 keeps the director away from the budget.
- `WaveOutcome` gained `CoreDestroyed`, `PressureCount` and `FirstAgentId`.
  `PressureRadiusTiles = 5` is a placeholder until spec gap S5 is closed.

**Findings**
- In SampleScene, `FlowFieldManager.targetGoal` is `PlayerCore`'s own transform
  (fileID 427945555), so `MapData.CoreWorld` is the right point for the lifetime
  derivation. The SampleScene spawn (−32, 2, −32) to core (32, 2, 32) distance is √8192 = 90.51, so
  the **base lifetime is 77.41 s** (the serialized 60 is overwritten at `Start()`).
- **An idle player loses in wave 1**, at tick 5,871 (117.4 s = 45 s prep + 72.4 s of
  travel): 10 of 100 agents leak and none stall. That is the game working as designed:
  the lifetime is the straight-line travel time plus only 5 s.
- The first determinism run (random input on an open board) passed but **never dug**:
  0 breaches, 0 stalls, because scattered walls leave detours. I added a second run with five full
  wall lines. It gives exactly 5 breaches, one per line, because once a line is holed
  the field sends every later agent through that hole. It also has 30 stalls, and the hashes
  match for all 20k ticks.
- **Review probe** (Bench_Choke with ChokePoints, 25 waves, dt 0.02 vs 0.01): the stall/leak
  counts are **identical**. Waves 1–7 all stall and 8–25 all leak, because every agent in an
  escalation wave is the same. The informative measure is the first-exit time. Stall
  times are identical, since they depend only on lifetime (60.20 s = Bench_Choke's base
  lifetime, plus 4 s per wave). Leak times differ by **0.00–0.03 s** (at most 1.5 coarse ticks),
  because movement is discretised. So the runs are close but not identical, and no rule counts ticks.
  WORKPLAN's probe text was refined to match.

**Tests added:** 36 headless results (90 → 126), plus the explicit probe (Unity runs it too: 127)
- `EscalationPlannerTests` (11): the base-lifetime derivation; 7 hand-computed anchor waves
  (1, 2, 4, 27, 28, 30, 32, covering the delay clamp, the speed cap and the life cap);
  waves 1–30 against the `WaveSpawner` formulas written with SampleScene's *literal*
  numbers, which also checks `SimConfig`; the pricer fills `ThreatSpent`; wave 0 is rejected.
- `SimulationPhaseTests` (7): prep is exactly 2,250 ticks; no stipend before wave 1; D4
  (a wave stays in Resolving while any agent lives); the intermission is 500 ticks and the
  stipend is paid after the outcome closes; the intermission timer starts wave 2; `StartWaveNow` is
  ignored mid-wave; groups interleave by tick with start delays; losing the core closes the
  wave, tells the planner and freezes the hash; placing a shape spends, logs and consumes the piece.
- `DeterminismTests` (8): **20k ticks with an identical hash on every tick** (random input, 4 waves,
  30 walls, 430 agents); the same under forced digging (5 breaches, 30 stalls); different
  seeds differ; a clone plus 5k ticks stays identical and independent; mutating a clone's grid
  cost, an agent's position, the budget, or the RNG state each changes its hash and leaves the original's alone.
- `PlannerContractTests` (9): the guard catches writes in `PlanWave` and in
  `OnWaveResolved`; `ValidatePlan` rejects Count < 0, a bad spawn index, zero lifetime,
  a spend that misses the budget, and a spend the cost table disagrees with; it accepts an exact
  spend; a planner can't edit a plan after handing it over.
- `SampleSceneRunTests` (1 + probe): an idle player loses in wave 1 at a plausible time.

**Commands run**
```
dotnet test Tools/dotnet/Thesis.Headless.sln                         → Passed 126/126 (~6 s)
dotnet test ... --filter ProbeTickLengthSensitivityOnBenchChoke       → table above
Unity MCP: refresh_unity(force, compile) + read_console              → 0 errors, 0 warnings
Unity MCP: run_tests EditMode, Thesis.Tests.EditMode                  → Passed 127/127 (~15 s)
```

**Known issues / next**
- Unity (Mono) and `dotnet` (CoreCLR) both pass the determinism tests, but nobody has yet checked
  whether they produce the **same** hash for the same run. That cross-runtime
  check is WP5's done-when condition. *(Checked in WP4 below: they did NOT, now fixed.)*
- Nothing from WP3 is committed.

---

## 2026-09-29 — WP4 Unity host rewire

**Changed**
- New (`Assets/Scripts`): `SimHost` (runs the `Simulation` from a scaled-`deltaTime`
  accumulator in fixed 0.02 s ticks, at most 20 ticks per frame; execution order −100;
  builds lazily, so any script can reach it from any `Awake`), `DirectorHost` (static
  escalation only for now), `SceneMapBuilder` (the scene → `MapData` scan, shared by
  `SimHost`, `ScenarioBenchmark` and `MapExporter`), and `Config/SimConfigAsset` plus
  `Config/SimConfig.asset` (it wraps the `[Serializable]` `SimConfig` directly, so there
  is no second copy of the field list).
- Adapters, keeping the member names the HUD reads:
  - `WaveSpawner`: wave read-outs, and the agent view pool driven by sim events.
  - `BlockManager`: one stable `BlockShape` clone per simulation shape instance, so a new
    piece is still a new reference and a rotation still bumps `shapeVersion`.
  - `PlayerCore`: HP changes become the existing UnityEvents; still freezes `timeScale` on game over.
  - `PlayerBuilder`: a click becomes a `PlaceShape` command. Walls are drawn from
    `PlacementLog` and `WallBreached`, not from the click.
  - `FlowAgent`: pure view, no `Update`, and interpolates between ticks.
  - `FlowFieldManager`: binds to whichever `SimGrid` is running.
  - `GridManager` and `Node`: slimmed to view data.
- Ported to read the simulation grid: `PathPreviewer` (now only redraws when the field
  changes) and `FlowFieldVisualizer` (arrows derived from `NextIndex`).
- `ScenarioBenchmark` now runs on `SimGrid` + `AgentSystem` at the fixed 50 Hz step
  and writes to **`Runs/`** instead of the project root.
- `BenchmarkRunner` deleted. It was in no scene. Its two pure scenarios (the rebuild
  sweep and the A* comparison) are now `Thesis.Harness.PathfindingBench` and the CLI
  command `thesis bench`. The two Unity-only ones (the agent tiers and edits under load)
  are covered by `ScenarioBenchmark`'s tiers; edits-under-load was not ported.
- `Thesis.Sim`:
  - `Simulation.FlushInput()` applies queued commands between ticks. This is needed
    because rotate and hold must work while paused, when no ticks run.
    `StartWaveRequested` moved into `SimState` so it survives a flush.
  - `BenchScenarios` moved from `Thesis.Harness` (Editor-only) into `Thesis.Sim/Debug`,
    because runtime code needs it. `RandomScatter` was restored on a seeded PCG.
- **Cross-runtime float fix**: explicit `(float)` casts on every chained float
  intermediate in `Vec2f`, `AgentSystem`, `SimGrid`, `EscalationPlanner`, `Simulation`,
  `SimConfig.Ticks`, `BenchScenarios` and `AsciiMap`. The new rule is ARCHITECTURE §9 rule 3.

**Deviations / decisions**
- `CanvasDashboard`, `GhostPreviewer`, `GameSpeedController`, `CameraMovement`, `Billboard`,
  `WallSpawnAnimator` and `BlockShape` have **no diff**, as planned.
- Input goes through `SimHost.Submit` → `FlushInput`, so it applies at once, not at the next tick.
  `InputTests.FlushInputEqualsApplyingAtTheNextTick` shows this is replay-equivalent
  (6,000 ticks, identical hash every tick). This is the answer to "rotate and hold while
  paused", which a tick-only model couldn't give.
- `ScenarioBenchmark` output moved to `Runs/`, so the committed `Benchmark_*.json`
  files stay the pre-port parity oracle.
- `BenchScenario.RandomScatter` uses `Pcg32(12345)`. The original's `UnityEngine.Random`
  layout can't be reproduced, so Bench_Stress's layout differs (it was already excluded from parity).
- Serialized fields removed from SampleScene when re-saved: `WaveSpawner`'s 14 tuning values,
  `PlayerCore.maxHealth` and `BlockManager.buildBudget`. **All 16 were checked against the
  committed scene and equal the values in `SimConfig.asset`**. Refs that are no longer
  needed were also dropped: `WaveSpawner.flowManager/blockManager/playerCore` and
  `PlayerBuilder.flowManager/enemySpawnPoint`.
- `SimHost` drops any tick backlog beyond one tick after a long hitch, so the game slows
  down rather than trying to catch up. This never desyncs: the sim only ever sees whole ticks.

**Findings**
- **Cross-runtime divergence (I1), found and fixed.**
  - What happened: a live SampleScene session played in the editor ended at tick 5036 with hash
    `305650b7869ff480`. Unity's test runner (Mono) replaying the same input from a script gave the
    same hash, which proves the script reproduces the session. `dotnet` (CoreCLR) gave `d88b7dfd957f0dcb`.
  - Where: a per-tick bisect (`CrossRuntimeBisectTests`) put the first difference at **tick 2916**,
    in agents only (grid, bag, occupancy and budget were equal): agent 0's `Position.Y`
    `c0a36c3d` (Mono) vs `c0a36c3c` (CoreCLR), i.e. one bit.
  - Why: a brute force over rounding choices reproduced Mono only when `toY / dist * step` and
    the add were evaluated in double and rounded once. CoreCLR rounds each step. C# permits both.
    A fingerprint of *single* float operations matched exactly under both runtimes, as
    theory predicts (double rounding is harmless for one +, −, ×, ÷ or √). So only chains diverge.
  - Fix: explicit casts, which the spec requires to round. After the fix, **all 5,036 ticks have
    identical full-state and component hashes under Mono and CoreCLR**, and
    `FloatDeterminismTests` pins the diverging step in both runners.
  - Even the *test* `EscalationPlannerTests.Waves1To30…` failed under Mono at wave 14
    (1.73750007 vs 1.73749995), because its own uncast formula ran in double. It was fixed the same way.
- **The screenshot tool pauses the editor** (`EditorApplication.isPaused = true`), which froze
  ticks and the wall pop animation in the smoke test. This is not a game bug. Unpause after capturing.
- **Recompiling during play mode** (Unity's default when a script changes) left the old
  session's wall visuals in the scene and restarted the simulation at tick 0. That rescan then
  counted those walls as static blockers (62 tiles), because the wall prefab and the ghost tiles are
  on the **Obstacle layer with colliders**. This is harmless in a real session, since the scan runs
  once at start before any wall exists, but it is a trap. Set Preferences → General → Script
  Changes While Playing → "Recompile After Finished Playing".
- **The flow field charges the tile being LEFT, not entered**: `neighbor.TerrainCost` in the
  outward flood. An agent standing on a wall pays for it; the goal tile is never charged.
  The original `BenchmarkRunner` A* charged the entered tile, so its costs differed from the
  field's whenever a start was a wall (258 vs 398 in the first cross-check). The ported A* now
  matches the field, and `PathfindingBenchTests` checks A* = field on every tile.
- **Performance, SampleScene-scale Maze, same machine, separate sessions, so indicative only.**
  Per frame at 100/250/500/1000 agents: 5.59/8.37/13.61/37.61 ms before vs 5.35/8.38/11.27/29.64 ms after.
  Field rebuild: 6.13 → 1.41 ms (the non-allocating neighbour buffer).

**Automated smoke test (SampleScene, play mode, driven through Unity MCP)** ✅
- `[Sim] Started: map 'SampleScene' 38x38, seed 1, planner 'escalation', config 'SimConfig'`, 0 errors.
- The prep countdown tracks ticks exactly (tick 342 → 38.2 s left = 45 − 6.84).
  The HUD showed HP 10/10, budget 60, the current piece and 3 previews with icons and costs.
- Rotate keeps the same piece reference and bumps the version. Hold swaps. A second hold is ignored.
- 5 placements: budget 60 → 40, 20 wall tiles, 5 field rebuilds, spawn route cost 434 → 452.
  The route line bends and the walls pop to full size.
- Wave 1 (via the Start handler) spawned 100 agents with exactly 100 views, each within 0.013 world units of its agent.
- 10 leaks → HP 0 → game over. The Game Over panel reads "You reached Wave 1", `timeScale` froze at 0,
  and the 10 leaked agents' views were released (90 live, 90 views).
- Bench_Open / Bench_Maze / Bench_Choke in play mode reported **35/35, 198/207.2 and 59/67.8**,
  identical to the committed pre-port files. Maze's full run wrote `Runs/Benchmark_Maze.json`.
- `Thesis/Export All Maps` through `SceneMapBuilder`: 0 cross-check mismatches, and all 4 map
  files are byte-identical to the committed ones.

**Manual playtest — still to do by the student** (WP4's done-when; the automated test called the same
methods the input calls, but no real mouse or keyboard was used):
- [ ] Left-click places the current piece where the ghost shows it, and the ghost turns red where a click would fail
- [ ] R rotates (the ghost and HUD icon agree), Shift holds (once per piece)
- [ ] Space pauses: building is blocked, R and Shift still work
- [ ] 1 / 2 / 3 and the speed button change speed; enemies move visibly faster
- [ ] Enemies chew walls (the wall shrinks), a breach removes the wall and pays +1
- [ ] A maze long enough to stall enemies pays +0.2 per stall
- [ ] Movement looks smooth at x1 (interpolation) with no jitter
- [ ] Restart after game over works

**Tests added:** 13 headless results (126 → 135 with the WP4 changes; Unity 139 including 4 explicit diagnostics)
- `InputTests` (4): flush equals tick (6k ticks); rotate/hold without time passing;
  a flushed StartWaveNow is acted on by the next tick; a flushed placement raises its events at once.
- `PathfindingBenchTests` (2): A* = flow field on every tile of a scattered map; bench JSON has every section and parses.
- `FloatDeterminismTests` (3): the tick-2916 step, the same step through `AgentSystem`, and the base-lifetime bits.
- `CrossRuntimeTests` (explicit), `CrossRuntimeBisectTests` (explicit), `FloatFingerprintTests` (explicit): diagnostics.

**Commands run**
```
dotnet test Tools/dotnet/Thesis.Headless.sln                         → Passed 135/135
dotnet run --project Tools/dotnet/Thesis.Cli -- bench --iterations 10 → Runs/BenchmarkResults_headless.json
Unity MCP: run_tests EditMode Thesis.Tests.EditMode                  → Passed 139/139
Unity MCP: play SampleScene + execute_code smoke test                → see above
Unity MCP: play Bench_Open/Maze/Choke                                → parity with committed files
diff Runs/xrt_mono.txt Runs/xrt_coreclr.txt                          → identical, 5,036 ticks
```

**Known issues**
- The manual playtest checklist above is unchecked.
- `SimHost` does not survive a play-mode recompile (see Findings). This is a dev-only issue.
- The live-session cross-check was done by replaying a hand-written script. WP5 automates
  it with real replay files recorded by `SimHost`.
- Nothing from WP3/WP4 is committed.

---

## 2026-10-02 — WP5 Replay, determinism proof, CLI basics

**Status:** built; all "must pass" items pass. I1 holds in the Unity editor and headless.
Two things remain open: a session played by hand, and the IL2CPP player-build check.
Result write-up: `Results/2026-10-02_determinism/README.md`.

**What changed**
- `Thesis.Sim/Replay` (new): `ReplayFile` (schema 1), `ReplayCommand`, `WaveHash`,
  `ReplaySetup` (one fingerprint of config + map + shapes) and `ReplayRecorder`.
- `Thesis.Sim/Debug`: `StateDump` (a state as JSON, every float also as its bit pattern) and
  `AsciiState` (header, agents and the route / occupancy / cost layers). `AsciiLayer` gained
  `Occupancy` and `Cost`.
- `Thesis.Sim/Build/ShapeLibraryFile` and `Maps/Shapes.json`: the wall-shape library exported
  from the scene, so headless runs use the game's shapes in the game's bag order.
- `Simulation`: `RngSeed` and `ShapeLibrary` are now readable (a replay needs both).
  `Placement.WallTerrainCost` / `WallHealth` factor out the two clamps `TryPlace` applied, so a
  policy's scratch placement and the real one use the same values.
- `Thesis.Harness`: `EpisodeRunner` (+ `EpisodeOptions`, `EpisodeResult`), `Registry`
  (planner and policy names), `IPlayerPolicy`, `IdlePolicy`, `GreedyDetourPolicy`,
  `ReplayRunner` (verify, first divergent wave, per-tick bisect, run-to-tick) and `ReplayReport`.
- `Thesis.Cli`: `run`, `replay`, `ascii` (and a small `Args` parser). `run --config` takes a JSON
  file of `SimConfig` overrides and rejects unknown field names.
- Unity: `SimHost` records every session and writes `Sessions/<time>_seed<seed>/replay.json` at
  each wave boundary and when play stops (project `Runs/` in the editor, `persistentDataPath` in
  a build). New inspector toggles: `recordReplay`, `recordTickHashes` (both on).
  Editor menus: **Thesis → Export Shapes (Active Scene)** and **Thesis → Replay → Verify Latest
  Session / Verify File… / Open Sessions Folder**.
- `SceneMapBuilder` strips negative zeros (see Findings).

**Deviations / decisions**
- **`ReplayFile` and `ReplayRecorder` are in `Thesis.Sim`, not `Thesis.Harness`** as the folder
  layout said. The harness assembly is editor-only, and `SimHost` must record in player builds
  (the study runs on one). `ReplayRunner` stays in the harness.
- **The file stores `Config`, `MapData` and `Shapes` in full**, plus a `SetupHash` over them,
  instead of only a map name and a config hash. A Unity recording then verifies headless with
  nothing else on disk, and keeps its meaning after a retune or a map re-export. Cost: about
  10 KB per file.
- **Per-tick hashes are stored in the file** (optional; base64, 8 bytes per tick). WORKPLAN asked
  for a per-tick bisect but only listed wave hashes, and a recording cannot be bisected to a
  tick without a reference hash for every tick. On by default in `SimHost`, because a
  participant session cannot be recorded again; off by default in `run` (add `--per-tick`),
  because a headless run can.
- **`FinalTick` / `FinalHash` added.** Without them a session that ends mid-wave, or input after
  the last wave boundary, would go unchecked.
- **`IPlayerPolicy.OnIntermission` takes `Simulation`, not `SimState`**: a policy needs the map
  and config to check legality. `send` applies each command at once, so the policy reads the
  result of its own input.
- **`EpisodeRunner` sends `StartWaveNow` after the policy builds** (`StartWavesEarly`, on by
  default). Nothing moves during a build phase, so the countdown is about 15,000 empty ticks over
  25 waves. The command is recorded like any other. `--wait` turns it off.
- **No estimator snapshot field yet.** There is no estimator. Adding the field later does not
  break old files.
- **The 25-wave "must pass" run uses `CoreMaxHp` 100,000.** With SampleScene's real settings the
  greedy policy is dead by wave 2 (see Findings), so 25 waves need a core that cannot die.
  The rules are otherwise unchanged.

**Findings**
- **I1 holds across runtimes, in both directions.** A 25-wave headless recording (111,861 ticks,
  466 commands) replays with every tick hash equal under .NET and under Unity's Mono. Two sessions
  recorded by `SimHost` in play mode (x1, x2, x3, `timeScale` 20, pause, input while paused,
  mid-wave builds, game over; and one quit mid-wave) replay with every tick hash equal under .NET.
- **Negative zero broke the first Unity recording.** SampleScene's `GridManager` sits at Z = −0.0.
  The simulation ran with −0.0; the JSON writer stores it as `0.0` (on both runtimes), so the
  file's setup fingerprint did not match the run and the file was refused when loaded. No effect
  on gameplay (−0.0 and +0.0 behave the same in the grid's sums, and WP4's cross-runtime hash
  matched with it), but the file did not describe what ran. `SceneMapBuilder` now strips
  negative zeros. This is the setup check doing its job on its first real file.
- **`InvalidDataException` is not an `IOException`** (it derives from `SystemException`). The
  CLI's `catch (IOException)` let a refused file through as an unhandled crash. Fixed; the CLI
  now prints the reason and exits 2.
- **`AsciiMap.Parse` walls do not reach a `Simulation`.** `'#'` tiles live on the fixture's own
  grid; a simulation builds a fresh grid from the map. `TestSims.AsciiWithWalls` copies them
  across. Existing tests were unaffected (they set walls by hand); my new ones tripped on it.
- **The greedy policy is a weak player, and why is informative.**
  - Version 1 judged a piece by flow-field cost alone. On open ground there are millions of
    equally short routes, so no single piece raises the cost: it placed 6 pieces near the spawn
    and then nothing, with 83 budget unspent. It stalled all of wave 1 and died in wave 2.
  - Version 2 (kept) breaks ties by the **number of shortest routes** left, so it keeps
    building. It builds every phase (194 placements over 25 waves) but walls in the core, and
    a swarm chews one wall tile almost at once: wall health is shared, 6 s for one agent is
    0.06 s for a hundred. It leaks most of wave 1.
  - Reason: cost is a poor stand-in for **time**, and time is what wins in the current game.
    This is not worth fixing now. WP-C1 replaces the clock with HP, which changes what a good
    policy is. WP12 has a note.
  - It also says something about balance: by wave 25 an enemy can cover about 370 world units in
    its lifetime, four times the direct route. Whether a human can hold that is for the
    balance pass, not this package.
- **Writing the replay costs about 11 ms at wave 25** under Mono (1.2 MB with per-tick hashes),
  on the main thread, in the same frame the wave resolves. That is inside one frame today but it
  shares the wave-boundary frame with `OnWaveResolved`, which G2 budgets at 16 ms. Measure in
  WP13; if it shows, move the file write to a background thread (the snapshot is already a copy).
- **Per-tick hashing costs under 82 µs per tick under Mono** (9.1 s for 111,861 ticks,
  simulation included). At x3 speed that is about 12 ms per second of play.
- **Bash heredocs in this environment fail when the text contains non-ASCII characters**
  (`→`, `…`). Write the script to a file instead. (Tooling note, not a project issue.)

**Unity sessions recorded (SampleScene, play mode, driven through Unity MCP)**
- Session A, `Runs/Sessions/20261002-034123_seed1`: 22 commands, 4,709 ticks, game over in wave 1.
- Session B, `Runs/Sessions/20261002-034230_seed1`: 18 commands, 882 ticks, stopped mid-wave
  while paused with input after the last tick.
- Both copied to `Results/2026-10-02_determinism/` and pinned by `PinnedReplayTests`.
- 0 console errors or warnings in either session.

**Tests added:** 59 (135 → 194 headless; Unity 198 including the 4 explicit diagnostics)
- `ReplayFileTests` (13): JSON round trip; base64 tick hashes; refused when config, map or shape
  is edited, when commands are out of order, on an unknown command, on garbage; negative zero is
  kept or refused, never silently changed; the setup hash covers **every** `SimConfig` field
  (by reflection, so a new tunable cannot be forgotten) and depends on shape order.
- `ReplayRunnerTests` (9): a recording verifies, also after a JSON round trip; **a command moved
  by one tick is pinned to its wave and its exact tick**; without tick hashes only the wave is
  named; a dropped command; a changed seed fails before tick 1; an unknown planner is named;
  run-to-tick lands on the recorded hashes; a session cut off mid-wave with trailing input.
- `EpisodeRunnerTests` (7): **the 25-wave greedy run records and replays with every wave hash
  matching**; same seed gives a byte-identical file; the idle player loses wave 1; one policy
  call per build phase; `--wait` behaviour; the tick limit; bad options.
- `GreedyDetourPolicyTests` (6), `ReplayRecorderTests` (8), `AsciiStateTests` (6),
  `StateDumpTests` (3), `ShapeLibraryFileTests` (3: the exported `Maps/Shapes.json` equals the
  hand-copied `TestShapes`).
- `PinnedReplayTests` (4 cases): the committed Unity and .NET recordings still replay exactly.
  Runs in both test runners, so each file is re-run on the runtime that did not record it.

**Commands run**
```
dotnet test Tools/dotnet/Thesis.Headless.sln                         → Passed 194/194
Unity MCP: run_tests EditMode                                        → Passed 194, 4 explicit skipped
Unity MCP: Thesis/Export Shapes (Active Scene)                       → Maps/Shapes.json (7 shapes: I O T S Z J L)
thesis run --policy greedy --seed 1 --waves 25 --per-tick --config Results/2026-10-02_determinism/long-core.config.json --out Runs/wp5_greedy_25waves
                                                                     → 25 waves, 111,861 ticks, final hash a1dc9171e8fd33f0
thesis replay Runs/wp5_greedy_25waves/replay.json --per-tick         → OK, every tick hash (also under Mono)
thesis replay Runs/wp5_tampered/replay.json --per-tick               → DIVERGED, wave 8, tick 32435
thesis replay Results/2026-10-02_determinism/unity-session-a.replay.json --per-tick   → OK, 4,709 ticks
thesis replay Results/2026-10-02_determinism/unity-session-b.replay.json --per-tick   → OK, 882 ticks
thesis ascii  Runs/wp5_greedy_25waves/replay.json --wave 25 --layer occupancy
```
(`thesis` = `dotnet run --project Tools/dotnet/Thesis.Cli --`)

**Known issues**
- **No session has been played by hand yet.** The WP4 playtest checklist is still unchecked, and
  WP5's cross-check was driven through the editor by script. Doing the WP4 checklist once now
  covers both: play, stop, then `thesis replay` on the file the console names (or the menu
  **Thesis → Replay → Verify Latest Session**).
- **IL2CPP player build not checked** (WORKPLAN WP5 trap). Needed before the build freeze.
- Both Unity recordings end in wave 1. The intermission path under Unity's frame loop is covered
  by tests and by replaying the headless 25-wave file under Mono, not by a Unity-recorded file.
- Editing the `SimConfig` asset in the inspector **during play** changes the running game and
  makes that session's replay invalid. The file is refused on load with a message that says so.
- `SimHost.Dispatch` iterates `LastTickEvents`; a view that calls `Submit` from inside an event
  handler would clear that list mid-loop. No view does today. (Present since WP4, noticed now.)
- `PinnedReplayTests` will fail at WP-C1 by design; WORKPLAN WP-C1 says what to do.

---

## 2026-10-02 — Review, decisions D5–D9, and WP-H hardening

**Status:** done. No game rule changed: every recording made before this package still
replays bit for bit after it.

**Why this happened.** Before starting the next package the student asked for the
architecture to be re-checked. The review is `Docs/REVIEW-2026-10-02.md`: the structure is
sound, five problems needed designing in before they bite, and five decisions were open.
The student chose one (walls can be sold) and delegated the other four.

**Decisions** (recorded in `ARCHITECTURE.md` §0)
- **D5 Study build:** Windows 64-bit, Mono, managed stripping off. Checked in Player Settings:
  that is already how the project is set (only Android is IL2CPP). It is the runtime WP5
  verified, so the IL2CPP risk is closed. The player-build replay check moves to before the pilot.
- **D6 Walls can be sold** (the student's choice): a whole piece at once, a partial refund,
  build phases only. Specified in §4.6, built in WP-C3. The default for selling *towers*
  changed with it, from "at any time" to "build phases only": selling mid-wave lets a player
  open and close gaps to walk enemies back and forth under fire. One switch
  (`SellDuringWave`) governs both; S11 can flip it.
- **D7 The primary metric (S7)** is defined in §5.9: entropy of the session's built tiles over
  a 6×6 partition, walls and towers pooled, from wave 1, both sessions cut to the shorter one.
  Provisional until frozen before the pilot; the supervisor should confirm it. It is computed
  offline from replays, so changing it before the freeze is free.
- **D8 Order of work:** WP-H now, then WP-C1; WP6 folded into WP-C3; WP7 in parallel; Flying
  is cut if WP-C3 is not finished by the end of W9.
- **D9 Seeds:** two seed sets crossed with condition and order (four groups). Built in WP14.

**What changed (code)**
- `Simulation`: `BeginWave` asks the planner and validates the plan before it writes anything;
  `CloseWave` finishes the outcome on a copy and commits it only after `OnWaveResolved` returns;
  the prep countdown is only written when no wave starts. A planner that throws, or a refused
  plan, now leaves the state exactly as it was.
- `Thesis.Sim/Waves/PlanValidator` (the plan rules, public) and `SafePlanner` (fallback wrapper).
  `DirectorHost` wraps every planner.
- `Thesis.Core/DetMath`: `Log` and `Exp`, ported line for line from fdlibm, constants as bit patterns.
- `ReplayFile.Save` writes a temp file and swaps it in. `SimConfig.Clone()`.
- `SimHost`: runs on a copy of the config asset; event dispatch goes through a queue, so a view
  that calls `Submit` from inside an event handler no longer invalidates the loop.
- `Thesis.Harness/Replay/PinnedEpisodes`, the CLI command `pin`, and the Unity menu
  **Thesis → Replay → Record Pinned Episodes (Mono)**.
- `Editor/BuildGuard`: a player build that is not Mono with stripping off fails with an explanation.
- `.gitattributes` (LF in the repository on every machine; `*.jsonl` always LF) and
  `.github/workflows/headless-tests.yml` (`dotnet test` on Linux and Windows, on every push).
- `Results/pinned-replays/`: three episodes recorded under .NET, the same three under Mono, and the
  two played Unity sessions. `PinnedReplayTests` now reads this folder.

**What changed (documents)**
- `CLAUDE.md`: status line; §1 rewritten for the code as it is (rules in `Thesis.Sim`, a new file
  map); walls sellable; I1 and I8 wording; the maths rule; the re-plan note.
- `ARCHITECTURE.md`: D5–D9; the tick order as built; `SafePlanner`; replay schema 2 (planned);
  wall selling; "every wave must end"; §5.9 the metric; session setup and build settings; the
  `DetMath` rule; spec gaps S7, S9, S11, S13.
- `WORKPLAN.md`: the re-plan; WP-H; WP6 folded into WP-C3; additions to WP-C1, WP-C3, WP-C4, WP7,
  WP10, WP11, WP12, WP14; a pre-pilot checklist.

**Findings**
- **A throwing planner corrupted the game** (shown before the fix): three throws in `PlanWave`
  made the first real wave "wave 4"; one throw in `OnWaveResolved` made every later tick die on a
  null reference. Nothing triggered it yet, because the escalation planner cannot throw.
- **`Math.Pow` differs between Unity's Mono and .NET 9 on this machine** (a fingerprint over one
  million inputs). `Math.Log`, `Exp`, `Sin`, `Cos` and `Sqrt` agreed here. `Thesis.*` used none of
  them, so the rule could be set before the first use.
- **`DetMath` is bit-identical on both runtimes**: one million `Log` and one million `Exp` results
  hash to the same value under .NET and under Mono (`ecae23c8f3ec7547`, `83dd88a47b9a8422`), and
  both stay within 1 ulp of the runtime's own functions over 400,000 random inputs. All 20 fdlibm
  constants were checked bit pattern against decimal.
- **The JSON library prints floats differently per runtime** (`0.20000458` under .NET,
  `0.200004578` under Mono; a double third is `0.3333333333333333` against `0.33333333333333331`).
  Both read back to the same bits and member order is the same, so replays are unaffected. A
  byte-identical telemetry golden file cannot use it (WP-C3).
- **The scripted player decides identically on both runtimes.** The greedy policy counts shortest
  routes in `double`; its episodes recorded under Mono and under .NET have the same commands and the
  same hash on every tick (`PinnedReplayTests.TheTwoRuntimesRecordedIdenticalRuns`).
- **Measured costs on a late-game board:** a flow-field rebuild is 0.32 ms under .NET and 0.88 ms in
  Unity (the documents said "microseconds"); a state hash is about 50 µs on both; `Simulation.Clone`
  is 0.15 ms and 0.39 ms.
- **No map has a static blocker.** The `X` tile path is exercised only by test fixtures. No agent
  was ever stuck on SampleScene: 0 of 12.5 million agent-ticks in the 25-wave run.
- **The Windows build is already Mono** with managed stripping disabled.
- **Every session uses seed 1** (`SimHost.seed`, `randomSeed` off). Right for development; D9 covers
  the study.

**Deviations**
- Wrapping the *baseline* planner in `SafePlanner` too was not asked for by anything. It was done so
  that the wrapper runs in every session from now on instead of first appearing with the director.
  `SafePlannerTests.AWrappedEscalationPlannerPlaysTheSameGameAsABareOne` shows it changes nothing.
- During `PlanWave`, `State.WaveIndex` is now the previous wave's number (it used to be already
  incremented). Planners must read `context.WaveIndex`. No existing planner read the other one.
- `BuildGuard` goes beyond recording D5: it enforces it. The define
  `THESIS_ALLOW_UNVERIFIED_RUNTIME` turns it off for a build that will never record study data.

**Tests added:** 50 (194 → 244 headless; Unity 248 including the 4 explicit diagnostics)
- `PlannerFailureTests` (5): a failed tick leaves the hash unchanged and the next one retries; a
  `StartWaveNow` request survives a failure; an invalid plan is refused without a change; a throw in
  `OnWaveResolved` leaves the wave open and the retry closes it once; what the state shows during `PlanWave`.
- `SafePlannerTests` (9): exception, invalid plan and null plan each become a fallback wave; a
  planner that always throws still gives four numbered waves; `OnWaveResolved` exceptions are
  reported; the name; wrapped equals bare for 3,000 ticks; a planner that writes the state still
  fails loudly; constructor arguments.
- `DetMathTests` (7): the constants; accuracy against the runtime; edge cases; `Exp(Log(x))`; **the
  cross-runtime fingerprint**.
- `ForbiddenApiTests` (20): the scan of `Assets/Thesis`, 18 cases proving the scan catches what it
  should and nothing else, and the waiver rule.
- `SimConfigTests` (2), `ReplayFileTests` (+1: atomic save).
- `PinnedReplayTests`: now 8 recordings from the folder, plus "both runtimes recorded the same
  episodes" and "the two runtimes recorded identical runs" (10 results, was 4).

**Commands run**
```
dotnet test Tools/dotnet/Thesis.Headless.sln                          → Passed 244/244
Unity MCP: run_tests EditMode                                         → Passed 244, 4 explicit skipped
thesis pin                                                            → Results/pinned-replays/dotnet-*.replay.json (3)
Unity MCP: Thesis/Replay/Record Pinned Episodes (Mono)                → Results/pinned-replays/mono-*.replay.json (3)
Unity MCP: play SampleScene, 4 placements, wave 1 to game over        → 0 console errors; planner is SafePlanner 'escalation';
                                                                        config is a copy; no .tmp left behind
thesis replay Runs/Sessions/20261002-062303_seed1/replay.json --per-tick   → OK, 3,670 ticks
```
(`thesis` = `dotnet run --project Tools/dotnet/Thesis.Cli --`)

**Known issues**
- **The CI workflow has never run.** It takes effect on the next push. The Linux job is the first
  time this code runs on Linux; a failure there is information, not necessarily a bug in the game.
- **`BuildGuard` has not been exercised by a real build**, only compiled.
- **The proposal is not in the repository.** WP8 and WP9 (spec gaps S9, S10) need its §6.2 and §6.3.
- **The metric in §5.9 is provisional** until the supervisor has seen it.
- The week numbers in the re-plan assume W5 began on 14 September.
- Still open from before: a session played by hand; the pre-pilot checklist at the end of `WORKPLAN.md`.
- `main` on GitHub is still the first commit; all work is on `sim-core/wp0-wp1`.

---

## 2026-10-02 — WP-C1 Combat core: hit points, towers, damage

**Status:** done. Every "must pass" item has a test, and all tests pass in both runners.
**This package changes the rules on purpose:** the lifetime clock is gone, enemies have hit
points, towers kill them. Every earlier recording describes the old game; the pinned
recordings were re-made.

**What changed**
- `Thesis.Sim/Combat` (new): `DamageType`, `TargetingMode`, `TowerDef`, `TowerState`,
  `TowerSystem` (the tower pass), `Targeting`, `DamageMap`, `TowerRoster` (placeholder roster).
- `AgentState`: `LifeTime` removed; `Hp`, `MaxHp`, `Resist[]`, `SlowTicks`, `SlowFactor`, `Killed`.
  `AgentSystem`: no clock; a slow scales the step; chewing through a tower tile destroys the tower.
- `SimNode`: `Occupant { None, Wall, Tower }` and `TowerId`. `SimGrid.SetTower`.
- `Placement.TryPlaceTower`, `PlacementRecord.Kind` / `TowerId`, and the command
  `PlaceTower(towerIndex, x, y)`.
- `Simulation`: takes a tower roster; tick order is now commands, phase machine, **tower pass**,
  agent pass, field rebuild, **wave backstop**, wave boundary, core. `SimState.Towers`, `SimState.Damage`.
- `AgentGroup`: `Hp`, `Resist[]`, `Archetype` instead of `LifeTime`. `EscalationPlanner` v2: hit
  points 10, +2 a wave (placeholder), instead of a lifetime that grew 4 s a wave.
- `WaveOutcome`: `Killed` instead of `Stalled`; `DamageByType[]`, `TowersDestroyed`, `Removed`, `TimedOut`.
- `SimConfig`: `KillReward` (was `DeathReward`), `TowerBreachReward`, `BaseHp`, `HpIncrementPerWave`,
  `MinSlowFactor`, `MaxWaveSeconds`; the three lifetime fields are gone.
- **Every wave must end** (review item A2): `PlanValidator` refuses `MoveSpeed` 0 and `Hp` 0;
  a slow has a floor; `MapData.Validate` runs the flow field and refuses a map whose spawn cannot
  reach the core; a wave still running after `MaxWaveSeconds` is ended by force.
- Events: `AgentKilled` (in place of `AgentStalled`), `TowerPlaced`, `TowerDestroyed`,
  `TowerFired`, `AgentRemoved`, `WaveTimedOut`. `SimEvent` gained a third integer slot.
- Replay **schema 2**: the tower roster is stored and hashed with the setup; `ReplayCommand.A`.
- `StateHasher`, `StateDump`, `AsciiState` (towers are `T`) cover the new state.
- Harness: `SentryPolicy` (towers), `SequencePolicy` (`mixed` = walls, then towers),
  `EpisodeOptions.Towers`, new pinned episodes. CLI: `--policy sentry|mixed`.
- Unity: `SimHost` passes the roster; `FlowAgent`'s bar shows HP; `WaveSpawner` releases a view
  on `AgentKilled` / `AgentRemoved`; `PlayerBuilder` draws a tower as a tinted wall cube, removes
  it on `TowerDestroyed`, and has **temporary keys Z / X / C** to buy the three towers at the
  mouse tile; `ScenarioBenchmark` adapted. `SimConfig.asset` re-saved.

**Deviations from WORKPLAN, and why**
- **`PlaceTower` is a player command now, not an internal helper until WP-C3.** Without a recorded
  way to place a tower, no replay could contain one, so the Mono-versus-.NET check would not have
  covered any of the new arithmetic (range, damage, slow) until the shop existed. It also made the
  "done when" run an ordinary recorded episode. WP-C3 changes the first argument from a roster
  index to a shop offer slot.
- **The simulation takes a tower roster** and the replay stores it (schema 2). Needed for the above.
- **Unity can place towers already** (three keys, cubes for visuals). WP-C4 owns the real UI; this
  is the smallest thing that keeps the game playable in between, because without towers every
  enemy now reaches the core.
- **Two explicit diagnostics deleted:** `CrossRuntimeTests` (it pinned one hash of the old game) and
  `CrossRuntimeBisectTests`. `PinnedReplayTests` and `thesis replay --per-tick --dump-tick` do both jobs.
- Extra small files for the one-type-per-file rule: `DamageTypes`, `TargetingMode`, `Occupant`,
  `PlacementKind`.
- `Targeting` has one mode (`First`). More are added when a tower needs one.

**Decided while building** (all in `ARCHITECTURE.md` §4.6 and pinned by tests)
- Damage always means **effective** damage (no overkill), in the damage map, the per-type totals
  and a tower's own total.
- A kill is settled at once: the enemy is dead to every later tower in the same tick.
- Splash hits every *other* live enemy near the target, in id order, for the same damage.
- Resist 0 is immunity: no damage and no slow.
- A slow affects movement, not digging, and lasts exactly `SlowTicks` steps.
- The backstop removes what is left with no core damage and no reward, and marks the outcome `TimedOut`.

**Findings**
- **Walls alone no longer win anything.** The walls-only bot loses in wave 1, exactly like the bot
  that builds nothing: there is no clock for a long route to run out. Walls now only buy time
  under fire. That is the intended design (CLAUDE.md §2), but it is a real change to how the game
  plays, and it is the first thing to check by hand.
- **The scripted players, real settings, seed 1:**

  | Player | Result |
  |---|---|
  | idle | lost in wave 1, 0 kills |
  | walls only (greedy) | lost in wave 1, 0 kills |
  | towers (sentry) | waves 1–9 with no leak, lost in wave 10 (35 towers) |
  | walls, then towers (mixed) | lost in wave 2 |
  | towers, then walls | lost in wave 1 |

  Towers-then-walls loses at once because the walls reroute the enemies away from the towers
  just bought. That order was the first version of `mixed`; it is now walls first. Even so, the
  mixed bot is worse than towers alone: with these placeholder prices a wall costs budget and
  buys little. Whether walls are worth building is a question for the balance check (WP-C5).
- **Splash is very strong against this game's enemy stream.** Enemies arrive 0.2 s apart, about
  an eighth of a tile, so a splash of one tile hits a dozen at once. The placeholder cannon was
  cut to 3 damage and 0.75 tiles before the numbers above were taken.
- **Combat is bit-identical on both runtimes.** The same three tower episodes were recorded
  under .NET and under Mono; commands and every tick hash are equal, and each file replays on the
  other runtime. A session played in the Unity editor (8,293 ticks, 3 waves, towers, a wall,
  input while paused, a mid-wave build, x3 and `timeScale` 12) replays headless with every tick
  hash equal. The new float pins (a slowed step, a range check on the edge) were computed under
  .NET and pass under Mono.
- **The config asset kept its old fields in YAML** and had none of the new ones. Unity filled the
  new ones from the C# initialisers (checked: the asset equals the defaults). Re-saved.
- **Tooling:** a file can be locked for a moment while Unity re-imports (one write failed and was
  repeated). A Bash heredoc also fails when its text has an odd number of apostrophes, not only on
  non-ASCII characters; scripts are written to a file instead.

**Unity play-mode check (SampleScene, driven through Unity MCP)**
- `[Sim] Started ... planner 'escalation'`, then the hint for the Z / X / C keys. 0 errors or warnings.
- `PlayerBuilder.TryPlaceTower` (what the keys call) placed an archer and a cannon and refused one on
  the spawn tile. The tower bot placed three more through `SimHost.Submit`. All five were drawn.
- Wave 1 at x3: 185 shots and 82 kills by tick 1,224; agent views equal live agents; the lowest HP
  bar read 0.70. Waves 1 and 2 were cleared with the core at 10/10.
- A tower and a wall piece were placed in the middle of wave 2; a rotate was sent while paused.
- Lost in wave 3 with 86 budget unspent (100 killed, 10 leaked).
- The session's replay verifies headless and is now `Results/pinned-replays/unity-session-towers.replay.json`.

**Tests:** 244 → 301 headless; Unity 303 including 2 explicit diagnostics.
- New: `TowerSystemTests` (18), `CombatTests` (11), `WaveTerminationTests` (10), `SentryPolicyTests` (8).
- Rewritten for the new rules: `AgentSystemTests`, `EscalationPlannerTests`, `SimulationPhaseTests`,
  `SampleSceneRunTests`, parts of the replay, episode and planner tests.
- `FloatDeterminismTests`: the base-lifetime pin is gone; a slowed step and a range check are pinned.
- `EpisodeRunnerTests`: the 25-wave run now uses the mixed player and checks no wave timed out;
  `TowersLetAPlayerSurviveClearlyLongerThanNoTowers` is the "done when".
- `PinnedReplayTests`: 7 recordings (3 .NET, 3 Mono, 1 Unity session).

**Commands run**
```
dotnet test Tools/dotnet/Thesis.Headless.sln                          → Passed 301/301
Unity MCP: run_tests EditMode                                         → Passed 301, 2 explicit skipped
thesis run --policy idle|greedy|sentry|mixed --seed 1 --waves 25      → the table above
thesis pin                                                            → Results/pinned-replays/dotnet-*.replay.json (3)
Unity MCP: Thesis/Replay/Record Pinned Episodes (Mono)                → Results/pinned-replays/mono-*.replay.json (3)
Unity MCP: play SampleScene with towers                               → see above
thesis replay Runs/Sessions/20261002-070116_seed1/replay.json --per-tick   → OK, 8,293 ticks, 3 waves
```
(`thesis` = `dotnet run --project Tools/dotnet/Thesis.Cli --`)

**Known issues**
- **Nothing is balanced.** Tower numbers, enemy hit points and prices are placeholders (WP-C5).
- **No shop, no selling, no tower UI.** Any tower can be bought at any time, in Unity only with the
  Z / X / C keys; a tower looks like a wall cube; nothing shows a shot or a range.
- **Not played by hand yet.** The WP4 checklist is still open and now has more to look at: do the
  towers feel right, do walls still feel worth building.
- The recordings in `Results/2026-10-02_determinism/` are schema 1 and are refused now. That is
  expected; they stay as the evidence for that result.
- Enemies still all share one movement class; flying and sapper enemies are WP-C2.
