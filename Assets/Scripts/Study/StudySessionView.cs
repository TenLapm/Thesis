using Thesis.Director;
using UnityEngine;
using UnityEngine.InputSystem;

// What a study session adds to the game scene (WP14): the end of the practice run, and the
// experimenter's way back to the start screen. In development (SimHost.Study is null) it does nothing.
//
// A study session has no last wave (the student, 2026-11-24: the game goes on until the core falls,
// and a lost wave can always be played again), so the experimenter ends it: Ctrl+Shift+Q goes back
// to the start screen, for the next session. The replay and the telemetry are saved on the way out
// (SimHost.OnDestroy), as they are after every wave.
public class StudySessionView : MonoBehaviour
{
    private SimHost simHost;
    private GameObject practicePanel;

    void Start()
    {
        simHost = SimHost.Find();
        if (simHost == null || simHost.Study == null) { enabled = false; return; }

        Canvas canvas = StudyUi.Overlay("Study session", 30);
        RectTransform shade = StudyUi.Backdrop(canvas.transform, StudyUi.Shade);
        RectTransform card = StudyUi.Centered("Card", shade, Vector2.zero, new Vector2(560f, 260f));
        StudyUi.Card(card, HudOverlay.Panel, HudOverlay.Teal);
        StudyUi.Label(card, "Practice over", 36, HudOverlay.Teal, new Vector2(0f, 70f), new Vector2(500f, 50f), true);
        StudyUi.Label(card, "That was " + StudySessions.PracticeWaves + " waves. The real session starts from the start screen.", 20, HudOverlay.Bright, new Vector2(0f, 10f), new Vector2(480f, 60f));
        StudyUi.Button(card, "Back to the start screen", new Vector2(0f, -75f), new Vector2(340f, 56f), SessionSetup.ReturnToStart);
        practicePanel = shade.gameObject;
        practicePanel.SetActive(false);
    }

    void Update()
    {
        if (practicePanel != null && simHost.PracticeOver && !practicePanel.activeSelf) practicePanel.SetActive(true);

        Keyboard k = Keyboard.current;
        if (k != null && k.qKey.wasPressedThisFrame && (k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed) && (k.leftShiftKey.isPressed || k.rightShiftKey.isPressed))
        {
            Debug.Log("[Study] The experimenter ended " + simHost.Study.Participant + (simHost.Study.Practice ? " practice" : " session " + simHost.Study.SessionNumber) + " at wave " + simHost.State.WaveIndex + ".");
            SessionSetup.ReturnToStart();
        }
    }
}
