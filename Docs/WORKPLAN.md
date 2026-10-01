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
Port the current game faithfully
WP0 ─┬─ WP1 ── WP2 ── WP3 ─┬─ WP4 ── WP5          (I1 proven on the known game)
     │                     └─ WP6                  (telemetry schema 1)
     └─ WP7                                        (Learning: independent; run it in a parallel session)

Then change the rules (towers)
WP5 ── WP-C1 combat core ─┬─ WP-C2 movement classes ─┐
                          ├─ WP-C3 shop (+schema 2) ──┼─ WP-C4 Unity towers + shop UI
                          └──────────────────────────┴─ WP-C5 roster v1 + balance sanity

Director
WP-C1 + WP-C3 + WP6 ── WP8 profile
WP-C2 + WP-C3 ──────── WP9 strategies ── WP10 layers ── WP11 full director ── WP12 harness ── WP13 gates
                                           WP10 + WP-C4 ── WP14 Unity director UX
```

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

**Traps:** repeat the cross-check once on a **Player build**, not just the Editor, before the S2 W6 build freeze. The IL2CPP and Mono float paths can differ, and this is the only test that would notice.
*(Update from WP4: Mono vs CoreCLR **did** differ, by one bit from tick 2916 of a real session, because Mono evaluated chained float expressions in double. This is fixed by the explicit-cast rule, ARCHITECTURE §9 rule 3, and all 5,036 ticks now match. IL2CPP compiles to C++, where the compiler flags decide contraction/FMA, so it still needs its own check. `CrossRuntimeBisectTests` shows how to bisect a divergence to a tick and component.)*

---

## WP6 — Telemetry v1 and placement entropy

**Reads:** §5.1, §5.8 · **Depends:** WP3 (can run in parallel with WP4 and WP5), **S7**

Schema 1 covers the ported game, which has no towers. WP-C3 bumps the schema to 2 (shop, tower placements, kills, damage by type).

**Creates** `Director/Telemetry/WaveRecord.cs` (schema 1; director fields nullable), `TelemetryWriter.cs`
(append, flush each line, never rewrite), `PlacementEntropy.cs`, and the CLI command `trace`.

**Must pass**
- A golden-file test: a fixed escalation run produces byte-identical JSONL. Timestamps are excluded or injected through an `IClock`.
- Rows for escalation-condition waves parse with the same `WaveRecord` class as director waves.
- Entropy: a uniform placement distribution gives maximum entropy, and a single repeated placement gives 0.

**Review probe:** open a JSONL file produced by a Unity session and run `trace` on it.

---

## WP-C1 — Combat core: HP, towers, damage (replaces the lifetime clock)

**Reads:** §4.6, `CLAUDE.md` §2 and I12 · **Depends:** WP5 (determinism proven on the ported game). S12 can use a placeholder roster.

**Creates / changes**
- `Sim/Combat/DamageType.cs`, `TowerDef.cs`, `TowerState.cs`, `TowerSystem.cs` (the step-3 tower pass), `Targeting.cs`, `DamageMap.cs`
- `AgentState`: **delete `Life` first**; the resulting compiler errors are the checklist. Add `Hp`, `MaxHp`, `Resist[]`, `SlowTicks`, `SlowFactor`.
- `SimNode`: add `Occupant { None, Wall, Tower }` and `TowerId`. `Placement.CanPlace` covers a single tower tile (I9). `Placement.PlaceTower(state, def, tile)`
  is internal: tests use it now, and the player-facing command arrives in WP-C3.
- `EscalationPlanner` v2: HP escalation replaces lifetime escalation (placeholder numbers until WP-C5).
- `WaveOutcome`: `Killed` replaces `Stalled`; add `DamageByType[]` and `TowersDestroyed`.

**Must pass**
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

**Traps:** `PinnedReplayTests` will fail here, **on purpose**. It re-runs recordings of the
pre-tower game (`Results/2026-10-02_determinism/`), and once HP replaces the clock they no longer
describe the game. Record new ones (a Unity session and a headless run, as that folder's README
describes), replace the files, bump `ReplayFile.CurrentSchema` if the command set changed, and
note it in DEVLOG. Do not delete the test: it is the only standing Mono-vs-.NET check.

---

## WP-C2 — Movement classes: Ground, Sapper, Flying

**Reads:** §4.6 · **Depends:** WP-C1

**Creates** `Sim/Movement/MovementClass.cs`, `FlowFieldSet.cs`, `FlyingMovement.cs`, and the class branches in the agent pass.

**Must pass**
- The Ground field is byte-identical to the WP1 parity results, so moving to `FlowFieldSet` changes nothing for Ground.
- In a fixture where one wall tile blocks a corridor with a 20-tile detour, Ground routes around it and Sapper digs through it.
- Flyers ignore walls and towers, never dig, and leak at the core.
- A tower with `CanHitFlying = false` never targets a flyer, even when it is the only agent in range.

**Review probe:** use `cli ascii --layer route` to render the Ground and Sapper routes on Bench_Maze side by side.

---

## WP-C3 — Shop and telemetry schema 2

**Reads:** §4.5 (counter-based shop RNG), §4.6 (shop defaults), `CLAUDE.md` I10 and I11 · **Depends:** WP-C1, WP6. S11 can start with the §4.6 defaults.

**Creates** `Sim/Shop/ShopState.cs`, `ShopRoller.cs`, `TowerOffer.cs`; the commands `PlaceTower`, `SellTower` and `RerollShop`; the events `ShopRolled`, `TowerPlaced` and `TowerSold`;
and `WaveRecord` schema 2 (`shop`, `placements[].kind`, `outcome.killed`, `outcome.damageByType`).

**Must pass**
- **I10:** with the same seed, offers for waves 1–30 are identical across (a) the Idle and GreedyDetour policies,
  (b) `EscalationPlanner` and a random planner, and (c) buying everything versus buying nothing. A different seed gives different offers.
- Across 100k rolls, each tower's offer frequency is within 1% of its shop weight.
- Buying the same slot twice fails the second time. Buying without enough budget is a no-op and emits nothing.
- A sale refunds `price × SellRefund`, clears the tile, and rebuilds the fields. Selling from an empty tile is a no-op.
- With rerolls off, `RerollShop` is a no-op. With rerolls on, `RerollCount` increases and the offers match the counter-based expectation.
- Schema 2 golden file. Rows from schema 1 still load.

**Review probe:** record a headless replay that includes purchases and sales, then verify it.

---

## WP-C4 — Unity towers and shop UI (functional, not polished)

**Reads:** §6 · **Depends:** WP4, WP-C2, WP-C3

**Creates** `Towers/TowerView.cs`, `ShopPanel.cs`, `EnemyHealthBar.cs`, `Config/TowerDefAsset.cs`, `EnemyArchetypeAsset.cs`, primitive placeholder prefabs,
the range ring in `GhostPreviewer`, a sell-mode input, and an HP bar replacing the life bar on `FlowAgent`.

**Must pass (manual playtest, logged in DEVLOG)**
- Buy each offer and place it, with the ghost agreeing with the click (I9) and the range ring matching the real range.
- Towers turn and fire cosmetic projectiles, and enemies lose HP and die. Only anti-air towers shoot flyers.
- Selling refunds the budget. Offers refresh at intermission start and stay unchanged during a wave.
- A Unity session with towers, recorded as a replay, verifies headless.

**Out of scope:** art, VFX, sound, and UI styling. Those belong to the S2 W1–W3 UX pass.

---

## WP-C5 — Tower roster v1 and balance sanity check

**Reads:** §4.6 · **Depends:** WP-C1, WP-C2, WP-C3, **S12**

**Creates**
- 4–6 `TowerDef` assets, covering at least single-target, splash, slow and anti-air across at least 3 damage types
- 3–4 enemy archetypes: basic, sapper, swarm and flyer
- HP escalation numbers
- The CLI command `balance`: policies × 10 seeds × 25 waves, reporting damage per budget spent on each tower, how often each policy buys each tower, and the wave each policy reaches

**Done when** `Results/<date>_balance-v1/README.md` exists with its hypothesis written **before** the run (for example: no tower exceeds 2× the median damage per budget; Idle dies before wave 5; no policy clears 25 waves untouched) and the table, whether it passes or not.
This is a sanity check only. The real balancing happens in the S2 W4 balance pass.

---

## WP7 — Learning assembly (independent: can run in a parallel session)

**Reads:** §5.6, `CLAUDE.md` §5 · **Depends:** WP0 only

**Creates** everything under `Assets/Thesis/Learning/`, plus the CLI command `synth`.

**Must pass**
- `BetaSampler`: at 200k draws, the sample mean and variance for (1,1), (2,5) and (30,10) are within 1% of the analytic values. It throws when either shape is below 1.
- `BinnedPosterior`: after 10⁵ random updates with G = 0.95, every α and β is ≥ 1. A strategy with no updates reports `Cold` and a correction of exactly 0.
  `Value` stays within [−1, +1] for any θ.
- `Export` → `Import` → the same RNG seed gives identical `SampleCorrection` sequences.
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

**Reads:** §5.3, `CLAUDE.md` §2 · **Depends:** WP-C1, WP-C3 (shop log), WP6, **S9**

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

**Must pass**
- A vetoed strategy scores `NegativeInfinity`, and a spy estimator that **throws when called** is never called for it (I5).
- When everything is vetoed, the director falls back to escalation and the trace records `fallback: "AllVetoed"`.
- Ties go to the lowest id. The trace contains one row per strategy, in id order.
- `TooSimilarToPrevious` vetoes a plan identical to the previous wave's and allows a plan above the threshold.
- `Uncounterable` vetoes `flying_bypass` when the player owns no anti-air tower and none is on offer, and allows it as soon as one is on offer.

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

**Review probe:** use the CLI `trace` on a 25-wave headless run and read three rows by hand. Check that the numbers in the rows are consistent with each other.

---

## WP12 — Harness: policies, counterfactual, ladder

**Reads:** §7 · **Depends:** WP5, WP11

**Creates** the rest of `Harness/Policies/*`, `Counterfactual.cs`, `Ladder.cs`, `ResultWriter.cs`, and the CLI command `ladder`.

*(From WP5: `GreedyDetourPolicy` as built is a weak player. On SampleScene's real settings it loses
in wave 1 or 2, because it maximises flow-field **cost** and a swarm chews through one wall tile
almost at once, so cost is a poor stand-in for time. The ladder needs policies that actually
survive; judge them after WP-C1, when damage, not time, decides a wave. `Registry` is where new
policies and planners get their names.)*

**Must pass**
- A counterfactual on a cloned state leaves the original simulation's hash unchanged.
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
(the latest `DirectorTrace` as a table), a session-condition selector, estimator snapshot import, and the director exception
fallback (§6). **Must pass:** throwing inside `WaveDirector` during play shows the fallback in telemetry and does not crash the session.
