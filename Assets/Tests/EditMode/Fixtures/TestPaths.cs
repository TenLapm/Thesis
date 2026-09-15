using System;
using System.IO;

namespace Thesis.Tests
{
    // Unity runs EditMode tests with the project root as the working directory;
    // `dotnet test` runs them from Tools/dotnet/Thesis.Tests/bin/<config>/net9.0.
    // Walk upward from both until the Unity project root is found.
    public static class TestPaths
    {
        private static string root;

        public static string ProjectRoot
        {
            get
            {
                if (root != null) return root;
                root = FindFrom(Directory.GetCurrentDirectory()) ?? FindFrom(AppContext.BaseDirectory);
                if (root == null)
                    throw new DirectoryNotFoundException("Could not find the Unity project root (a folder with both Assets/ and ProjectSettings/) above " + Directory.GetCurrentDirectory() + " or " + AppContext.BaseDirectory + ".");
                return root;
            }
        }

        public static string Maps => Path.Combine(ProjectRoot, "Maps");

        public static string MapFile(string name) { return Path.Combine(Maps, name + ".map.json"); }

        private static string FindFrom(string start)
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Assets")) && Directory.Exists(Path.Combine(dir.FullName, "ProjectSettings")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
