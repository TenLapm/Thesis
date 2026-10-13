using System.Collections.Generic;
using UnityEngine;

// The rocks of a map (WP-C4, GDD §3.2): fixed tiles that belong to the map and not to
// the player. To the simulation a rock is a static blocker, the 'X' of MapData, exactly
// like a collider on GridManager.unwalkableMask; SceneMapBuilder reads both. Nothing can
// be built on a rock, enemies on foot go round it, flyers pass over it.
//
// The layout is text, so it can be read, diffed and checked headless before anyone
// plays on it (`thesis ascii`, Tools/gdd/routetool): one line per row, the TOP row
// first, '.' open ground and 'X' rock, the orientation MapData.Rows and the ASCII test
// fixtures use. Spaces are ignored. It must have exactly as many rows and columns as
// the grid has tiles.
//
// Changing the layout changes the map, and with it every recorded session: export the
// maps again and record the pinned replays again.
public class RockLayout : MonoBehaviour
{
    public GridManager gridManager;

    [TextArea(12, 40)]
    public string rows = "";

    [Header("Look")]
    [Tooltip("Rocks should read as ground, not as something built: darker and duller than a wall.")]
    public Color rockColor = new Color(0.36f, 0.42f, 0.56f);
    [Tooltip("Height of a rock as a share of the tile's width.")]
    public float heightShare = 0.45f;

    private string[] parsed;

    // True when the layout marks tile (x, y) as rock. width and height are the grid's
    // size in tiles; a layout of another size is an authoring mistake and is reported
    // once, then treated as "no rocks" so the scene still starts.
    public bool IsRock(int x, int y, int width, int height)
    {
        string[] lines = Parse(width, height);
        return lines != null && lines[height - 1 - y][x] == 'X';
    }

    private string[] Parse(int width, int height)
    {
        if (parsed != null && parsed.Length == height) return parsed;

        var lines = new List<string>();
        foreach (string raw in (rows ?? "").Split('\n'))
        {
            string line = raw.Replace(" ", "").Replace("\t", "").Replace("\r", "");
            if (line.Length > 0) lines.Add(line);
        }
        if (lines.Count == 0) return null;

        if (lines.Count != height)
        {
            Debug.LogError("RockLayout: " + lines.Count + " rows, but the grid is " + height + " tiles high. No rocks are used.");
            return null;
        }
        for (int r = 0; r < lines.Count; r++)
        {
            if (lines[r].Length != width)
            {
                Debug.LogError("RockLayout: row " + r + " has " + lines[r].Length + " tiles, but the grid is " + width + " wide. No rocks are used.");
                return null;
            }
            foreach (char c in lines[r])
            {
                if (c != '.' && c != 'X')
                {
                    Debug.LogError("RockLayout: row " + r + " has '" + c + "'; only '.' and 'X' are allowed. No rocks are used.");
                    return null;
                }
            }
        }

        parsed = lines.ToArray();
        return parsed;
    }

    void OnValidate() { parsed = null; }

    // Draws the rocks. View only: the simulation has them from SceneMapBuilder.
    void Start()
    {
        if (gridManager == null) gridManager = GetComponent<GridManager>();
        if (gridManager == null || gridManager.grid == null) return;

        int width = gridManager.gridSizeX;
        int height = gridManager.gridSizeY;
        float cell = gridManager.nodeRadius * 2f;
        float tall = cell * heightShare;

        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", rockColor);

        // A generated map (SimHost.generateMap) has its rocks in the simulation's map, not in this text.
        SimHost host = SimHost.Find();
        Thesis.Sim.MapData generated = host != null && host.generateMap ? host.Map : null;

        // The ring nothing is built on: a faint line along its inner edge.
        if (generated != null && generated.BuildMargin > 0)
        {
            LineRenderer ring = TowerView.Line(transform, "BuildBorder", new Color(1f, 1f, 1f, 0.28f), 0.12f, true);
            float halfX = gridManager.gridWorldSize.x / 2f - generated.BuildMargin * cell;
            float halfZ = gridManager.gridWorldSize.y / 2f - generated.BuildMargin * cell;
            Vector3 c = gridManager.transform.position + Vector3.up * 0.08f;
            ring.positionCount = 4;
            ring.SetPositions(new[] { c + new Vector3(-halfX, 0f, -halfZ), c + new Vector3(halfX, 0f, -halfZ), c + new Vector3(halfX, 0f, halfZ), c + new Vector3(-halfX, 0f, halfZ) });
        }

        var parent = new GameObject("Rocks").transform;
        parent.SetParent(transform, false);
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (generated != null ? generated.IsWalkable(x, y) : !IsRock(x, y, width, height)) continue;
                GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rock.name = "Rock " + x + "," + y;
                // No collider: a rock is a rule of the map, not a physics object, and the
                // ground under it must stay what the mouse ray hits.
                Destroy(rock.GetComponent<Collider>());
                TowerView.UseSolidMaterial(rock);
                rock.transform.SetParent(parent, false);
                rock.transform.position = gridManager.grid[x, y].worldPosition + new Vector3(0f, tall / 2f, 0f);
                rock.transform.localScale = new Vector3(cell, tall, cell);
                rock.GetComponent<MeshRenderer>().SetPropertyBlock(block);
            }
        }
    }
}
