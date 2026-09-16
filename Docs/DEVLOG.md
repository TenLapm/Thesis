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

**Tests added:** 33 (94 total: 90 pure + a few counted differently by each runner's
TestCase expansion — both toolchains agree at 90 distinct test methods before
TestCase expansion, 94+ individual results after)
- `OccupancyMapTests` (3): increment per node, reset, rejects a negative size.
- `AgentSystemTests` (8): **two agents breach in exactly half the ticks of one**
  (6 vs 3, hand-computed); **breach reward credited exactly once for 1, 2 and 8
  agents sharing a tile** (this is also WP2's review probe — see below); a stalled
  agent credits `deathReward` and never touches core HP; a leaked agent costs 1
  core HP and pays no reward; movement with `speed*dt` more than double the tile
  spacing still lands exactly on the next tile, never past it; a dead agent is
  fully skipped; occupancy and `MinCostSeen` update for live agents and are left
  untouched for one that leaks on its first tick.
- `ShapeBagTests` (7): rotation matches `(x,y) -> (y,-x)` over all 4 turns; a
  clone's rotation never touches its master; **every consecutive run of 7 draws is
  a permutation of a 7-shape library** (verified across 4 bags, i.e. 28 draws);
  rotating a drawn instance never leaks into a later draw of the same shape; hold
  works once per piece and preserves the held piece's rotation across a swap back;
  rejects an empty library.
- `PlacementTests` (8, one of them a 6-case `TestCaseSource`): the legality table
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
