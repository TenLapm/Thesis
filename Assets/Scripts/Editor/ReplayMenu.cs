using System.IO;
using Thesis.Harness;
using Thesis.Sim;
using UnityEditor;
using UnityEngine;

// Re-runs a recorded session inside the editor, i.e. under Unity's own runtime
// (Mono), and checks its hashes. The headless CLI does the same under .NET. A
// session that verifies in both ran identically on both runtimes; if one of them
// reports a divergence, dump the same tick from each and diff the two files
// (ARCHITECTURE.md §8).
public static class ReplayMenu
{
    [MenuItem("Thesis/Replay/Verify Latest Session")]
    public static void VerifyLatestSession()
    {
        string path = LatestSession();
        if (path == null)
        {
            Debug.LogWarning("[Replay] No session found under " + SimHost.SessionsRoot() + ". Play the game once first.");
            return;
        }
        Verify(path);
    }

    [MenuItem("Thesis/Replay/Verify File...")]
    public static void VerifyFile()
    {
        string start = Directory.Exists(SimHost.SessionsRoot()) ? SimHost.SessionsRoot() : Application.dataPath;
        string path = EditorUtility.OpenFilePanel("Replay file", start, "json");
        if (!string.IsNullOrEmpty(path)) Verify(path);
    }

    // The Mono half of the pinned episodes (the .NET half is the CLI command `pin`).
    // It runs the scripted episodes right here in the editor, with no play mode:
    // what is being pinned is how Mono does the arithmetic, not the frame loop.
    [MenuItem("Thesis/Replay/Record Pinned Episodes (Mono)")]
    public static void RecordPinnedEpisodes()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        MapData map;
        ShapeDef[] shapes;
        try
        {
            map = MapData.Load(Path.Combine(root, "Maps", "SampleScene.map.json"));
            shapes = ShapeLibraryFile.Load(Path.Combine(root, "Maps", "Shapes.json"));
        }
        catch (System.Exception e) when (e is IOException || e is InvalidDataException)
        {
            Debug.LogError("[Replay] Cannot record pinned episodes: " + e.Message);
            return;
        }

        string dir = Path.Combine(root, PinnedEpisodes.Folder);
        var written = PinnedEpisodes.RecordAll(dir, PinnedEpisodes.MonoTag, "Unity " + Application.unityVersion + " editor (Mono)", map, shapes, TowerRoster.Placeholder());
        Debug.Log("[Replay] Recorded " + written.Count + " pinned episodes under Mono into " + dir
                  + ". Record the .NET half with `thesis pin`, then run the tests in both runners.");
    }

    [MenuItem("Thesis/Replay/Open Sessions Folder")]
    public static void OpenSessionsFolder()
    {
        Directory.CreateDirectory(SimHost.SessionsRoot());
        EditorUtility.RevealInFinder(SimHost.SessionsRoot());
    }

    public static ReplayReport Verify(string path)
    {
        ReplayFile file;
        try
        {
            file = ReplayFile.Load(path);
        }
        catch (System.Exception e) when (e is IOException || e is InvalidDataException) // unreadable, or refused by validation
        {
            Debug.LogError("[Replay] Cannot read " + path + ": " + e.Message);
            return null;
        }

        ReplayReport report = ReplayRunner.Verify(file, perTick: true);
        string text = report.Describe() + "\n  file: " + path + "\n  runtime: Unity " + Application.unityVersion + " editor (Mono)";
        if (report.Ok) Debug.Log(text);
        else Debug.LogError(text);
        return report;
    }

    // The newest replay.json by folder name; session folders start with a timestamp.
    private static string LatestSession()
    {
        string root = SimHost.SessionsRoot();
        if (!Directory.Exists(root)) return null;

        string best = null;
        foreach (string dir in Directory.GetDirectories(root))
        {
            if (!File.Exists(Path.Combine(dir, "replay.json"))) continue;
            if (best == null || string.CompareOrdinal(Path.GetFileName(dir), Path.GetFileName(best)) > 0) best = dir;
        }
        return best == null ? null : Path.Combine(best, "replay.json");
    }
}
