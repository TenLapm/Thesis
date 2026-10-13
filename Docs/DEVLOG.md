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

---

## 2026-10-02 — WP-C2 Movement classes: Ground, Sapper, Flying

**Status:** done. Every "must pass" item has a test, and all tests pass in both runners.
**This package adds rules but changes none for ground enemies:** the static baseline plays
exactly as it did after WP-C1 (shown three ways, under Findings). The recordings were re-made
all the same, because the state hash covers more than it did.

**What changed**
- `Thesis.Sim/Movement` (new): `MovementClass` (Ground, Sapper, Flying), `MovementClasses`,
  `FlowFieldSet` (both fields, rebuilt together), `FlyingMovement`, `RouteCost`.
- `FlowField`: the same flood now fills either field. `Generate` is the ground field (as before);
  `GenerateSapper` is the sapper field; `SapperTerrainCost` is what a sapper pays for a tile.
- `SimNode`: `SapperCost`, `SapperNextIndex`, `CostFor(class)`, `NextIndexFor(class)`.
  `SimGrid`: `NextOf(node, class)`, `TileSize`. `Route`: every method takes a class (default Ground).
- `AgentGroup.Movement` and `AgentState.Movement`. `AgentSystem.Step` takes the core's node:
  an enemy leaks on that tile whatever its class; a flyer steps straight at it; a walker or a
  sapper follows its own field.
- `Targeting.CanHit` (the anti-air rule) and `Targeting.Pick` ranking by `RouteCost`.
  `TowerSystem.Step` takes the core's node and applies `CanHit` to the splash as well.
- `Simulation` rebuilds through `FlowFieldSet`; `Placement.TryPlace` and `TryPlaceTower` take one.
- `SimConfig.SapperDigCostFactor` (0.2, placeholder), validated to [0, 1].
- `WaveOutcome`: `SpawnedByClass[]`, `KilledByClass[]`, `LeakedByClass[]`. `PlanValidator` refuses
  an undefined class.
- `ClassCyclePlanner` (`Thesis.Sim/Waves`): a development planner, see Deviations.
- `StateHasher`, `StateDump`, `AsciiState` and `AsciiMap` cover the new state. A render can show
  the sapper's route or cost field; `+` marks where a route goes through a wall or a tower; `f` is
  a flying agent.
- Replay **schema 3**. `ReplayRunner.Outcomes(file)` re-runs a recording and returns how each wave went.
- CLI: `run --planner class-cycle`; `ascii --class ground|sapper`; `ascii --map <map> [--scenario …]
  [--sapper-factor F]` (a bare map and a route, no recording needed); `replay --outcomes`.
  `bench` also times the two-field rebuild.
- Harness: `Registry` knows `class-cycle`; `PinnedEpisodes` has a fourth episode, `class-cycle-6waves`.
- Unity: `FlowAgent` tints a sapper orange and draws a flyer blue and 2.6 units up; `DirectorHost` has
  `Condition.DevClassCycle`; `ScenarioBenchmark` adapted; `SimConfig.asset` re-saved with the new field.

**Deviations from WORKPLAN, and why**
- **`ClassCyclePlanner`, a development planner.** The package listed no way to send a sapper or a
  flyer; the strategies that will are WP9. With nothing sending them, no replay could contain one,
  so the Mono-versus-.NET check would not have run any of the new arithmetic, and nobody could
  see them in Unity. It turns each escalation wave into another class in a fixed cycle: ground,
  sappers (half as many; 1.5× HP, 0.8× speed, 4× dig rate), flyers (half as many; 0.5× HP, 1.2×
  speed), then all three (a half, a quarter, a quarter). It is not a study condition and the numbers
  are placeholders (S12). Same reasoning as making `PlaceTower` a command in WP-C1.
- **The must-pass fixture has a 10-tile detour, not the plan's 20.** A wall of cost 15 reads as a
  14-tile detour to a walker, so with 20 extra tiles even Ground digs. One test has the 10-tile
  corridor (Ground round, Sapper through) and one the 20-tile corridor (both through).
- **`WaveOutcome` counts all three classes** where §4.6 planned `FlyersSpawned` and `FlyersLeaked`.
  The same numbers, and the sapper ones come free.
- **Extra files:** `MovementClasses` (array size, one type per file), `RouteCost`, `ClassCyclePlanner`.
- **Replay schema 3**, although the file layout did not change: the hashes cover the sapper field and
  each enemy's class, so an older file's hashes describe less. The number says so plainly, instead
  of an older file failing on its setup hash with a misleading message.
- **`replay --outcomes`, `ascii --map` and the `+` route marker** were not in the plan. The first was
  needed to choose a pinned episode that covers every case; the other two are the review probe.

**Decided while building** (all in `ARCHITECTURE.md` §4.6 and pinned by tests)
- A sapper pays `max(1, round(cost × factor))` for a built tile and what anyone pays for open ground.
- The class chooses the route only. How fast a sapper digs is its group's `DigRate`.
- A flyer flies to the centre of the core tile and leaks when it is over that tile.
- `CanHitFlying` covers the whole shot: target, splash and slow.
- "Nearest the core" is measured the way each enemy travels: its field's cost for a walker or a
  sapper, the straight line in tenths of a tile for a flyer. Target choice and the pressure
  statistic both use it.
- Flyers are not counted in the occupancy map (traffic on the ground). Damage to them is booked
  in the damage map like any other.
- Both fields are one rebuild: `FieldVersion` goes up by one.
- `FlowField.Generate` alone builds the ground field only, for a caller with its own grid.

**Findings**
- **Ground play is unchanged**, shown three ways. (1) Before anything else was touched, the
  two-field flow field replaced the old one and all seven WP-C1 recordings replayed to the same
  hash on every tick, under .NET and under Mono. (2) `FlowFieldSetTests` compares the ground field,
  tile for tile, with the WP1 flood fill on 60 random boards. (3) The three ground-only pinned
  episodes, recorded again, have the same commands, the same length and the same wave timings as
  at WP-C1; only the hashes differ.
- **The new arithmetic is bit-identical on both runtimes.** The class-cycle episode (26,607 ticks:
  sappers breaching walls and a tower, flyers shot down and leaking) was recorded under .NET and
  under Mono with identical commands and tick hashes. The float pins for a flyer's step and its
  distance to the core were worked out by hand or under .NET and pass under Mono.
- **A session played in the Unity editor verifies headless:** 57,899 ticks, 13 waves, 50 commands,
  with the class cycle, speeds x12, x3 and x1, a pause, an editor pause, and builds in mid-wave.
  Every tick hash matches. Its first five waves are the pinned `unity-session-classes`.
- **Income depends on the shape of the wave (new spec gap S14).** `KillReward` is paid per enemy.
  The cycle's sapper and flyer waves have half as many enemies, so over waves 2 to 4 the player
  earns 44 in kill rewards where the escalation pays 66. The tower bot then meets the same wave 5
  with 16 towers instead of 17, and that is the difference between clearing it and losing in it
  (it clears the escalation up to wave 9). A director that spends its budget on fewer, stronger
  enemies would do this to the player every time, which is what I11 forbids. It has to be settled
  with the threat cost table (WP9): pay a kill in proportion to the enemy's price, or pay per wave.
- **On open ground a sapper walks round a single tile.** Passing one built tile costs two diagonal
  steps (0.8 tile more), cheaper than chewing it even at the sapper's price. Sappers dig where the
  way round is long. On the benchmark maze (walls of cost 200) a share of 0.2 takes the sapper
  through two of the five rows (86 tiles against 198); at 0.3 it digs nothing; at 0.1 it goes
  through four (54 tiles). So the share needs tuning together with the wall costs (S12).
