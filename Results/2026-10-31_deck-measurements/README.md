# Measurements for the committee deck, 2026-10-31

**Code:** branch `gameplay-prototype`, commit `4944e1d` (the rules; only docs differ in the working tree).
**Machine:** Windows 11, 12 cores. **Runtime:** .NET 9.0.10 unless a row says otherwise. Game numbers: `Maps/SimConfig.json`.

## Hypotheses (written before the runs)

1. A session played in the Unity editor (Mono) replays under .NET with every per-tick hash equal, and its telemetry
   is remade byte for byte, under today's rules.
2. A rebuild of all four flow fields on the 48 x 48 board stays near a millisecond under .NET. The figure in
   the docs was two fields on 38 x 38.

It fails if any hash or byte differs (1), or if the rebuild is far above a few milliseconds (2).

## Results

**Both held.**

| Check | Result |
|---|---|
| `thesis replay session/replay.json --per-tick` | OK: 14,807 ticks, 54 commands, 5 wave hashes, final `0eb84b0ee6408a6f` |
| `thesis telemetry session/replay.json --check session/telemetry.jsonl` | OK: 7 rows, byte for byte |
| `dotnet test Tools/dotnet/Thesis.Tests -c Release` | 661 of 661 passed |
| Unity EditMode | 661 passed, 2 skipped (explicit diagnostics) |

`session/` is the Unity session of that day, seed 27590234: played by a script through `SimHost.Submit`, with no
in-memory changes, including a lost wave and a restart.

Full board: 20 generated 48 x 48 maps, up to 28 deposits collected, about 200 built tiles, one lure tower.

| Operation | Mean | Median | p99 | Max | n |
|---|---|---|---|---|---|
| All four fields (`FlowFieldSet.Generate`) | 1.320 ms | 1.252 | 2.405 | 2.777 | 1000 |
| Ground | 0.341 ms | 0.330 | 0.627 | 0.813 | 1000 |
| Sapper | 0.434 ms | 0.412 | 0.758 | 1.064 | 1000 |
| Raid | 0.213 ms | 0.199 | 0.385 | 0.587 | 1000 |
| Lure | 0.214 ms | 0.205 | 0.403 | 0.650 | 1000 |
| `StateHasher.Compute` | 0.191 ms | 0.187 | 0.305 | 0.783 | 4000 |
| `SimState.Clone` | 0.213 ms | 0.040 | 4.604 | 15.999 | 1000 |
| `RecomputePower` | 0.007 ms | 0.006 | 0.012 | 0.043 | 1000 |
| `RoughDirector.PlanWave` | 0.007 ms | 0.006 | 0.022 | 0.117 | 1000 |

Whole episodes: 25 waves, `raider` policy, 8 seeds, `CoreMaxHp` raised to 100000 so that a weak scripted player
reaches wave 25. Without per-tick hashes unless the row says so.

| Planner | Ticks each | Time each | Ticks/s | 10^6 episodes, one core |
|---|---|---|---|---|
| rough-director | 54,013 | 176 ms | 307,357 | 49 hours |
| escalation | 59,785 | 124 ms | 480,330 | 35 hours |
| rough-director, per-tick hashes | 54,013 | 9,475 ms | 5,701 | 2,632 hours |

`thesis bench --iterations 30` (38 x 38, 10 % walls): one ground field 0.142 ms; A* for 500 agents 18.2 ms, for
1,000 agents 32.1 ms. The 100-agent row (9.2 ms) ran first and looks inflated by JIT warm-up, so it is not quoted.

## Reproduce

```
dotnet build bench/bench.csproj -c Release
dotnet bench/bin/Release/net9.0/bench.dll E:/Thesis
dotnet bench/bin/Release/net9.0/bench.dll routes out/routes.json
dotnet Tools/dotnet/Thesis.Cli/bin/Release/net9.0/thesis.dll bench --iterations 30 --out out/bench.json
python Docs/presentation/make_figures.py
```

## Deviations

- **Mono not measured.** Three attempts to run the timing inside the Unity editor through the MCP bridge timed out.
  On 2026-10-02 the editor was about twice as slow as .NET for the same rebuild; nothing here confirms that for today's board.
- The per-tick hash cost is high in headless episodes (about 98 % of the run), which is why it is off by default.
