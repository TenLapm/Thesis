using System.Collections.Generic;
using Thesis.Sim;
using UnityEngine;

// The look of one tower: a ring in the colour of its damage type, a barrel that turns
// towards what it shot at, pips for its level and a dot for each piece of gear. Built
// from primitives; art is not this package.
//
// Everything here is drawn after the fact. The simulation resolves a shot in the tick
// it is fired (I12: an instant hit, never a flying projectile), raises TowerFired once
// per bolt, and PlayerBuilder passes each on to Fire, which only shows a line for a
// moment.
public class TowerView : MonoBehaviour
{
    public int TowerId { get; private set; } = -1;

    private const float ShotSeconds = 0.09f;
    private const int MaxBolts = 4;

    private Transform turret;
    private readonly List<LineRenderer> shots = new List<LineRenderer>();
    private LineRenderer splash;
    private float shotUntil;
    private int shotFrame = -1;
    private int boltsThisFrame;
    private float cell;
    private Color color;
    private Transform marks;
    private static Material lineMaterial;

    private static readonly Color[] GearColors =
    {
        new Color(0.55f, 0.9f, 1f),   // scope
        new Color(1f, 0.85f, 0.3f),   // whetstone
        new Color(0.7f, 0.6f, 1f),    // sky-sight
        new Color(1f, 0.5f, 0.2f),    // powder
        new Color(0.6f, 0.8f, 1f),    // chill
    };

    public static Color ColorOf(DamageType type)
    {
        switch (type)
        {
            case DamageType.Fire: return new Color(1f, 0.45f, 0.1f);
            case DamageType.Frost: return new Color(0.3f, 0.8f, 1f);
            default: return new Color(0.88f, 0.9f, 0.95f);
        }
    }

    public static Color ColorOfGear(int index) { return GearColors[Mathf.Abs(index) % GearColors.Length]; }

    public static string LevelName(int level) { return level >= 3 ? "EPIC" : level == 2 ? "RARE" : "COMMON"; }

    public static Color ColorOfLevel(int level)
    {
        return level >= 3 ? new Color(0.85f, 0.5f, 1f) : level == 2 ? new Color(0.4f, 0.7f, 1f) : new Color(0.8f, 0.82f, 0.86f);
    }

    // A shared unlit material for every line the views draw (shots, rings, routes).
    public static Material LineMaterial()
    {
        if (lineMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            lineMaterial = new Material(shader);
        }
        return lineMaterial;
    }

    public static TowerView Create(TowerState tower, Vector3 tileCentre, float cell)
    {
        Color color = ColorOf(tower.Def.DamageType);
        var root = new GameObject("Tower " + tower.Id + " " + tower.Def.Id);
        root.transform.position = tileCentre;
        var view = root.AddComponent<TowerView>();
        view.TowerId = tower.Id;
        view.cell = cell;
        view.color = color;

        // From above: a ring in the colour of the damage type round a dark middle, so
        // that a tower reads as a tower and not as a wall tile.
        Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, cell * 0.2f, 0f), new Vector3(cell * 0.92f, cell * 0.2f, cell * 0.92f), color);
        Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, cell * 0.45f, 0f), new Vector3(cell * 0.62f, cell * 0.08f, cell * 0.62f), new Color(0.05f, 0.07f, 0.11f));

        view.turret = new GameObject("Turret").transform;
        view.turret.SetParent(root.transform, false);
        view.turret.localPosition = new Vector3(0f, cell * 0.6f, 0f);
        Part(PrimitiveType.Cube, view.turret, new Vector3(0f, 0f, cell * 0.22f), new Vector3(cell * 0.2f, cell * 0.16f, cell * 0.6f), color);

        view.splash = Line(root.transform, "Splash", color, cell * 0.05f, true);
        return view;
    }

    // Shows the tower's level (pips along its lower edge: none, one, two) and its
    // gear (a dot per piece along its upper edge). Called when either changes.
    public void Show(TowerState tower, int gearKinds)
    {
        if (marks != null) Destroy(marks.gameObject);
        marks = new GameObject("Marks").transform;
        marks.SetParent(transform, false);

        float y = cell * 0.7f;
        Color level = ColorOfLevel(tower.Level);
        // A rare or epic tower stands on a plate in its level's colour: the one mark
        // that is easy to see from above at any size.
        if (tower.Level > 1) Part(PrimitiveType.Cube, marks, new Vector3(0f, cell * 0.06f, 0f), new Vector3(cell * 0.98f, cell * 0.1f, cell * 0.98f), level);
        for (int i = 0; i < tower.Level - 1; i++)
        {
            float x = (i - (tower.Level - 2) * 0.5f) * cell * 0.26f;
            Part(PrimitiveType.Cube, marks, new Vector3(x, y, -cell * 0.36f), new Vector3(cell * 0.2f, cell * 0.06f, cell * 0.14f), level);
        }

        int shown = 0;
        for (int slot = 0; slot < TowerState.MaxGearSlots; slot++)
        {
            if (tower.Gear[slot] == TowerState.NoGear) continue;
            float x = (shown - 0.5f) * cell * 0.3f;
            Part(PrimitiveType.Sphere, marks, new Vector3(x, y, cell * 0.36f), new Vector3(cell * 0.2f, cell * 0.2f, cell * 0.2f), ColorOfGear(tower.Gear[slot]));
            shown++;
        }
    }

    // The tower has just hit `target` (world position). Called once per bolt: a shot
    // of several bolts arrives as several calls in one frame, each its own line.
    public void Fire(Vector3 target, float splashRadius)
    {
        if (Time.frameCount != shotFrame)
        {
            shotFrame = Time.frameCount;
            boltsThisFrame = 0;
            for (int i = 0; i < shots.Count; i++) shots[i].enabled = false;
        }
        if (boltsThisFrame >= MaxBolts) return;
        while (shots.Count <= boltsThisFrame) shots.Add(Line(transform, "Shot", color, cell * 0.07f, false));

        Vector3 from = turret.position;
        Vector3 flat = new Vector3(target.x - from.x, 0f, target.z - from.z);
        if (boltsThisFrame == 0 && flat.sqrMagnitude > 1e-6f) turret.rotation = Quaternion.LookRotation(flat);

        LineRenderer shot = shots[boltsThisFrame++];
        shot.positionCount = 2;
        shot.SetPosition(0, from);
        shot.SetPosition(1, target);
        shot.enabled = true;

        if (splashRadius > 0f)
        {
            DrawCircle(splash, target, splashRadius);
            splash.enabled = true;
        }
        shotUntil = Time.time + ShotSeconds;
    }

    void Update()
    {
        if (Time.time < shotUntil) return;
        for (int i = 0; i < shots.Count; i++) shots[i].enabled = false;
        if (splash != null) splash.enabled = false;
    }

    public static void DrawCircle(LineRenderer line, Vector3 centre, float radius, int segments = 40)
    {
        line.loop = true;
        line.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            line.SetPosition(i, centre + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
        }
    }

    public static LineRenderer Line(Transform parent, string name, Color color, float width, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = LineMaterial();
        line.useWorldSpace = true;
        line.startWidth = line.endWidth = width;
        line.startColor = line.endColor = color;
        line.loop = loop;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.positionCount = 0;
        line.enabled = false;
        return line;
    }

    private static void Part(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 scale, Color color)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = scale;

        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", color);
        part.GetComponent<MeshRenderer>().SetPropertyBlock(block);
    }
}