- **The scripted players, real settings, seed 1, against the class cycle:**

  | Player | Result |
  |---|---|
  | idle | lost in wave 1 |
  | walls only (greedy) | lost in wave 1 |
  | towers (sentry) | waves 1–4 with no leak, lost in wave 5 (16 towers) |
  | walls, then towers (mixed) | lost in wave 2, the first sapper wave: 5 of 52 killed |

  The tower bot stops all 55 flyers of wave 3: on an open board the ground route is the straight
  line, so towers beside the route are also under the flight path, and a third of its towers are
  archers. That will not hold once a maze bends the route away from the line.
- **Rebuild cost, both fields, 38×38 with 0 / 10 / 25% of the tiles built:** 0.29 / 0.46 / 0.58 ms
  under .NET 9 (release), 0.76 / 0.91 / 1.13 ms in the Unity editor. The ground field alone is
  0.13 / 0.17 / 0.26 ms and 0.39 / 0.46 / 0.55 ms. Two floods cost a little more than twice one.
- **The tests notice a broken rule.** Sixteen deliberate breakages (a sapper on the ground field,
  splash ignoring the anti-air rule, a flyer ranked by the ground under it, a clone dropping the
  sapper field, and so on) were each applied in turn; every one failed at least one test.
- **Tooling:** the Unity MCP screenshot tool leaves the editor paused; un-pause it afterwards or the
  session stands still. A slow `execute_code` call can time out and still have run.

**Unity play-mode check (SampleScene, driven through Unity MCP)**
- `DirectorHost.condition` set to `DevClassCycle` and the core to 1,000 HP, both in memory only; the
  scene and the config asset on disk were not changed (checked afterwards).
- `[Director] DEVELOPMENT planner 'class-cycle' …`, `[Sim] Started … planner 'class-cycle'`. 0 errors.
- Wave 3, flyers: 46 alive, 46 views, all tinted blue and drawn at height 2.75.
  Wave 5, ground: 116 views, none tinted, none raised, although the pooled objects had been flyers
  and sappers. Wave 6, sappers: 50 views, all orange, on the ground.
  Wave 12, all three: 47 plain, 38 orange, 38 blue and raised; no view disagreed with its agent.
- Screenshots: the flyers are a straight line of pale-blue cubes above the board, each with its
  HP bar; the sappers are orange cubes in the stream on the route.
- Two archers and a wall piece were placed in the middle of wave 4; rotate and hold were sent
  while paused; the wall bot placed eight pieces in the middle of wave 8.
- The core fell in wave 13. The replay verified headless (above).

