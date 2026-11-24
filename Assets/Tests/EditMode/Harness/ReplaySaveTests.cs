using System;
using System.IO;
using NUnit.Framework;
using Thesis.Sim;

namespace Thesis.Tests.Harness
{
    // A tester's 35-wave session, 2026-11-24: the replay stopped at wave 27 with a replay.json.tmp
    // left beside it. Swapping the new file in (File.Replace) fails on Windows while anything holds
    // the old one open without letting it be deleted (an editor, a sync tool, a virus scan), and
    // after one failure the game saved nothing more. A save now falls back to writing over the file.
    public class ReplaySaveTests
    {
        private static string Folder()
        {
            string dir = Path.Combine(Path.GetTempPath(), "thesis-save-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Test]
        public void ASaveGoesThroughWhileAnotherProgramHoldsTheOldFile()
        {
            string dir = Folder();
            string path = Path.Combine(dir, "replay.json");
            new ReplayFile { Session = "first" }.Save(path);

            // Open for reading and writing by others, but not for deleting: File.Replace fails then.
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                new ReplayFile { Session = "second" }.Save(path);
            }
            StringAssert.Contains("\"second\"", File.ReadAllText(path));
            Assert.IsFalse(File.Exists(path + ".tmp"), "no temporary file is left behind");
            Directory.Delete(dir, true);
        }

        [Test]
        public void AnOrdinarySaveStillSwapsTheFileIn()
        {
            string dir = Folder();
            string path = Path.Combine(dir, "replay.json");
            new ReplayFile { Session = "first" }.Save(path);
            new ReplayFile { Session = "second" }.Save(path);
            StringAssert.Contains("\"second\"", File.ReadAllText(path));
            Assert.IsFalse(File.Exists(path + ".tmp"));
            Directory.Delete(dir, true);
        }
    }
}
