using System;
using System.Collections.Generic;
using Thesis.Core;

namespace Thesis.Sim
{
    // A SimState written out as readable JSON, for diffing two runs that should
    // have matched (ARCHITECTURE.md §8: `replay --dump-tick T`). Dump the same tick
    // from both runs, or from both runtimes, and diff the two files; the first line
    // that differs names the value that went wrong.
    //
    // Every float is written twice: as a number for reading, and as its bit pattern
    // for comparing. The bits are the truth. Two runtimes can print the same float
    // with different digits, and two floats one bit apart can print the same.
    public static class StateDump
    {
        public static string ToJson(SimState s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));

            var d = new Dump
            {
                Hash = StateHasher.Compute(s).ToString("x16"),
                Tick = s.Tick,
                Phase = s.Phase.ToString(),
                PhaseTicksRemaining = s.PhaseTicksRemaining,
                WaveIndex = s.WaveIndex,
                BuildBudget = s.BuildBudget,
                BuildBudgetBits = Bits(s.BuildBudget),
                CoreHp = s.CoreHp,
                FieldVersion = s.Grid.FieldVersion,
                AgentsSpawned = s.Agents.Count,
                OccupancyTotal = s.Occupancy.Total(),
                DamageTotal = s.Damage.Total(),
                DamageTotalBits = Bits(s.Damage.Total()),
                Placements = s.PlacementLog.Count,
                BagRngState = s.BagRng.Save().State.ToString("x16"),
                SpawnRngState = s.SpawnRng.Save().State.ToString("x16"),
                StartWaveRequested = s.StartWaveRequested,
                CurrentOutcome = s.CurrentOutcome,
                LastOutcome = s.LastOutcome,
                Bag = BagOf(s.Bag),
            };

            for (int i = 0; i < s.Pending.Count; i++) d.Pending.Add(s.Pending[i].ToString());

            // x outer, y inner: the same order StateHasher walks the grid in.
            for (int i = 0; i < s.Grid.NodeCount; i++)
            {
                SimNode n = s.Grid.ByIndex(i);
                if (!n.HasWall && n.TerrainCost == 1) continue;
                d.Walls.Add(new Wall { X = n.X, Y = n.Y, Occupant = n.Occupant.ToString(), TowerId = n.TowerId, TerrainCost = n.TerrainCost, Health = n.WallHealth, HealthBits = Bits(n.WallHealth), BestCost = n.BestCost, SapperCost = n.SapperCost });
            }

            for (int i = 0; i < s.Towers.Count; i++)
            {
                TowerState t = s.Towers[i];
                d.Towers.Add(new Tower
                {
                    Id = t.Id,
                    Def = t.Def.Id,
                    X = t.Tile.X,
                    Y = t.Tile.Y,
                    Alive = t.IsAlive,
                    Cooldown = t.Cooldown,
                    Shots = t.Shots,
                    Kills = t.Kills,
                    DamageDealt = t.DamageDealt,
                    DamageDealtBits = Bits(t.DamageDealt),
                });
            }

            IReadOnlyList<AgentState> live = s.LiveAgents;
            for (int i = 0; i < live.Count; i++)
            {
                AgentState a = live[i];
                SimNode on = s.Grid.NodeFromPosition(a.Position);
                d.LiveAgents.Add(new Agent
                {
                    Id = a.Id,
                    Movement = a.Movement.ToString(),
                    Wave = a.WaveIndex,
                    X = a.Position.X,
                    Y = a.Position.Y,
                    XBits = Bits(a.Position.X),
                    YBits = Bits(a.Position.Y),
                    TileX = on.X,
                    TileY = on.Y,
                    Hp = a.Hp,
                    HpBits = Bits(a.Hp),
                    MaxHp = a.MaxHp,
                    SlowTicks = a.SlowTicks,
                    SlowFactor = a.SlowFactor,
                    Speed = a.MoveSpeed,
                    MinCostSeen = a.MinCostSeen,
                });
            }

            return Json.SerializeIndented(d);
        }

        private static string Bits(float value) { return BitConverter.SingleToInt32Bits(value).ToString("x8"); }

        private static BagDump BagOf(ShapeBag bag)
        {
            var b = new BagDump
            {
                Current = bag.CurrentShape == null ? null : bag.CurrentShape.Name,
                CurrentTurns = bag.CurrentRotationTurns,
                Hold = bag.HoldShape == null ? null : bag.HoldShape.Name,
                HoldTurns = bag.HoldRotationTurns,
                Version = bag.Version,
            };
            foreach (ShapeDef s in bag.Preview) b.Next.Add(s.Name);
            return b;
        }

        private sealed class Dump
        {
            public string Hash;
            public int Tick;
            public string Phase;
            public int PhaseTicksRemaining;
            public int WaveIndex;
            public float BuildBudget;
            public string BuildBudgetBits;
            public int CoreHp;
            public int FieldVersion;
            public int AgentsSpawned;
            public int OccupancyTotal;
            public float DamageTotal;
            public string DamageTotalBits;
            public int Placements;
            public string BagRngState;
            public string SpawnRngState;
            public bool StartWaveRequested;
            public List<string> Pending = new List<string>();
            public BagDump Bag;
            public WaveOutcome CurrentOutcome;
            public WaveOutcome LastOutcome;
            public List<Wall> Walls = new List<Wall>();
            public List<Tower> Towers = new List<Tower>();
            public List<Agent> LiveAgents = new List<Agent>();
        }

        private sealed class BagDump
        {
            public string Current;
            public int CurrentTurns;
            public string Hold;
            public int HoldTurns;
            public int Version;
            public List<string> Next = new List<string>();
        }

        private sealed class Tower
        {
            public int Id;
            public string Def;
            public int X;
            public int Y;
            public bool Alive;
            public int Cooldown;
            public int Shots;
            public int Kills;
            public float DamageDealt;
            public string DamageDealtBits;
        }

        private sealed class Wall
        {
            public int X;
            public int Y;
            public string Occupant;
            public int TowerId;
            public int TerrainCost;
            public float Health;
            public string HealthBits;
            public int BestCost;
            public int SapperCost;
        }

        private sealed class Agent
        {
            public int Id;
            public string Movement;
            public int Wave;
            public float X;
            public float Y;
            public string XBits;
            public string YBits;
            public int TileX;
            public int TileY;
            public float Hp;
            public string HpBits;
            public float MaxHp;
            public int SlowTicks;
            public float SlowFactor;
            public float Speed;
            public int MinCostSeen;
        }
    }
}
