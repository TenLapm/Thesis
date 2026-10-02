using System.Collections.Generic;
using Thesis.Core;
using Thesis.Sim;
using UnityEngine;

// WP4 adapter. The shape queue - 7-bag randomiser, preview, hold, rotation - is now
// Thesis.Sim.ShapeBag inside the simulation, seeded from the run seed instead of
// UnityEngine.Random (CLAUDE.md I1). The build budget lives in the simulation too.
//
// This component keeps:
//   * shapeLibrary, the scene's list of BlockShape assets. Its ORDER matters: it is
//     the order the bag shuffles, so reordering it changes every seeded run.
//   * the members CanvasDashboard and GhostPreviewer read, under the same names.
//     currentShape / holdShape / nextShapes hand out BlockShape clones that mirror
//     the simulation's shapes: one stable clone per simulation shape instance, so a
//     new piece is still a new reference (the HUD's "piece changed" trigger), and a
//     rotation still keeps the reference and bumps shapeVersion.
public class BlockManager : MonoBehaviour
{
    [Header("Available Shapes")]
    public List<BlockShape> shapeLibrary; // Drag your .asset files here

    public SimHost simHost;

    private readonly Dictionary<ShapeDef, BlockShape> views = new Dictionary<ShapeDef, BlockShape>();
    private readonly Dictionary<string, BlockShape> masters = new Dictionary<string, BlockShape>();
    private readonly Queue<BlockShape> nextQueue = new Queue<BlockShape>();
    private readonly List<ShapeDef> stale = new List<ShapeDef>();
    private int syncedVersion = -1;

    private SimHost Host => simHost != null ? simHost : (simHost = SimHost.Find());

    private ShapeBag Bag => Host.State.Bag;

    // --- read-outs ---

    public BlockShape currentShape
    {
        get
        {
            Sync();
            return View(Bag.CurrentShape);
        }
    }

    public BlockShape holdShape
    {
        get
        {
            Sync();
            return View(Bag.HoldShape);
        }
    }

    public Queue<BlockShape> nextShapes
    {
        get
        {
            Sync();
            return nextQueue;
        }
    }

    // Float: kill rewards are fractional (0.2 per enemy killed).
    public float buildBudget => Host.State.BuildBudget;

    // Bumped whenever a piece could LOOK different (pull, swap, rotate).
    public int shapeVersion => Bag.Version;

    // --- input ---

    public void SwapHoldShape()
    {
        Host.Submit(SimCommand.Hold());
    }

    public void RotateCurrentShape()
    {
        Host.Submit(SimCommand.Rotate());
    }

    // Tiles the current piece would cover with its origin at (x, y). Read straight
    // from the simulation's shape, so the ghost always shows what a click places.
    public List<Vector2Int> GetTargetGridPositions(int originGridX, int originGridY)
    {
        var targetTiles = new List<Vector2Int>();
        ShapeDef shape = Bag.CurrentShape;
        if (shape == null) return targetTiles;
        foreach (TileCoord offset in shape.LocalTiles)
        {
            targetTiles.Add(new Vector2Int(originGridX + offset.X, originGridY + offset.Y));
        }
        return targetTiles;
    }

    // --- used by SimHost / PlayerBuilder ---

    // The simulation's copy of the library, in shapeLibrary order. BlockShape leaves
    // digCost / wallHealth unserialized on the shipped assets, so its C# defaults
    // (15 / 6) come through here exactly as they did in PlayerBuilder.
    public ShapeDef[] BuildShapeLibrary()
    {
        masters.Clear();
        var defs = new ShapeDef[shapeLibrary.Count];
        for (int i = 0; i < shapeLibrary.Count; i++)
        {
            BlockShape s = shapeLibrary[i];
            string name = string.IsNullOrEmpty(s.shapeName) ? s.name : s.shapeName;
            if (masters.ContainsKey(name))
                Debug.LogError("BlockManager: two shapes are named '" + name + "'. Shape names must be unique; the simulation matches shapes by name.");
            masters[name] = s;

            var tiles = new TileCoord[s.localTiles.Length];
            for (int t = 0; t < tiles.Length; t++) tiles[t] = new TileCoord(s.localTiles[t].x, s.localTiles[t].y);
            defs[i] = new ShapeDef { Name = name, BuildCost = s.buildCost, DigCost = s.digCost, WallHealth = s.wallHealth, LocalTiles = tiles };
        }
        return defs;
    }

    // The library asset a shape came from (for its colour and icon).
    public BlockShape MasterFor(string shapeName)
    {
        if (masters.Count == 0) _ = Host.Sim; // starting the simulation runs BuildShapeLibrary
        masters.TryGetValue(shapeName, out BlockShape master);
        return master;
    }

    // --- mirroring ---

    private BlockShape View(ShapeDef def)
    {
        if (def == null) return null;
        if (!views.TryGetValue(def, out BlockShape view))
        {
            BlockShape master = MasterFor(def.Name);
            if (master == null) return null;
            // Clone so the HUD's rotated tiles never touch the .asset file.
            view = Instantiate(master);
            view.name = master.name;
            views[def] = view;
            CopyTiles(def, view);
        }
        return view;
    }

    private void Sync()
    {
        ShapeBag bag = Bag;
        if (bag.Version == syncedVersion) return;
        syncedVersion = bag.Version;

        // Rotation mutates the simulation's shape in place; mirror its tiles.
        foreach (var pair in views) CopyTiles(pair.Key, pair.Value);

        nextQueue.Clear();
        foreach (ShapeDef def in bag.Preview) nextQueue.Enqueue(View(def));

        // Drop clones of pieces that were placed (the old ScriptableObject leak fix).
        stale.Clear();
        foreach (ShapeDef def in views.Keys)
        {
            if (def == bag.CurrentShape || def == bag.HoldShape) continue;
            bool queued = false;
            foreach (ShapeDef p in bag.Preview) if (p == def) queued = true;
            if (!queued) stale.Add(def);
        }
        foreach (ShapeDef def in stale)
        {
            Destroy(views[def]);
            views.Remove(def);
        }
    }

    private static void CopyTiles(ShapeDef def, BlockShape view)
    {
        if (view.localTiles == null || view.localTiles.Length != def.LocalTiles.Length) view.localTiles = new Vector2Int[def.LocalTiles.Length];
        for (int i = 0; i < def.LocalTiles.Length; i++) view.localTiles[i] = new Vector2Int(def.LocalTiles[i].X, def.LocalTiles[i].Y);
    }
}
