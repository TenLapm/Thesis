using Thesis.Sim;
using UnityEngine;
using UnityEngine.UI;

// The look of one agent (WP4). Everything it used to decide - moving along the flow
// field, chewing walls, leaking, paying rewards - is now Thesis.Sim, run by the
// simulation in fixed ticks, which is what makes a run reproducible (CLAUDE.md I1).
// The bar over its head shows hit points since WP-C1 (it used to show the lifetime
// clock, which no longer exists). This component has no Update of its own
// (I8: nothing per-agent per-frame beyond drawing): its owner (WaveSpawner, or
// ScenarioBenchmark) calls OnSimTick after each tick and Render every frame.
//
// Rendering interpolates between the previous and the current tick's position, so
// motion is smooth at any frame rate even though the simulation steps at 50 Hz.
//
// Movement classes (WP-C2) are told apart by a placeholder look until WP-C4 gives
// each enemy type its own prefab: a sapper is tinted, a flyer is tinted and drawn
// above the walls. The height is view only; the simulation is flat.
public class FlowAgent : MonoBehaviour
{
    // Grid nodes are all at y=0, so this is the resting height that sits the mesh
    // on the floor: mesh bounds extent (0.5) * prefab scale (0.3).
    public float groundHeight = 0.15f;

    [Header("UI Visuals")]
    public Image lifeBarFill;

    [Header("Movement classes (placeholder look until WP-C4)")]
    [Tooltip("How far above the ground a flyer is drawn. Walls are one tile (2 units) tall, so this clears them.")]
    public float flyHeight = 2.6f;
    public Color sapperColor = new Color(1f, 0.3f, 0.08f);
    public Color flyerColor = new Color(0.3f, 0.85f, 1f);

    public int AgentId { get; private set; } = -1;

    private Vector3 previous;
    private Vector3 current;
    private float maxHp = 1f;
    private float height;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock tint;

    public void Bind(AgentState agent)
    {
        AgentId = agent.Id;
        maxHp = Mathf.Max(agent.MaxHp, 1e-4f);
        height = agent.Movement == MovementClass.Flying ? groundHeight + flyHeight : groundHeight;
        ApplyClassLook(agent.Movement);
        current = previous = ToWorld(agent.Position);
        transform.position = current;
        gameObject.SetActive(true);
        UpdateLifeBar(agent.Hp);
    }

    public void OnSimTick(AgentState agent)
    {
        previous = current;
        current = ToWorld(agent.Position);
        UpdateLifeBar(agent.Hp);
    }

    public void Render(float tickAlpha)
    {
        Vector3 p = Vector3.Lerp(previous, current, tickAlpha);
        Vector3 delta = p - transform.position;
        transform.position = p;
        if (delta.sqrMagnitude > 0.0000001f) transform.rotation = Quaternion.LookRotation(delta.normalized);
    }

    public void Release()
    {
        AgentId = -1;
        gameObject.SetActive(false);
    }

    private Vector3 ToWorld(Thesis.Core.Vec2f p)
    {
        return new Vector3(p.X, height, p.Y);
    }

    // Views are pooled, so the same object draws a walker in one wave and a flyer in
    // the next: the look is set on every Bind, and a ground enemy clears the tint
    // to get the prefab's own material back. A property block, not a material
    // instance, so the agents still batch.
    private void ApplyClassLook(MovementClass movement)
    {
        if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null) return;

        if (movement == MovementClass.Ground)
        {
            meshRenderer.SetPropertyBlock(null);
            return;
        }

        Color color = movement == MovementClass.Flying ? flyerColor : sapperColor;
        Color emission = color * 2.5f;
        emission.a = 1f;

        if (tint == null) tint = new MaterialPropertyBlock();
        tint.SetColor("_BaseColor", color);
        tint.SetColor("_EmissionColor", emission);
        meshRenderer.SetPropertyBlock(tint);
    }

    // lifeBarFill keeps its old name because the prefab refers to it; it is the HP bar now.
    private void UpdateLifeBar(float hp)
    {
        if (lifeBarFill == null) return;
        float fraction = Mathf.Clamp01(hp / maxHp);
        lifeBarFill.fillAmount = fraction;
        lifeBarFill.color = Color.Lerp(Color.red, Color.green, fraction);
    }
}
