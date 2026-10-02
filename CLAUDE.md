# CLAUDE.md — Thesis project (`E:\Thesis`)

Unity / C# / URP. Maze tower-defense with a flow-field pathfinder. The thesis
work is an adaptive wave director driven by a contextual bandit.

**Status (2026-10-02).** The game runs on a deterministic, headless simulation core
(`Assets/Thesis`), with Unity as a thin host around it (`Docs/WORKPLAN.md` WP0–WP5,
the hardening pass WP-H and the combat core WP-C1 are done). Enemies have hit points
and towers shoot them. **Not built yet:** flying and sapper enemies (WP-C2), the shop
and selling (WP-C3), the tower UI (WP-C4), balance (WP-C5), and the director itself.
Next package: WP-C2.

Read §1 and §2 before writing any code.

- Code structure: `Docs/ARCHITECTURE.md` (decisions D1–D9 are in its §0).
- Work packages and their order: `Docs/WORKPLAN.md` (re-planned 2026-10-02).
- What was done and found, package by package: `Docs/DEVLOG.md`.
- The review after WP5 and what became of each finding: `Docs/REVIEW-2026-10-02.md`.

---

## 1. What the game actually is today

A maze tower defense with one unusual rule: **nothing the player builds blocks the
path.**

- The player builds two things from one budget: **tetromino-shaped walls** (a 7-bag
  randomiser with a hold slot and rotation; SampleScene's library is I, O, T, S, Z,
  J, L in that order) and, since WP-C1, **towers** (one tile each).
- Neither is an obstacle. Both are expensive, destructible *terrain*:
  `SimNode.TerrainCost` (a shape's `DigCost`, default 15; a tower's, default 20) plus
  `SimNode.WallHealth` (seconds of chewing). A path to the core therefore **always
  exists**.
- Enemies follow the flow field. When standing on a built tile they **chew through
  it** instead of walking. The field's cost model means enemies automatically trade
  "walk around" against "dig through" — nobody scripts that choice. Chewing through a
  tower's tile destroys the tower.
- Enemies have **hit points**. Every tower in range shoots; an enemy at 0 HP is
  killed.
  - Killed → the player gains `KillReward` = 0.2 build budget.
  - Enemy reaches the core → core HP − 1; the core has 10 HP.
  - Enemy fully breaches a wall tile → the player gains `WallBreakReward` = 1.0 (yes,
    the player is *paid* when a wall is destroyed). A destroyed tower pays nothing.

So the player has two levers: **damage** (towers) and **time under fire** (walls,
which bend and lengthen the route past the towers). Walls alone win nothing: the
lifetime clock that made a long route lethal in the original game was removed in
WP-C1 (§2). The static baseline escalates every wave: more enemies, faster, closer
together, with more hit points.

What is still a placeholder: three unbalanced tower types (`TowerRoster`: a
single-target, a splash and a slowing tower, one per damage type), bought in Unity
with the Z / X / C keys. There is no shop, no selling, no real tower UI yet.

### Where the rules live

All of the above is `Thesis.Sim`: plain C# with no `UnityEngine`, advancing in fixed
ticks of 0.02 s. The MonoBehaviours in `Assets/Scripts` are a **host and views**:
`SimHost` runs the simulation, passes player input in as commands and hands events
out; `FlowAgent`, `PlayerBuilder`, `WaveSpawner` and the rest only draw state and
forward clicks. **A game rule never goes into a MonoBehaviour.** The same simulation
runs headless under `dotnet` (`Tools/dotnet`), which is how tests, replays and the
offline harness work.

### Flow field — read this before touching pathfinding

`Thesis.Sim.FlowField.Generate()` is a **synchronous, whole-map, weighted flood fill
(SPFA).** The previous chunked/background-threaded version, along with
`isCalculating`, `ValidatePath`, sinkhole detection and the global fallback, was
**deliberately deleted**. On this grid a full rebuild costs well under a
millisecond (measured 2026-10-02 on a late-game board: 0.3 ms under .NET, 0.9 ms
in Unity). Do not reintroduce chunking, threading or incremental invalidation
without a measured reason.