**Tests:** 301 → 356 headless; Unity 358 including 2 explicit diagnostics.
- New: `FlowFieldSetTests` (16), `MovementClassTests` (22), `ClassCyclePlannerTests` (7).
- Added to existing files: three float pins (a flyer's step, its distance to the core, the
  sapper's rounding), two ASCII state renders, one ASCII map render, a 20,000-tick determinism run
  with every class, `ReplayRunner.Outcomes`.
- `PinnedReplayTests`: 9 recordings (4 .NET, 4 Mono, 1 Unity session).
- Fixtures: `TestFields`; `FixedPlanner.Sappers` / `Flyers`; `TestTowers` can make anti-air towers
  and agents of a class.

**Commands run**
```
dotnet test Tools/dotnet/Thesis.Headless.sln                               → Passed 356/356
Unity MCP: run_tests EditMode                                              → Passed 356, 2 explicit skipped
thesis run --planner class-cycle --policy idle|greedy|sentry|mixed --seed 1 --waves 12   → the table above
thesis ascii --map Maps/Bench_Maze.map.json --scenario maze --class ground|sapper [--sapper-factor F]
thesis bench --iterations 200          (release build)                     → the rebuild times above
thesis pin                                                                 → Results/pinned-replays/dotnet-*.replay.json (4)
Unity MCP: Thesis/Replay/Record Pinned Episodes (Mono)                     → Results/pinned-replays/mono-*.replay.json (4)
Unity MCP: play SampleScene with DevClassCycle                             → see above
thesis replay Runs/Sessions/20261002-084648_seed1/replay.json --per-tick --outcomes   → OK, 57,899 ticks, 13 waves
```
(`thesis` = `dotnet run --project Tools/dotnet/Thesis.Cli --`)

**Known issues**
- **No study condition sends sappers or flyers.** Only the development planner does. The static
  baseline is ground only, as it should be.
- **Nothing is balanced:** the sapper and flyer numbers, `SapperDigCostFactor`, and which towers can
  hit flyers (only the archer) are placeholders (WP-C5).
- **Nothing on screen explains the classes.** The route preview draws the ground route only, so it
  does not show where sappers will dig, and nothing says that only the archer reaches a flyer.
  The looks are a tint and a height (WP-C4).
- **The scripted players do not know the classes exist** (WP12).
- **S14 is open:** income depends on the wave's shape. It must be closed before the strategies are priced.
- **Still not played by hand.** To see the classes: select `GameManager` in SampleScene, set
  `DirectorHost > Condition` to `DevClassCycle`, and play. Set it back for the baseline.
- The recordings of WP-C1 (schema 2) are refused now; they are in git history at that commit.

---

## 2026-10-11 — WP-C3 Shop, selling and telemetry

**Status:** done, in one session (not split into C3a and C3b). Every "must pass" item has a
test, and all tests pass in both runners. **This package changes the rules:** a tower can only
be bought from the shop's offers, and towers and wall pieces can be sold. All recordings were
made again (replay schema 4). It also contains what was WP6: every session now writes telemetry.

**What changed**
- `Thesis.Sim/Shop` (new): `TowerOffer`, `ShopState`, `ShopRoller` (`SeedFor`, `Roll`).
  `SimState.Shop`. The simulation rolls at game start and each time a wave resolves.
- Commands: `PlaceTower(offerSlot, x, y)` (was a roster index), `SellTower(x, y)`,
  `SellWall(x, y)`, `RerollShop`. Events: `ShopRolled`, `TowerSold`, `WallSold`.
- `Placement`: `TryPlaceTower` takes the price; new `TrySellTower` and `TrySellWall`. Every
  one rebuilds through `FlowFieldSet`.
- The build log: `PlacementKind` is `Wall`, `Tower`, `WallSold`, `TowerSold` or `ShopReroll`;
  `PlacementRecord` has `PieceId`, `Cost`, `Refund`, and the tiles it built or cleared.
- `SimNode.PieceId` (which piece a wall tile belongs to); `TowerState.PricePaid` and `Sold`.
- `SimConfig`: `ShopOffers` (4), `RerollCost` (-1, off), `SellRefund` (0.5), `WallSellRefund`
  (0.5), `SellDuringWave` (off), all validated.
- `StateHasher` and `StateDump` cover the shop, piece ids, prices, sales and the new log fields.
- Replay **schema 4**; a command name the simulation does not know is refused.
- `Thesis.Core`: `Json.SerializeLine` and `FormatFloat` / `FormatDouble` / `FormatNumber` (number
  text that is the same on every runtime), `DeterministicNumberConverter`, `NamedValues`.
- `Thesis.Director/Telemetry` (new, the first code in that assembly): `WaveRecord` (schema 1),
  `TelemetryRecorder`, `TelemetryWriter`, `PlacementEntropy`, `PlacementScope`.
- Harness: `EpisodeRunner` records telemetry beside the replay (`EpisodeResult.Telemetry`);
  `ReplayRunner.Telemetry(file)` makes it again from a replay; `SentryPolicy` buys from the
  offers; `SellerPolicy` and `SequencePolicy.Restless()` (`--policy restless`);
  `PinnedEpisodes` writes each episode's telemetry and has a fifth episode, `restless-5waves`.
- CLI: `run` also writes `telemetry.jsonl`; new `telemetry`, `trace`, `entropy`.
- Unity: `SimHost` writes `telemetry.jsonl` beside `replay.json`; `PlayerBuilder` has the
  temporary shop keys, sell key and offers panel, and draws sales from the build log;
  `SimConfig.asset` re-saved with the new fields.

**Deviations from WORKPLAN, and why**
- **Four shop offers, not three.** Measured before choosing (the table is under Findings): with
  three, the tower bot loses the first wave on four seeds of ten, because the start budget of 60
  buys four towers and the first roll offers only three.
- **I10 is tested across `EscalationPlanner` and `ClassCyclePlanner`,** not "a random planner":
  none exists until the baselines of WP10. Five scripted players against both planners, 30 waves,
  offers read from each session's telemetry.
- **The golden files are the pinned episodes' telemetry.** The plan asked for one fixed run that
  gives byte-identical JSONL in both runners. As built, every pinned recording has its telemetry
  beside it, written by the session itself, and the test makes it again from the replay and
  compares bytes, on both runtimes: ten scripted files and one from a session played in Unity.
  That checks more than a hand-kept file would (the live recorder against the replayed one, and
  one runtime against the other) and is re-made by the same two commands as the replays. The
  exact bytes of one baseline row are written out in `WaveRecordTests` as well, so a renamed or
  moved field cannot slip through a re-recording.
- **No `IClock`.** A row holds no wall-clock time at all. The session label has the start time in
  it, but that is a string the host passes in, and the replay stores the same string.
- **A scripted player that sells** (`SellerPolicy`, as `restless`). The review probe needs a
  recording with sales, and without a seller no pinned episode would run the selling rules on
  the other runtime. It is crude: weakest tower by damage dealt, oldest wall piece.
- **A third CLI command, `entropy`.** The metric had no way to be looked at otherwise.
- **`SimHost` owns the telemetry writer,** not `DirectorHost` as ARCHITECTURE §6 said: telemetry is
  written in every condition and needs nothing from the director.
- **`TelemetryRecorder` reads `SafePlanner` for the `fallback` field.** That is the one thing in a
  row that does not come from `SimState`; it comes from the planner object the simulation holds,
  so a replay reproduces it too.
- **Not in the row yet:** the participant code and session number (WP14), the recorded plans in
  the replay (WP10). The director's fields exist in `WaveRecord` and are null.
- **Temporary Unity input** (keys and a text panel), as WP-C1 did for towers. WP-C4 replaces it.

**Decided while building** (all in `ARCHITECTURE.md` §4.6, §5.8 and §5.9, and pinned by tests)
- A roll's `waveIndex` is the number of waves resolved when it was made: 0 at game start, N the
  moment wave N resolves. The offers of roll N stay until wave N+1 resolves.
- Each slot is drawn on its own, weighted by `ShopWeight`, so a tower can fill two slots.
- An offer's price is fixed when it is rolled; a sale refunds a share of the price **paid**.
- A sold tower stays in the list (`IsAlive` false, `Sold` true), like a destroyed one.
- A wall piece's id is the index of its row in the build log. A tile breached and later built
  over belongs to the newer piece. The refund is in proportion to the tiles still standing.
- Selling is for build phases, towers and walls alike. A sale command during a wave is recorded
  and changes nothing.
- A telemetry row is a wave's fight **and the build phase after it**; row 0 is the build phase
  before the first wave. Something built on the tick a wave starts belongs to the row before.
- A row's `offered` is the roll made in its window (its first set; rerolled sets can be derived).
- **A completed wave is one whose next wave began.** §5.9 said "waves completed" and no more.
  The last wave a session reached has no build phase after it, so it cannot be compared.
- The metric's `K` is the number of regions with a buildable tile.
- Floats in a row have 7 significant digits and doubles 12. The text is for reading and comparing,
  not the exact bits; those are in the replay.

**Findings**
- **Telemetry is a pure function of the replay**, shown three ways. (1) In tests, five sessions
  (walls and towers, selling, both planners, a game over) recorded live and made again from
  their replays give the same text. (2) Each of the ten pinned telemetry files is made again from
  its replay on the *other* runtime, byte for byte, and the .NET and Mono files of an episode
  differ only in the two labels that name the recorder. (3) A session played in Unity through
  `SimHost` (16,774 ticks, bought and sold by the calls the keys make, stopped in the middle of
  wave 6) wrote 7 rows live under Mono; `thesis telemetry` under .NET gives the same 7 rows,
  byte for byte. So telemetry can always be made again, also after its schema grows.
- **The JSON library does not print a float the same way on the two runtimes:** 0.20000458 is
  `0.20000458` under .NET and `0.200004578` under Unity's Mono. Both read back to the same bits,
  which is why replays never noticed. Telemetry text therefore goes through its own formatter
  (one multiplication by a power of ten, a floor, integers); every expected string in
  `JsonNumberTests` is checked by both runners.
- **A host and a replay do not see the same events.** A host applies a command between ticks, so
  the event it raises is never among a tick's events; a replay applies it at the start of the
  next tick, so it is. The recorder may therefore react only to what a tick itself raises (a wave
  starting or resolving, the roll after it, game over). This was found by the mutation check: one
  of 44 deliberate breakages (a paid reroll overwriting the recorded offers) passed every test,
  because no test rerolled in a recorded session. `ASessionWithPaidRerollsIsMadeAgainTheSame` now
  does, and all 44 are caught.
- **The new arithmetic is bit-identical on both runtimes.** `restless-5waves` (7,240 ticks, 14
  towers bought, 3 sold, 4 wall pieces sold) was recorded under .NET and under Mono with identical
  commands and tick hashes.
- **Three offers or four** (tower bot, static baseline, real settings; the wave it lost in):

  | Offers | seed 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
  |---|---|---|---|---|---|---|---|---|---|---|
  | 3 | 1 | 1 | 25 | 7 | 2 | 12 | 1 | 1 | 5 | 14 |
  | 4 | 10 | 13 | 18 | 7 | 9 | 8 | 25 | 14 | 25+ | 25+ |

  (25+ = still alive after 25 waves.) Five and six offers changed little more. Seed 3 is the
  warning in the table: more offers is not always better for a bot that buys whatever comes
  first; that is the bot, and balance is WP-C5.
- **The scripted players with the shop, real settings** (the wave each lost in):

  | Player | seed 1, static baseline | seed 1, class cycle | seeds 1 to 10, static baseline |
  |---|---|---|---|
  | idle | 1 | 1 | |
  | walls only (greedy) | 1 | 1 | |
  | towers (sentry) | 10 | 7 | 10 13 18 7 9 8 25 14 25+ 25+ |
  | walls, then towers (mixed) | 2 | 2 | 2 1 3 5 1 1 1 3 1 3 |
  | sell, walls, towers (restless) | 4 | 3 | 4 1 4 24 1 1 1 6 1 3 |

  The two players that build walls first are weak: walls take the budget the first towers need.
  They are stand-ins that exercise the rules, not the ladder's players (WP12).
- **First numbers from the metric** (static baseline; they show the tool works, nothing more):
  tower bot, seed 7, 24 completed waves, 90 tiles: H = 0.487. Restless, seed 4, 23 completed waves,
  268 tiles: H = 0.469. Tower bot, seed 4, 6 completed waves, 18 tiles: H = 0.343. A session lost
  in wave 1 has no completed wave and scores 0.
- **A row is about 1.5 to 2 KB**; a 25-wave session is under 50 KB. Recording costs nothing
  measurable: the recorder reads four kinds of event per tick and builds a row once per wave.
- **A float sum shows in the file:** `"budgetBefore":9.000015` is a budget that kill rewards of 0.2
  were added to one at a time. Seven digits print the float as it is; they do not tidy it.
- **The tests notice a broken rule.** 44 deliberate breakages, each applied alone: the shop seed
  ignoring a counter, an offer bought twice, selling during a wave, a refund that ignores the
  rate or the breached tiles, a clone dropping piece ids, a window ending a tick late, stale or
  rerolled offers in a row, placements repeated, the metric counting row 0 or sales or every
  region, swapped region axes, a half rounding down, a renamed field, Windows line ends, and so
  on. 43 failed at least one test at once; the 44th led to the test named above.
- **Tooling.** A Python edit script that opens a C# file while Unity is leaving play mode can
  fail with "Invalid argument" (the file was untouched; run it again). The Edit and Write tools
  turn `\uXXXX` in their input into the character, so an escape in source has to be written by a
  script that builds the backslash itself. An ad-hoc run of 44 builds takes about 15 minutes.

**Unity play-mode check (SampleScene, driven through Unity MCP)**
- `DirectorHost.condition` set to `DevClassCycle` and the core to 1,000 HP, both in memory only; the
  scene and the config asset's other values on disk were not changed (checked afterwards).
- The offers panel is at the top right, the one corner the HUD leaves free. (A first version sat
  on the controls list on the left and was too small in a small Game view; both fixed.)
- Bought through `PlayerBuilder.TryBuyOffer`, the call the keys make: four offers before wave 1,
  eleven more later; a slot bought twice was refused with a message.
- Sold through `PlayerBuilder.TrySell`, the call the sell key makes: eight towers and two wall
  pieces in build phases, with refunds of half (frost 6, archer 5, cannon 9, a T piece 2.5, an L
  piece 1.5); open ground refused. Cubes disappeared with each sale.
- Two sale commands were sent on the first tick of wave 3, from an event handler: the simulation
  refused both (no log row, no budget change), and both are in the replay.
- Waves 1 to 5 of the class cycle: 100, 52, 55 and 113 enemies all killed, then 47 of 120 with
  most towers sold. The session was stopped 19 ticks into wave 6.
- `thesis replay … --per-tick`: 16,774 ticks, 5 wave hashes, every tick hash and the final state
  match. `thesis telemetry … --check`: the 7 rows are the file's, byte for byte. Both files are
  the pinned `unity-session-shop`.
- 0 errors and 0 warnings in the console after compiling.

**Tests:** 356 → 510 headless; Unity 512 including 2 explicit diagnostics.
- New: `ShopTests` (21), `SellingTests` (19), `JsonNumberTests` (32), `WaveRecordTests` (14, with the
  file tests), `TelemetryRecorderTests` (24), `PlacementEntropyTests` (21), `SellerPolicyTests` (6),
  `ShopIndependenceTests` (1: ten sessions of 30 waves).
- `PinnedReplayTests`: 11 recordings (5 .NET, 5 Mono, 1 Unity session), each replayed and each
  with its telemetry made again; the two runtimes' telemetry compared; the set must contain
  walls, towers and both kinds of sale.
- Added to existing files: sales in the 20,000-tick determinism run (12 wall pieces, 5 towers),
  piece ids in the grid clone test, the sentry buying by slot, the new commands in a replay file.
- Fixtures: `TestRows` (rows by hand, and a full director row); `TestTowers.Buy` / `BuySlot` /
  `BuyAny`; `CommandScript` buys, sells and rerolls.

**Commands run**
```
dotnet test Tools/dotnet/Thesis.Headless.sln                               → Passed 510/510
Unity MCP: run_tests EditMode                                              → Passed 510, 2 explicit skipped
thesis pin                                                                 → Results/pinned-replays/dotnet-* (5 replays, 5 telemetry files)
Unity MCP: Thesis/Replay/Record Pinned Episodes (Mono)                     → Results/pinned-replays/mono-* (the same)
Unity MCP: play SampleScene with DevClassCycle                             → see above
thesis replay Runs/Sessions/20261011-152552_seed1/replay.json --per-tick   → OK, 16,774 ticks, 5 waves
thesis telemetry Runs/Sessions/20261011-152552_seed1/replay.json --check Runs/Sessions/20261011-152552_seed1/telemetry.jsonl   → OK, 7 rows, byte for byte
thesis run --policy restless --seed 4 --waves 5 --per-tick --out Runs/c3_probe          (the review probe)
thesis replay Runs/c3_probe/replay.json --per-tick                         → OK, 7,240 ticks (10 walls, 14 towers, 4 wall sales, 3 tower sales)
thesis telemetry Runs/c3_probe/replay.json --check Runs/c3_probe/telemetry.jsonl        → OK, 6 rows, byte for byte
thesis trace Runs/c3_probe/telemetry.jsonl [--wave 2]
thesis entropy Runs/c3_probe/replay.json
thesis run --planner escalation|class-cycle --policy idle|greedy|sentry|mixed|restless --seed 1..10 --waves 25   → the tables above
python c3_mutate.py  (scratch; 44 one-line breakages, each with dotnet test on the affected classes)   → 44 of 44 caught
```
(`thesis` = `dotnet run --project Tools/dotnet/Thesis.Cli --`)

**Known issues**
- **Still not played by hand.** Z / X / C / B buy the four offers onto the tile under the mouse;
  Delete (or Backspace) sells the tower or wall piece under it, between waves. The panel at the
  top right names the keys.
- **The temporary input shows too little:** no range before a tower is placed, and no preview of
  which tiles and what refund a wall sale takes (WP-C4). The C key also resets the camera's roll
  (`CameraMovement`), an overlap that has been there since WP-C1.
- **Nothing is balanced:** four offers from a roster of three means most rolls have a duplicate;
  half back on a sale and no rerolls are first guesses (WP-C5, S11).
- **The scripted players do not choose among offers.** The tower bot buys whatever the budget
  covers, in slot order (WP12).
- **S14 is still open** (income depends on the wave's shape), and the shop makes it matter more:
  what a player can buy next turn is what the last wave paid.
- **Secondary measures of §5.9:** walls only, towers only and the 4×4 and 8×8 partitions are
  built; tower type × region, the mean of the per-wave entropies and the sale counts are left
  to the analysis; `restructure` needs the profile (WP8).
- **A built player has not written telemetry yet.** Like the replay (I1), it is verified in the
  editor and headless; a session from the Windows build must be checked before the pilot.
- **WP7 has not been started.** It was planned to run beside this package.
- **`ProjectSettings/TimeManager.asset` has an unrelated change in the working tree.** Unity
  re-saved its physics fixed timestep in a newer format, as the fraction 2822399 / 141120000:
  0.02 to seven digits. Nothing in the game reads it (`SimHost` counts its own ticks from
  `SimConfig.TickSeconds`; no script uses `FixedUpdate`). Not part of this package; revert it or
  commit it apart.
- The recordings of WP-C2 (schema 3) are refused now; they are in git history at that commit.

---

## 2026-10-12 — The game design document, and the student's first read of it

**Status:** documents only. No code of the game changed and nothing was re-recorded.

**What was made**
- `Docs/GDD.md` and `Docs/GDD.pdf`: the game from the player's side, with every part marked
  Built, Planned, Proposed or Open, and a list of the open questions with who answers each.
- `Docs/gdd/`: three drawn pictures (where enemies walk; a build phase; a wave) and one
  screenshot of the game as it is.
- `Tools/gdd/`: what makes the PDF and the pictures (`README.md` there).

**What the student decided on reading it (not built; WORKPLAN WP-C4)**
- The board is seen from straight above. The mockups were drawn flat only to be readable;
  the student preferred that to the tilted 3-D camera.
- Walls do not need to be tetromino pieces. Proposed in their place: single tiles, drawn by
  click and drag, one price a tile.
- Later the same day, yes to four of the five questions the read had raised (GDD §12,
  "Answered"): the walls as proposed; rocks on the map; the wave watched from above too; one
  route line for each kind of enemy the player has met. The last is still to be shown to the
  supervisor, because the screens of the two conditions differ by it.

**What the student asked, and what was answered**
- *Should the board start with walls or terrain on it?* Proposed: rocks (the map's static
  blockers, which the rules have always had and no map has used), not walls. A standing
  wall can be sold and pays when it is chewed through, so it would be money on the board.
  The rocks must not decide the maze, or both study conditions build the same thing.
- *Do enemies walk the same path, or does the director choose?* One path rule; one route per
  movement class and spawn; the director chooses the class and never the path (GDD §5.3).
  The GDD had not said this anywhere in one place.

**Found while drawing the picture of the routes**
The walker and sapper routes in the pictures come from the real flow field (`routetool`), on a
38 × 38 sample board with 167 rock tiles, 34 wall tiles and 9 towers. Walkers: about 71 tiles,
past all 9 towers, chewing nothing. Sappers: about 50 tiles through two walls one tile thick,
past 5 towers. Flyers: 44 tiles, in reach of 1 tower that can hit them. Two things went wrong
on the way, and both are how the game is meant to work:
- With the far ends of the middle ridge left open, the walkers went round the whole board
  through a corner (cost 67.8) and never entered the maze.
- With one baffle three tiles longer, the walkers chewed through a wall one tile thick
  (cost 73.0 with the dig) because the way round had become dearer than 15 tiles.
So a detour is worth at most one wall's price per wall, and a hand-made map has to be read
with the route drawn on it before it is trusted. This matters for the rocks: a layout is
checked with `thesis ascii` or `routetool`, not by eye.

**Open, for the student:** GDD §12, question 19 (the size of the board), besides the fourteen
that were there before the read. The decisions add an estimated two to three days to WP-C4.

---

## 2026-10-12 — WP-C4 The board from above, one-tile walls, rocks, and the tower UI

**Status:** done, with the deviations below. 522 tests pass in both runners (12 new). A Unity
session driven through the real input path replays headless, ticks and telemetry. **Nobody
has played it by hand yet.**

**What changed for the player (GDD §3, §4.1, §5.3, §9, §10)**
- The board is 24 × 24 (the student: "24 for now"), seen from straight above by a fixed camera
  (`TopDownCamera`), in the build phase and during a wave.
- Rocks: 68 tiles (11.8 %) in eleven groups (`RockLayout`, text in the scene; `SceneMapBuilder`
  turns them into the map's `X` tiles). Checked with the path rule before it went into the scene:
  on the empty board the walkers' way is 29.6 tiles, the straight line 26.9.
- A wall is one tile, 1 budget, drawn by click or drag; a sale is one tile.
- The shop is a panel of cards; a tower picked from its card shows a ghost and its range ring;
  Wall and Sell are tools; a tooltip says what a click would do and what a sale would pay.
- Towers are a ring in their damage type's colour with a barrel that turns to its target and a
  shot drawn for a moment (I12: the hit is resolved before anything is drawn).
- Enemy classes by shape: walker a small square, sapper an orange disc, flyer a blue diamond.
- Route lines by the rule of GDD §5.3: build phase, every class met so far (walkers from the
  start); wave, the classes in it. Seen under `DevClassCycle`: wave 1 pink only, wave 2 the
  sappers' line only, wave 3 the flyers' straight line only.

**What changed in the code**
- New: `RockLayout`, `TopDownCamera`, `TowerShopPanel`, `TowerView`. Rewritten: `PlayerBuilder`
  (the hand: `BuildTool`, drag, `SalePreview`), `GhostPreviewer`, `PathPreviewer`. Smaller:
  `FlowAgent`, `CanvasDashboard` (`showPieceQueue`), `SceneMapBuilder`.
- **No rule changed.** `Thesis.Sim` is untouched. The shape library is one shape of one tile
  (`Maps/Shapes.json`), so a drag is one `PlaceShape` per tile. Replay schema still 4.
- Scene: grid 48 × 48 world units, spawn tile (2,2), core (21,21); camera movement off; the hold
  and next-piece slots and the controls list hidden.
- Tests: the rigs written in the old board's coordinates keep it as a fixture
  (`Maps/Fixture_Open38.map.json`, `TestSims.Open38`); `GameMapTests` (10) is the real board.
- All pinned sessions recorded again on the new board; `unity-session-mouse` added.

**Deviations from the plan**
- The file names of **Creates** were not kept, and there is no `TowerDefAsset` or
  `EnemyArchetypeAsset`: the roster and the enemy types are WP-C5's.
- The bag, the hold slot and rotation stay in the simulation, unused.
- The manual playtest was done by script: a second mouse device fed through the Input System,
  so clicks went through the EventSystem, the raycast and `PlayerBuilder` as a hand's would.
  (Two false alarms on the way were the rig's: the real mouse overrides a queued position,
  and screen coordinates computed outside the player loop use another resolution.)

**Measured on the new board** (static baseline, real settings, wave lost in, seeds 1 to 5):
sentry 1, 11, 18, 11, 25+; mixed 3, 5, 18, 21, 2; restless 2, 11, 15, 25+, 3. An idle player
loses in wave 1. Nothing is balanced; these are only to show the scripted players still play.

**Open, for WP-C5 or the student**
- A wall tile costs 1 and pays 1 when it is chewed through: a chewed wall is free. The start
  budget of 60 is 60 tiles on a board of 576. Both are balance (GDD question 9).
- Enemies are small on the screen, the HUD text is the default font, and the old budget and
  health labels are plain. Styling is the second semester's.
- The pictures in GDD §5.3 and §9.2 show a larger example board, not the game's.
- A flyer has no shadow (GDD, proposed).

---

## 2026-10-12 — WP-C5 Roster v1 and the balance sanity check

**Status:** done. 526 tests pass in both runners (4 new). The check's result is
`Results/2026-10-12_balance-v1/README.md`: four of six hypotheses hold, two fail and are
reported as failing.

**What changed**
- **Towers:** five, as the GDD proposed (the student had not answered question 2): archer,
  ballista, cannon, brazier, frost. `TowerDefAsset` (one asset each, `Assets/Scripts/Towers`),
  listed in `SimHost.towerRoster`, exported by `Thesis/Export Towers` to `Maps/Towers.json`
  (`TowerLibraryFile`). `TowerRoster.Placeholder()` is deleted; its three towers are now the
  tests' fixture roster.
- **Enemy types:** `EnemyArchetype` and `EnemyArchetypes` (basic, swarm, sapper, flyer), each a
  set of multiples of that wave's basic enemy. In code, not assets. `ClassCyclePlanner` uses
  them and its cycle is five waves (a swarm was added).
- **The game's numbers left the C# defaults.** `SimConfig.asset` holds them, `Thesis/Export
  Config` writes `Maps/SimConfig.json`, and `thesis run` and `thesis balance` read that file
  when no `--config` is given. The defaults stay what they were: several hundred test
  expectations and the pinned replays are written against them.
- `thesis balance`; `EpisodeResult.Towers` (what every tower cost and did).
- Pinned replays recorded again (the roster is part of a replay's setup); the restless episode
  moved to seed 9. `unity-session-shop` deleted: it was recorded on the old planner cycle.

**The numbers, before and after**

| | Before | After |
|---|---|---|
| Wave 1 | 100 enemies, 10 HP, 0.20 s apart | 40 enemies, 8 HP, 0.40 s apart |
| Each wave | +5 enemies, +2 HP | +5 enemies, +3.5 HP, gap −0.01 s down to 0.20 |
| Cannon | 3 damage, splash 0.75 (placeholder) | the same, after trying 5 and splash 1 |
| Frost | 1 damage | 2 damage |

**Found**
- With the old waves and the new roster the game was "lose in wave 1 or never": 12 of 30
  runs ended wave 25 untouched and 9 ended in wave 1.
- Damage per budget per minute on the board does not measure a slowing tower. Frost fails
  that hypothesis in every run and would with any numbers.
- `sentry` loses in wave 1 on seeds 6 and 8 in all five runs.
- A cleared wave 1 now pays 8 budget where it paid 20, because a kill pays per enemy (S14).

**Deviations**
- The enemy types are code, not assets, and there is no export for them.
- Two hypotheses fail in the final run (frost's value; one run of 30 untouched). Tuning
  stopped there on purpose.
- The pinned replays use the default numbers with the new roster, which is neither the old
  game nor the new one. They test determinism, which does not care.

**Open**
- GDD questions 2 and 3 (the roster is built as proposed; the student may change it).
- S14 (income per wave shape), sharper now.
- WP7 has still not been started.

---

## 2026-10-12 — After the first play: Start-only waves, flat income, cards, levels, gear

**Status:** built as a trial, at the student's request, outside the work plan. 540 tests pass
in both runners (14 new). A Unity session with a coin card, a tower and an upgrade replays
headless, telemetry included.

**What the student found on playing WP-C5:** the build phase should last until the player
starts the wave; too much money after wave 3; one chokepoint with stacked towers wins
without attention; the random towers feel useless; only one kind of enemy ever comes.

**What changed in the rules** (all behind `SimConfig`, off in the defaults)
- `WaitForStart`: a build phase has no clock.
- Income: the game's config pays 16 a wave and nothing per kill or per chewed wall, starting
  at 50. Closes S14 for now: income cannot depend on a wave's shape.
- A shop slot is a card (`CardKind`: tower, coin, gear). `ShopRoller.Roll` takes the config
  and the gear roster; it draws a random number only for a choice that is switched on, so
  with everything off the offers of every seed are what they were (tested), and I10 holds.
- `TowerState` has a `Level` (1 to 3) and two gear slots; the numbers of a shot are worked
  out from definition, level and gear by `Recompute` and are the tower's, not the
  definition's. `TowerDef.Levels` says what rare and epic add; a level can add bolts
  (`ExtraTargets`), which the tower pass fires at distinct enemies, one `TowerFired` each.
- `GearRoster` (five pieces, in code). Commands `TakeCoin`, `BuyGear`, `MoveGear`,
  `UpgradeTower`; build-log kinds `TowerUpgraded`, `GearBought`, `GearMoved`, `CoinTaken`;
  event `TowerChanged`. Replay schema 5, telemetry schema 2.
- `SentryPolicy` takes coin cards and buys gear; with `UpgradeWithWhatIsLeft` (policy name
  `upgrader`) it also buys levels. Pinned episode `cards-6waves`.

**In Unity:** cards of three kinds in the shop panel, tools Upgrade and Move Gear, a tower's
level as a coloured plate and its gear as dots, several shot lines for several bolts, "press
Start" in place of the countdown. SampleScene's condition is `DevClassCycle`, so sappers,
flyers and a swarm come in waves 2 to 5.

**Measured** (10 seeds, median wave lost in): static baseline, sentry 10.5 and upgrader 10.5;
development planner, sentry 6 and upgrader 7. Not a balance run.

**Risk, recorded:** levels and gear are investment that is not placement, and the primary
metric counts placements. GDD question 20; to be raised with the supervisor before the
metric is frozen.

**Not done:** the work plan and the architecture document do not describe this yet; the
pictures in the GDD do not show it; the old Unity session recording (`unity-session-mouse`)
was deleted with the schema change and not replaced.

---

## 2026-10-12 — Second round after play: the wave is announced, cards are a pick, gear has a shop

**Status:** built as a trial at the student's request. 551 tests pass in both runners (9 new).

**What the student asked:** to know where the enemies will walk next wave; to pick only one
card per roll (free); a separate shop, for gear only.

**Rules** (behind `SimConfig`, off in the defaults)
- `PlanAhead`: the planner is asked when a build phase begins and the plan is kept in
  `SimState.NextPlan` until the wave starts. The planner therefore sees the board as it stood
  when the last wave resolved, not the finished build. This is a change to WHEN the director
  will decide, and it has to be carried into WP8 to WP11.
- `PicksPerRoll`, `PicksAtStart`: a roll's cards cost nothing and only so many are taken
  (`ShopState.PicksLeft`).
- `GearShopSlots`, `GearShopRefreshWaves`: `SimState.GearShop`, stock a pure function of the
  seed and the stock number (I10 holds for it); command `BuyShopGear`. Replay schema 6.
- Game config: three cards a roll (towers and coins, no gear), one pick (two at the start),
  three gear pieces restocked every five waves, 30 budget and 10 a wave.

**In Unity:** the top bar names the next wave; the route lines in a build phase are those of
the next wave; the panel is "Pick one card", a gear shop, the tools and the key.

**Measured** (10 seeds, median wave lost in, static baseline / development planner):
sentry 11.5 / 10.5, upgrader 14.5 / 13. With 16 a wave the upgrader survived 25 waves on
half the seeds, so income was cut to 10. Seeds 5 and 7 lose in wave 1 whatever is done: their
first cards are two Frost towers and a coin.

**Not done:** work plan and architecture document; GDD pictures; a Unity session recording
in the new format.

---

## 2026-10-13 — Third round after play: reroll, combining, a tower cap, moving towers, eight towers

**Status:** built as a trial at the student's request. 566 tests pass in both runners (11 new).
Nothing since WP-C4 is committed.

**Rules** (behind `SimConfig`, off in the defaults)
- `RerollCostStep`: a reroll costs `RerollCost` plus the step for every reroll already made of
  this wave's cards (`Simulation.RerollPrice`). In a draft the unused picks carry over and a
  reroll with no pick left is refused. Offers are still a function of seed, wave and reroll
  count (I10).
- `CombineCount` (`TowerCombine`): that many standing towers of one type and level merge when
  the last is placed. The lowest id stays; the others are off the board with
  `TowerState.Combined`. Gear moves to the keeper or is paid back. Build-log kind
  `TowerCombined`, telemetry kind `combine`.
- `TowerCap`, `TowerCapEveryWaves`: a limit on standing towers; a tower that completes a set
  is exempt. `TowerCombine.HasRoomFor` is asked by the simulation, the scripted player and
  the Unity preview.
- `MoveTowers`: command `MoveTower`, build phases only, free. `TowerState.Tile` and `Position`
  are no longer read-only. Build-log kind `TowerMoved`, telemetry kind `moveTower`.
- `UpgradeCostFactor` 0 now switches the paid upgrade off (it used to make it free).
- `TowerDef.Targets`: a tower type can fire at several enemies from level 1.
- Replay schema 7, telemetry schema 3.

**Game config:** four cards a roll, gear cards back (weight 0.25, coins 0.25), reroll 2 +1,
sets of three, cap 4 +1 every 3 waves, moving on, paid upgrades off, rare 10% and epic 2%.
Roster: sniper, shard and flak added (`Maps/Towers.json`, eight towers).

**In Unity:** a Reroll button under the cards; "towers n / cap" in the panel's header; a card
says "completes a set" or "you have 1 of 3" or "tower limit reached"; the tools are Wall, Sell,
Move and Gear in one row. Checked in play mode: reroll, a move, and a merge drawn on the board.

**Pinned replays:** all re-recorded. The bigger roster changed every seed's offers, so three
older episodes got new seeds (sentry 7, mixed 27, restless 7). New: `combine-10waves`.

**Measured** (sentry, 10 seeds, wave lost in, 25 = survived): static baseline
23 25 2 14 25 3 14 20 10 25; development planner 13 3 3 15 25 3 15 18 8 3. The spread is wide
and four seeds lose by wave 3 against sappers and flyers. Not balanced.

**Deviations and open points**
- The balance check of WP-C5 (`Results/2026-10-12_balance-v1`) describes five towers and the
  old shop. It was not run again.
- The scripted player never moves a tower and rerolls only when no card can be taken, so no
  pinned recording holds a `MoveTower` or a paid reroll in a draft; unit tests cover both.
- A free move and a merge both change the layout without a "wall" or "tower" row. Whether the
  primary metric should count them is a study-design question (GDD 4.4).
- Flak overlaps the cannon. Work plan and architecture document still not updated.

---

## 2026-10-13 — Fourth round: few, heavy actions (generated map, entries, wall stock, moves, bench)

**Status:** built as a trial at the student's request, after a design discussion (chess-like
turns; RimWorld's raids as the model for where enemies come from). 584 tests pass in both
runners (18 new). The state before it is commit `341a31f`.

**Rules** (behind `SimConfig`, off in the defaults)
- `MapGenerator.Generate(seed, ...)`: ridges of rock from the seed, a ring of `BuildMargin`
  tiles kept open and unbuildable (`Placement.CanPlace`), every second tile of the outermost
  line an entry point (`MapData.Spawns`, 60 on 32 x 32), pockets turned to rock with the real
  flow field so every open tile reaches every other.
- `CorePlacement`: command `PlaceCore`, before the first wave only. The simulation now owns a
  CLONE of the map and moves the core on it; `Simulation.InitialMap` is the map as given and is
  what the replay stores. `SimState.Core` is hashed.
- `RaidPlanner` (development, id `raid`): the class cycle's enemies shared over 1, 2 or 3
  entries (from waves 1, 4, 8). Odd waves take the entries whose routes are least covered by
  towers, even waves a spread by wave number. An entry needs `MinEntryRouteTiles` of route and
  entries are `MinEntrySpacingTiles` apart.
- `WallStock`, `WallStockPerWave`, `WallRelayPerWave`, wall card, `BuyWalls`: walls cost no
  budget; `SellWall` takes a wall up (free if laid this phase, else one pick-up).
- `MovesPerWave`, `BuyMove`: moving, selling, benching, deploying or swapping a tower on the
  board costs a move; a refused action costs none.
- `BenchSlots` (`TowerBench`): `BenchCard`, `DeployTower` (onto a tower = swap), `RecallTower`,
  `SwapBench`, `SellBenched`. A benched tower is alive, on no tile, and does not shoot.
- `FeedDuplicates`: `FeedCard` merges a tower card into a tower of its type.
- Replay schema 8, telemetry schema 4 (rows walls, buyMove, bench, deploy, recall, feed, core).

**Harness:** `RaidPolicy` ("raider") uses every new command; `SentryPolicy` reads the announced
wave's entries and has a `RouteTail` option ("turtle": towers round the core); the episode
runner waits one tick for the announced plan and calls the policy again after a core move.
`thesis run --gen-map <seed>`. New pinned episode `raid-10waves`.

**In Unity:** SampleScene is 32 x 32, generated, random seed each run, planner `DevRaid`. A left
panel (`BenchPanel`) shows the core prompt, moves, wall tiles and the bench; `PathPreviewer`
draws a line per entry and enemy kind and a marker on each entry. Checked in play mode: core
placed, a card benched, deployed and recalled, the third action refused for want of a move,
walls from the stock, and a wave announced "from 2 sides".

**Measured** (10 generated maps, wave lost in). The way from the edge to a core in the middle
is about half as long as the old corner-to-corner route, and the tower limit keeps towers few:

| Waves | sentry | turtle (towers round the core) |
|---|---|---|
| 40 +5 enemies, 8 +3.5 hp (the old numbers) | 4 2 2 3 2 3 2 2 2 4 | 2 2 2 2 4 5 2 2 2 10 |
| 24 +3 enemies, 6 +2.5 hp (now the game's) | 4 3 2 3 4 4 4 4 3 9 | 4 3 2 5 9 13 5 5 3 14 |

The sentry never moves a tower, so a wave from another side passes it by: that is the design
working, not a fault. The second row is a first guess to make the game playable; it is not a
balance.

**Deviations and open points**
- Every route ends at the core, so the "turtle" may be the one good layout (GDD 4.4).
- Wave strength does not yet account for the entries used (I2): a wave split over three sides
  is as many enemies as one from one side, but harder to cover. That belongs to the threat
  cost table (WP9).
- The static baseline (`EscalationPlanner`) still always uses entry 0.
- Telemetry's `mapSeed` is still 0; the map's name carries the seed.
- The balance check of WP-C5 no longer describes the game. Work plan and architecture document
  still not updated; WP7 not started.

---

## 2026-10-14 — Gear inventory, and a trial scene with a tilted board, stand-up figures and drag and drop

**Status:** built at the student's request after playing the fourth round ("the interface is
hard to understand"). 585 tests pass in both runners (1 new). Nothing since `341a31f` is committed.

**Rules** (behind `SimConfig.GearInventorySlots`, 0 = off; the game's config has 6)
- `SimState.GearInventory`: gear owned without being on a tower. Commands `StoreGearCard`,
  `StoreShopGear` (into the inventory), `EquipGear`, `UnequipGear` (onto and off a tower on the
  board or the bench; free; in build phases unless `MoveGearDuringWave`). Build-log kinds
  GearStored, GearEquipped, GearUnequipped; telemetry rows gearStore, equip, unequip.
- Unequipping leaves a tower's `PricePaid` alone. A piece bought straight ONTO a tower (the old
  commands, still there) therefore stays in that tower's sale price after it is taken off: a
  small leak that only exists when both ways of buying are used in one game.
- Replay schema 9, telemetry schema 5. Pinned replays re-recorded; `raid-10waves` now also
  holds StoreShopGear, EquipGear and UnequipGear.

**Unity: a second scene, `Assets/Scenes/ArknightsTest.unity`** (SampleScene is kept as it was,
with the top-down camera and the left panel).
- `TiltCamera`: orthographic, 52 degrees, fitted to the free part of the screen; the mouse
  wheel zooms (up to 3x), the middle button or the arrow keys move the view.
- `PlayerBuilder.billboards`: a tower is a flat figure that faces the camera
  (`TowerView.Figure`: a placeholder body and head in the damage type's colour, a frame in the
  level's colour, the first two letters of its name). The tower in the hand is shown standing on
  the tile under the mouse.
- `DeckBar` (bottom of the screen): moves and wall tiles with their buy buttons, the bench as
  tower cards, the gear inventory, and a card with a tower's full numbers for the tower or bench
  card under the mouse. `DragCard`: bench cards, inventory pieces and the shop's cards are
  dragged onto a tile, a tower or a slot; a tower on the board is dragged to a tile or the bench.
  Clicking still works. With the bar present, gear from a card or the gear shop goes into the
  inventory.
- The card overlay is hidden by a CanvasGroup instead of being switched off, so a drag that
  starts on it survives (both scenes).

**Checked in play mode** (driven from code): the scene starts, the figures, the bench card and
the inventory piece are drawn, the buttons moved under the shop panel. **Not checked by hand:**
every drag gesture. Enemies are still the 3D shapes; only towers are figures.

**Open:** on 32 x 32 a tile is about 16 pixels at 720p, so a figure is small until zoomed in; an
Arknights board is roughly 10 x 7. Whether the board should shrink is a design question.

---

## 2026-10-14 — Merging three of a kind by choice; gear comes back from sold and merged towers

**Status:** built at the student's request ("how do I merge, I got three towers"). 587 tests
pass in both runners (2 new). Nothing since `341a31f` is committed.

**Rules**
- `SimConfig.MergeCount` (default 2; the game's config 3) and `TowerMerge`: that many towers of
  one type and level make the next level. Command `MergeTower` (source and target each a board
  tile or a bench slot): the tower dropped ON stays and gains the level, the source and the
  other copies are used up wherever they are (benched copies first). One move if any of them is
  on the board. Logged as `TowerCombined`, telemetry `combine`.
- A tower card is one copy of a set: with `MergeCount` 3, `FeedCard` needs a card of the tower's
  level and one more copy, which is used up. With 2 it works as before. A card above the
  tower's level still simply gives the tower that level.
- With a gear inventory, the gear of a tower that is sold or merged away comes back into it.
  What does not fit goes to `SimState.GearOverflow`: `EquipOverflow` puts a piece on a tower,
  `SellOverflow` sells it for half its price, a freed inventory slot takes one by itself, and
  `StartWaveNow` is ignored while any waits. The episode runner sells what a scripted player
  leaves, so no episode can hang on it.
- Replay schema 10, telemetry schema 6 (row `gearSold`). Pinned replays re-recorded.

**In Unity (ArknightsTest):** dropping a benched tower or a board tower on another of its kind
merges when the set is complete (else it changes places or picks up, as before); the preview
says which. A "spare gear" panel appears above the bottom bar while pieces wait, each with a
sell button. `SimHost` switches the gear inventory off in a scene without the bottom bar, so
SampleScene cannot be blocked by spare gear it has no way to show.

**Checked in play mode** (from code, through PlayerBuilder's own click path): three braziers,
one benched, merged into a rare; the piece that did not fit showed in the spare panel.

**Open:** no pinned recording holds a `MergeTower` (the scripted player merges with cards before
it owns three); unit tests cover it. 

---

## 2026-10-14 — After the first outside tester: the core prompt, a free first build, distinct cards, the pink build, text, difficulty

**Status:** six points from the student after sending a build to a friend. 589 tests pass in both
runners (2 new). Nothing since `341a31f` is committed.

1. **Placing the core was not understood.** A banner over the board now says "STEP 1: PLACE YOUR
   CORE" with the instruction and one large button; a gold frame marks where the core may stand;
   the core pulses. Until the first wave a MOVE CORE button reopens it. (`TowerShopPanel`,
   `PlayerBuilder.ShowCoreZone`, both scenes.)
2. **Before the first wave nothing is limited:** `SimConfig.FreeFirstBuild`; a move costs
   nothing while `WaveIndex` is 0 (`Simulation.MovesAreFree`). Walls laid in a build phase were
   already free to take up again.
3. **No two cards of a roll are alike:** `SimConfig.DistinctCards`; a slot that repeats is drawn
   again from the same generator (up to 24 times), so a roll is still a function of seed, wave and
   reroll count (I10). The panel shows the income ("+5 after the wave") under the budget.
4. **The pink build.** Shapes made at run time with `GameObject.CreatePrimitive` (towers, rocks,
   entry markers) kept the primitive's built-in default material, which the editor swaps for
   URP's and a built player does not. They now take `Assets/Resources/ViewLit.mat`
   (`TowerView.UseSolidMaterial`). **Not verified in a build:** the student's own Player.log
   shows no shader error, so if the friend's machine alone was pink the cause may be its
   graphics card or driver instead.
5. **Overlapping text.** Every label the panels make shrinks to its box and is then cut with
   dots; the card's type, state and tags each have their own box; the wave line at the top
   auto-sizes.
6. **Too easy.** The student reached wave 11 without a leak after wave 3. The waves had been
   made smaller on 2026-10-13 because scripted players could not cope; a person can. Now 32 +5
   enemies with 8 +4 hit points (was 24 +3 and 6 +2.5). Tower and gear numbers are unchanged.
   The scripted players lose in waves 1 to 5 again, which says more about them than about the
   game: they do not regroup when the entry changes.

**Open:** difficulty is tuned by one person's play; whether gear or tower levels should be
weakened instead is undecided.

---

## 2026-10-14 — Five points after more play: gear-shop reroll, taking gear off, flyers, the single file, rare cards

**Status:** 590 tests pass in both runners (1 new). Not committed (the last commit is `132d8ca`).

1. **The gear shop can be rolled again:** `GearShopRerollCost` 3, `GearShopRerollStep` 2, command
   `RerollGearShop`, build-log kind `GearShopReroll`, telemetry row `gearReroll`. The new stock is a
   function of the seed, the stock number and the reroll count. Replay schema 11, telemetry 7.
2. **Taking gear off a tower** is one click in the scene with the bottom bar: the Gear tool
   (now "UNGEAR", key G) on a tower or a bench card sends its first piece to the inventory.
3. **Flyers** have 0.35 of a walker's hit points (was 0.5) and a walker's speed (was 1.2 times).
4. **Enemies no longer walk in single file on screen:** each is drawn up to 0.65 world units from
   its true position, by an amount fixed by its id (`FlowAgent.spread`). View only; the
   simulation, the replays and where a tower's shot is drawn to are unchanged.
5. **Rare and epic cards** come up 4% and 0.5% of the time (were 10% and 2%), so that a level is
   mostly something the player merges for.

**Open:** with one pick a wave and no two cards of a roll alike, three of a kind takes at least
three waves of picks, and an epic nine commons. Whether a merge should need two copies instead
of three is the student's call.

---

## 2026-10-14 — A lost wave is played again; moves are unlimited

**Status:** built at the student's request. No rule of the simulation changed, so no recording
was re-made; the 590 tests are as they were. Not committed (the last commit is `132d8ca`).

- **Try the wave again.** The game-over screen has a second button, "TRY WAVE n AGAIN". Nothing
  is rewound: `SimHost.RetryWave` loads the scene again and plays the same run from its own
  recording (same seed, the commands before the build phase that preceded the lost wave) up to
  that build phase, with no frame drawn, and the player goes on from there. Checked in play mode:
  after a loss in wave 2 the state hash at a tick of that build phase is the one the first
  attempt had at the same tick. Core hit points are those the player had then, not a full core.
- **What that means for the data.** The lost session keeps its replay and telemetry. The new
  session is a full recording from tick 0 whose label says `human, wave n again after <session>`;
  its first rows repeat the lost session's. An analysis must count a player's retries and must
  not count those rows twice.
- `PlayerBuilder` draws a wall or a tower from the build log only if it stands there now, so the
  whole log can be walked at once after a retry.
- **Moves:** `MovesPerWave` is 0 in the game's config, so moves are unlimited in every build
  phase. The "two moves a wave" of the fourth round is off, not removed.

**Open:** with unlimited moves and the wave announced, moving every tower to the announced route
each wave is free. Whether that makes the answer to every wave obvious is to be seen in play.

---

## 2026-10-14 — After Emberward: one chosen upgrade instead of merging, free walls, target orders, ten enemy types

**Status:** built at the student's request, who had played Emberward and listed how it works.
596 tests pass in both runners (6 new). Not committed (the last commit is `132d8ca`).

- **One upgrade per tower, with a choice** (`ChoiceUpgradeCost` 12, `TowerUpgradeChoices`): Power
  (+50% damage), Rapid (40% faster), Reach (+1.5 range) or Volley (one more enemy). Command
  `ChooseUpgrade`; build phases only; the build-log row is `TowerUpgraded` with the name
  `<tower>+<choice>`. **Merging is off** in the game's config (`FeedDuplicates` false); its rules
  and tests stay. Rare and epic cards still exist as luck.
- **Any wall comes up again in a build phase** (`FreeWallPickup`). With moves already unlimited,
  everything can be rearranged between waves.
- **A target order per tower** (`ChooseTargeting`, `TowerState.Mode`, command `SetTargeting`):
  closest to the core, strongest, weakest, nearest. Free, at any time.
- **Six more enemy types**, all variants of a walker or a flyer: brute, runner, armored (35% of
  physical damage), ember (25% of fire), frostborn (immune to frost), bats. `RaidPlanner` now has
  a twelve-wave cycle of its own through all ten. `AgentState.Archetype` is a label for the views.
- Replay schema 12. Pinned replays re-recorded.

**In Unity:** tools are two rows of three (Wall, Sell, Move / Gear, Upgrade [U], Target [T]); the
Upgrade tool opens a window with the four choices; the tower card names the upgrade and the
order; enemy types differ in size and colour; the line at the top says what an unusual type
does. Checked in play mode: the order changed, the chooser opened, Reach took the range from 3
to 4.5 and 12 budget.

**Open**
- The numbers of the new types and of the four upgrades are first guesses. The scripted player
  that gathers its towers round the core now loses in waves 2 to 3 on nine of ten maps (runners
  come in wave 2); a person will do better, but the opening may be too hard.
- Resistances make the mix of damage types matter, which the proposal's `damage_type_mix`
  feature assumes; the director's strategies (WP9) will have to price these types.
