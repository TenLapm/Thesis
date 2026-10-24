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
    private bool billboard;
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

    // One colour per tower type (2026-10-24: the damage type's colour made three towers
    // look alike). Picked far apart in hue and brightness; the ore's gold is left to the
    // collectors.
    public static Color ColorOfTower(TowerDef def)
    {
        switch (def.Id)
        {
            case "archer": return new Color(0.96f, 0.96f, 0.96f);   // white
            case "ballista": return new Color(0.62f, 0.42f, 0.24f); // brown
            case "cannon": return new Color(1f, 0.22f, 0.18f);      // red
            case "brazier": return new Color(1f, 0.55f, 0.05f);     // orange
            case "frost": return new Color(0.35f, 0.75f, 1f);       // sky blue
            case "sniper": return new Color(0.62f, 0.3f, 1f);       // violet
            case "shard": return new Color(0.1f, 0.9f, 0.8f);       // teal
            case "flak": return new Color(1f, 0.35f, 0.7f);         // pink
            case "pylon": return new Color(0.55f, 1f, 0.25f);       // lime
            default: return ColorOf(def.DamageType);
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

    // A flat figure that stands upright and faces the camera (the tilted trial scene):
    // a body and a head in the colour of the damage type, on a frame in the colour of
    // the level, with the tower's first letters. A placeholder for a drawn character.
    public static GameObject Figure(TowerDef def, int level, float cell, float alpha = 1f)
    {
        var root = new GameObject("Figure");
        root.AddComponent<Billboard>();
        Color color = ColorOfTower(def);
        Color frame = ColorOfLevel(level);
        color.a = frame.a = alpha;
        Color dark = new Color(0.05f, 0.07f, 0.11f, alpha);

        float w = cell * 1.5f, h = cell * 2.3f;
        Flat(root.transform, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, 1f), frame, 0);
        Flat(root.transform, new Vector3(0f, h * 0.5f, 0f), new Vector3(w * 0.9f, h * 0.93f, 1f), dark, 1);
        Flat(root.transform, new Vector3(0f, h * 0.36f, 0f), new Vector3(w * 0.6f, h * 0.5f, 1f), color, 2);
        Flat(root.transform, new Vector3(0f, h * 0.76f, 0f), new Vector3(w * 0.36f, w * 0.36f, 1f), color, 2);

        var text = new GameObject("Name").AddComponent<TMPro.TextMeshPro>();
        text.transform.SetParent(root.transform, false);
        text.transform.localPosition = new Vector3(0f, h * 0.34f, -0.02f);
        text.rectTransform.sizeDelta = new Vector2(w, h * 0.4f);
        text.text = def.Id.Length <= 2 ? def.Id.ToUpperInvariant() : def.Id.Substring(0, 2).ToUpperInvariant();
        text.fontSize = cell * 5.5f;
        text.fontStyle = TMPro.FontStyles.Bold;
        text.alignment = TMPro.TextAlignmentOptions.Center;
        text.color = dark;
        text.sortingOrder = 3;
        return root;
    }

    private static void Flat(Transform parent, Vector3 at, Vector3 scale, Color color, int order)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = at + new Vector3(0f, 0f, -0.004f * order);
        quad.transform.localScale = scale;
        MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = LineMaterial();
        renderer.sortingOrder = order;
        var block = new MaterialPropertyBlock();
        block.SetColor("_Color", color);
        renderer.SetPropertyBlock(block);
    }

    public static TowerView Create(TowerState tower, Vector3 tileCentre, float cell, bool billboard = false)
    {
        Color color = ColorOfTower(tower.Def);
        var root = new GameObject("Tower " + tower.Id + " " + tower.Def.Id);
        root.transform.position = tileCentre;
        var view = root.AddComponent<TowerView>();
        view.TowerId = tower.Id;
        view.cell = cell;
        view.color = color;

        if (billboard)
        {
            // A low base on the tile, and the figure standing on it.
            Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, cell * 0.06f, 0f), new Vector3(cell * 0.9f, cell * 0.06f, cell * 0.9f), color);
            view.billboard = true;
            view.turret = new GameObject("Turret").transform;
            view.turret.SetParent(root.transform, false);
            view.turret.localPosition = new Vector3(0f, cell * 1.1f, 0f);
            view.splash = Line(root.transform, "Splash", color, cell * 0.05f, true);
            return view;
        }

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

        if (billboard)
        {
            // The figure carries the level in its frame; gear is a row of dots at its feet.
            GameObject figure = Figure(tower.Def, tower.Rarity, cell);
            figure.transform.SetParent(marks, false);
            int dots = 0;
            for (int slot = 0; slot < TowerState.MaxGearSlots; slot++)
            {
                if (tower.Gear[slot] == TowerState.NoGear) continue;
                Part(PrimitiveType.Sphere, marks, new Vector3((dots - 0.5f) * cell * 0.34f, cell * 0.2f, -cell * 0.42f), new Vector3(cell * 0.24f, cell * 0.24f, cell * 0.24f), ColorOfGear(tower.Gear[slot]));
                dots++;
            }
            return;
        }

        float y = cell * 0.7f;
        Color level = ColorOfLevel(tower.Rarity);
        // A rare or epic tower stands on a plate in its level's colour: the one mark
        // that is easy to see from above at any size.
        if (tower.Rarity > 1) Part(PrimitiveType.Cube, marks, new Vector3(0f, cell * 0.06f, 0f), new Vector3(cell * 0.98f, cell * 0.1f, cell * 0.98f), level);
        for (int i = 0; i < tower.Rarity - 1; i++)
        {
            float x = (i - (tower.Rarity - 2) * 0.5f) * cell * 0.26f;
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

    // A red ring at the tower's foot while it has no power (SimConfig.PowerRule).
    private LineRenderer noPower;

    public void SetPowered(bool powered)
    {
        if (powered && noPower == null) return;
        if (noPower == null)
        {
            noPower = Line(transform, "NoPower", new Color(1f, 0.25f, 0.2f, 0.95f), cell * 0.08f, true);
            const int points = 24;
            noPower.positionCount = points;
            for (int i = 0; i < points; i++)
            {
                float a = i * Mathf.PI * 2f / points;
                noPower.SetPosition(i, transform.position + new Vector3(Mathf.Cos(a) * cell * 0.48f, cell * 0.12f, Mathf.Sin(a) * cell * 0.48f));
            }
        }
        noPower.enabled = !powered;
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

    // The material of every shape the views make at run time. A primitive's own default
    // material is the built-in one, which a built URP player draws pink (seen in the
    // first build sent to a tester, 2026-10-14); in the editor Unity swaps it silently.
    // This one is an asset in a Resources folder, so it and its shader are in every build.
    public static Material SolidMaterial()
    {
        if (solidMaterial == null) solidMaterial = Resources.Load<Material>("ViewLit");
        return solidMaterial;
    }

    private static Material solidMaterial;

    public static void UseSolidMaterial(GameObject primitive)
    {
        Material solid = SolidMaterial();
        if (solid != null) primitive.GetComponent<MeshRenderer>().sharedMaterial = solid;
    }

    private static void Part(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 scale, Color color)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        UseSolidMaterial(part);
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = scale;

        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", color);
        part.GetComponent<MeshRenderer>().SetPropertyBlock(block);
    }
}
