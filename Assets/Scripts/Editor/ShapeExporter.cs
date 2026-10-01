using System.IO;
using Thesis.Sim;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Exports the active scene's wall-shape library to Maps/Shapes.json, so headless
// runs (Thesis.Cli `run`) build with the same shapes, costs and order as the game.
// It goes through BlockManager.BuildShapeLibrary - the same call SimHost makes - so
// the file is exactly what a play session's simulation is given.
public static class ShapeExporter
{
    [MenuItem("Thesis/Export Shapes (Active Scene)")]
    public static void ExportActiveScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        BlockManager blockManager = Object.FindFirstObjectByType<BlockManager>();
        if (blockManager == null || blockManager.shapeLibrary == null || blockManager.shapeLibrary.Count == 0)
        {
            Debug.LogError("[ShapeExporter] '" + scene.name + "' has no BlockManager with a shape library.");
            return;
        }

        var file = new ShapeLibraryFile { Source = scene.name, Shapes = blockManager.BuildShapeLibrary() };
        try
        {
            file.Validate();
        }
        catch (InvalidDataException e)
        {
            Debug.LogError("[ShapeExporter] " + e.Message);
            return;
        }

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Maps"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "Shapes.json");
        File.WriteAllText(path, file.ToJson());

        var names = new string[file.Shapes.Length];
        for (int i = 0; i < names.Length; i++) names[i] = file.Shapes[i].Name;
        Debug.Log("[ShapeExporter] '" + scene.name + "' -> " + path + "  (" + names.Length + " shapes: " + string.Join(" ", names) + ")");
    }
}
