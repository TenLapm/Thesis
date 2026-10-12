# ARCHITECTURE.md — code structure for the wave director

**Status: 2026-10-02.** WP0–WP5, the hardening pass (WP-H), the combat core (WP-C1) and the movement
classes (WP-C2) are built. Decisions D1–D9 are made (D1 = Option B, towers). Nothing here overrides `CLAUDE.md`. The order
of work was re-planned on 2026-10-02 (D8, `Docs/WORKPLAN.md`).

Companion file: `Docs/WORKPLAN.md` (ordered work packages, each with its tests
and a definition of done).

---

## 0. Decisions

D1–D4 were made on 2026-09-15.

| # | Decision | Outcome |
|---|---|---|
| D1 | `CLAUDE.md` §2: re-derive the features without towers (A) or add towers (B) | **B: towers.** Random shop, damage replaces the lifetime clock, towers occupy a tile as diggable terrain, towers persist and can be sold. See §4.6. |
| D2 | Move the game rules out of MonoBehaviours into a plain-C# simulation core (§1) | **Yes.** |
| D3 | Reword I9: placement legality lives in `Thesis.Sim.Placement.CanPlace`, and `PlayerBuilder.AreTilesPlaceable` wraps it | **Yes.** `CLAUDE.md` I9 has been updated. |
| D4 | The next intermission starts when the wave's **last agent resolves**, not when it spawns | **Yes.** It changes pacing, so playtest it. |

D5–D9 were made on 2026-10-02, after the review that followed WP5
(`Docs/REVIEW-2026-10-02.md`). The student chose D6 and delegated the other four.

| # | Decision | Outcome |
|---|---|---|
| D5 | Which runtime the study build uses | **Windows 64-bit, Mono scripting backend, managed stripping off.** This is the project's current setting and the runtime WP5 verified, so the IL2CPP question is closed. `BuildGuard` fails any other player build. A session recorded by a built player must still be replayed headless **before the pilot**. |
| D6 | Can the player remove walls? | **Yes: walls can be sold.** A whole placed piece is sold at once for a partial refund, in build phases only. Rules in §4.6; built in WP-C3. |
| D7 | Spec gap S7: what exactly G5's primary metric is | **Decided provisionally in §5.9:** the entropy of the session's built tiles over a 6×6 partition of the map, walls and towers together. It is computed offline from replays, so it can be changed at no cost until it is **frozen before the pilot**. The supervisor should confirm it before then. |
| D8 | Order of work | **Hardening first (done), then WP-C1. WP6 is folded into WP-C3. WP7 runs in parallel.** Flying units are cut if WP-C3 is not finished by the end of W9. See `WORKPLAN.md`. |
| D9 | Seeds and session setup (spec gap S13) | **Two seed sets, crossed with condition and order.** No participant meets the same seed twice, and each condition gets each seed and each position equally often. Development keeps the fixed seed 1. See §6. |

**Order of work that follows from D1 + D2:** port the *current* game faithfully
first (WP0–WP5, lifetime clock included) so that parity and determinism are proven
against a game that is already known to work. Only then change the rules
(WP-C1…C5). If the port and the tower change happened in the same package,
a parity failure could not be traced to either one.

Also a small wording change to note: `CLAUDE.md` §5/§7/§9 say "extend `Benchmark/`"
for the harness. What gets reused are the scenario layouts, the environment
block, the result-file convention, and the recorded path metrics (as a parity
oracle). The `StringBuilder` JSON writers are replaced (§2.3).

---

## 1. Why the game rules must leave Unity's frame loop

Evidence from the current code:

- `FlowAgent.Update` advances lifetime, digging and movement by `Time.deltaTime`.
  The result therefore depends on frame rate *and* on the x1/x2/x3 speed setting
  (`GameSpeedController` scales `Time.timeScale`). Seeding the RNG alone cannot
  satisfy I1.
- When several agents chew the same tile, the order is Unity's `Update` order,
  which Unity does not define. `ChewWall` calls `GenerateFlowField()` in the middle of
  that loop, so agents later in the order see the new field a frame before the
  earlier ones.
- `WaveSpawner` spaces spawns with `WaitForSeconds`, so spawn times snap to frame boundaries.
- `BlockManager.RefillBag` uses `UnityEngine.Random`.
- All game state lives on scene objects: `Node[,]` in `GridManager` and
  `buildBudget` in `BlockManager`. So the profiler (W7–W9), the strategies (W10) and
  the harness (W13) could not be tested without loading a scene.

The fix: game rules become plain C# that advances in **fixed ticks**. Unity
becomes a *host*: it passes player input in as commands and draws the resulting state.

**Unchanged:** the flow-field algorithm, the ×10 cost model, the corner-cut
rule, diggable walls, and everything in `CLAUDE.md` §8.

Alternative considered and rejected: keep the MonoBehaviours but have a host
tick them manually. That gives determinism inside Unity, but every profiler,
strategy and harness test would still need a scene, and every debug cycle
would still wait on a domain reload.

---

## 2. Assemblies

```
                    ┌───────────────┐
                    │  Thesis.Core  │  Pcg32, hashing, JSON settings, TileCoord, Vec2f
                    └───────┬───────┘
             ┌──────────────┴──────────────┐
     ┌───────▼───────┐             ┌───────▼────────┐
     │  Thesis.Sim   │             │ Thesis.Learning │  I3 lives here: estimators only,
     │  game rules   │             │                 │  cannot see any game type
     └───────┬───────┘             └───────┬────────┘
             └──────────────┬──────────────┘
                    ┌───────▼────────┐
                    │ Thesis.Director │  profile, strategies, layers, reward, telemetry
                    └───────┬────────┘
                    ┌───────▼────────┐
                    │ Thesis.Harness  │  scripted players, replay, ladder, gates
                    └────────────────┘

 Assembly-CSharp         (existing Assets/Scripts, no asmdef) → Core, Sim, Learning, Director
 Assembly-CSharp-Editor  (Assets/Scripts/Editor)              → + Harness
 Thesis.Tests.EditMode   (Assets/Tests/EditMode)              → all Thesis.* assemblies
```

### 2.1 Rules

- Every `Thesis.*` asmdef sets `"noEngineReferences": true` and lists its
  references **by name**, not by GUID.
- `Thesis.Learning` must never reference `Thesis.Sim` or `Thesis.Director`. It
  sees only `DecisionContext` (a bin index and two floats).
- References only point downward in the diagram. No cycles, and nothing in `Thesis.*`
  references `Assembly-CSharp`.

### 2.2 Enforced twice

