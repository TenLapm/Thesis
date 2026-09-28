# ARCHITECTURE.md — code structure for the wave director

**Status: 2026-09-15. Decisions D1–D4 made (D1 = Option B, towers).** Nothing here
overrides `CLAUDE.md`. The revised semester-1 schedule in `Docs/WORKPLAN.md` still
needs the student's approval.

Companion file: `Docs/WORKPLAN.md` (ordered work packages, each with its tests
and a definition of done).

---

## 0. Decisions (all made 2026-09-15)

| # | Decision | Outcome |
|---|---|---|
| D1 | `CLAUDE.md` §2: re-derive the features without towers (A) or add towers (B) | **B: towers.** Random shop, damage replaces the lifetime clock, towers occupy a tile as diggable terrain, towers persist and can be sold. See §4.6. |
| D2 | Move the game rules out of MonoBehaviours into a plain-C# simulation core (§1) | **Yes.** |
| D3 | Reword I9: placement legality lives in `Thesis.Sim.Placement.CanPlace`, and `PlayerBuilder.AreTilesPlaceable` wraps it | **Yes.** `CLAUDE.md` I9 has been updated. |
| D4 | The next intermission starts when the wave's **last agent resolves**, not when it spawns | **Yes.** It changes pacing, so playtest it. |

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

---

## 3. Folder layout

```
Assets/
  Thesis/
    Core/        Thesis.Core.asmdef      Pcg32.cs IRandom.cs RngStreams.cs Fnv1a64.cs TileCoord.cs Vec2f.cs Json.cs
    Sim/         Thesis.Sim.asmdef
      Map/         MapData.cs
      Grid/        SimNode.cs SimGrid.cs FlowField.cs
      Agents/      AgentState.cs AgentSystem.cs
      Build/       ShapeDef.cs ShapeBag.cs Placement.cs PlacementRecord.cs
      Waves/       IWavePlanner.cs WaveContext.cs WavePlan.cs AgentGroup.cs WaveOutcome.cs EscalationPlanner.cs
      Combat/      DamageType.cs TowerDef.cs TowerState.cs TowerSystem.cs Targeting.cs DamageMap.cs   (WP-C1)
      Movement/    MovementClass.cs FlowFieldSet.cs FlyingMovement.cs                                (WP-C2)
      Shop/        ShopState.cs ShopRoller.cs TowerOffer.cs                                          (WP-C3)
      Commands/    SimCommand.cs
      Events/      SimEvent.cs
      Stats/       OccupancyMap.cs
      Debug/       AsciiMap.cs StateHasher.cs
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
      Telemetry/   WaveRecord.cs TelemetryWriter.cs PlacementEntropy.cs
      DirectorConfig.cs
    Harness/     Thesis.Harness.asmdef
      Policies/    IPlayerPolicy.cs IdlePolicy.cs GreedyDetourPolicy.cs RepeatTemplatePolicy.cs
                   ChokepointPolicy.cs SpreadPolicy.cs ReactivePolicy.cs
      Scenarios/   BenchScenarios.cs            (OpenField / Maze / ChokePoints, ported from ScenarioBenchmark)
      Replay/      ReplayFile.cs ReplayRecorder.cs ReplayRunner.cs
      EpisodeRunner.cs Ladder.cs Counterfactual.cs ResultWriter.cs
      Gates/       G1LearnerAccuracy.cs G2DirectorTiming.cs G3GradedVsBinary.cs
  Scripts/                     (Assembly-CSharp: Unity host and views; existing files stay here)
    SimHost.cs DirectorHost.cs  (new)
    Towers/  TowerView.cs ShopPanel.cs EnemyHealthBar.cs              (new, WP-C4)
    Config/  SimConfigAsset.cs DirectorConfigAsset.cs BucketThresholdsAsset.cs
             ThreatCostTableAsset.cs AnnouncementTableAsset.cs
             TowerDefAsset.cs EnemyArchetypeAsset.cs                  (ScriptableObjects → plain config)
    Debug/   DirectorOverlay.cs  (new, F9)
    Editor/  MapExporter.cs HarnessMenu.cs ReplayMenu.cs
    Benchmark/ ...               (ported onto SimHost in WP4)
  Tests/EditMode/  Thesis.Tests.EditMode.asmdef
    Core/ Sim/ Learning/ Director/ Harness/   (mirrors Assets/Thesis)
    Fixtures/  TestMaps.cs  (ASCII map fixtures)
Maps/        SampleScene.map.json Bench_Open.map.json ...   (exported static geometry)
Runs/        (gitignored) raw run output: telemetry.jsonl, replay.json
Results/     (committed) one folder per reported result, per CLAUDE.md §7
Docs/        ARCHITECTURE.md WORKPLAN.md DEVLOG.md
Tools/dotnet/
  Directory.Build.props   LangVersion 9.0, netstandard2.1 for libraries, net9.0 for Cli/Tests
  Thesis.Headless.sln
  Thesis.Core/ Thesis.Sim/ Thesis.Learning/ Thesis.Director/ Thesis.Harness/   (csproj only, glob-include Assets sources)
  Thesis.Cli/   Program.cs
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
        public Simulation(SimConfig config, MapData map, ShapeDef[] shapeLibrary,
                          ulong rngSeed, IWavePlanner planner);

        public SimState State { get; }                  // callers outside Thesis.Sim treat it as read-only
        public IReadOnlyList<SimEvent> LastTickEvents { get; }

        public void Enqueue(SimCommand command);        // applied at the start of the next Tick()
        public void Tick();                             // advances exactly config.TickSeconds
        public Simulation Clone(IWavePlanner planner);  // deep copy incl. RNG state; for counterfactuals
        public ulong ComputeHash();
    }
}
```

