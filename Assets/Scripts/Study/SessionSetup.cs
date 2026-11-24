using Thesis.Sim;
using UnityEngine;

// What the start screen chose, carried into the game scene (WP14, D9). SimHost reads it once, when
// the simulation starts, and takes the session's seed and planner from it (StudySessions); with
// none (a development run started from the game scene in the editor) the scene's own seed and
// DirectorHost.condition apply, as before.
public static class SessionSetup
{
    public const string StartScene = "SessionStart";
    public const string GameScene = "StaticTest";

    public static StudyInfo Current { get; private set; }

    public static void Choose(StudyInfo study) { Current = study != null ? study.Copy() : null; }

    // Back to the start screen. The game scene's SimHost saves the replay and the telemetry as it
    // is destroyed (OnDestroy), so nothing is lost by leaving.
    public static void ReturnToStart()
    {
        Current = null;
        UnityEngine.SceneManagement.SceneManager.LoadScene(StartScene);
    }

    // Entering play mode without a domain reload keeps static fields: a session chosen in an
    // earlier play must not leak into the next one.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() { Current = null; }
}
