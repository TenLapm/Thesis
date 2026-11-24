using Thesis.Director;
using Thesis.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// The first screen of the study build (WP14, D9; the SessionStart scene). The experimenter types the
// participant's code and picks the practice run, session 1 or session 2; StudySessions decides the
// condition and the seed. The screen never names the condition: the participant sits in front of
// it, and knowing which version they play would change how they play it.
public class SessionStartScreen : MonoBehaviour
{
    private TMP_InputField code;
    private TextMeshProUGUI message;
    private RectTransform choosePane, confirmPane;
    private TextMeshProUGUI confirmText;
    private StudyInfo chosen;

    void Start()
    {
        Canvas canvas = StudyUi.Overlay("Start screen", 10);
        StudyUi.Backdrop(canvas.transform, HudOverlay.Ink);

        RectTransform card = StudyUi.Centered("Card", canvas.transform, Vector2.zero, new Vector2(640f, 520f));
        StudyUi.Card(card, HudOverlay.Panel, HudOverlay.Edge);
        StudyUi.Label(card, Application.productName.ToUpperInvariant(), 44, HudOverlay.Teal, new Vector2(0f, 200f), new Vector2(560f, 60f), true);

        choosePane = StudyUi.Stretch("Choose", card);
        StudyUi.Label(choosePane, "Participant code", 22, HudOverlay.Dim, new Vector2(0f, 120f), new Vector2(400f, 30f));
        code = StudyUi.Field(choosePane, "P01", new Vector2(0f, 70f), new Vector2(260f, 58f));
        StudyUi.Button(choosePane, "Practice (3 waves)", new Vector2(0f, -20f), new Vector2(360f, 56f), () => Choose(0));
        StudyUi.Button(choosePane, "Session 1", new Vector2(-95f, -95f), new Vector2(170f, 56f), () => Choose(1));
        StudyUi.Button(choosePane, "Session 2", new Vector2(95f, -95f), new Vector2(170f, 56f), () => Choose(2));
        message = StudyUi.Label(choosePane, "", 20, new Color(0.98f, 0.45f, 0.35f), new Vector2(0f, -170f), new Vector2(560f, 50f));

        confirmPane = StudyUi.Stretch("Confirm", card);
        confirmText = StudyUi.Label(confirmPane, "", 30, HudOverlay.Bright, new Vector2(0f, 60f), new Vector2(560f, 120f), true);
        StudyUi.Button(confirmPane, "Start", new Vector2(95f, -80f), new Vector2(170f, 56f), Begin);
        StudyUi.Button(confirmPane, "Back", new Vector2(-95f, -80f), new Vector2(170f, 56f), () => Show(false));
        Show(false);
        code.Select();
    }

    private void Choose(int session)
    {
        if (!StudySessions.TryParse(code.text, out _))
        {
            message.text = "Type a participant code: P and a number, like P07.";
            return;
        }
        chosen = session == 0 ? StudySessions.Practice(code.text) : StudySessions.For(code.text, session);
        confirmText.text = chosen.Participant + "\n" + (chosen.Practice ? "Practice run" : "Session " + chosen.SessionNumber + " of 2");
        Show(true);
    }

    private void Show(bool confirm)
    {
        choosePane.gameObject.SetActive(!confirm);
        confirmPane.gameObject.SetActive(confirm);
        if (message != null) message.text = "";
    }

    private void Begin()
    {
        SessionSetup.Choose(chosen);
        Debug.Log("[Study] " + chosen.Participant + (chosen.Practice ? " practice" : " session " + chosen.SessionNumber) + ": group " + chosen.Group
                  + ", condition " + chosen.Condition + ", seed set " + chosen.SeedSet + " (seed " + StudySessions.SeedOf(chosen) + ").");
        SceneManager.LoadScene(SessionSetup.GameScene);
    }
}
