using UnityEngine;

// Placement flourish: a wall "pops" up to full size with a slight overshoot the
// moment it's built. Self-contained on the wall GameObject so it cleans itself up
// automatically if the wall gets chewed through mid-pop. Uses UNSCALED time so
// the pop feels identical at x1/x2/x3 - it's a flourish for the player's action,
// not part of the simulation that the speed controls are meant to scale.
//
// Driven by PlayerBuilder, which calls Play() AFTER it has set the wall's final
// scale (so we capture the correct target and don't fight the placement code).
public class WallSpawnAnimator : MonoBehaviour
{
    [Tooltip("Seconds the pop takes once it starts.")]
    public float duration = 0.18f;
    [Tooltip("Back-ease overshoot strength - higher briefly scales past full size for a springier pop.")]
    public float overshoot = 2f;
    [Tooltip("Scale (fraction of final) the wall sits at before its pop begins - kept tiny so staggered tiles are nearly invisible until their turn.")]
    public float startScaleFactor = 0.05f;

    private Vector3 targetScale = Vector3.one;
    private float delay;
    private float t;
    private bool animating;

    // Call AFTER the wall's final localScale is set. startDelay staggers tiles of
    // the same shape so a multi-tile piece ripples in instead of popping flat.
    public void Play(float startDelay = 0f)
    {
        targetScale = transform.localScale;
        transform.localScale = targetScale * startScaleFactor;
        delay = startDelay;
        t = 0f;
        animating = true;
    }

    void Update()
    {
        if (!animating) return;

        if (delay > 0f)
        {
            delay -= Time.unscaledDeltaTime;
            return; // stay small until this tile's turn in the ripple
        }

        t += Time.unscaledDeltaTime;
        float p = Mathf.Clamp01(t / duration);
        transform.localScale = targetScale * BackOut(p, overshoot);

        if (p >= 1f)
        {
            transform.localScale = targetScale;
            animating = false;
        }
    }

    // Back-out ease: rushes toward 1, overshoots slightly, then settles.
    private float BackOut(float x, float s)
    {
        x -= 1f;
        return x * x * ((s + 1f) * x + s) + 1f;
    }
}