`SimState` holds everything that changes during a run: `Tick`, `Phase`,
`PhaseTicksRemaining`, `WaveIndex`, `Grid`, `Agents` (pooled array, where agent
id = array index), `Bag`, `BuildBudget`, `CoreHp`, `CurrentPlan`,
`CurrentOutcome`, `Occupancy`, `PlacementLog`, `Rng` (Bag and Spawn streams),
and `IsGameOver`.

`PlacementLog` is needed from the very first WP. The primary metric in G5 is the
entropy of wall placement, and three of the five Option-A features read it.

### 4.2 Tick order (normative; tests assert this order)

This is the order for the **faithful port** (WP3). WP-C1 adds a tower pass and
removes the lifetime clock; the combat order is in §4.6.

```
0. if IsGameOver: return                        (Tick does not advance)
1. Apply queued commands in enqueue order.
     PlaceShape rebuilds the flow field immediately (same as PlayerBuilder today),
     so this tick's agents already see the new wall.
2. Phase machine
     Prep | Intermission: PhaseTicksRemaining--; at 0, or on a StartWaveNow command → BeginWave():
         WaveIndex++; plan = planner.PlanWave(ctx); ValidatePlan(plan);
         Occupancy.Reset(); CurrentOutcome = new(...); emit WaveStarted; Phase = Spawning
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
     close CurrentOutcome → planner.OnWaveResolved(outcome) → emit WaveResolved
     BuildBudget += stipend; Phase = Intermission; PhaseTicksRemaining = intermissionTicks
6. if CoreHp <= 0 → IsGameOver = true; emit GameOver
7. Tick++
```

Behaviour changes from the Unity version. Record each one in `Docs/DEVLOG.md`
when it lands:

- Step 4: a breach rebuilds the field **once, at the end of the tick**, instead
  of in the middle of the agent loop. Every agent in a tick now sees the same field.
- Step 5: the D4 wave boundary.

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
| `EscalationPlanner` | `Thesis.Sim` | Today's `WaveSpawner` math, ported verbatim. It is the static baseline condition and the default when no director is present. |
| `RandomPlanner`, `RoundRobinPlanner` | `Director/Decision/Baselines` | Rungs of the ladder and the G1 comparison baselines. They spend the same threat budget as everything else. |
| `WaveDirector` (`DirectorMode.HeuristicOnly`) | `Director/Decision` | Layers 1 and 2. This is the ablation condition. |
| `WaveDirector` (`DirectorMode.Full`) | `Director/Decision` | Layers 1, 2 and 3. |

The mutation guard: `Simulation` hashes the state before and after `PlanWave`
**and** `OnWaveResolved`, and throws `InvalidOperationException` if the hash
changed. It is always on (it costs two hashes per wave boundary), not only in
DEBUG as first planned. See DEVLOG WP3.

```csharp
public sealed class WavePlan   { public int WaveIndex; public string StrategyId; public AgentGroup[] Groups;
                                 public float ThreatSpent; public string AnnouncementId; }
public sealed class AgentGroup { public int SpawnIndex; public int Count; public float MoveSpeed; public float LifeTime;
                                 public float DigRate; public int SpawnIntervalTicks; public int StartDelayTicks; }
```

`WaveOutcome` contains `WaveIndex`, `StrategyId`, `Spawned`, `Stalled`, `Leaked`,
`WallsBreached`, `CoreHpBefore/After`, `BudgetBefore/After`,
`TickStarted/Resolved`, and `PressureShare` (the fraction of agents whose
`MinCostSeen / 10f` fell to `≤ config.PressureRadiusTiles` or below; see spec gap S5).

