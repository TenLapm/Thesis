using System.Globalization;
using System.Text;
using Thesis.Director;
using Thesis.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// F9: why the director chose the next wave (WP14). A development tool for the student and the
// supervisor, never seen by a participant: it is switched off in every study session (SimHost.Study),
// because reading the director's reasons would change how a participant plays.
//
// It shows the director's trace for the wave on offer (DirectorTrace): the build profile it read, the
// context bin, the threat budget, and for each of the four strategies whether a constraint vetoed it,
// its authored score, the learner's correction and the total; the chosen one is marked. Under it,
// the reward the previous wave earned and how it was made. View only: it reads the trace the
// director already keeps for telemetry and changes nothing.
public class DirectorOverlay : MonoBehaviour
{
    private SimHost simHost;
    private RectTransform panel;
    private TextMeshProUGUI text;
    private bool shown;
    private int drawnWave = -2;
    private DirectorTrace drawnTrace;

    void Start()
    {
        simHost = SimHost.Find();
        Canvas canvas = StudyUi.Overlay("Director overlay", 20);
        panel = StudyUi.Centered("Panel", canvas.transform, new Vector2(-520f, 40f), new Vector2(760f, 640f));
        StudyUi.Card(panel, HudOverlay.Panel, HudOverlay.Teal);
        text = StudyUi.Label(panel, "", 17, HudOverlay.Bright, Vector2.zero, new Vector2(720f, 610f), false, TextAlignmentOptions.TopLeft);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.richText = true;
        panel.gameObject.SetActive(false);
    }

    void Update()
    {
        if (simHost == null || simHost.Study != null || Keyboard.current == null) return;
        if (Keyboard.current.f9Key.wasPressedThisFrame)
        {
            shown = !shown;
            panel.gameObject.SetActive(shown);
            drawnWave = -2;
        }
        if (!shown) return;

        SimState s = simHost.State;
        int wave = s.NextPlan != null ? s.NextPlan.WaveIndex : s.WaveIndex;
        IWavePlanner inner = simHost.Sim.Planner is SafePlanner safe ? safe.Inner : simHost.Sim.Planner;
        DirectorTrace trace = inner is IDecisionSource source ? source.TraceFor(wave) : null;
        if (wave == drawnWave && trace == drawnTrace) return;
        drawnWave = wave;
        drawnTrace = trace;
        text.text = Describe(inner, wave, trace, inner is IDecisionSource src ? src.RewardFor(wave - 1) : null);
    }

    private static string F(float v) { return v.ToString("0.00", CultureInfo.InvariantCulture); }

    private static string Describe(IWavePlanner planner, int wave, DirectorTrace t, RewardRecord previous)
    {
        var b = new StringBuilder();
        b.Append("<b><color=#2dd4bf>DIRECTOR</color></b>  <color=#94a3b8>F9 to close · development only</color>\n");
        b.Append("planner <b>").Append(planner.Name).Append("</b>, wave ").Append(wave).Append("\n\n");
        if (t == null)
        {
            b.Append("<color=#94a3b8>This planner keeps no reasons (only the heuristic and the full director do),\nor wave ").Append(wave).Append(" has not been planned yet.</color>");
            return b.ToString();
        }

        b.Append("<b>Build profile</b> (what it read off your board)\n");
        if (t.Profile != null)
            for (int i = 0; i < t.Profile.Names.Length; i++)
                b.Append("  ").Append(t.Profile.Names[i].PadRight(22)).Append(t.Profile.HasData[i] ? F(t.Profile.Values[i]) : "no data").Append('\n');
        b.Append("context bin <b>").Append(t.Bin).Append("</b> of 0 to 4, threat budget ").Append(F(t.ThreatBudget)).Append("\n\n");

        b.Append("<b>").Append("strategy".PadRight(16)).Append("veto".PadRight(16)).Append("authored  learned  total</b>\n");
        foreach (StrategyRow r in t.Rows)
        {
            bool chosen = r.Name == t.Chosen;
            if (chosen) b.Append("<color=#2dd4bf>");
            b.Append(r.Name.PadRight(16));
            b.Append((r.Veto ?? "-").PadRight(16));
            b.Append((r.Heuristic.HasValue ? F(r.Heuristic.Value) : "-").PadRight(10));
            string learned = r.Correction.HasValue ? (r.Correction.Value.Cold ? "cold" : (r.Correction.Value.Value >= 0f ? "+" : "") + F(r.Correction.Value.Value)) : "-";
            b.Append(learned.PadRight(9));
            b.Append(float.IsNegativeInfinity(r.Score) ? "vetoed" : F(r.Score));
            if (chosen) b.Append("  (chosen)</color>");
            b.Append('\n');
        }
        if (t.Fallback != null) b.Append("<color=#f87171>every strategy was vetoed: the static wave is sent (").Append(t.Fallback).Append(")</color>\n");
        if (t.Announcement != null) b.Append("banner: \"").Append(t.Announcement).Append("\"\n");
        b.Append("decided in ").Append((t.ElapsedMicroseconds / 1000f).ToString("0.0", CultureInfo.InvariantCulture)).Append(" ms\n\n");

        if (previous != null && previous.Reward != null)
        {
            RewardBreakdown r = previous.Reward;
            b.Append("<b>Reward for wave ").Append(previous.Wave).Append("</b>  ").Append(F(r.Total)).Append('\n');
            b.Append("  rebuilt ").Append(F(r.Restructure)).Append(" · survived ").Append(F(r.Survived))
             .Append(" · pressure ").Append(F(r.Pressure)).Append(" · novelty ").Append(F(r.Novelty)).Append('\n');
            b.Append("  credited to: ");
            for (int i = 0; i < previous.Credited.Count; i++)
                b.Append(i > 0 ? ", " : "").Append("wave ").Append(previous.Credited[i].Wave).Append(' ').Append(previous.Credited[i].Strategy);
            b.Append('\n');
        }
        else if (planner.Name == WaveDirector.FullId) b.Append("<color=#94a3b8>No reward yet: a wave is rewarded when the next one is planned.</color>\n");
        return b.ToString();
    }
}
