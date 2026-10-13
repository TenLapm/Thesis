using System.IO;
using Thesis.Sim;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Exports the active scene's tower roster to Maps/Towers.json, so headless runs
// (Thesis.Cli) sell the same towers, at the same prices and in the same order as the
// game. It goes through SimHost.BuildTowerRoster - the same call a play session makes -
// so the file is exactly what the simulation is given.
public static class TowerExporter
{
    [MenuItem("Thesis/Export Towers (Active Scene)")]
    public static void ExportActiveScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        SimHost host = Object.FindFirstObjectByType<SimHost>();
        if (host == null || host.towerRoster == null || host.towerRoster.Count == 0)
        {
            Debug.LogError("[TowerExporter] '" + scene.name + "' has no SimHost with a tower roster.");
            return;
        }

        var file = new TowerLibraryFile { Source = scene.name, Towers = host.BuildTowerRoster() };
        try
        {
            file.Validate();
        }
        catch (InvalidDataException e)
        {
            Debug.LogError("[TowerExporter] " + e.Message);
            return;
        }

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Maps"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "Towers.json");
        File.WriteAllText(path, file.ToJson());

        var names = new string[file.Towers.Length];
        for (int i = 0; i < names.Length; i++) names[i] = file.Towers[i].Id;
        Debug.Log("[TowerExporter] '" + scene.name + "' -> " + path + "  (" + names.Length + " towers: " + string.Join(" ", names) + ")");
    }
}
