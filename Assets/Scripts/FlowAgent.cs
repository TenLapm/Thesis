using UnityEngine;
using UnityEngine.UI;

public class FlowAgent : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;

    // Grid nodes are all at y=0, so bestDirection is always purely horizontal.
    // This is the resting height that sits the mesh on the floor: mesh bounds
    // extent (0.5) * prefab scale (0.3).
    public float groundHeight = 0.15f;

    [Header("Digging")]
    [Tooltip("Wall health chewed away per second while standing on a wall tile. Several agents on one tile chew in parallel.")]
    public float digRate = 1f;

    [Header("Rewards")]
    [Tooltip("Build budget credited when this agent is successfully stalled (its life timer runs out before reaching the core).")]
    public float deathReward = 0.2f;
    [Tooltip("Build budget credited when this agent fully breaches (destroys) a wall tile.")]
    public float wallBreakReward = 1f;

    [Header("Time-Stall Mechanics")]
    public float maxLifeTime = 60f;
    private float currentLifeTime;

    // References passed in when spawned
    private FlowFieldManager flowManager;
    private GridManager gridManager;
    private PlayerCore targetCore;
    private BlockManager blockManager;

    [Header("UI Visuals")]
    public Image lifeBarFill;

    public void Initialize(FlowFieldManager manager, PlayerCore core, float lifeTime, float speed, BlockManager builder)
    {
        flowManager = manager;
        gridManager = flowManager != null ? flowManager.gridManager : null;
        targetCore = core;
        blockManager = builder;
        maxLifeTime = lifeTime;
        currentLifeTime = maxLifeTime;
        moveSpeed = speed;

        // Snap to the floor immediately - spawnPoint's own Y is unrelated to
        // where the agent should actually rest.
        Vector3 pos = transform.position;
        pos.y = groundHeight;
        transform.position = pos;
    }

    void Update()
    {
        if (gridManager == null) return;

        // Keep the agent glued to the floor regardless of horizontal movement.
        if (transform.position.y != groundHeight)
        {
            Vector3 pos = transform.position;
            pos.y = groundHeight;
            transform.position = pos;
        }

        // THE CLOCK IS TICKING - and note it keeps ticking while chewing a
        // wall, which is the whole point: chewing wastes their time too.
        currentLifeTime -= Time.deltaTime;

        if (lifeBarFill != null)
        {
            float lifePercentage = currentLifeTime / maxLifeTime;
            lifeBarFill.fillAmount = lifePercentage;
            lifeBarFill.color = Color.Lerp(Color.red, Color.green, lifePercentage);
        }

        // Did we successfully stall them?
        if (currentLifeTime <= 0)
        {
            Die(success: true);
            return;
        }

        Node currentNode = gridManager.NodeFromWorldPoint(transform.position);
        if (currentNode == null) return;

        // Did the enemy reach the core?
        if (currentNode.bestCost == 0)
        {
            Die(success: false);
            return;
        }

        // DIGGING: standing on a wall tile means chewing it instead of walking.
        // This is also the recovery path for "a wall was dropped on my head" -
        // the agent is never frozen anymore, it just starts eating its way out.
        if (currentNode.HasWall)
        {
            ChewWall(currentNode);
            return;
        }

        // Flow-field movement, one tile at a time toward the cheapest neighbour
        // (nextNode). We move to the actual tile CENTRE with MoveTowards rather
        // than sliding along a free direction vector, and this is the fix for
        // enemies occasionally slipping through walls:
        //   * MoveTowards clamps at the target, so no matter how fast the agent
        //     is (x3 speed, low frame-rate) it can never overshoot past a tile -
        //     it always stops at nextNode and re-samples, so no wall tile is
        //     ever skipped in a single frame.
        //   * The flow field only ever links a diagonal edge when BOTH shared
        //     cardinal tiles are corner-safe (GridManager.GetNeighbors), so every
        //     centre-to-centre hop crosses only open ground or the destination -
        //     never the corner of a standing wall.
        // The old free-vector movement let agents drift off-centre and graze
        // wall corners, which is exactly the intermittent pass-through we saw.
        Node stepNode = currentNode.nextNode;
        if (stepNode != null)
        {
            Vector3 target = stepNode.worldPosition;
            target.y = groundHeight;

            Vector3 previous = transform.position;
            transform.position = Vector3.MoveTowards(previous, target, moveSpeed * Time.deltaTime);

            Vector3 delta = transform.position - previous;
            if (delta.sqrMagnitude > 0.0000001f)
            {
                transform.rotation = Quaternion.LookRotation(delta.normalized);
            }
        }
    }

    private void ChewWall(Node node)
    {
        node.wallHealth -= digRate * Time.deltaTime;

        // Shrink the wall so chew progress is readable at a glance.
        if (node.visualObject != null && node.maxWallHealth > 0f)
        {
            float frac = Mathf.Clamp01(node.wallHealth / node.maxWallHealth);
            Vector3 s = node.visualObject.transform.localScale;
            float full = s.x; // walls are uniform cubes; x keeps the original size
            s.y = full * Mathf.Lerp(0.15f, 1f, frac);
            node.visualObject.transform.localScale = s;

            Vector3 p = node.visualObject.transform.position;
            p.y = node.worldPosition.y + s.y / 2f;
            node.visualObject.transform.position = p;
        }

        if (node.wallHealth <= 0f)
        {
            // Breach! Clear the tile back to open ground...
            node.wallHealth = 0f;
            node.maxWallHealth = 0f;
            node.terrainCost = 1;
            if (node.visualObject != null)
            {
                Destroy(node.visualObject);
                node.visualObject = null;
            }

            // ...and rebuild the field so everyone reroutes through (or away
            // from) the fresh hole. Synchronous and cheap on this grid size.
            if (flowManager != null) flowManager.GenerateFlowField();

            // Safe from double-crediting even with several agents chewing the
            // same tile: HasWall is re-checked fresh at the top of every
            // agent's Update(), so only the one call that actually crosses
            // wallHealth <= 0 ever reaches this block.
            if (blockManager != null) blockManager.buildBudget += wallBreakReward;
        }
    }

    private void Die(bool success)
    {
        if (success)
        {
            // Successfully stalled - the closest thing to a "kill" this game has.
            if (blockManager != null) blockManager.buildBudget += deathReward;
        }
        else if (targetCore != null)
        {
            // Enemy reached the end - player failed to stall it.
            targetCore.TakeDamage(1);
        }

        gameObject.SetActive(false);
    }
}