### 4.5 Commands, events, RNG

- `SimCommand` mirrors the player's actual inputs: `PlaceShape(originX, originY)`,
  `Rotate`, `Hold`, and `StartWaveNow`. The current shape and its rotation live in
  `ShapeBag`, so a replay reproduces the bag's state as well. The input log is a
  list of `(tick, command)`.
- `SimEvent` is a `readonly struct` holding a `Kind` enum plus integer and float payload
  fields: `WallPlaced`, `WallBreached`, `AgentSpawned`, `AgentStalled`,
  `AgentLeaked`, `WaveStarted`, `WaveResolved`, and `GameOver`. Views rely on these
  events and never poll for changes.
- Randomness comes only from `Pcg32` streams derived from one `rngSeed`, one stream per
  subsystem (`RngStreams.Bag`, `Spawn`, `Strategy`, `Thompson`, `RewardBernoulli`,
  `Policy`). Adding a draw in one subsystem therefore never shifts another subsystem's
  sequence. That property is what keeps a determinism divergence local and findable.
- **The shop is the exception: its RNG is counter-based, not a running stream.** Each roll
  seeds a new `Pcg32` from `Fnv1a64(rngSeed, "shop", waveIndex, rerollCount)`. So the
  offers for wave 7 are the same no matter what the player bought earlier, which
  planner is running, or how many draws other systems have made. This is what makes I10 hold.

### 4.6 Combat, movement classes and shop (WP-C1…C3)

**Enemies.** `AgentState` drops `Life` and gains `Hp`, `MaxHp`, `Resist[]` (one
damage multiplier per `DamageType`; 1 means normal), `MovementClass`, `SlowTicks`
and `SlowFactor`. The stats come from `AgentGroup`, which now names an enemy
archetype and carries its numbers.

**Towers.** A tower is `TowerDef` data (cost, `DamageType`, damage, range in tiles,
`FireIntervalTicks`, splash radius, slow, `CanHitFlying`, `DigCost`, `TowerHealth`,
shop weight) plus `TowerState` (id, tile, cooldown). On the grid it occupies **one
tile as diggable terrain**. `SimNode` gains `Occupant { None, Wall, Tower }` and
`TowerId`, and `terrainCost`/`wallHealth` work exactly as they do for walls. When
an enemy chews through the tile, the tower is destroyed.

**Movement classes.** `Ground` uses the normal flow field. `Sapper` uses a second field
in which wall and tower tiles cost `terrainCost × config.SapperDigCostFactor`, so
sappers prefer to dig. `Flying` uses no field: it moves in a straight line to the
core, ignores terrain, and never digs. `FlowFieldSet` rebuilds both fields together.
Each rebuild costs microseconds, so do not add caching (the reasoning is the same as
in `CLAUDE.md` §8).

**Tick order after WP-C1** (replaces steps 3–4 of §4.2; the other steps are unchanged):

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
     Flying:          move toward the core; Leaked once inside the core tile
     Ground / Sapper: exactly as in §4.2, using the field for its class and speed × slow.
                      Chewing through a tower tile → emit TowerDestroyed
                      (no breach reward by default; see S12)
