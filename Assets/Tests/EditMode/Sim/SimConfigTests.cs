using System.Reflection;
using NUnit.Framework;
using Thesis.Sim;

namespace Thesis.Tests.Sim
{
    public class SimConfigTests
    {
        // SimHost gives the simulation a Clone() of the config asset, so that editing
        // the asset during play cannot change a running game.
        [Test]
        public void CloneCopiesEveryFieldAndIsIndependent()
        {
            var original = new SimConfig();
            FieldInfo[] fields = typeof(SimConfig).GetFields(BindingFlags.Public | BindingFlags.Instance);

            // Give every field a value that is not its default, so a field the copy
            // missed would show.
            foreach (FieldInfo f in fields)
            {
                if (f.FieldType == typeof(float)) f.SetValue(original, (float)f.GetValue(original) + 0.25f);
                else if (f.FieldType == typeof(int)) f.SetValue(original, (int)f.GetValue(original) + 3);
                else Assert.Fail("SimConfig." + f.Name + " has type " + f.FieldType.Name + ". A shallow Clone() is only a full copy while every field is a plain number.");
            }

            SimConfig copy = original.Clone();
            Assert.AreNotSame(original, copy);
            foreach (FieldInfo f in fields) Assert.AreEqual(f.GetValue(original), f.GetValue(copy), f.Name);

            original.DigRate = 99f;
            original.CoreMaxHp = 99;
            Assert.AreNotEqual(99f, copy.DigRate);
            Assert.AreNotEqual(99, copy.CoreMaxHp);
        }

        // Three runs of the same game. One shares its config object with "the
        // inspector", one was given a copy, one is the untouched reference. Editing
        // the shared object mid-wave changes the run that shares it and not the copy.
        [Test]
        public void EditingTheSourceConfigChangesARunThatSharesItButNotOneThatCopiedIt()
        {
            MapData map = AsciiMap.Parse("S . . . C").Map;
            var source = new SimConfig { PrepSeconds = 0.1f };
            var untouched = new SimConfig { PrepSeconds = 0.1f };

            var shares = new Simulation(source, map, TestShapes.SampleSceneLibrary(), 1, new EscalationPlanner(source, map));
            SimConfig own = source.Clone();
            var copied = new Simulation(own, map, TestShapes.SampleSceneLibrary(), 1, new EscalationPlanner(own, map));
            var reference = new Simulation(untouched, map, TestShapes.SampleSceneLibrary(), 1, new EscalationPlanner(untouched, map));

            TestSims.Run(shares, 50);
            TestSims.Run(copied, 50);
            TestSims.Run(reference, 50);
            Assert.AreEqual(reference.ComputeHash(), shares.ComputeHash(), "identical until the edit");

            source.DeathReward = 7f; // "edited in the inspector during play": agents still to spawn carry it

            TestSims.Run(shares, 200);
            TestSims.Run(copied, 200);
            TestSims.Run(reference, 200);

            Assert.AreEqual(reference.ComputeHash(), copied.ComputeHash(), "the copy is immune");
            Assert.AreNotEqual(reference.ComputeHash(), shares.ComputeHash(), "the shared one is not: this is what SimHost avoids by copying");
        }
    }
}
