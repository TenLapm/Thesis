using System.IO;
using Thesis.Core;

namespace Thesis.Sim
{
    // The wall-shape library exported from a scene's BlockManager (menu
    // Thesis/Export Shapes -> Maps/Shapes.json), so a headless run uses the same
    // shapes, costs and ORDER as the game. Order is part of the data: the bag
    // shuffles the library in the order given, so reordering it changes every
    // seeded run.
    public sealed class ShapeLibraryFile
    {
        public const int CurrentSchema = 1;

        public int Schema = CurrentSchema;
        public string Source; // the scene it was exported from
        public ShapeDef[] Shapes;

        public void Validate()
        {
            if (Schema != CurrentSchema) throw new InvalidDataException("Shape library '" + Source + "': schema " + Schema + ", expected " + CurrentSchema + ".");
            if (Shapes == null || Shapes.Length == 0) throw new InvalidDataException("Shape library '" + Source + "' has no shapes.");
            for (int i = 0; i < Shapes.Length; i++)
            {
                ShapeDef s = Shapes[i];
                if (s == null || string.IsNullOrEmpty(s.Name)) throw new InvalidDataException("Shape library '" + Source + "': shape " + i + " is missing or unnamed.");
                if (s.LocalTiles == null || s.LocalTiles.Length == 0) throw new InvalidDataException("Shape library '" + Source + "': shape '" + s.Name + "' has no tiles.");
            }
        }

        public string ToJson() { return Json.SerializeIndented(this); }

        public static ShapeLibraryFile FromJson(string json)
        {
            ShapeLibraryFile file = Json.Deserialize<ShapeLibraryFile>(json);
            if (file == null) throw new InvalidDataException("Shape library JSON was empty.");
            file.Validate();
            return file;
        }

        public static ShapeDef[] Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Shape library file not found. Export it from Unity with the menu Thesis/Export Shapes.", path);
            return FromJson(File.ReadAllText(path)).Shapes;
        }
    }
}