```

The range check is O(towers × agents) per tick. At this game's scale that is
trivial, so do **not** add spatial hashing unless there is a measured reason.
Tower hits resolve within the tick (I12). `TowerView` draws a cosmetic projectile when it receives
`TowerFired`.

**Shop.** `ShopState` holds `Offers[]` (tower def id, price, bought flag),
`RerollCount` and `WaveIndex`. Defaults (all in `SimConfig`, all tunable; the rules
are spec gap S11):

| Rule | Default |
|---|---|
| When offers roll | At game start and at each intermission start. Unbought offers are replaced. |
| Offers per roll | 3, drawn weighted by `TowerDef.ShopWeight` using counter-based RNG (§4.5) |
| Buying | `PlaceTower(offerSlot, x, y)` checks legality and budget, spends the budget, and places the tower all in one command, the same way walls work today. Each slot can be bought once. |
| Rerolls | Off in v1 (`RerollCost = -1`). Turning them on adds `RerollShop`. |
| Selling | `SellTower(x, y)` refunds `price × SellRefund` (default 0.5), clears the tile, and rebuilds the fields. Allowed at any time. |
| Shop during waves | Open. Offers refresh only at intermission start. |
| Selling walls | Not allowed, same as today. |

New commands: `PlaceTower`, `SellTower`, `RerollShop`. New events: `TowerPlaced`,
`TowerSold`, `TowerDestroyed`, `TowerFired`, `AgentKilled`, `ShopRolled`.
`WaveOutcome` replaces `Stalled` with `Killed` and adds `DamageByType[]`,
`TowersDestroyed`, `FlyersSpawned` and `FlyersLeaked`. `EscalationPlanner` v2 raises
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

```json
{"schema":1,"session":"p07","condition":"WaveDirector.Full","build":"3f9cf57","map":"SampleScene",
 "mapSeed":0,"rngSeed":1234,"wave":7,"tickStart":41250,"tickResolved":44900,
 "profile":{"maze_length":2.41,"breach_vulnerability":0.12,"chokepoint_reliance":0.61,"tower_concentration":0.33,"damage_type_mix":0.20},
 "bin":3,"threatBudget":118.0,
 "strategies":[{"id":"breach_thin_wall","veto":null,"heuristic":0.40,"theta":0.71,"cold":false,"correction":0.42,"score":0.82},
               {"id":"flying_bypass","veto":"Uncounterable","heuristic":null,"theta":null,"cold":null,"correction":null,"score":"-Infinity"}],
 "chosen":"breach_thin_wall","fallback":null,"announcement":"b3.breach_thin_wall",
 "plan":{"groups":[{"spawn":0,"archetype":"sapper","count":45,"hp":38.0,"moveSpeed":1.5,"digRate":2.5,"intervalTicks":10}]},
 "outcome":{"spawned":45,"killed":39,"leaked":6,"breaches":4,"towersDestroyed":1,"coreHpBefore":8,"coreHpAfter":2,
            "budgetBefore":31.0,"budgetAfter":57.8,"pressure":0.27,"damageByType":{"physical":812.0,"fire":240.5}},
 "placements":[{"tick":44950,"kind":"wall","shape":"L","rot":1,"x":12,"y":7},{"tick":45010,"kind":"tower","def":"frost","x":14,"y":9}],
 "shop":{"offered":["frost","cannon","archer"],"bought":["frost"],"sold":[{"def":"archer","x":3,"y":20}],"rerolls":0},
 "reward":{"restructure":0.31,"survived":1,"pressure":0.27,"novelty":0.50,"total":0.5245,"binary":0,
           "credited":[{"wave":7,"weight":1.0},{"wave":6,"weight":0.5},{"wave":5,"weight":0.25}]},
 "estimatorDigest":"9f2c41d0","directorMicros":412,"stateHash":"a81f09c3e2b7d154"}
