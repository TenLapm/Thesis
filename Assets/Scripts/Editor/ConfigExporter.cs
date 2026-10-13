using System.IO;
using Thesis.Core;
using Thesis.Sim;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Exports the game's numbers (the SimConfig asset the scene's SimHost uses) to
// Maps/SimConfig.json, so headless runs (`thesis run`, `thesis balance`) play with the
// numbers the game is played with. Without this file they would use SimConfig's C#
// defaults, which the tests are written against and which are not the game's since
// the balance check of WP-C5.
public static class ConfigExporter
{
    [MenuItem("Thesis/Export Config (Active Scene)")]
    public static void ExportActiveScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        SimHost host = Object.FindFirstObjectByType<SimHost>();
        if (host == null || host.configAsset == null)
        {
            Debug.LogError("[ConfigExporter] '" + scene.name + "' has no SimHost with a config asset.");
            return;
        }

        SimConfig config = host.configAsset.config.Clone();
        try
        {
            config.Validate();
        }
        catch (System.InvalidOperationException e)
        {
            Debug.LogError("[ConfigExporter] " + e.Message);
            return;
        }

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Maps"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "SimConfig.json");
        File.WriteAllText(path, Json.SerializeIndented(config));
        Debug.Log("[ConfigExporter] '" + scene.name + "' -> " + path);
    }
}
