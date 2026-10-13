using System.Collections.Generic;
using System.IO;
using Thesis.Core;

namespace Thesis.Sim
{
    // The tower roster exported from the scene (menu Thesis/Export Towers ->
    // Maps/Towers.json), so a headless run buys from the same towers, at the same
    // prices and in the same ORDER as the game. Order is part of the data: a shop
    // offer names a tower by its index in the roster, so reordering it changes what
    // every seeded shop offers.
    public sealed class TowerLibraryFile
    {
        public const int CurrentSchema = 1;

        public int Schema = CurrentSchema;
        public string Source; // the scene it was exported from
        public TowerDef[] Towers;

        public void Validate()
        {
            if (Schema != CurrentSchema) throw new InvalidDataException("Tower roster '" + Source + "': schema " + Schema + ", expected " + CurrentSchema + ".");
            if (Towers == null || Towers.Length == 0) throw new InvalidDataException("Tower roster '" + Source + "' has no towers.");

            var seen = new HashSet<string>();
            for (int i = 0; i < Towers.Length; i++)
            {
                TowerDef t = Towers[i];
                if (t == null) throw new InvalidDataException("Tower roster '" + Source + "': tower " + i + " is missing.");
                try
                {
                    t.Validate();
                }
                catch (System.InvalidOperationException e)
                {
                    throw new InvalidDataException("Tower roster '" + Source + "': " + e.Message);
                }
                if (!seen.Add(t.Id)) throw new InvalidDataException("Tower roster '" + Source + "': two towers are called '" + t.Id + "'.");
            }
        }

        public string ToJson() { return Json.SerializeIndented(this); }

        public static TowerLibraryFile FromJson(string json)
        {
            TowerLibraryFile file = Json.Deserialize<TowerLibraryFile>(json);
            if (file == null) throw new InvalidDataException("Tower roster JSON was empty.");
            file.Validate();
            return file;
        }

        public static TowerDef[] Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Tower roster file not found. Export it from Unity with the menu Thesis/Export Towers.", path);
            return FromJson(File.ReadAllText(path)).Towers;
        }
    }
}