- Costs are **integers ×10**: `CardinalCost = 10`, `DiagonalCost = 14`, multiplied
  by the `TerrainCost` of the tile being **left** (the flood runs outward from the
  core and prices "neighbour → current" by the neighbour's terrain). So
  `BestCost / 10f` is in tile units. Any feature derived from `BestCost` must divide by 10.
- `Generate()` resets `BestCost` and `NextIndex` for every node up front. The
  simulation calls it after every placement, and once at the end of a tick in which
  a wall was breached.
- `SimGrid.GetNeighbors` enforces a corner-cut rule via `SimNode.BlocksCorner`:
  an agent may enter a wall tile head-on (that is how digging starts) but may not
  slip diagonally between two solid tiles. Its iteration order decides ties, so it
  is part of the rules.
- `SimNode.IsWalkable` is **static geometry only**: the map's `X` tiles, scanned
  once from `unwalkableMask` by `SceneMapBuilder`. Player walls never touch it.
  (No current map has a single static blocker.)

### File map

```
Assets/Thesis/                pure C#, no UnityEngine; each folder is one assembly
  Core/       Pcg32, RngStreams, Fnv1a64, Vec2f, TileCoord, Json, DetMath
  Sim/        the game rules
    Simulation.cs SimState.cs SimConfig.cs   tick loop, all mutable state, all tunables
    Grid/     SimGrid (GetNeighbors, corner-cut rule), SimNode, FlowField, Route
    Agents/   AgentState, AgentSystem          hit points, movement, digging, slow
    Combat/   TowerDef, TowerState, TowerSystem (the tower pass), Targeting,
              DamageMap, DamageType, TowerRoster (placeholder roster)
    Build/    ShapeDef, ShapeBag (7-bag, hold, rotate), Placement (CanPlace = I9)
    Waves/    IWavePlanner, EscalationPlanner (static baseline), SafePlanner,
              PlanValidator, WavePlan, WaveOutcome
    Replay/   ReplayFile, ReplayRecorder, ReplaySetup
    Debug/    StateHasher, StateDump, AsciiMap, AsciiState, BenchScenarios
    Map/ Commands/ Events/ Stats/
  Learning/   empty until WP7        Director/   empty until WP8
  Harness/    editor and headless only: EpisodeRunner, scripted policies (Idle,
              GreedyDetour, Sentry, Sequence), ReplayRunner, PinnedEpisodes, PathfindingBench
Assets/Scripts/               Unity host and views
  SimHost.cs               runs the Simulation: fixed ticks, input in, events out, records the replay
  DirectorHost.cs          picks the planner (the study condition), wraps it in SafePlanner
  SceneMapBuilder.cs       scene → MapData
  WaveSpawner.cs BlockManager.cs PlayerCore.cs PlayerBuilder.cs
                           thin adapters that keep the member names the HUD reads
  FlowAgent.cs             view only: interpolates one AgentState, shows its HP bar
  GridManager.cs Node.cs FlowFieldManager.cs   view data and bindings, no rules
  FlowFieldVisualizer.cs PathPreviewer.cs GhostPreviewer.cs CanvasDashboard.cs
  GameSpeedController.cs   pause / speed; pause blocks building
  CameraMovement.cs Billboard.cs WallSpawnAnimator.cs BlockShape.cs
  Config/SimConfigAsset.cs inspector home of SimConfig
  Benchmark/ScenarioBenchmark.cs   the Unity-only frame-time scenarios
  Editor/                  MapExporter, ShapeExporter, ReplayMenu, BuildGuard
Assets/Tests/EditMode/        one set of tests, run by Unity and by `dotnet test`
Tools/dotnet/                 headless build of Assets/Thesis, and Thesis.Cli
                              (run, replay, ascii, pin, bench)
Maps/                         exported maps and Shapes.json
Results/                      committed results; Results/pinned-replays is re-run by tests
Runs/                         (gitignored) raw output and Unity session replays
Assets/Scenes/                SampleScene + Bench_Open / Bench_Maze / Bench_Choke / Bench_Stress
Assets/Scripts/BlockS/        the tetromino .asset files
```

The offline harness is `Thesis.Harness` plus `Thesis.Cli`. `ScenarioBenchmark`
keeps only the scenarios that need Unity's frame loop.

---

## 2. RESOLVED — Option B: towers with a random shop (decided 2026-09-15)

The proposal's player model assumed towers, and the base game had none. The
student chose to **add towers** instead of re-deriving the features, because the
game should be fun to play, not only a testbed. The rejected Option A is recorded
in §8, and its feature and strategy tables are in git history.

### Tower design (student's decisions)

| Question | Decision |
|---|---|
| Randomness | **Random shop.** Each intermission rolls a few random tower offers. The player chooses which to buy with build budget and where to place them. |
| How enemies lose | **Damage replaces the lifetime clock.** Enemies have HP and the clock is removed. Walls shape and lengthen the path, which means more time under fire. |
| Placement | **Open tiles, acts like a wall.** A tower occupies one tile as expensive diggable terrain. Enemies route around it or chew through it, which destroys the tower. |
| Persistence | **Towers persist and can be sold** for a partial refund. |
| Walls | **Walls can be sold too** *(student, 2026-10-02)*. A whole placed piece is sold at once for a partial refund, in build phases. Before this a wall could only leave the board by being breached. |

Consequences to keep in mind:

- Walls stay as they are: tetromino bag, diggable terrain, and a path always
  exists. Everything in §8 about the flow field still applies. The one addition is
  that the player can sell them (`Docs/ARCHITECTURE.md` §4.6).
- The proposal's five features now describe the game: `maze_length`,
  `breach_vulnerability`, `chokepoint_reliance` (entropy of damage per path tile),
  `tower_concentration` and `damage_type_mix`. Their exact formulas are spec gaps
  in `Docs/ARCHITECTURE.md` §10.
- **Shop randomness is a confound for the primary metric.** Random offers push
  players to rebuild in *both* conditions. Therefore offers are a pure function of
  `(rngSeed, waveIndex, rerollCount)` (I10), telemetry records what was offered,
  bought and sold, and features must describe what the player *chose*, not what
  the roll happened to offer.
- The proposal's strategy pool applies: `breach_thin_wall`, `swarm_chokepoint`,
  `split_groups` and `flying_bypass`. Flying units need a movement mode that ignores
  the flow field, and `flying_bypass` must be vetoed in layer 1 whenever the player
  has no way to hit flyers. `split_groups` probably needs multi-spawn support;
  `WaveSpawner` currently has a single `spawnPoint`.
- A wall breach currently pays the player +1.0 budget. Whether that stays under
  the tower economy is a balance question, not a given.

The combat core exists since WP-C1, so `BuildProfiler` is no longer blocked by it. It
still needs the shop log (WP-C3) for the features that must tell a choice from an offer.

---

## 3. What the thesis claims

**Not that the AI wins more. That players rebuild more.**

A competent player finds one strong maze within a few waves and then repeats it.
The hypothesis is that a wave director responding to the specific weakness of the
current maze keeps the player making structural decisions — measured as the
Shannon entropy of their wall-placement distribution across waves. The exact
definition is in `Docs/ARCHITECTURE.md` §5.9; it is provisional until it is frozen
before the pilot.

Therefore:

- Do not optimise the director for core damage, leak rate or stall rate.
- Do not add difficulty scaling. Difficulty is held **constant** on purpose.
- A change that makes the game harder but does not change how the player builds
  is a regression.

Note that the game **already escalates difficulty** every wave (`EscalationPlanner`:
agent count, speed, hit points, spawn delay). That escalation is the *static baseline
condition* for the study. The director varies attack **shape** on top of it; the
shared threat budget must hold total strength constant across strategies at a given
wave number.

---

## 4. Hard invariants

| # | Invariant | Why |
|---|---|---|
| I1 | Deterministic runs: same `{mapSeed, rngSeed, inputLog}` → identical outcome. Replace `UnityEngine.Random` (used in `BlockManager.RefillBag`) and any `System.Random` with a seeded PCG/xorshift. Fixed timestep. | Replay, regression benchmarks, and the answer to "did both study conditions see comparable waves". **Satisfied since WP5 (2026-10-02)** in the Unity editor (Mono) and headless (.NET): every session is recorded and replays to the same state hash on every tick (`Results/2026-10-02_determinism/`). The study build is Windows, Mono (D5), the runtime this was verified on; `BuildGuard` refuses any other. A session recorded by a built player must still be replayed headless before the pilot. |
| I2 | All strategies spend an identical threat budget at a given wave number. Only the shared cost table is tuned. | Otherwise "the adaptive condition was harder" explains the entire result. |
| I3 | The estimator classes have **zero** Unity dependencies. No `MonoBehaviour`, no `UnityEngine` imports. | They must run headless at ~10⁶ episodes and be unit-testable. |
| I4 | The learned term is an **additive correction bounded to ±1** on an authored heuristic score. It never selects directly. | Cold-start safety, debuggability, and it gives the ablation for free. |
| I5 | Hard constraints veto with `float.NegativeInfinity` **before** the learner is consulted. | Legality and fairness sit above learning, not beside it. |
| I6 | After the threshold freeze, bin boundaries and normalisation centres are immutable. | A threshold tuned against outcome data invalidates the comparison. |
| I7 | After the build freeze, no code changes except crash fixes. | Otherwise the participants did not play the same game. |
| I8 | No director logic on the per-frame path. | The simulation ticks 50 to 150 times a second. The director runs once per wave boundary and has 16 ms there (G2). |
| I9 | `Thesis.Sim.Placement.CanPlace` is the single source of truth for placement legality, for walls and towers. `PlayerBuilder.AreTilesPlaceable` only wraps it, and `GhostPreviewer` keeps calling that wrapper. *(Reworded 2026-09-15, D3.)* | The preview can never disagree with a real click, and the harness uses the same rule. |
| I10 | Shop offers are a pure function of `(rngSeed, waveIndex, rerollCount)`. They never depend on the study condition, the director, or the player's board. *(Added 2026-09-15.)* | Both conditions must see the same tower offers, or shop luck could explain the result. |
| I11 | The director shapes enemy waves only. It never touches the shop, tower stats, prices, or the player's budget. *(Added 2026-09-15.)* | Otherwise the director changes the player's power, and difficulty is no longer held constant (§3). |
| I12 | Tower attacks resolve inside the simulation in whole ticks (an instant hit, or a delay fixed at the moment of firing). Projectile motion exists only in the Unity view. *(Added 2026-09-15.)* | Projectile physics would put frame-rate-dependent flight time back into the rules (I1). |

If a task seems to require breaking one of these, stop and say so.

---

## 5. Director design (to build, not yet built)

### Layers — strict precedence

```
1. CONSTRAINT  veto: over budget, repeat streak, unit the player cannot counter.
               Absolute, returns NegativeInfinity.
2. HEURISTIC   authored score per strategy given the build profile.
               A complete, shippable director on its own — this is the
               ablation condition and a baseline rung. Build it properly.
3. LEARNED     bounded correction added to (2). Re-ranks close calls only.
```

### Estimator

Binning, not a linear model. An inspected open-source implementation of a linear
per-arm contextual bandit failed because the optimal action depended on a
**threshold** in one context feature, which a linear model cannot represent. This
domain has the same kind of threshold. Do not "simplify" it back.

- **`BinnedPosterior`** — one context feature discretised into 5 bins; each
  `(bin, strategy)` cell holds a Beta posterior. 5 × 4 = 20 total.
- **`KernelEstimator`** — Nadaraya-Watson weighted average over a 128-entry ring
  buffer of `(profile, strategy, reward)`, Gaussian-weighted by distance in a
  2-D subspace, σ = 0.15. Supplies an **empirical prior for thin cells**:
  `α₀ = 1 + k·Q̂ₖ`, `β₀ = 1 + k·(1 − Q̂ₖ)`.

### Selection — Thompson sampling as a correction

```csharp
foreach (var a in survivingStrategies) {
    float theta = SampleBeta(alpha[bin, a], beta[bin, a]);
    float corr  = IsCold(a) ? 0f : 2f * (theta - 0.5f);   // bounded [-1, +1]
    score[a] = Heuristic(a, profile) + corr;
}
Play(ArgMax(score));
```

Beta from two Gammas via **Marsaglia–Tsang**. Because the shape parameters never
drop below 1 (see the update), the shape-boost branch is unnecessary — the whole
sampler is ~40 lines.

### Reward — behaviour change, not victory

```
r = 0.45 * restructure   // |Δ profile| in normalised L1, capped at tau
  + 0.25 * survived      // core still alive after this wave
  + 0.20 * pressure      // share of the wave that got close to the core
  + 0.10 * novelty       // distance from the previous wave's realised composition
```

`survived` is what stops a strategy profiting by ending the run — a dead player
builds nothing more. A binary control reward `(restructure >= tau) && survived`
is **also implemented**; it is the control condition for gate G3, not dead code.

### Update

```csharp
int rTilde = Bernoulli(r);                 // keeps Beta conjugacy exact for r in [0,1]
alpha[bin,a] = 1f + G * (alpha[bin,a] - 1f) + rTilde;
beta [bin,a] = 1f + G * (beta [bin,a] - 1f) + (1 - rTilde);   // G = 0.95
```

Decaying toward `Beta(1,1)` rather than toward zero is what guarantees `α, β ≥ 1`.
The discount exists because the environment is non-stationary in *both*
directions — the player learns while the director does.

Credit for an observed restructure is spread backwards with weight `0.5^lag`,
because a player may be reacting to the last two or three waves.

### Anti-collapse and announcement

- Suppress a strategy whose realised composition lies within a threshold L2
  distance of the previous wave's. This replaces any hardcoded "no repeat" rule.
- Announce every selected strategy before the wave, one authored line keyed by
  `(bin, strategy)`. Templated, never generated. It is both game feel and the
  manipulation check for gate G7.

### Classes to add

```
BuildProfiler        gated     grid + tower + damage logs → feature vector   [after WP-C1]
ContextBucketizer    gated     feature → bin 0-4; thresholds in a ScriptableObject
BinnedPosterior      gated     20 Beta posteriors, sampler, discounted update, JSON
KernelEstimator      gated     ring buffer, NW query, ExportState/ImportState
IWaveStrategy x4     gated     threat budget + seed → wave parameters
WaveDirector         gated     profile → veto → heuristic → correction → play
AdaptationAnnouncer  gated     (bin, strategy) → authored line
RewardEvaluator      offline   four components, recency-weighted credit
TelemetryLogger      offline   one JSONL row per wave
SelfPlayHarness      editor    headless loop over scripted player policies
```

`ExportState` / `ImportState` exist for two reasons: seeding a participant from
an offline snapshot, and the **transfer condition** in the study — does a
director trained against player A make player B rebuild more than a cold one?

---

## 6. Gates

Pass number and fail number, both fixed before implementation. The fail branch
names the action, so a failure produces a decision rather than a moved goalpost.

| Gate | Pass | Fail → |
|---|---|---|
| **G1** learner works | ≥ 85% best-strategy selection within 300 episodes, **and** beats uniform-random and round-robin | < 70% or no separation → implementation defect, or the context carries no information. Revisit the features. |
| **G2** costs nothing visible | Director work ≤ 16 ms per wave boundary | Frame spike > 33 ms → make it incremental |
| **G3** graduated beats binary | Reaches G1 accuracy in fewer episodes than the binary control, 20 seeded runs | No difference → ship binary, report it |
| **G4** enough data per bin | Pilot n=5: median observations per visited bin ≥ 8 per session | Median < 4 → drop to 3 bins, document |
| **G5** players rebuild more *(primary)* | n≈24 within-subject, Wilcoxon p < .05, dz ≥ 0.5 | p > .05 or dz < 0.2 → report the negative result |
| **G6** does not feel unfair | Non-inferiority: fairness no worse by > 0.5 on a 7-point scale | Worse → reward or announcement wording at fault |
| **G7** players notice | ≥ 70% identify unprompted that enemies adapted | < 50% → G5 uninterpretable until fixed |

**G1 passing does not license claiming G5.** A learner can converge cleanly
offline and change nothing about how a human plays. That outcome is the finding.

**Offline ladder** — before any participant: `random < static escalation <
authored heuristic < full director`, run against scripted player policies. If the
full director does not beat the authored heuristic, that must surface offline in
semester 1.

---

## 7. Conventions

- **Telemetry**: one JSONL row per wave — seeds, profile vector, bin, strategy,
  reward components, wave outcome, timings. Append-only; schema changes bump a
  version field.
- **Benchmarks**: reuse the existing `Benchmark/` JSON writers. Every result gets
  its own file with date, the exact command that reproduces it, the hypothesis
  stated *before* the table, then the table — including results that failed.
  This file set becomes the methodology chapter.
- **Deviations**: record them as they happen, in the same file. They become the
  limitations chapter.
- **Persistence**: JSON. Not raw byte reinterpretation.
- **Maths**: nothing in `Thesis.*` calls `Math.Log`, `Math.Exp`, `Math.Pow` or the
  trig functions (they differ in the last bit between runtimes). Use
  `Thesis.Core.DetMath`. Chained float arithmetic is cast step by step. Both rules
  are in `Docs/ARCHITECTURE.md` §9 rule 3, and tests enforce the first.
- **Estimator selection**: one interface behind a config asset. Not compile-time
  flags with exclusivity enforced by comments.
- Keep the existing code's habit of explaining *why* in comments, especially
  where a simpler-looking approach was tried and removed.

---

## 8. Explicitly rejected — do not reintroduce

| Thing | Why |
|---|---|
| Chunked / background-threaded flow field | Deliberately deleted. The synchronous rebuild is under a millisecond here and removed an entire bug class. |
| Walls as `isWalkable = false` | Deliberately changed. Diggable terrain is what guarantees a path always exists and killed the sinkhole/validate/revert machinery. |
| Linear contextual bandit (LinUCB-style) | Cannot represent the thresholds this domain is built around. Documented failure in an inspected implementation. |
| Deep RL / policy network | ~25 episodes per session cannot train one. Out of scope, on record, with the reason. |
| ε-greedy exploration | Thompson sampling is better motivated and has no ε to tune. |
| LLM-generated behaviour | Breaks I1 and adds an unbounded failure surface for no measurable gain over a bounded space. |
| Difficulty scaling as the objective | Wrong target. See §3. |
| Hardcoding the layout→counter mapping | It is a *result* to be reported, not a rule. |
| Option A: time-stall features without towers (`dig_share`, `funnel_reliance`, …) | The student chose towers on 2026-09-15 so the game is fun to play, not only a testbed. The tables are in git history. |
| Jev (TypeSafe AI, 2026-09-15) or any hosted/pretrained "System One" decision model inside the director | Reviewed 2026-09-29. Cloud-only, sampled with no seed control, and versioned remotely → breaks I1. At 70–500 ms per call and a 1,200 req/min limit it cannot serve the ~10⁶ headless episodes I3 needs. It is a fixed prior that does not learn from the player, which is the whole thesis claim, and swapping it in for layer 2 would make the ablation test "Jev" instead of "authored rules" (I4). Open replications (CUA-S1, laya) remove the cloud dependency but not the other objections. What *was* borrowed: calibration as a checked property of the learner (WORKPLAN WP7). |

---

## 9. Schedule

Semester 1 has 12 weeks (W5–W16), no presentation. Then a 3–4 week break, then
semester 2 (15 weeks). Weekly capacity is uneven.

```
S1 W5      wave loop restructure + JSONL telemetry
S1 W6      determinism (seeded RNG, fixed timestep, replay test)   <- critical path
S1 W7      build profile: the cheap features                       <- BLOCKED on §2
S1 W8-W9   build profile: the expensive features (2 weeks)         <- critical path
S1 W10     strategy pool + shared threat-budget cost table
S1 W11     constraint + heuristic layers
S1 W12     learned layer
S1 W13     self-play harness (extend Benchmark/)
S1 W14     gates G1, G2, G3
S1 W15     remediation                                             <- reserve
S1 W16     semester report + session prep
Break B1   pilot n=5, gate G4
Break B2   threshold freeze                                        <- irreversible
Break B3-4 buffer                                                  <- do not schedule
S2 W1-W3   UX pass (route-delta preview, arrow overlay, seeded maze)
S2 W4      balance pass                                            <- guards the study
S2 W5      second pilot + questionnaire reliability
S2 W6      build freeze                                            <- irreversible
S2 W7-W11  main study, n≈24
S2 W12-W13 analysis
S2 W14-W15 writing + artifact release
```

**Superseded for semester 1 (approved 2026-09-15):** the schedule that includes the
tower work is in `Docs/WORKPLAN.md`. It shortens the old W8–W9 because the damage
logging moved into the combat package. The student accepted this explicitly. The
semester-2 rows above still apply.

**Re-planned 2026-10-02 (D8):** WP6 is folded into WP-C3, WP7 runs in parallel, and
Flying is cut if WP-C3 is not finished by the end of W9. The order and the reason
are in `Docs/WORKPLAN.md`. If W5 began on 14 September the plan is one week behind,
which is the W15 reserve.

Cut order: (1) generated-strategies stretch goal, (2) the transfer condition,
(3) the second pilot, (4) n from 24 to 18. **Never cut** determinism, the balance
pass, or the build freeze. Do not recover time by compressing W8–W9.

Recruiting participants and departmental paperwork are handled by the student and
are not tasks here.

---

## 10. Related work

- **Spronck et al., Dynamic Scripting (2006)** — nearest neighbour; will be
  raised. Cite it first and unprompted. Differences: Bayesian posterior vs
  heuristic weights; explicit per-player context vs a global weight vector;
  build diversity vs difficulty balance.
- Al Enezi & Verbrugge (AIIDE 2023) — closest methodological model for the study.
- Sutoyo et al. (Procedia CS 59, 2015) — the one published DDA treatment in TD.
- Thompson (1933); Chapelle & Li (2011); Agrawal & Goyal (2013); Li et al.
  (2010); Marsaglia & Tsang (2000); Nadaraya (1964); Watson (1964).
- `github.com/katopz/katgpt-rs` — inspected engineering practice only; its arena
  figures are **not** citable evidence. Taken from it: the documented
  linear-bandit failure, the tier/layer architecture, per-decision reward
  shaping, recency credit, the novelty filter, and gates-with-fail-numbers.
  This includes its Jev scoreboard (Research 562: "10⁵–10⁷× faster per
  decision"), which compares a local dot product against a networked
  transformer call and is self-run. Do not cite any "N× faster than Jev" figure.
- TypeSafe AI, "Introducing System One Models & Jev" (blog, 2026-09-15) — a
  contrast case for related work: a pretrained, stateless decision model that
  reacts to the current state, versus this director, which learns online from
  one player. No paper or weights exist; cite the blog, and treat its speed and
  quality numbers as vendor claims.
- Guo et al. (2017, temperature scaling / ECE) and Brier (1950) — the
  calibration check on the learner's posteriors (WORKPLAN WP7).