```

`placements` for wave N covers everything placed from wave N's start until wave
N+1's start. The escalation condition writes the same row with the director
fields set to `null`, so every condition can be analysed with the same code.

---

## 6. Unity host layer (`Assets/Scripts`)

| Class | Becomes |
|---|---|
| `SimHost` (new) | Builds `MapData` (from the `GridManager` scan plus spawn and core transforms) and `ShapeDef[]` (from `BlockShape` assets), and creates the `Simulation`. In `Update` it adds `Time.deltaTime` to an accumulator and runs ticks, at most `maxTicksPerFrame` per frame. It then drains events into C# events for the views. It writes the replay file to `persistentDataPath/Sessions/<session>/`. `GameSpeedController` needs no change, because scaled `deltaTime` already means more ticks per frame. |
| `DirectorHost` (new) | Turns config assets into a planner according to the session condition and owns the `TelemetryWriter`. If the director throws, it logs `[Director]`, uses `EscalationPlanner` for that wave, and writes `"fallback":"Exception"`. **A participant session never crashes because of the director.** |
| `WaveSpawner`, `BlockManager`, `PlayerCore` | Thin adapters that **keep the member names the HUD already reads**: `currentWave`, `isIntermission`, `intermissionTimeRemaining`, `ActiveAgentCount`, `StartWave()`, `buildBudget`, `currentShape`, `holdShape`, `nextShapes`, `shapeVersion`, `OnHealthChanged`, `OnGameOver`. Fields turn into forwarding properties, and actions become enqueued commands. `CanvasDashboard` should compile unchanged. |
| `FlowAgent` | Visual only. Reads `AgentState` by id, interpolates between the previous and current tick positions, and draws the life bar (an HP bar after WP-C1). No game rules, so I8 holds trivially. |
| `TowerView`, `ShopPanel` (new, WP-C4) | `TowerView` spawns on `TowerPlaced`, turns toward its target and fires a cosmetic projectile on `TowerFired`, and is destroyed on `TowerSold`/`TowerDestroyed`. `ShopPanel` shows the offers and prices; clicking an offer arms the ghost for tower placement, and the ghost shows the range ring. Sell mode sends `SellTower`. |
| `Node` | Slimmed down to view data only (`worldPosition`, `gridX`, `gridY`, `visualObject`). **Delete the gameplay fields first**, and the compiler errors become the exact list of code to migrate (`PathPreviewer`, `FlowFieldVisualizer`, `Benchmark/`). |
| `PlayerBuilder` | Mouse input becomes `PlaceShape` commands, and wall visuals spawn on `WallPlaced` events. `AreTilesPlaceable` wraps `Placement.CanPlace` (D3). |
| `Benchmark/` | Rebuild timing and A* comparison move to the CLI (`bench`), since they never needed Unity. Frame-time tiers stay in Unity but run on `SimHost`. |

---

## 7. Harness and CLI

```
dotnet run --project Tools/dotnet/Thesis.Cli -- run     --map Maps/SampleScene.map.json --planner escalation --policy greedy --seed 1 --waves 25 --out Runs/<name>
dotnet run --project Tools/dotnet/Thesis.Cli -- replay  Runs/<name>/replay.json [--per-tick]
dotnet run --project Tools/dotnet/Thesis.Cli -- ascii   Runs/<name>/replay.json --wave 7 [--tick 44000] [--layer route|occupancy|cost]
dotnet run --project Tools/dotnet/Thesis.Cli -- trace   Runs/<name>/telemetry.jsonl --wave 7
dotnet run --project Tools/dotnet/Thesis.Cli -- ladder  --map Maps/SampleScene.map.json --seeds 20 --out Results/<yyyy-MM-dd>_ladder
dotnet run --project Tools/dotnet/Thesis.Cli -- synth   --episodes 1000000 --estimator binned --out Results/<yyyy-MM-dd>_g1-synth
dotnet run --project Tools/dotnet/Thesis.Cli -- gate    G1|G2|G3 ...
dotnet run --project Tools/dotnet/Thesis.Cli -- bench   --map Maps/Bench_Maze.map.json
```

- `IPlayerPolicy.OnIntermission(SimState view, Action<SimCommand> send, IRandom rng)`.
  Every policy both builds walls and buys from the shop. The policies are `Idle`;
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
| Two runs that should match don't | `cli replay <file>` finds the first divergent **wave**, then `--per-tick` finds the first divergent **tick**. `--dump-tick T` writes both states as JSON to diff. |
| Agents behave strangely | `cli ascii <replay> --wave N --tick T` renders the map, route, agents and occupancy as text |
| Unity and headless disagree | Unity writes the same replay format. If it verifies headless, the bug is in the view layer. |
| The learner doesn't converge | `cli synth --dump-every 1000` writes α/β for each cell over time as CSV |
| Frame spike at a wave boundary | `directorMicros` in telemetry, plus a `ProfilerMarker("Director.PlanWave")` in `DirectorHost` |
| Compile error in Unity only | Unity MCP `read_console`. If `dotnet build` passed, it is almost always the asmdef or meta layer. |

ASCII render format (also used as the fixture format in tests):

```
wave 7  tick 44000  budget 31.0  core 8/10  layer=route
     0         1         2         3
     01234567890123456789012345678901234567
  0  XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX
  1  XS****....####........................X
  2  X....*....#..#.....a..................X
legend  X static  # wall  S spawn  C core  * route  a agent  d digging agent
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
| S7 | G5 entropy: what the placement distribution is taken over (tiles, regions, or kind × region); whether it counts walls only or walls and towers; whether the unit is the wave or the session; and whether changes forced by the shop are discounted | WP6 |
| S8 | Where the extra spawn points for `split_groups` go on SampleScene | WP9 |
| S9 | The exact formulas of the five proposal features. §5.3 gives starting definitions, which must be checked against the proposal's §6.2 | WP8 |
| S10 | The strategy definitions from the proposal's §6.3 (composition and levers). Also: is damage-type resistance part of each strategy, or a fifth arm? A fifth arm means 5×5 = 25 cells and raises the data G4 needs. | WP9 |
| S11 | Shop rules (§4.6 defaults): offers per roll, single-use slots, rerolls and their cost, sell refund, and whether the shop stays open during waves | WP-C3 |
| S12 | Tower roster v1 (types, damage types, range, fire rate, splash, slow, anti-air); enemy archetypes and the HP escalation formula; `SapperDigCostFactor`; whether wall and tower breaches pay the player under the tower economy | WP-C5 |
| S13 | Study design with a shop: a participant who plays both conditions on the same seed sees the same offers twice and may remember them. Should conditions use paired seeds across participants in a counterbalanced order? (This is the student's call; the code supports either.) | Before the pilot |