1. **Unity:** `noEngineReferences` rejects `using UnityEngine`.
2. **`dotnet`:** `Tools/dotnet/` holds one `.csproj` per assembly. Each one
   compiles the same `Assets/Thesis/<Name>/**/*.cs` files, targets
   **`netstandard2.1`** (the API surface Unity actually has) with
   **`LangVersion 9.0`** (Unity 6000.4's C# version), and has no UnityEngine
   reference. Using a .NET 9-only API or a C# 10 feature fails `dotnet build`
   before Unity ever sees it.

### 2.3 JSON

Newtonsoft.Json everywhere: Unity package `com.unity.nuget.newtonsoft-json`
3.2.2 (added to `manifest.json` explicitly in WP0; before that it was only a transitive dependency)
and NuGet `Newtonsoft.Json` 13.0.2 on the `dotnet` side. `Thesis.Core.Json` holds the one
shared `JsonSerializerSettings` (invariant culture, round-trip floats, ordered
properties). Never use `JsonUtility` in `Thesis.*`.

**Two ways to write a number (WP-C3).** `Json.Serialize` keeps every bit of a float; replays,
maps and snapshots use it. `Json.SerializeLine` is for telemetry lines, whose *text* must be
the same on every runtime, and the library's own number text is not: it prints the float
0.20000458 as `0.20000458` under .NET and as `0.200004578` under Unity's Mono. So a line's
floats go through `Json.FormatFloat` (7 significant digits) and its doubles through
`Json.FormatDouble` (12): plain decimals, no exponent, no trailing zeros, made with one
multiplication by a power of ten, a floor and integers, so that no library routine formats
a floating-point number. `NaN` and the infinities are written as strings. That text is for
reading and for comparing files; it is not the exact bits. Whatever must be bit-exact is in
the replay. `NamedValues` is an ordered name → number list written as a JSON object
(`{"physical":812,"fire":240.5}`), used where a `Dictionary`'s order would be an accident.

---

## 3. Folder layout

```
Assets/
  Thesis/
    Core/        Thesis.Core.asmdef      Pcg32.cs IRandom.cs RngStreams.cs Fnv1a64.cs TileCoord.cs Vec2f.cs Json.cs DetMath.cs
                                         NamedValues.cs NamedValuesConverter.cs DeterministicNumberConverter.cs   (WP-C3)
    Sim/         Thesis.Sim.asmdef
      Map/         MapData.cs
      Grid/        SimNode.cs SimGrid.cs FlowField.cs Route.cs Occupant.cs
      Agents/      AgentState.cs AgentSystem.cs
      Build/       ShapeDef.cs ShapeBag.cs Placement.cs PlacementRecord.cs PlacementKind.cs ShapeLibraryFile.cs
      Waves/       IWavePlanner.cs WaveContext.cs WavePlan.cs AgentGroup.cs WaveOutcome.cs EscalationPlanner.cs
                   PlanValidator.cs SafePlanner.cs ClassCyclePlanner.cs (development only, until WP9)
      Combat/      DamageType.cs DamageTypes.cs TargetingMode.cs TowerDef.cs TowerState.cs TowerSystem.cs
                   Targeting.cs DamageMap.cs TowerRoster.cs (placeholder roster until WP-C5)             (WP-C1, built)
      Movement/    MovementClass.cs MovementClasses.cs FlowFieldSet.cs FlyingMovement.cs RouteCost.cs    (WP-C2, built)
      Shop/        ShopState.cs ShopRoller.cs TowerOffer.cs                                          (WP-C3, built)
      Commands/    SimCommand.cs
      Events/      SimEvent.cs
      Stats/       OccupancyMap.cs
      Debug/       AsciiMap.cs AsciiState.cs StateHasher.cs StateDump.cs BenchScenarios.cs
      Replay/      ReplayFile.cs ReplayCommand.cs WaveHash.cs ReplaySetup.cs ReplayRecorder.cs
                   (in Sim, not Harness: SimHost records in player builds, and Harness is editor-only)
      SimConfig.cs SimState.cs SimPhase.cs Simulation.cs
    Learning/    Thesis.Learning.asmdef
      IStrategyEstimator.cs DecisionContext.cs Correction.cs BetaSampler.cs
      BinnedPosterior.cs KernelEstimator.cs EstimatorState.cs CalibrationReport.cs
      Synthetic/   ThresholdBanditEnv.cs
    Director/    Thesis.Director.asmdef
      Profile/     BuildProfile.cs IBuildFeature.cs ProfileInput.cs BuildProfiler.cs   [after WP-C1]
        Features/  MazeLength.cs BreachVulnerability.cs ChokepointReliance.cs TowerConcentration.cs DamageTypeMix.cs
      Context/     ContextBucketizer.cs
      Strategies/  IWaveStrategy.cs ThreatCostTable.cs BreachThinWall.cs SwarmChokepoint.cs SplitGroups.cs FlyingBypass.cs
      Layers/      IConstraint.cs VetoReason.cs IHeuristic.cs AuthoredHeuristic.cs
        Constraints/ OverBudget.cs RepeatStreak.cs Uncounterable.cs TooSimilarToPrevious.cs
      Decision/    WaveDirector.cs DirectorMode.cs DirectorTrace.cs StrategyRow.cs DecisionHistory.cs
        Baselines/ RandomPlanner.cs RoundRobinPlanner.cs
      Reward/      RewardEvaluator.cs RewardBreakdown.cs CreditAssigner.cs
      Announce/    AnnouncementTable.cs
      Telemetry/   WaveRecord.cs TelemetryRecorder.cs TelemetryWriter.cs
                   PlacementEntropy.cs PlacementScope.cs                                             (WP-C3, built)
      DirectorConfig.cs
    Harness/     Thesis.Harness.asmdef
      Policies/    IPlayerPolicy.cs IdlePolicy.cs GreedyDetourPolicy.cs SentryPolicy.cs SequencePolicy.cs
                   SellerPolicy.cs                                                                   (built)
                   RepeatTemplatePolicy.cs
                   ChokepointPolicy.cs SpreadPolicy.cs ReactivePolicy.cs
      Bench/       PathfindingBench.cs
      Replay/      ReplayRunner.cs ReplayReport.cs PinnedEpisodes.cs
      EpisodeRunner.cs EpisodeOptions.cs EpisodeResult.cs Registry.cs   (Registry: planner and policy names → instances)
      Ladder.cs Counterfactual.cs ResultWriter.cs
      Gates/       G1LearnerAccuracy.cs G2DirectorTiming.cs G3GradedVsBinary.cs
  Scripts/                     (Assembly-CSharp: Unity host and views; existing files stay here)
    SimHost.cs DirectorHost.cs  (new)
    Towers/  TowerView.cs ShopPanel.cs EnemyHealthBar.cs              (new, WP-C4)
    Config/  SimConfigAsset.cs DirectorConfigAsset.cs BucketThresholdsAsset.cs
             ThreatCostTableAsset.cs AnnouncementTableAsset.cs
             TowerDefAsset.cs EnemyArchetypeAsset.cs                  (ScriptableObjects → plain config)
    Debug/   DirectorOverlay.cs  (new, F9)
    Editor/  MapExporter.cs ShapeExporter.cs ReplayMenu.cs BuildGuard.cs HarnessMenu.cs
    Benchmark/ ...               (ported onto SimHost in WP4)
  Tests/EditMode/  Thesis.Tests.EditMode.asmdef
    Core/ Sim/ Learning/ Director/ Harness/   (mirrors Assets/Thesis)
    Fixtures/  TestMaps.cs  (ASCII map fixtures)
Maps/        SampleScene.map.json Bench_Open.map.json ...   (exported static geometry)
             Shapes.json                                    (exported wall-shape library, in bag order)
Runs/        (gitignored) raw run output: telemetry.jsonl, replay.json
             Runs/Sessions/<time>_seed<seed>/ is what a Unity editor session writes: replay.json and telemetry.jsonl
Results/     (committed) one folder per reported result, per CLAUDE.md §7
             Results/pinned-replays/ is the standing set PinnedReplayTests re-runs (re-recorded when the rules change):
             each <name>.replay.json has its <name>.telemetry.jsonl beside it
Docs/        ARCHITECTURE.md WORKPLAN.md DEVLOG.md REVIEW-2026-10-02.md
             GDD.md (the game from the player's side; its pictures are in Docs/gdd/) and GDD.pdf, made from it
Tools/gdd/   pdf.py, pictures.py, board.py, routes.txt, routetool/ (README.md there): what makes GDD.pdf and Docs/gdd
.github/workflows/headless-tests.yml   `dotnet test` on every push, Linux and Windows
.gitattributes                          LF in the repository on every machine; *.jsonl is LF in the working tree too
Tools/dotnet/
  Directory.Build.props   LangVersion 9.0, netstandard2.1 for libraries, net9.0 for Cli/Tests
  Thesis.Headless.sln
  Thesis.Core/ Thesis.Sim/ Thesis.Learning/ Thesis.Director/ Thesis.Harness/   (csproj only, glob-include Assets sources)
  Thesis.Cli/   Program.cs Args.cs RunCmd.cs ReplayCmd.cs AsciiCmd.cs PinCmd.cs TelemetryCmd.cs TraceCmd.cs EntropyCmd.cs
  Thesis.Tests/ (csproj glob-includes Assets/Tests/EditMode/**/*.cs; NUnit 3.x)
```

---

## 4. Simulation core — `Thesis.Sim`

### 4.1 Facade

```csharp
namespace Thesis.Sim
{
    public sealed class Simulation
    {
        public Simulation(SimConfig config, MapData map, ShapeDef[] shapeLibrary, TowerDef[] towerLibrary,
                          ulong rngSeed, IWavePlanner planner);

        public SimState State { get; }                  // callers outside Thesis.Sim treat it as read-only
        public IReadOnlyList<SimEvent> LastTickEvents { get; }

        public void Enqueue(SimCommand command);        // applied at the start of the next Tick()
        public void FlushInput();                       // apply queued commands now, without advancing time (WP4)
        public void Tick();                             // advances exactly config.TickSeconds
        public Simulation Clone(IWavePlanner planner);  // deep copy incl. RNG state; for counterfactuals
        public ulong ComputeHash();

        public ulong RngSeed { get; }                   // what a replay needs to rebuild this run (WP5)
        public ShapeDef[] ShapeLibrary { get; }
        public TowerDef[] TowerLibrary { get; }         // the roster a PlaceTower command chooses from (WP-C1)
        public SimConfig Config { get; }
        public MapData Map { get; }
        public IWavePlanner Planner { get; }
    }
}
```

`SimState` holds everything that changes during a run: `Tick`, `Phase`,
`PhaseTicksRemaining`, `WaveIndex`, `Grid`, `Agents` (pooled array, where agent
id = array index), `Towers` (the same: tower id = index, a destroyed tower stays in
the list), `Bag`, `BuildBudget`, `CoreHp`, `CurrentPlan`, `CurrentOutcome`, `Occupancy`
and `Damage` (both per wave), `PlacementLog`, `Rng` (Bag and Spawn streams), and
`IsGameOver`.

`PlacementLog` is needed from the very first WP. The primary metric in G5 is the
entropy of wall placement, and three of the five Option-A features read it.

### 4.2 Tick order (normative; tests assert this order)

This is the order of the **faithful port** (WP3), kept here because the phase machine,
the field rebuild and the wave boundary are unchanged. **Step 3 below is no longer what
runs:** since WP-C1 there is a tower pass, the agents have hit points and the lifetime
clock is gone, and since WP-C2 an agent moves by its movement class. The tower and agent
passes as built are in §4.6. "The field" in steps 1 and 4 is now two fields, rebuilt
together by `FlowFieldSet`.

```
0. if IsGameOver: return                        (Tick does not advance)
1. Apply queued commands in enqueue order.
     PlaceShape rebuilds the flow field immediately (same as PlayerBuilder today),
     so this tick's agents already see the new wall.
2. Phase machine
     Prep | Intermission: if the countdown would reach 0, or on a StartWaveNow command → BeginWave():
         plan = planner.PlanWave(ctx for wave WaveIndex+1); ValidatePlan(plan);    ← nothing is written before this passes
         WaveIndex++; Occupancy.Reset(); CurrentOutcome = new(...); emit WaveStarted; Phase = Spawning
       otherwise: PhaseTicksRemaining--
     Spawning: spawn every agent whose scheduled tick <= Tick
         (groups interleaved by scheduled tick; ties → lower group index, then lower slot)
         when all are spawned → Phase = Resolving
3. Agent pass over live agents in ascending id
     life -= dt;  if life <= 0 → Stalled, BuildBudget += deathReward, continue
     node = Grid.NodeFromPosition(pos);  if node.BestCost == 0 → Leaked, CoreHp -= 1, continue
     Occupancy[node]++;  agent.MinCostSeen = min(agent.MinCostSeen, node.BestCost)
     if node.HasWall → dig: wallHealth -= digRate*dt;
         if wallHealth <= 0 → clear tile, BuildBudget += wallBreakReward,
                             fieldDirty = true, emit WallBreached
         continue
     else → pos = MoveTowards(pos, node.Next.Position, moveSpeed*dt)
4. if fieldDirty → FlowField.Generate(Grid) once
5. if Phase == Resolving and no live agent has WaveIndex == State.WaveIndex:
     finish a COPY of CurrentOutcome → planner.OnWaveResolved(copy) → commit it as LastOutcome → emit WaveResolved
     BuildBudget += stipend; Phase = Intermission; PhaseTicksRemaining = intermissionTicks
6. if CoreHp <= 0 → IsGameOver = true; emit GameOver
7. Tick++
```

Behaviour changes from the Unity version. Record each one in `Docs/DEVLOG.md`
when it lands:

- Step 4: a breach rebuilds the field **once, at the end of the tick**, instead
  of in the middle of the agent loop. Every agent in a tick now sees the same field.
- Step 5: the D4 wave boundary.

**A planner call never leaves the state half-written** *(2026-10-02)*. `PlanWave` and
`OnWaveResolved` are called *before* the simulation commits anything for that step. If
either throws, or `ValidatePlan` refuses the plan, the state is exactly as it was and
the next `Tick()` tries again. During `PlanWave`, `State.WaveIndex` is therefore still
the previous wave's number; the wave being planned is `context.WaveIndex`.
`PlannerFailureTests` pins this. (Before the change, a planner that threw three times
made the first real wave "wave 4", and one throw in `OnWaveResolved` made every later
tick crash.) In a real session planners run inside `SafePlanner` (§4.4), so the
simulation never sees a throw at all.

### 4.3 Porting rules (parity depends on these)

| Rule | Why |
|---|---|
| Positions stay in **world units on the XZ plane**, and `MapData` carries `WorldSizeX/Y`, `NodeRadius` and `Origin`. `NodeFromPosition` is a line-for-line port of `GridManager.NodeFromWorldPoint`, **including** its `(gridSize-1)*percent` rounding skew and banker's rounding (`MathF.Round` default). | Speeds (1.25 world units/s) and the tile an agent occupies mid-hop (which decides when digging starts) must not change. Fixing the skew would be a deviation; record it if you choose to do it. |
| `GetNeighbors` iterates `x = -1..1` outer, `y = -1..1` inner, exactly as today, filling a reused buffer. | In SPFA, iteration order decides which of two equal-cost parents wins, and so decides `Next`. A different order produces a different path. |
| SPFA uses a FIFO `Queue<SimNode>` with strict `<` relaxation. | Same reason. |
| `MoveTowards` is a port of Unity's `Vector3.MoveTowards` (return the target when `sqDist == 0 \|\| sqDist <= maxDelta²`). | It is what stops fast agents from skipping wall tiles (see the comment in `FlowAgent`). |
| `SimNode.NextIndex` is an `int` (−1 = none), not a reference. | Makes `Clone()` and hashing trivial. |
| Every duration in config is stored in seconds and converted to ticks **once** at construction (`(int)MathF.Round(s / TickSeconds)`). | Keeps rounding in one place. |
| Tick length is 0.02 s (50 Hz). | Matches Unity's default fixed timestep, and x3 speed means 3× as many ticks per frame. |
| Game values come from the **scene and prefab**, not the C# defaults. For example, SampleScene has `agentsPerWave: 100` and `spawnDelay: 0.2`, while the C# defaults are 15 and 0.5. `digRate`, `deathReward` and `wallBreakReward` are not serialized on `Assets/Scripts/Prefabs/Agent.prefab` (the one SampleScene uses), so the C# defaults of 1 / 0.2 / 1.0 apply. | The static baseline must be the game as players actually experienced it. |

### 4.4 The planner seam: one interface for every study condition

```csharp
public interface IWavePlanner
{
    string Name { get; }                           // written to telemetry as "condition"
    WavePlan PlanWave(WaveContext context);        // once per wave start; must NOT mutate context.State
    void OnWaveResolved(WaveOutcome outcome);      // once per wave, after its last agent resolves
}
```

| Planner | Where | Role |
|---|---|---|
| `EscalationPlanner` | `Thesis.Sim` | Today's `WaveSpawner` math, ported verbatim. It is the static baseline condition and the default when no director is present. Ground enemies only. |
| `ClassCyclePlanner` | `Thesis.Sim` | **Development only, not a condition.** Turns the escalation wave into sappers, flyers or a mix, in a fixed four-wave cycle, so the movement classes can be played, recorded and compared across runtimes before the strategies of WP9 exist. Its archetype numbers are placeholders (S12). Remove it, or keep it as a test planner, once the strategies send sappers and flyers. |
| `RandomPlanner`, `RoundRobinPlanner` | `Director/Decision/Baselines` | Rungs of the ladder and the G1 comparison baselines. They spend the same threat budget as everything else. |
| `WaveDirector` (`DirectorMode.HeuristicOnly`) | `Director/Decision` | Layers 1 and 2. This is the ablation condition. |
| `WaveDirector` (`DirectorMode.Full`) | `Director/Decision` | Layers 1, 2 and 3. |

The mutation guard: `Simulation` hashes the state before and after `PlanWave`
**and** `OnWaveResolved`, and throws `InvalidOperationException` if the hash
changed. It is always on (it costs two hashes per wave boundary), not only in
DEBUG as first planned. See DEVLOG WP3.

**`SafePlanner`** (`Thesis.Sim/Waves`) wraps any planner and is what keeps the promise
in §6 that a session never crashes because of the director. If the inner planner throws,
or returns a plan `PlanValidator` refuses, that wave uses the escalation plan instead
(the same threat budget, so I2 holds) and the wrapper records why: `LastFallbackReason`
is `Exception`, `InvalidPlan` or `ExceptionInOnWaveResolved`. `DirectorHost` wraps every
planner, the baseline included. The wrapper keeps the inner planner's `Name`, and a
wrapped escalation run is tick for tick the same game as a bare one, so a Unity
recording still replays headless. What it cannot rescue is a planner that *writes* to
the state: the mutation guard still throws, because the state is already wrong.

**Cloning and planners.** `Simulation.Clone(planner)` copies the simulation, not the
planner. A planner with state (the director: estimator, RNG streams, history) must
never be shared between the real run and a clone, or the clone's wave would train and
advance the real director. `Counterfactual` (WP12) gives every clone its own
fixed-strategy planner.

```csharp
public sealed class WavePlan   { public int WaveIndex; public string StrategyId; public AgentGroup[] Groups;
                                 public float ThreatSpent; public string AnnouncementId; }
public sealed class AgentGroup { public int SpawnIndex; public int Count; public string Archetype;
                                 public MovementClass Movement;  // Ground (default), Sapper or Flying (WP-C2)
                                 public float MoveSpeed; public float Hp; public float DigRate;
                                 public float[] Resist;          // one multiplier per DamageType; null = all 1
                                 public int SpawnIntervalTicks; public int StartDelayTicks; }
```

`PlanValidator` refuses a group with `Count > 0` whose `Hp` or `MoveSpeed` is not a
positive number, whose `Resist` has the wrong length or a negative entry, or whose
`Movement` is not one of the three classes.

`WaveOutcome` contains `WaveIndex`, `StrategyId`, `Spawned`, `Killed`, `Leaked` (and the
same three per movement class: `SpawnedByClass[]`, `KilledByClass[]`, `LeakedByClass[]`),
`Removed` and `TimedOut` (the backstop of §4.6), `WallsBreached`, `TowersDestroyed`,
`DamageByType[]`, `CoreHpBefore/After`, `BudgetBefore/After`,
`TickStarted/Resolved`, and `PressureShare` (the fraction of agents whose
`MinCostSeen / 10f` fell to `≤ config.PressureRadiusTiles` or below; see spec gap S5).
`MinCostSeen` is each enemy's own measure of the way left (`RouteCost`, §4.6): its
field's cost for a walker or a sapper, the straight line for a flyer.

### 4.5 Commands, events, RNG

- `SimCommand` mirrors the player's actual inputs: `PlaceShape(originX, originY)`,
  `Rotate`, `Hold`, `StartWaveNow`, and since WP-C3 `PlaceTower(offerSlot, x, y)` (buys one
  of the shop's offers and puts it on a tile), `SellTower(x, y)`, `SellWall(x, y)` and
  `RerollShop`. The current shape and its rotation live in `ShapeBag`, so a replay
  reproduces the bag's state as well. The input log is a list of `(tick, command)`.
  **A command records what the player tried.** One that cannot work (an illegal tile, an
  empty budget, a slot already bought, a sale during a wave) changes nothing and raises
  no event, but it is still in the replay.
- `SimEvent` is a `readonly struct` holding a `Kind` enum plus integer and float payload
  fields: `WallPlaced`, `WallBreached`, `AgentSpawned`, `AgentKilled`,
  `AgentLeaked`, `WaveStarted`, `WaveResolved`, `GameOver`, since WP-C1 `TowerPlaced`,
  `TowerDestroyed`, `TowerFired`, `AgentRemoved` and `WaveTimedOut`, and since WP-C3
  `ShopRolled(waveIndex, rerollCount)`, `TowerSold(towerId, x, y, refund)` and
  `WallSold(x, y, pieceId)` (one per tile cleared). Views rely on these
  events and never poll for changes.
- Randomness comes only from `Pcg32` streams derived from one `rngSeed`, one stream per
  subsystem (`RngStreams.Bag`, `Spawn`, `Strategy`, `Thompson`, `RewardBernoulli`,
  `Policy`). Adding a draw in one subsystem therefore never shifts another subsystem's
  sequence. That property is what keeps a determinism divergence local and findable.
- **The shop is the exception: its RNG is counter-based, not a running stream.** Each roll
  seeds a new `Pcg32` (on the `RngStreams.Shop` stream) from
  `Fnv1a64(rngSeed, "shop", waveIndex, rerollCount)`: `ShopRoller.SeedFor`. So the
  offers for wave 7 are the same no matter what the player bought earlier, which
  planner is running, or how many draws other systems have made. This is what makes I10 hold.

**Replay file (WP5).** `Thesis.Sim.ReplayFile`, written by `ReplayRecorder`. Schema 4
since WP-C3. (Schema 2, WP-C1, added the tower roster and the `PlaceTower` command.
Schema 3, WP-C2, kept the layout; every hash also covered the sapper field and each
enemy's movement class. Schema 4: `PlaceTower` names a shop offer slot where it named a
roster index, there are three more commands, and every hash also covers the shop, each
wall tile's piece, and what a tower cost and whether it was sold.) A file of another
schema is refused, because its commands and hashes describe a different game. A command
name the simulation does not know is refused too, also when it is a number.

| Field | Meaning |
|---|---|
| `Build`, `Session`, `Policy` | Labels for people. `Policy` is `"human"` or a scripted policy's name. Never read by a replay. |
| `Map`, `MapSeed`, `RngSeed`, `Planner` | What to rebuild. `MapSeed` is 0 until seeded mazes exist. `Planner` is `IWavePlanner.Name`, resolved by `Harness.Registry`. |
| `Config`, `MapData`, `Shapes`, `Towers` | **Stored in full**, so the file is self-contained and still means the same thing after a retune or a re-export. `Towers` is the roster, in order: the shop draws its offers from it by index. |
| `SetupHash` | `ReplaySetup.Hash(Config, MapData, Shapes, Towers)` taken when recording **started**. Loading recomputes it; a mismatch means a number did not survive JSON, the file was edited, or the config was changed in the inspector mid-session. The file is then refused. |
| `InitialHash` | State hash before the first tick and the first command. A mismatch here means the seed or the rules differ. |
| `Commands` | `[{Tick, Cmd, X, Y, A}]` (`A` is the offer slot of a `PlaceTower`). `Tick` is `State.Tick` when the command was applied; commands sharing a tick apply in file order. |
| `WaveHashes` | `[{Wave, Tick, Hash}]`, taken right after the tick that raised `WaveResolved`. |
| `FinalTick`, `FinalHash` | Where the recording stops, after any commands applied at that tick. A session quit mid-wave is still checked up to here. |
| `TickHashes` | Optional. One hash per tick, 8 bytes little-endian each, base64. Entry `i` is the state right after tick `i` ran. Needed to pin a divergence to a tick. |

Hashes are 16 hex digits in strings, not JSON numbers, because readers that go through
`double` cannot hold 64 bits. The estimator snapshot planned for this file arrives with the
director (WP11); `MissingMemberHandling.Ignore` makes adding it non-breaking.

**The next schema arrives with the first director planner (WP10): the file also records
each wave's `WavePlan`.** A replay then plays the recorded plans back through a
`RecordedPlanner` and needs no director at all. The simulation replay is exact for every
condition and on every runtime, and it stays valid after the director's code changes.
Checking the director becomes a second, separate step (`replay --rerun-director`):
rebuild it from the `DirectorConfig` and estimator snapshot stored in the file, run it
again, and compare its plans with the recorded ones. The reason for the split: the
director's arithmetic is the part most likely to differ between machines, and the study
data must not depend on reproducing it. It also means telemetry can always be
regenerated from a replay. *(For the conditions that exist today that is already so:
`ReplayRunner.Telemetry(file)` gives back the session's telemetry file byte for byte,
§5.8.)*

Rule for anything that ends up in a replay's setup: **no negative zeros**. JSON drops the
sign, so the file would describe a different input from the one that ran (found in WP5;
`SceneMapBuilder` strips them).

### 4.6 Combat, movement classes and shop (WP-C1…C3)

**Built: the combat core (WP-C1), the movement classes (WP-C2), and the shop and selling
(WP-C3).** The roster is still `TowerRoster.Placeholder()`: three unbalanced towers (single
target, splash, slow), one per damage type (WP-C5).

**Enemies.** `AgentState` has `Hp`, `MaxHp`, `Resist[]` (one damage multiplier per
`DamageType`; 1 means normal, 0 means immune), `SlowTicks` and `SlowFactor`, a
`MovementClass`, and no lifetime. The stats come from `AgentGroup`, which names an enemy
archetype and carries its numbers. An enemy ends in one of three ways:
**killed** (the player is paid `KillReward`), **leaked** (the core loses 1 HP), or
**removed** by the wave backstop below.

**Towers.** A tower is `TowerDef` data (cost, `DamageType`, damage, range in tiles,
`FireIntervalTicks`, splash radius, slow, `CanHitFlying`, `DigCost`, `TowerHealth`,
shop weight) plus `TowerState` (id, tile, cooldown). On the grid it occupies **one
tile as diggable terrain**. `SimNode` gains `Occupant { None, Wall, Tower }`,
`TowerId` and, for a wall tile, `PieceId` (the index of the placement that built it,
which is how `SellWall` finds the rest of the piece; WP-C3). `terrainCost`/`wallHealth` work
exactly as they do for walls. When
an enemy chews through the tile, the tower is destroyed.

**Movement classes (WP-C2).** `AgentGroup.Movement` says how a group gets to the core:

| Class | Route | Digs | Towers |
|---|---|---|---|
| `Ground` | The ground flow field (`SimNode.BestCost` / `NextIndex`): the field the game has always had. | Chews whatever built tile it stands on. | Every tower can hit it. |
| `Sapper` | A second flow field (`SimNode.SapperCost` / `SapperNextIndex`) in which a built tile costs `max(1, round(terrainCost × SimConfig.SapperDigCostFactor))`. With the default 0.2, a wall of cost 15 reads as a 3-tile detour, not 15, so a sapper goes **through** what a walker goes round. | The same way. How fast is the group's `DigRate`; the class only chooses the route. | Every tower can hit it. |
| `Flying` | No field. A straight line to the centre of the core tile, over walls, towers and static blockers. | Never. | Only a tower with `CanHitFlying`. |

`FlowFieldSet` rebuilds both fields together, and the simulation rebuilds only through
it, so the two can never be out of step. One flood-fill implementation (`FlowField`)
fills either field. A rebuild of both fields costs about a millisecond (measured
2026-10-02 on 38×38 with 0%, 10% and 25% of the tiles built, the last far denser than a
real game: 0.29 / 0.46 / 0.58 ms under .NET, 0.76 / 0.91 / 1.13 ms in the Unity editor;
the ground field alone is a little under half of that). It happens once per placement
and at most once per tick, so there is still no caching and no "skip the sapper field
while no sapper is alive" (the reasoning is the same as in `CLAUDE.md` §8). A strategy
or policy that *searches* by rebuilding the field must count those rebuilds against G2's
16 ms: about 30 ground-only rebuilds in Unity. `FlowField.Generate` on its own builds
the ground field only: that is what a policy trying a wall on a scratch grid wants, and
it is never called on the simulation's own grid.

**Tick order since WP-C1 and WP-C2.** These three steps replace steps 3 and 4 of §4.2.
The wave backstop (below), the wave boundary and the core check follow them, as there:

```
3. Tower pass, ascending tower id
     cooldown--; if cooldown > 0 → continue
     candidates = live agents within range that this tower can hit (Flying only if CanHitFlying)
     target = Targeting.Pick(def.Mode, candidates)       ties → lowest agent id
     if none → continue                                  (tower fires as soon as one appears)
     hit(target): dmg = def.Damage × target.Resist[def.DamageType]
                  target.Hp -= dmg; DamageMap[tile under target] += dmg
     splash: hit every other agent within SplashRadius of the target, in id order
     slow:   SlowTicks = max(current, def.SlowTicks); SlowFactor = min(current, def.SlowFactor)
     cooldown = def.FireIntervalTicks; emit TowerFired(towerId, targetId)
     every agent with Hp <= 0 → Killed, BuildBudget += killReward, emit AgentKilled
4. Agent pass, ascending agent id (no lifetime clock)
     speed = MoveSpeed × SlowFactor while slowed; the slow counts down
     on the core tile → Leaked, CoreHp -= 1               (every class; a flyer over it counts)
     MinCostSeen = min(MinCostSeen, RouteCost of this agent)
     Flying:          MoveTowards(core tile centre, speed × dt); nothing else
     Ground / Sapper: Occupancy[node]++
                      on a built tile → chew it; at 0 health clear it, fieldDirty = true,
                          wall → BuildBudget += wallBreakReward, emit WallBreached
                          tower → destroy it, emit TowerDestroyed (no reward by default; S12)
                      otherwise → MoveTowards(next tile on ITS field, speed × dt)
5. if fieldDirty → FlowFieldSet.Generate once (ground field, then sapper field)
```

Decided while building it (WP-C1), and pinned by tests:

- **Damage always means effective damage:** the hit points actually removed. A 6-damage shot
  at an enemy with 2 HP deals 2. `DamageMap`, `WaveOutcome.DamageByType` and
  `TowerState.DamageDealt` all count it that way, so overkill never inflates a feature.
- **A kill is settled the moment it happens.** The enemy is dead to every later tower in the
  same tick, so the reward is paid once and no damage is booked on a dead enemy.
- **Splash** hits every *other* live enemy within `SplashRadiusTiles` of where the target
  stood, in ascending id, for the same damage (each scaled by its own resistance).
- **Resist 0 is immunity:** no damage and no slow. The shot is still spent.
- **A slow affects movement only**, not digging, and counts down every tick the enemy is
  alive. Its effect on a step is taken before the countdown, so `SlowTicks` N slows exactly
  N steps.
- **Range** is centre of the tower's tile to the enemy's position, in tiles; exactly on the
  edge is in range. Reach is turned into squared world units once, when the tower is placed.
- **`DamageMap` books a hit on the tile under the enemy**, not the tower's, and is reset at
  the start of every wave, like `Occupancy`. A tower's own totals run across waves.
- **Targeting** has one mode, `First`: the lowest route cost to the core, ties to the lowest id.
- **A destroyed tower stays in `SimState.Towers`** with `IsAlive` false, so ids never shift.

Decided while building the movement classes (WP-C2), and pinned by tests:

- **The ground field is exactly the WP1 field.** `FlowFieldSetTests` compares it, tile for
  tile, with the WP1 flood fill on 60 random boards, and every recording made before WP-C2
  replayed to the same hash on every tick, on both runtimes, with the two-field code in place.
- **The class chooses the route, not the speed of digging.** A sapper is `Movement = Sapper`
  plus a high `DigRate` in its group. The corner-cut rule holds for sappers too: they enter a
  wall head-on, never diagonally between two built tiles.
- **A sapper still walks round when that is cheaper.** On open ground a single built tile
  costs two diagonal steps to pass (0.8 tile more), which beats even the discounted price
  of chewing it. Sappers dig where the way round is long: a wall line, a sealed corridor.
- **A flyer leaks when it is over the core tile**, the same test as for a walker standing on
  it. It flies at `MoveSpeed`, and a slow from a tower that can hit it slows it like anyone.
- **`CanHitFlying` covers the whole shot** (`Targeting.CanHit`): a tower without it never
  picks a flyer as its target, and its splash and its slow pass a flyer by even when the
  flyer is at the centre of the blast.
- **"Nearest the core" is measured the way each enemy travels** (`RouteCost.ToCore`): the
  ground field's cost of its tile for `Ground`, the sapper field's for `Sapper`, and the
  straight line in tenths of a tile for `Flying`. `Targeting.First` and
  `AgentState.MinCostSeen` (the pressure statistic) both use it. Ranking a flyer by the
  ground under it would be wrong exactly when it matters: above a thick maze a flyer is
  close to the core and the ground below it is a long walk away.
- **Flyers are not in the occupancy map.** It counts traffic on the ground, which is what
  the player's building shapes; a flyer's line is the same whatever is built. Damage to a
  flyer is booked in the `DamageMap` like any other, on the tile under it.
- **The fields count as one rebuild.** `SimGrid.FieldVersion` goes up once per
  `FlowFieldSet.Generate`.
- **`WaveOutcome` counts each class:** `SpawnedByClass[]`, `KilledByClass[]`,
  `LeakedByClass[]`, indexed by `MovementClass`. They replace the `FlyersSpawned` and
  `FlyersLeaked` first planned, and give the same numbers for sappers.

**Every wave must end** *(2026-10-02 review; built in WP-C1)*. The lifetime clock used
to guarantee it. Without the clock, an enemy that cannot move would stand forever, the
wave would never resolve (D4 waits for the last agent) and the game would lock. Four
rules, each tested in `WaveTerminationTests`:
- `PlanValidator` requires `MoveSpeed > 0`;
- `SimConfig.MinSlowFactor` (default 0.25) is the floor of any slow, so a slowed enemy still advances;
- `MapData.Validate` checks that every spawn reaches the core over walkable tiles under the
  corner-cut rule (no current map has a static blocker, so this path is so far untested by real maps);
- `SimConfig.MaxWaveSeconds` (default 600) is a backstop. When a wave has run that long, its
  remaining agents are removed with no core damage and no reward, the outcome is marked
  `TimedOut`, and telemetry shows it. It should never fire; a test checks that it does not in
  a normal 25-wave run.

The range check is O(towers × agents) per tick. At this game's scale that is
trivial, so do **not** add spatial hashing unless there is a measured reason.
Tower hits resolve within the tick (I12). `TowerView` draws a cosmetic projectile when it receives
`TowerFired`.

**Shop (WP-C3, built).** `SimState.Shop` is a `ShopState`: `Offers[]` (each a
`TowerOffer`: index into the roster, price, bought flag), `WaveIndex` and `RerollCount`.
`ShopRoller.Roll(rngSeed, waveIndex, rerollCount, roster, count)` makes a set of offers and
reads nothing else, which is I10. The numbers are in `SimConfig` (spec gap S11; all of them
can still move in the balance pass):

| Rule | As built |
|---|---|
| When offers roll | At game start (`waveIndex` 0) and the moment a wave resolves (`waveIndex` = that wave's number). The new set replaces the old one, bought or not. No roll when the game ends. |
| Offers per roll | `ShopOffers` = **4**. Each slot is drawn on its own, with a chance in proportion to `TowerDef.ShopWeight`, so the same tower can fill two slots. |
| Buying | `PlaceTower(offerSlot, x, y)` checks legality (I9) and budget, spends, and places the tower in one command, the way walls work. Each slot can be bought once. The price is the offer's, fixed when it was rolled. |
| Shop during waves | Open. The offers of a roll stay through the next wave's fight, until that wave resolves. |
| Rerolls | Off (`RerollCost = -1`). With a price set, `RerollShop` pays it and rolls `(waveIndex, rerollCount + 1)`: the next set of the same wave. |
| Selling towers | `SellTower(x, y)` refunds `price paid × SellRefund` (0.5), clears the tile and rebuilds both fields. The tower stays in the list with `IsAlive` false and `Sold` true, so its id and its totals keep their meaning. |
| Selling walls | **Allowed (D6).** `SellWall(x, y)` sells the whole **piece** the tile belongs to: every tile of that placement still standing is cleared, both fields rebuild, and the player gets `piece cost × WallSellRefund × (tiles still standing ÷ tiles placed)`, with `WallSellRefund` 0.5. |
| When selling is allowed | In build phases only (`SellDuringWave`, default off), for towers and walls alike: selling mid-wave lets a player open and close gaps to walk enemies back and forth under fire, the classic maze-TD exploit. *The plan first said "towers at any time"; changed 2026-10-02.* |

Decided while building it (WP-C3), and pinned by tests:

- **Four offers, not the three first planned.** Measured with the tower bot against the
  static baseline, real settings, seeds 1 to 10, waves survived: with three offers
  1 1 25 7 2 12 1 1 5 14; with four 10 13 18 7 9 8 25 14 25 25. The start budget of 60 buys
  four towers, and a first roll of three leaves a third of it unspent on the wave that
  matters most. Five and six offers changed little more.
- **The draws are independent.** With a roster of three, "one of each" would be the only
  possible roll otherwise, and the shop would offer no choice at all.
- **`waveIndex` is the number of waves resolved when the roll was made**, so offers are
  indexed the way telemetry rows are: the roll of index N is what the player can buy
  while answering wave N.
- **A wall piece's id is the index of its row in the build log** (`SimNode.PieceId`,
  `PlacementRecord.PieceId`). A tile "still stands" for its piece when it is a wall *and*
  still carries that id: a tile that was breached and later built over belongs to the
  newer piece, and selling the older one leaves it alone.
- **A wall tile put there without a placement** (a test fixture, a benchmark scenario) has
  no piece and cannot be sold.
- **The build log (`SimState.PlacementLog`) is the one record of what was built and what
  left the board by the player's hand.** `PlacementKind` is `Wall`, `Tower`, `WallSold`,
  `TowerSold` or `ShopReroll`. A row carries the tiles it built or cleared, its cost or
  its refund, and the piece or tower it concerns. The log only grows. The primary metric
  (§5.9), telemetry and Unity's drawing of walls all read it.
- **The state hash covers all of it:** the shop, each tile's piece id, each tower's price
  and whether it was sold, and the new fields of every log row.

`WaveOutcome` replaced `Stalled` with `Killed` and added `DamageByType[]` and
`TowersDestroyed` (WP-C1) and the per-class counts (WP-C2). `EscalationPlanner` v2 raises
HP instead of lifetime each wave (formula in S12).

---

## 5. Director — `Thesis.Director` and `Thesis.Learning`

### 5.1 Timeline: what is known when

This ordering is easy to get wrong. The reward for decision N depends on
**how the player rebuilt after wave N**, and that rebuild can only be measured when
wave N+1 starts.

```
 PlanWave(N)          wave N plays        OnWaveResolved(N)     intermission       PlanWave(N+1)
 ─────┬───────────────────────────────────────────┬───────────────(player builds)──────┬──────────
      │ profile_N, bin_N, decide, plan_N          │ store outcome_N                     │ 1. profile_{N+1}
      │ novelty_N known                           │ survived_N, pressure_N known        │ 2. restructure_N = |Δprofile|
      │                                           │                                     │ 3. reward_N → credit N, N-1, N-2 (0.5^lag)
      │                                           │                                     │ 4. estimator updates
      │                                           │                                     │ 5. write telemetry row for wave N
      │                                           │                                     │ 6. decide N+1
```

The last wave of a session gets a telemetry row with `reward: null`,
written on session end.

### 5.2 `WaveDirector.PlanWave`: a pipeline that records everything it does

```csharp
public WavePlan PlanWave(WaveContext ctx)
{
    var sw = Stopwatch.StartNew();
    var trace = new DirectorTrace(ctx.WaveIndex);

    BuildProfile profile = profiler.Compute(ProfileInput.From(ctx));      trace.Profile = profile;
    rewardStep.ResolvePending(profile, trace);                          // steps 2–5 of §5.1
    int bin = bucketizer.Bin(profile);                                  trace.Bin = bin;
    DecisionContext dc = bucketizer.Context(profile);                   // bin + 2-D kernel coords
    float budget = costTable.BudgetForWave(ctx.WaveIndex);              trace.ThreatBudget = budget;

    foreach (IWaveStrategy s in strategies)                             // ascending Id
    {
        var row = trace.AddRow(s.Id);
        row.Plan = s.BuildPlan(budget, ctx, rng.Strategy);
        row.Veto = constraints.FirstVeto(s, row.Plan, history);         // I5: before the learner
        if (row.Veto != null) { row.Score = float.NegativeInfinity; continue; }
        row.Heuristic = heuristic.Score(s.Id, profile);
        if (mode == DirectorMode.Full)
            row.Correction = estimator.SampleCorrection(dc, s.Id, rng.Thompson);   // I4: [-1, +1], 0 if cold
        row.Score = row.Heuristic + row.Correction.Value;
    }

    StrategyRow chosen = trace.ArgMax();                // ties → lowest Id; all vetoed → escalation fallback
    trace.ElapsedMicroseconds = sw.ElapsedTicks * 1_000_000 / Stopwatch.Frequency;   // G2
    LastTrace = trace;
    history.Push(trace);
    return chosen.Plan;
}
```

Every intermediate value ends up in `DirectorTrace`, which is serialized into
that wave's telemetry row. So "why did it pick X in wave 7?" can be answered by reading one
line of JSON.

### 5.3 Profile (implement after WP-C1)

```csharp
public interface IBuildFeature { string Name { get; } float Compute(ProfileInput input); }
```

`ProfileInput` is a read-only bundle holding:
- the grid, including tower occupants;
- the empty-board spawn cost, computed once per map;
- the last wave's `DamageMap` and `Occupancy`;
- the placement log;
- the shop log (offered, bought, sold).

`BuildProfile` is a named, fixed-order `float[]`, and its schema version goes into
telemetry. There is one `IBuildFeature` per proposal feature:

| Feature | Starting definition (confirm in S9) |
|---|---|
| `maze_length` | `bestCost[spawn] / 10` ÷ the same value on an empty board |
| `breach_vulnerability` | Share of the Ground route's cost that runs through diggable tiles. High means the route depends on walls or towers holding. |
| `chokepoint_reliance` | 1 − normalised Shannon entropy of `DamageMap` over tiles on the route. High means damage is concentrated on a few tiles. |
| `tower_concentration` | 1 − (mean tower distance from the tower centroid ÷ map diagonal) |
| `damage_type_mix` | Normalised entropy of damage dealt per `DamageType` last wave |

**Shop confound rule:** a feature describes the board the player chose to build.
Telemetry also keeps the offers, so the analysis can separate choices the player
made from changes the shop forced on them (S7).

### 5.4 Strategies and the threat budget (I2)

```csharp
public interface IWaveStrategy { int Id { get; } string Name { get; }
                                 WavePlan BuildPlan(float threatBudget, WaveContext ctx, IRandom rng); }
```

I2 is anchored to the static baseline: **`BudgetForWave(w) =
ThreatCostTable.Price(EscalationPlanner.Plan(w))`**. Every strategy spends
exactly that amount, and `Simulation.ValidatePlan` throws if
`|plan.ThreatSpent − budget| > config.ThreatEpsilon`. As `CLAUDE.md` I2 requires,
only the cost table gets tuned.

### 5.5 Layers

- `IConstraint.Check(...)` returns a `VetoReason` or `null`. The implementations are
  `OverBudget`; `RepeatStreak`; `Uncounterable` (for example, flyers when the player owns
  no tower with `CanHitFlying` and none is in the current shop offers); and
  `TooSimilarToPrevious`, the anti-collapse rule based on the L2 distance between
  this plan's composition and the previous wave's. A veto sets the score to `NegativeInfinity`, and the estimator is
  **never called** for a vetoed strategy. A test checks this with an estimator that
  throws if called.
- `AuthoredHeuristic.Score(strategyId, profile)` must work on its own as a complete
  director.

### 5.6 Estimator (`Thesis.Learning`, I3)

```csharp
public readonly struct DecisionContext { public readonly int Bin; public readonly float KernelX, KernelY; }
public struct Correction { public float Theta, Alpha, Beta, Value; public bool Cold; }

public interface IStrategyEstimator                 // chosen by DirectorConfig.EstimatorKind, not #if
{
    int BinCount { get; }  int StrategyCount { get; }
    Correction SampleCorrection(DecisionContext ctx, int strategy, IRandom rng);
    void Update(DecisionContext ctx, int strategy, float reward, float creditWeight, IRandom rng);
    EstimatorState Export();
    void Import(EstimatorState state);
}
```

- `BetaSampler`: builds Beta from two Gamma draws using Marsaglia–Tsang. It **throws** if a
  shape is below 1 instead of silently clamping, because α or β < 1 can only
  come from a bug in the update.
- `BinnedPosterior`: arrays `alpha[5,4]` and `beta[5,4]`, with the discounted
  Bernoulli update exactly as in `CLAUDE.md` §5.
- `KernelEstimator`: a 128-entry ring buffer with a Nadaraya–Watson query (σ = 0.15),
  used as the prior for thin cells.
- `ThresholdBanditEnv`: a synthetic environment whose best arm flips at a
  threshold in the context. It is the known-answer test for G1 and G3 and runs 10⁶ episodes
  in seconds under `dotnet`.
- **Maths.** Every `log` and `exp` in this assembly comes from `Thesis.Core.DetMath`
  (bit-identical on every runtime; §9 rule 3). The normal draws inside Marsaglia–Tsang use
  the polar method, which needs only `DetMath.Log` and `Math.Sqrt`. Work in `double` and
  round to `float` once, at the end.
- `CalibrationReport`: Brier score and 10-bin ECE over `(posterior mean, realised reward)`
  pairs. A diagnostic reported next to G1, not a gate. Added after reviewing Jev
  (2026-09-29), whose one transferable idea is that a decision probability should be
  checked against outcomes; see `CLAUDE.md` §8 for why Jev itself is not used.

### 5.7 Reward

`RewardEvaluator.Evaluate(...)` returns a `RewardBreakdown` with `Restructure`,
`Survived`, `Pressure`, `Novelty`, `Total` (weights 0.45 / 0.25 / 0.20 / 0.10), and
`Binary`, the G3 control. `CreditAssigner` yields `(wave, bin, strategy, weight)`
entries with weight `0.5^lag`.

### 5.8 Telemetry: `WaveRecord`, schema 1 (one JSONL line per wave)

Built in WP-C3. A row of the static baseline, as written today: wave 2 of the pinned
episode `dotnet-restless-5waves` (one line in the file; broken up here, and three of its
seven placements left out):

```json
{"schema":1,"session":"dotnet-restless-5waves","condition":"escalation","player":"restless",
 "build":"headless .NET 9.0.10","map":"SampleScene","mapSeed":0,"rngSeed":4,
 "wave":2,"tickStart":1597,"tickResolved":2998,"tickEnd":2999,
 "profile":null,"bin":null,"threatBudget":null,"strategies":null,"chosen":null,"fallback":null,"announcement":null,
 "plan":{"strategy":"escalation","threatSpent":0,"groups":[{"spawn":0,"archetype":"basic","movement":"ground","count":105,
         "hp":12,"moveSpeed":1.2875,"digRate":1,"resist":{"physical":1,"fire":1,"frost":1},"intervalTicks":9,"startDelayTicks":0}]},
 "outcome":{"spawned":105,"killed":105,"leaked":0,"removed":0,"timedOut":false,"breaches":0,"towersDestroyed":0,
            "coreHpBefore":10,"coreHpAfter":10,"coreDestroyed":false,"budgetBefore":9.000015,"budgetAfter":30.00006,"pressure":0,
            "damageByType":{"physical":44,"fire":1170,"frost":46},
            "spawnedByClass":{"ground":105,"sapper":0,"flying":0},"killedByClass":{"ground":105,"sapper":0,"flying":0},
            "leakedByClass":{"ground":0,"sapper":0,"flying":0}},
 "placements":[{"tick":2999,"kind":"sellTower","name":"frost","rot":0,"x":5,"y":3,"tiles":[[5,3]],"cost":0,"refund":6,"piece":-1,"tower":3},
               {"tick":2999,"kind":"sellWall","name":"O","rot":0,"x":3,"y":5,"tiles":[[3,5],[4,5],[3,6],[4,6]],"cost":0,"refund":3.5,"piece":1,"tower":-1},
               {"tick":2999,"kind":"wall","name":"Z","rot":0,"x":3,"y":5,"tiles":[[3,5],[4,5],[4,6],[5,6]],"cost":3,"refund":0,"piece":12,"tower":-1},
               {"tick":2999,"kind":"tower","name":"cannon","rot":0,"x":5,"y":5,"tiles":[[5,5]],"cost":18,"refund":0,"piece":-1,"tower":5}],
 "shop":{"offered":["cannon","cannon","cannon","frost"],"bought":["cannon","cannon","frost"],"sold":[{"def":"frost","x":5,"y":3,"refund":6}],"rerolls":0},
 "reward":null,"estimatorDigest":null,"directorMicros":null,"stateHash":"252b3954e1cf82c1"}
```

- **A row's window** runs from the start of its wave to the start of the next one: the
  fight, and the build phase after it. That is the stretch in which the player answers
  this wave, so it is what `placements` and `shop` cover. **Row 0** is the build phase
  before the first wave; it has no plan and no outcome.
- **Ticks.** `tickStart` is the tick the wave began on, `tickResolved` the tick it
  resolved on (-1 if it did not), `tickEnd` the next wave's start or the session's last
  tick. Something built on the very tick a wave begins was built before it (a tick applies
  the player's commands first) and is in the row before, so a row's placements have
  `tickStart < tick <= tickEnd`.
- **`placements`** is the build log (§4.6) for the window, in order: `kind` is `wall`,
  `tower`, `sellWall`, `sellTower` or `reroll`; `tiles` lists every tile built or cleared,
  so a reader needs no shape geometry.
- **`shop.offered`** is the roll made in the window: at game start for row 0, at the moment
  the wave resolved for every other row. Those offers are what the player could buy in
  this window's build phase and during the next wave's fight. Rolls after a paid reroll
  are not stored; they are `ShopRoller.Roll(seed, wave, 1)`, `(…, 2)` and so on.
- **`stateHash`** is the simulation's hash on the tick the wave resolved: the value the
  replay stores for that wave. A row can be matched to its replay without running anything.
- **One class for every condition.** What only a director knows (`profile`, `bin`,
  `strategies`, `reward` …) is `null` in the baseline. The fields exist in `WaveRecord`
  now, with `"score":"-Infinity"` for a vetoed strategy, so that the baseline's files and
  the director's are read by the same code. `fallback` is `SafePlanner`'s reason when it
  had to plan the wave.
- **When rows are written.** `TelemetryRecorder` watches the simulation the way
  `ReplayRecorder` does (`AfterTick()` after every tick) and hands out a row when its
  window closes: at the next wave's start, at game over, or at `Finish()` when the session
  is stopped. `TelemetryWriter` appends each row and flushes at once, so a crash keeps
  every finished wave; a row is never gone back to. The file is UTF-8 without a byte-order
  mark, with `\n` line ends, on every machine.
- **The bytes are fixed.** Every field's JSON name and position is written out in
  `WaveRecord` (never the C# name or declaration order), numbers go through
  `Json.SerializeLine` (§2.3), and nothing in a row depends on the clock, the frame rate
  or the machine. So **telemetry is a pure function of the replay**:
  `ReplayRunner.Telemetry(file)` (`thesis telemetry <replay> --check <file>`) gives back
  the file a session wrote, byte for byte, on either runtime. `PinnedReplayTests` checks
  that on every test run, including for a session played through `SimHost`'s frame loop.
  Lost or doubted telemetry can therefore always be made again; and when the schema grows,
  old sessions can be given the new fields.
- A float in a row shows the float's own error (`"budgetBefore":9.000015` is a budget that
  kill rewards of 0.2 were added to one by one). That is the game's number, not a
  formatting fault. A scripted player builds everything on the last tick of the build
  phase, so its placements all carry the tick the next wave starts on; a person's are
  spread over the phase.

`threatSpent` is 0 until the threat cost table exists (WP9). `mapSeed` is 0 until seeded
mazes exist. The participant code and the session number of D9 are added by WP14.

### 5.9 The primary metric for G5 (provisional, D7; freeze before the pilot)

`CLAUDE.md` §3 states the claim: players rebuild more under the director, measured as
the Shannon entropy of their placement distribution. This section fixes what that
means. It was decided on 2026-10-02 so that work is not blocked. It is **provisional
until it is frozen before the pilot**, and the supervisor should confirm it. Changing it
before the freeze costs nothing, because it is computed offline from replays, never live.

| Question (S7) | Decision |
|---|---|
| What is counted | Every **tile** the player builds: each tile of a wall piece, and the tile of a tower. A tile that is later sold or breached still counts. Selling and rerolling build nothing. |
| Over what | The **region** the tile is in: a fixed 6×6 partition of the map (`rx = 6x ÷ width`, `ry = 6y ÷ height`, whole-number division), 36 regions on SampleScene. A region with no buildable tile (all static blocker, or nothing but the spawn or the core) is left out. |
| Walls, towers, or both | **Both, pooled.** The director attacks tower weaknesses as much as maze weaknesses, so the response is often a tower move. |
| Which part of the session | The telemetry rows of waves 1 to *W* (§5.8: each row is a wave's fight and the build phase after it). Row 0, what is built before wave 1, is excluded: no wave has been seen yet, so it cannot be a response. |
| A completed wave | A wave whose whole window was played: the **next wave began**. The last wave a session reached never is, because the session stopped, or the core fell, before the player could answer it. A session whose rows go up to wave *N* has *N* − 1 completed waves. *(Fixed in WP-C3; the table did not say.)* |
| Comparison window | The first *W* waves, where *W* is the smaller number of waves the participant completed in their two sessions. Both sessions are cut to the same length, so a session that ended early is not compared with a longer one. |
| The number | `H = −Σ p_r ln p_r ÷ ln K`, where `p_r` is the share of counted tiles in region `r` and `K` is the number of regions that have a buildable tile. 0 means everything went into one region (repairing the same spot); 1 means spread evenly. `H = 0` when nothing was built. The logarithm is `DetMath.Log` (§9 rule 3), so the same rows give the same number on every machine. |
| Unit of analysis | One value per **session**. G5 compares each participant's two sessions (Wilcoxon signed-rank, as in `CLAUDE.md` §6). |
| Changes forced by the shop | **Not discounted.** D9 gives both conditions the same seeds, so the same offers and the same piece order; the shop pushes equally on both sides. |

Reported next to it, and fixed now so they cannot be chosen after the fact: walls only;
towers only (tower type × region); the mean of the per-wave entropies; tiles sold per
wave and towers sold per wave; the `restructure` term per wave; and the same metric on
4×4 and 8×8 partitions. None of them can rescue a failed G5.

A weak point to state in the thesis: region entropy measures *where* building happens,
not directly *how much the structure changes*. A player who slowly extends one maze
across the whole map scores high without rebuilding anything. The within-subject design
takes each player's own style out of the comparison; `restructure` and the sale counts
are the cross-check.

`PlacementEntropy` (WP-C3) implements exactly this table and nothing else: `Compute(rows,
map, waves, partition, scope)`, `CompletedWaves(rows)` and `ComparisonWindow(a, b)`. It
reads telemetry rows, after the fact; nothing in the game reads it. `thesis entropy
<replay> [<replay>]` prints it for one session, or for two cut to the same length. Of the
secondary measures, walls only, towers only (by region) and the 4×4 and 8×8 partitions
are built (`PlacementScope`, the `partition` argument); tower type × region, the mean of
the per-wave entropies and the sale counts are a few lines over the same rows and are
left for the analysis; `restructure` needs the profile (WP8).

---

## 6. Unity host layer (`Assets/Scripts`)

| Class | Becomes |
|---|---|
| `SimHost` (new) | Builds `MapData` (from the `GridManager` scan plus spawn and core transforms) and `ShapeDef[]` (from `BlockShape` assets), and creates the `Simulation`, giving it a **copy** of the config asset (an inspector edit during play changes the next session, never the running one). In `Update` it adds `Time.deltaTime` to an accumulator and runs ticks, at most `maxTicksPerFrame` per frame. It then drains events into C# events for the views. It records every session with a `ReplayRecorder` and writes `Sessions/<session>/replay.json` at each wave boundary and when the session ends: under the project's `Runs/` in the editor, under `persistentDataPath` in a build. Beside it, since WP-C3, a `TelemetryRecorder` and a `TelemetryWriter` append `telemetry.jsonl`, one row as each wave's window closes and the open one when the session ends (§5.8). A failed write is logged once and never stops the game. `GameSpeedController` needs no change, because scaled `deltaTime` already means more ticks per frame. |
| `DirectorHost` (new) | Turns config assets into a planner according to the session condition. *(The plan had it own the `TelemetryWriter`; as built in WP-C3 `SimHost` does, next to the replay, because telemetry is written for every condition and is made from the simulation alone.)* It wraps every planner in `SafePlanner` (§4.4): if the director throws or returns an invalid plan, that wave uses `EscalationPlanner`, the host logs `[Director]`, and telemetry gets `"fallback":"Exception"` or `"InvalidPlan"`. **A participant session never crashes because of the director.** The wrapper exists since WP-H; the planner factory itself moves into `Thesis.Director` in WP10, so that Unity and the headless `Registry` build planners from one place. Its `Condition` list has one entry that is **not** a study condition: `DevClassCycle` runs `ClassCyclePlanner` (§4.4), to play sappers and flyers before the strategies exist. |
| `WaveSpawner`, `BlockManager`, `PlayerCore` | Thin adapters that **keep the member names the HUD already reads**: `currentWave`, `isIntermission`, `intermissionTimeRemaining`, `ActiveAgentCount`, `StartWave()`, `buildBudget`, `currentShape`, `holdShape`, `nextShapes`, `shapeVersion`, `OnHealthChanged`, `OnGameOver`. Fields turn into forwarding properties, and actions become enqueued commands. `CanvasDashboard` should compile unchanged. |
| `FlowAgent` | Visual only. Reads `AgentState` by id, interpolates between the previous and current tick positions, and draws the life bar (an HP bar after WP-C1). No game rules, so I8 holds trivially. Since WP-C2 a sapper is tinted and a flyer is tinted and drawn above the walls; that is a placeholder until WP-C4 gives each enemy type a prefab. |
| `TowerView`, `ShopPanel` (new, WP-C4) | `TowerView` spawns on `TowerPlaced`, turns toward its target and fires a cosmetic projectile on `TowerFired`, and is destroyed on `TowerSold`/`TowerDestroyed`. `ShopPanel` shows the offers and prices; clicking an offer arms the ghost for tower placement, and the ghost shows the range ring. Sell mode sends `SellTower`. |
| `Node` | Slimmed down to view data only (`worldPosition`, `gridX`, `gridY`, `visualObject`). **Delete the gameplay fields first**, and the compiler errors become the exact list of code to migrate (`PathPreviewer`, `FlowFieldVisualizer`, `Benchmark/`). |
| `PlayerBuilder` | Mouse input becomes `PlaceShape` commands. Walls and towers are drawn by walking the build log, which also holds the sales (a sale row lists the tiles to clear); a breach removes its cube on the `WallBreached` or `TowerDestroyed` event. `AreTilesPlaceable` wraps `Placement.CanPlace` (D3). **Temporary since WP-C3, replaced by WP-C4:** Z / X / C / B buy shop offers 1 to 4 onto the tile under the mouse, Delete (or Backspace) sells the tower or the whole wall piece under it, and a plain text panel at the top right lists the offers, their prices and the keys. |
| `Benchmark/` | Rebuild timing and A* comparison move to the CLI (`bench`), since they never needed Unity. Frame-time tiers stay in Unity but run on `SimHost`. |

**Session setup and seeds (D9; built in WP14).** In development `SimHost.seed` stays 1,
so a bug reproduces. A study session starts from a setup screen that takes a participant
code and looks up, in an assignment table shipped with the build, that participant's two
sessions: a condition and a seed set for each. There are two seed sets, X and Y, and four
groups, so that condition, seed and order are all crossed:

| Group | Session 1 | Session 2 |
|---|---|---|
| 1 | static, seed X | adaptive, seed Y |
| 2 | static, seed Y | adaptive, seed X |
| 3 | adaptive, seed X | static, seed Y |
| 4 | adaptive, seed Y | static, seed X |

No participant meets the same seed twice, so nobody can remember the shop offers or the
order of the pieces. Across participants, each condition is played on each seed and in
each position equally often (6 per group at n = 24). Participant code, session number,
condition and seed are written into the replay and into every telemetry row.

**Build settings (D5).** Windows 64-bit, Mono, managed stripping off.
`Editor/BuildGuard` fails any other player build with a message that says why. Set the
company and product name (still `DefaultCompany/InternProj`) before the first build that
records data, because they decide where `persistentDataPath` is.

---

## 7. Harness and CLI

```
dotnet run --project Tools/dotnet/Thesis.Cli -- run     --map Maps/SampleScene.map.json --planner escalation|class-cycle --policy sentry|mixed|restless|greedy|idle
                                                         --seed 1 --waves 25 --out Runs/<name>      (writes replay.json and telemetry.jsonl)
                                                         [--shapes Maps/Shapes.json] [--config overrides.json] [--per-tick] [--wait]
dotnet run --project Tools/dotnet/Thesis.Cli -- replay  Runs/<name>/replay.json [--per-tick] [--dump-tick T] [--dump-out file]
dotnet run --project Tools/dotnet/Thesis.Cli -- ascii   Runs/<name>/replay.json [--wave 7 | --tick 44000] [--layer route|terrain|occupancy|cost] [--class ground|sapper]
dotnet run --project Tools/dotnet/Thesis.Cli -- ascii   --map Maps/Bench_Maze.map.json [--scenario open|maze|choke|scatter] [--layer route|terrain]
                                                         [--class ground|sapper] [--sapper-factor 0.2]
dotnet run --project Tools/dotnet/Thesis.Cli -- telemetry Runs/<name>/replay.json [--out file] [--check Runs/<name>/telemetry.jsonl]
dotnet run --project Tools/dotnet/Thesis.Cli -- trace   Runs/<name>/telemetry.jsonl [--wave 7]
dotnet run --project Tools/dotnet/Thesis.Cli -- entropy Runs/<a>/replay.json [Runs/<b>/replay.json] [--waves W] [--partition P]
dotnet run --project Tools/dotnet/Thesis.Cli -- ladder  --map Maps/SampleScene.map.json --seeds 20 --out Results/<yyyy-MM-dd>_ladder
dotnet run --project Tools/dotnet/Thesis.Cli -- synth   --episodes 1000000 --estimator binned --out Results/<yyyy-MM-dd>_g1-synth
dotnet run --project Tools/dotnet/Thesis.Cli -- gate    G1|G2|G3 ...
dotnet run --project Tools/dotnet/Thesis.Cli -- bench   --map Maps/Bench_Maze.map.json
dotnet run --project Tools/dotnet/Thesis.Cli -- pin     [--out Results/pinned-replays]
```

- `run`, `replay` and `ascii` exist as of WP5; `bench` since WP4; `pin` since WP-H; `telemetry`,
  `trace` and `entropy` since WP-C3; `ladder`, `synth` and `gate` are planned.
  `telemetry` makes a session's rows again from its replay; with `--check` it compares them
  with a file and exits 1 on the first byte that differs, naming the line. `trace` reads a
  telemetry file: one line per wave, or with `--wave N` everything that row holds (and, once
  a director writes rows, how each strategy was scored). `entropy` prints the primary metric
  (§5.9) and its variants for one session, or for two cut to the shorter one.
  `pin` re-records the .NET half of the pinned episodes and their telemetry; the Mono half is the Unity menu
  **Thesis → Replay → Record Pinned Episodes (Mono)**. Run both after any deliberate rule change.
  `run --config` takes a JSON file listing only the `SimConfig` fields to change, and
  rejects a field name `SimConfig` does not have. `--wait` lets build-phase countdowns
  run out instead of sending `StartWaveNow`. `replay` exits 1 on a divergence.
  `ascii --class sapper` draws the sapper's route and cost field instead of the ground
  one. `ascii --map` needs no recording: it draws a bare map (optionally with a benchmark
  wall layout) and the route of one class across it, which is the quick way to see what a
  value of `SapperDigCostFactor` does (`--sapper-factor`).
- `IPlayerPolicy.OnIntermission(Simulation sim, Action<SimCommand> send, IRandom rng)`,
  called once at the start of every build phase by `EpisodeRunner`. `sim` is for reading
  (`State`, `Map`, `Config`); input goes only through `send`, which records the command and
  applies it at once. *(WP5: `Simulation` instead of the `SimState` first planned, because a
  policy needs the map and config to check legality.)*
  Built so far: `Idle`; `GreedyDetour` (walls only: the piece that raises the route's
  cost the most, ties broken by how many shortest routes it closes); `Sentry` (towers only:
  buys every shop offer the budget covers, in slot order, and puts each on the free tile
  beside the route that sees the most of it); `mixed`, a `SequencePolicy` of walls first,
  then towers; and `restless` (WP-C3), the same with a `SellerPolicy` in front, which each
  build phase sells the tower that has dealt the least damage and the oldest wall piece
  still standing. `restless` is crude as a player; it exists so that recorded episodes
  contain sales.
  The plan for WP12 is that every policy both builds walls and buys from the shop: `Idle`;
  `GreedyDetour` (maximise spawn cost per unit of budget, and buy the offer with the
  best damage per cost); `RepeatTemplate` (the "competent player who repeats" from
  `CLAUDE.md` §3); `Chokepoint` (one kill zone that holds every tower); `Spread`;
  and `Reactive` (patches wherever the last wave leaked, and sells towers that dealt no damage).
- `Counterfactual`: at a wave boundary, `Clone()` the simulation once per strategy, play
  each clone through the wave and its following intermission, and record the reward each
  strategy actually earned. This gives a **ground-truth best strategy under the real game rules** for G1,
  in addition to the synthetic environment.
- `ResultWriter` creates `Results/<date>_<name>/README.md` with the date, the exact
  command line, an empty **Hypothesis** section that must be filled in *before*
  the run, and the result table (§7 of `CLAUDE.md`).

---

## 8. Debugging toolkit

| Symptom | First tool |
|---|---|
| The director picked something odd | `cli trace <telemetry> --wave N`: per-strategy veto, heuristic, θ, correction, score |
| What happened in a session, wave by wave | `cli trace <telemetry>`: one line per wave (sent, killed, leaked, built, sold, offered); `--wave N` for the plan, the outcome and every build action of one |
| A telemetry file is missing, cut short or doubted | `cli telemetry <replay> --out <file>` makes it again from the replay; `--check <file>` names the first line that differs |
| Two runs that should match don't | `cli replay <file>` finds the first divergent **wave**, then `--per-tick` finds the first divergent **tick** (the file must carry `TickHashes`; Unity sessions do by default). `--dump-tick T` writes the re-run's state at `State.Tick == T` as JSON with float bit patterns; dump the same tick from the other run or the other runtime and `diff` the two. |
| Agents behave strangely | `cli ascii <replay> --wave N --tick T` renders the map, route, agents and occupancy as text |
| Unity and headless disagree | Unity writes the same replay format. If it verifies headless, the bug is in the view layer. The menu **Thesis → Replay → Verify Latest Session** re-runs a file under Unity's Mono. `PinnedReplayTests` re-runs committed Unity and .NET recordings on both runtimes on every test run. |
| The learner doesn't converge | `cli synth --dump-every 1000` writes α/β for each cell over time as CSV |
| Frame spike at a wave boundary | `directorMicros` in telemetry, plus a `ProfilerMarker("Director.PlanWave")` in `DirectorHost` |
| Compile error in Unity only | Unity MCP `read_console`. If `dotnet build` passed, it is almost always the asmdef or meta layer. |

ASCII render format (also used as the fixture format in tests):

```
wave 7  tick 44000  phase Resolving  budget 31.0  core 8/10  live 42  layer=route
     0         1         2         3
     01234567890123456789012345678901234567
  0  XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX
  1  XS****....####........................X
  2  X....*....#..#.....a..................X
legend  X static  # wall  T tower  S spawn  C core  * route  + route through a wall or tower
        a agent  d digging agent  f flying agent
```

Log prefixes: `[Sim]`, `[Director]`, `[Telemetry]`, `[Harness]`, `[Replay]`.

---

## 9. Conventions for implementers

1. One type per file, with the file name matching the type name. Keep files under roughly 300 lines. The namespace equals the assembly name.
2. **C# 9 only.** No `record`, no `init`, no file-scoped namespaces, no global usings.
   DTOs are `sealed class` with public fields, following the existing code style.
3. **Determinism rules for `Thesis.*`:**
   - No `System.Random`, no `UnityEngine.Random`, no `DateTime.Now`, and no `Environment.TickCount` in any logic path.
   - No `Dictionary` or `HashSet` iteration wherever order affects the result.
   - No static mutable state.
   - Every random draw takes an `IRandom` from a named stream.
   - **Every float operation whose result feeds another operation is wrapped in an explicit `(float)` cast**,
     for example `(float)(x + (float)((float)(dx / d) * step))`. This applies even when the intermediate goes
     through a local variable. The reason: C# lets a runtime keep float intermediates at higher precision.
     Unity's Mono evaluates a chain like `a + b / c * d` in double and rounds once, while .NET rounds after every step,
     so the two produced different bits (WP4: first divergence at tick 2916, one bit of one agent's position). The spec
     *requires* an explicit cast to round, so casts make the runtimes agree. A single operation that is stored straight
     to a field needs no cast: double rounding is harmless for one +, −, ×, ÷ or √. `FloatDeterminismTests` pins the
     known cases and runs in both test runners. Tower damage, splash and slow maths (WP-C1) must follow this rule.
   - **No `Math.Log`, `Math.Exp`, `Math.Pow` or trigonometry, from `Math` or `MathF`.** Each runtime takes
     these from its own maths library, and they may differ in the last bit. Measured 2026-10-02 over a million
     inputs: `Math.Pow` already differs between Unity's Mono and .NET on one machine; `Log` and `Exp` agreed
     there, which is luck, not a guarantee. Use `Thesis.Core.DetMath.Log` and `DetMath.Exp`: ports of the
     fdlibm routines, built only from `+ − × ÷` on doubles, within 1 ulp of the runtime's own, and pinned bit
     for bit in both test runners. Raise to a whole power by multiplying. `Math.Sqrt`, `Abs`, `Min`, `Max`,
     `Round` and `Floor` are exact and allowed. `ForbiddenApiTests` reads the source of every `Thesis.*`
     assembly and fails on a violation (it also catches `System.Random`, `DateTime.Now` and `Environment.TickCount`).
4. No LINQ and no allocation inside `Simulation.Tick`. LINQ is fine in the director (it runs once per wave) and in the harness.
5. Invariant violations throw `InvalidOperationException` with context
   (wave, strategy, values). Only `SimHost` and `DirectorHost` catch them.
6. Tests use plain NUnit (`[Test]`, `Assert.AreEqual`, `Assert.That`). Never use
   `UnityTest` or `Assert.Multiple` in pure tests, because the same files must build under `dotnet`.
7. Grid tests use ASCII fixtures (`TestMaps.Parse`), not hand-built arrays.
8. Comments explain *why*, especially where a simpler-looking approach was tried and failed (`CLAUDE.md` §7).
9. Every work package ends by appending an entry to `Docs/DEVLOG.md`: what changed, any deviations,
   tests added, commands run, and known issues.
10. If a task seems to need breaking an invariant or anything in `CLAUDE.md` §8, **stop and say so**.

---

## 10. Spec gaps (close each before the package that needs it)

| # | Gap | Needed by |
|---|---|---|
| S1 | Which single feature is binned into 5 bins, and which two features form the kernel's 2-D subspace | WP10 |
| S2 | How the kernel prior combines with a cell's own counts (add `k·Q̂` on top of α/β, or replace them?), the value of `k`, and the definition of a "thin" or "cold" cell | WP11 |
| S3 | How the `0.5^lag` credit weight combines with the Bernoulli update: scale `r` before the draw, or apply a fractional update? | WP11 |
| S4 | τ for `restructure`, and the normalisation centres for the L1 distance on profiles | WP11 |
| S5 | What counts as "got close to the core" for `pressure` (a radius in tile units) | WP3 (field) / WP11 (reward) |
| S6 | The formula of the threat cost table: price as a function of count, HP, speed, resistances, digRate, and flying | WP9 |
| S7 | G5 entropy: what the placement distribution is taken over, walls or towers, wave or session, shop-forced changes. **Decided provisionally in §5.9 (D7). Freeze it before the pilot; confirm with the supervisor.** | Before the pilot |
| S8 | Where the extra spawn points for `split_groups` go on SampleScene | WP9 |
| S9 | The exact formulas of the five proposal features. §5.3 gives starting definitions, which must be checked against the proposal's §6.2. **The proposal is not in the repository: the student must add it, or those sections, to `Docs/` before WP8.** | WP8 |
| S10 | The strategy definitions from the proposal's §6.3 (composition and levers). Also: is damage-type resistance part of each strategy, or a fifth arm? A fifth arm means 5×5 = 25 cells and raises the data G4 needs. | WP9 |
| S11 | Shop rules: offers per roll, single-use slots, rerolls and their cost, sell refund for towers and for walls (D6), whether selling is allowed during a wave, and whether the shop stays open during waves. **Built in WP-C3 with the values in the §4.6 table** (4 offers, single use, no rerolls, half back on a sale, selling between waves only, shop open during waves). They are `SimConfig` numbers and stay open to the balance pass; changing one after the pilot means re-recording, not re-coding. | Balance (WP-C5, S2 W4) |
| S12 | Tower roster v1 (types, damage types, range, fire rate, splash, slow, anti-air); enemy archetypes and the HP escalation formula; `SapperDigCostFactor`; whether wall and tower breaches pay the player under the tower economy | WP-C5 |
| S13 | Study design with a shop: a participant who plays both conditions on the same seed sees the same offers twice and may remember them. **Decided (D9, §6): two seed sets crossed with condition and order.** | Done |
| S14 | **Income depends on the wave's shape.** `KillReward` is paid per enemy. A strategy that spends the same threat budget on fewer, stronger enemies therefore pays the player less, and flyers the player cannot hit pay nothing; so the director's choice changes the player's budget, which I11 forbids. *(Found in WP-C2: against the development planner's half-size sapper and flyer waves the tower bot runs out of money and loses in wave 5; against the escalation it lasts to wave 10.)* Options: pay a kill in proportion to the enemy's price in the threat table, so a wave that is wiped out pays the same whatever its shape; or pay per wave, not per kill. | WP9 |
