using System;
using System.Collections.Generic;
using Thesis.Core;

namespace Thesis.Sim
{
    // Port of BlockManager's shape queue: a 7-bag randomiser (Fisher-Yates over one
    // shuffled copy of the whole library, drawn to empty before the next bag is
    // shuffled) with a 3-shape preview queue, a hold slot, and rotation. Draws are
    // deterministic given the IRandom passed in (RngStreams.Bag).
    //
    // "7-bag" is Tetris jargon, not a hardcoded size: the bag holds exactly one
    // copy of every entry in Library (SampleScene wires up 7 - I,J,L,O,S,T,Z - but
    // nothing here assumes that number).
    public sealed class ShapeBag
    {
        public readonly ShapeDef[] Library;

        public ShapeDef CurrentShape { get; private set; }
        public ShapeDef HoldShape { get; private set; }

        // How many quarter-turns CurrentShape/HoldShape are away from its library
        // master's orientation. The original never tracked this (localTiles alone
        // encode the current orientation); it exists so a PlacementRecord can log a
        // "rot" field without the log reader re-deriving it from tile offsets.
        public int CurrentRotationTurns { get; private set; }
        public int HoldRotationTurns { get; private set; }

        // Bumped whenever CurrentShape/HoldShape could look different (pull, swap,
        // rotate) - mirrors BlockManager.shapeVersion, which exists because rotating
        // mutates a shape in place, so the reference alone never changes.
        public int Version { get; private set; }

        private readonly Queue<ShapeDef> nextShapes = new Queue<ShapeDef>();
        private readonly List<ShapeDef> bagDraws = new List<ShapeDef>();
        private readonly IRandom rng;

        private bool hasHeldThisTurn;
        private bool isHoldSlotEmpty = true;

        public ShapeBag(ShapeDef[] library, IRandom rng)
        {
            if (library == null || library.Length == 0) throw new ArgumentException("A shape library needs at least one shape.", nameof(library));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            Library = library;
            this.rng = rng;

            for (int i = 0; i < 3; i++) nextShapes.Enqueue(DrawFromBag());
            PullNextShape();
        }

        // Deep copy for Simulation.Clone(). The rng passed in must already hold the
        // same state as this bag's rng (SimState clones the Pcg32 and hands it in),
        // so both bags draw the same sequence from here on. Held and queued shapes
        // are cloned (they are mutable); bagDraws hold library masters, which are
        // never mutated, so sharing those references is safe.
        internal ShapeBag CloneWith(IRandom clonedRng)
        {
            return new ShapeBag(this, clonedRng);
        }

        private ShapeBag(ShapeBag source, IRandom clonedRng)
        {
            Library = source.Library;
            rng = clonedRng ?? throw new ArgumentNullException(nameof(clonedRng));
            CurrentShape = source.CurrentShape?.Clone();
            HoldShape = source.HoldShape?.Clone();
            CurrentRotationTurns = source.CurrentRotationTurns;
            HoldRotationTurns = source.HoldRotationTurns;
            Version = source.Version;
            hasHeldThisTurn = source.hasHeldThisTurn;
            isHoldSlotEmpty = source.isHoldSlotEmpty;
            foreach (ShapeDef s in source.nextShapes) nextShapes.Enqueue(s.Clone());
            bagDraws.AddRange(source.bagDraws);
        }

        internal void AddToHash(ref Fnv1a64 h)
        {
            AddShape(ref h, CurrentShape);
            AddShape(ref h, HoldShape);
            h.Add(CurrentRotationTurns);
            h.Add(HoldRotationTurns);
            h.Add(Version);
            h.Add(hasHeldThisTurn);
            h.Add(isHoldSlotEmpty);
            h.Add(nextShapes.Count);
            foreach (ShapeDef s in nextShapes) AddShape(ref h, s);
            h.Add(bagDraws.Count);
            for (int i = 0; i < bagDraws.Count; i++) h.Add(bagDraws[i].Name);
        }

        private static void AddShape(ref Fnv1a64 h, ShapeDef s)
        {
            if (s == null)
            {
                h.Add(-1);
                return;
            }
            h.Add(s.Name);
            h.Add(s.BuildCost);
            h.Add(s.DigCost);
            h.Add(s.WallHealth);
            h.Add(s.LocalTiles.Length);
            for (int i = 0; i < s.LocalTiles.Length; i++)
            {
                h.Add(s.LocalTiles[i].X);
                h.Add(s.LocalTiles[i].Y);
            }
        }

        public IEnumerable<ShapeDef> Preview => nextShapes;

        public void PullNextShape()
        {
            CurrentShape = nextShapes.Count > 0 ? nextShapes.Dequeue() : DrawFromBag();
            nextShapes.Enqueue(DrawFromBag());
            CurrentRotationTurns = 0; // a fresh draw always starts at its master's orientation
            hasHeldThisTurn = false;
            Version++;
        }

        public void SwapHold()
        {
            if (hasHeldThisTurn) return;

            if (isHoldSlotEmpty)
            {
                HoldShape = CurrentShape;
                HoldRotationTurns = CurrentRotationTurns;
                isHoldSlotEmpty = false;
                PullNextShape(); // current now lives in the hold slot
            }
            else
            {
                ShapeDef shapeTmp = CurrentShape;
                int turnsTmp = CurrentRotationTurns;
                CurrentShape = HoldShape;
                CurrentRotationTurns = HoldRotationTurns;
                HoldShape = shapeTmp;
                HoldRotationTurns = turnsTmp;
            }

            hasHeldThisTurn = true;
            Version++;
        }

        public void RotateCurrent()
        {
            if (CurrentShape == null) return;
            CurrentShape.Rotate();
            CurrentRotationTurns = (CurrentRotationTurns + 1) % 4;
            Version++;
        }

        // Draws from the END of the shuffled bag, exactly like BlockManager.GetRandomShape,
        // and always hands out a Clone() so Rotate() never mutates a library master.
        private ShapeDef DrawFromBag()
        {
            if (bagDraws.Count == 0) RefillBag();

            int lastIndex = bagDraws.Count - 1;
            ShapeDef master = bagDraws[lastIndex];
            bagDraws.RemoveAt(lastIndex);
            return master.Clone();
        }

        private void RefillBag()
        {
            bagDraws.Clear();
            bagDraws.AddRange(Library);

            // Fisher-Yates shuffle, matching BlockManager.RefillBag's UnityEngine.Random
            // usage: Random.Range(0, i + 1) is max-exclusive, i.e. uniform over [0, i].
            for (int i = bagDraws.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                ShapeDef tmp = bagDraws[i];
                bagDraws[i] = bagDraws[j];
                bagDraws[j] = tmp;
            }
        }
    }
}
