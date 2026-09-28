using Thesis.Sim;
using UnityEngine;
using UnityEngine.UI;

// The look of one agent (WP4). Everything it used to decide - moving along the flow
// field, chewing walls, the lifetime clock, stalling, leaking, paying rewards - is
// now Thesis.Sim.AgentSystem, run by the simulation in fixed ticks, which is what
// makes a run reproducible (CLAUDE.md I1). This component has no Update of its own
// (I8: nothing per-agent per-frame beyond drawing): its owner (WaveSpawner, or
// ScenarioBenchmark) calls OnSimTick after each tick and Render every frame.
//
// Rendering interpolates between the previous and the current tick's position, so
// motion is smooth at any frame rate even though the simulation steps at 50 Hz.
public class FlowAgent : MonoBehaviour
{
    // Grid nodes are all at y=0, so this is the resting height that sits the mesh
    // on the floor: mesh bounds extent (0.5) * prefab scale (0.3).
    public float groundHeight = 0.15f;

    [Header("UI Visuals")]
    public Image lifeBarFill;

    public int AgentId { get; private set; } = -1;

    private Vector3 previous;
    private Vector3 current;
    private float maxLife = 1f;

    public void Bind(AgentState agent)
    {
        AgentId = agent.Id;
        maxLife = Mathf.Max(agent.LifeTime, 1e-4f);
        current = previous = ToWorld(agent.Position);
        transform.position = current;
        gameObject.SetActive(true);
        UpdateLifeBar(agent.LifeTime);
    }

    public void OnSimTick(AgentState agent)
    {
        previous = current;
        current = ToWorld(agent.Position);
        UpdateLifeBar(agent.LifeTime);
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
        return new Vector3(p.X, groundHeight, p.Y);
    }

    private void UpdateLifeBar(float life)
    {
        if (lifeBarFill == null) return;
        float fraction = Mathf.Clamp01(life / maxLife);
        lifeBarFill.fillAmount = fraction;
        lifeBarFill.color = Color.Lerp(Color.red, Color.green, fraction);
    }
}
