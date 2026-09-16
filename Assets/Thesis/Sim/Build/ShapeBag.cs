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
