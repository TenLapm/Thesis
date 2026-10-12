using Thesis.Sim;
using UnityEngine;

// The look of one tower (WP-C4): a base in the colour of its damage type and a barrel
// that turns towards what it shot at. Built from primitives; art is not this package.
//
// Everything here is drawn after the fact. The simulation resolves a shot in the tick
// it is fired (I12: an instant hit, never a flying projectile), raises TowerFired, and
// PlayerBuilder passes it on to Fire, which only shows a line for a moment.
public class TowerView : MonoBehaviour
{
    public int TowerId { get; private set; } = -1;

    private const float ShotSeconds = 0.09f;

    private Transform turret;
    private LineRenderer shot;
    private LineRenderer splash;
    private float shotUntil;
    private float splashRadius;
    private static Material lineMaterial;

    public static Color ColorOf(DamageType type)
    {
        switch (type)
        {
            case DamageType.Fire: return new Color(1f, 0.45f, 0.1f);
            case DamageType.Frost: return new Color(0.3f, 0.8f, 1f);
            default: return new Color(0.88f, 0.9f, 0.95f);
        }
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
        view.splashRadius = tower.Def.SplashRadiusTiles * cell;

        // From above: a ring in the colour of the damage type round a dark middle, so
        // that a tower reads as a tower and not as a wall tile.
        Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, cell * 0.2f, 0f), new Vector3(cell * 0.92f, cell * 0.2f, cell * 0.92f), color, 0f);
        Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, cell * 0.45f, 0f), new Vector3(cell * 0.62f, cell * 0.08f, cell * 0.62f), new Color(0.05f, 0.07f, 0.11f), 0f);

        view.turret = new GameObject("Turret").transform;
        view.turret.SetParent(root.transform, false);
        view.turret.localPosition = new Vector3(0f, cell * 0.6f, 0f);
        Part(PrimitiveType.Cube, view.turret, new Vector3(0f, 0f, cell * 0.22f), new Vector3(cell * 0.2f, cell * 0.16f, cell * 0.6f), color, 0f);

        view.shot = Line(root.transform, "Shot", color, cell * 0.07f, false);
        if (view.splashRadius > 0f) view.splash = Line(root.transform, "Splash", color, cell * 0.05f, true);
        return view;
    }

    // The tower has just hit `target` (world position).
    public void Fire(Vector3 target)
    {
        Vector3 from = turret.position;
        Vector3 flat = new Vector3(target.x - from.x, 0f, target.z - from.z);
        if (flat.sqrMagnitude > 1e-6f) turret.rotation = Quaternion.LookRotation(flat);

        shot.positionCount = 2;
        shot.SetPosition(0, from);
        shot.SetPosition(1, target);
        shot.enabled = true;

        if (splash != null)
        {
            DrawCircle(splash, target, splashRadius);
            splash.enabled = true;
        }
        shotUntil = Time.time + ShotSeconds;
    }

    void Update()
    {
        if (shot != null && shot.enabled && Time.time >= shotUntil)
        {
            shot.enabled = false;
            if (splash != null) splash.enabled = false;
        }
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

    private static void Part(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 scale, Color color, float glow)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = scale;

        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", color);
        if (glow > 0f)
        {
            Color emission = color * glow;
            emission.a = 1f;
            block.SetColor("_EmissionColor", emission);
        }
        part.GetComponent<MeshRenderer>().SetPropertyBlock(block);
    }
}
