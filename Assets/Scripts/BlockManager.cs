using System.Collections.Generic;
using UnityEngine;

public class BlockManager : MonoBehaviour
{
    [Header("Available Shapes")]
    public List<BlockShape> shapeLibrary; // Drag your .asset files here

    [Header("Current State")]
    public BlockShape currentShape;
    public BlockShape holdShape;

    [Header("Build Budget")]
    // Spent per placed shape. Without this, there's nothing stopping the player
    // from tiling the whole map before ever starting a wave.
    // Float, not int: FlowAgent credits fractional amounts per stalled enemy.
    public float buildBudget = 60f;

    public Queue<BlockShape> nextShapes = new Queue<BlockShape>();

    // Bumped every time the piece the player is holding could LOOK different
    // (pull, swap, rotate, reset). The HUD watches this instead of only
    // comparing references - which is what made rotation invisible on the
    // panel before (rotating mutates the clone in place, same reference).
    [HideInInspector] public int shapeVersion = 0;

    private bool hasHeldThisTurn = false;
    private bool isHoldSlotEmpty = true;

    // 7-bag randomizer: each "bag" is one shuffled copy of every shape in the
    // library, drawn to empty before the next bag is shuffled. Guarantees every
    // shape shows up once every 7 pulls (no droughts) and caps streaks at
    // 2-in-a-row, which matters because placement costs limited budget.
    private List<BlockShape> currentBag = new List<BlockShape>();

    void Start()
    {
        for (int i = 0; i < 3; i++) nextShapes.Enqueue(GetRandomShape());
        PullNextShape(false);
    }

    [ContextMenu("Reset Queue")] // Right-click the component in the Inspector
    public void ResetQueue()
    {
        // Destroy the old runtime clones instead of abandoning them - fixes a
        // slow ScriptableObject leak over long sessions.
        foreach (BlockShape s in nextShapes)
        {
            if (s != null) Destroy(s);
        }
        nextShapes.Clear();

        if (currentShape != null)
        {
            Destroy(currentShape);
            currentShape = null;
        }

        for (int i = 0; i < 3; i++) nextShapes.Enqueue(GetRandomShape());
        PullNextShape(false);
        Debug.Log("Queue Reset with current Library!");
    }

    private BlockShape GetRandomShape()
    {
        if (currentBag.Count == 0) RefillBag();

        int lastIndex = currentBag.Count - 1;
        BlockShape source = currentBag[lastIndex];
        currentBag.RemoveAt(lastIndex);

        // Clone so Rotate() mutates the runtime copy, never the .asset file.
        return Instantiate(source);
    }

    private void RefillBag()
    {
        currentBag = new List<BlockShape>(shapeLibrary);

        // Fisher-Yates shuffle
        for (int i = currentBag.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            BlockShape temp = currentBag[i];
            currentBag[i] = currentBag[j];
            currentBag[j] = temp;
        }
    }

    // destroyCurrent: true when the current clone was consumed (placed) and
    // nothing else references it; false when it just moved to the hold slot.
    public void PullNextShape(bool destroyCurrent = false)
    {
        if (destroyCurrent && currentShape != null) Destroy(currentShape);

        currentShape = nextShapes.Count > 0 ? nextShapes.Dequeue() : GetRandomShape();
        nextShapes.Enqueue(GetRandomShape());
        hasHeldThisTurn = false;
        shapeVersion++;
    }

    public void SwapHoldShape()
    {
        if (hasHeldThisTurn) return;

        if (isHoldSlotEmpty)
        {
            holdShape = currentShape;
            isHoldSlotEmpty = false;
            PullNextShape(false); // current now lives in the hold slot - don't destroy it
        }
        else
        {
            BlockShape temp = currentShape;
            currentShape = holdShape;
            holdShape = temp;
        }
        hasHeldThisTurn = true;
        shapeVersion++;
    }

    public void RotateCurrentShape()
    {
        if (currentShape == null) return;
        currentShape.Rotate();
        shapeVersion++;
    }

    public List<Vector2Int> GetTargetGridPositions(int originGridX, int originGridY)
    {
        List<Vector2Int> targetTiles = new List<Vector2Int>();
        foreach (Vector2Int offset in currentShape.localTiles)
        {
            targetTiles.Add(new Vector2Int(originGridX + offset.x, originGridY + offset.y));
        }
        return targetTiles;
    }
}
