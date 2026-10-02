using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Thesis.Core;
using Thesis.Sim;

namespace Thesis.Tests.Harness
{
    public class ReplayFileTests
    {
        [Test]
        public void JsonRoundTripKeepsEverything()
        {
            ReplayFile a = TestEpisodes.SmallReplay();
            ReplayFile b = ReplayFile.FromJson(a.ToJson());

            Assert.AreEqual(a.RngSeed, b.RngSeed);
            Assert.AreEqual(a.Planner, b.Planner);
            Assert.AreEqual(a.Policy, b.Policy);
            Assert.AreEqual(a.Map, b.Map);
            Assert.AreEqual(a.SetupHash, b.SetupHash);
            Assert.AreEqual(a.InitialHash, b.InitialHash);
            Assert.AreEqual(a.FinalTick, b.FinalTick);
            Assert.AreEqual(a.FinalHash, b.FinalHash);
            Assert.AreEqual(a.TickHashes, b.TickHashes);

            Assert.Greater(a.Commands.Count, 3, "the episode must have sent some input");
            Assert.AreEqual(a.Commands.Count, b.Commands.Count);
            for (int i = 0; i < a.Commands.Count; i++) Assert.AreEqual(a.Commands[i].ToString(), b.Commands[i].ToString());

            Assert.AreEqual(3, a.WaveHashes.Count);
            Assert.AreEqual(a.WaveHashes.Count, b.WaveHashes.Count);
            for (int i = 0; i < a.WaveHashes.Count; i++) Assert.AreEqual(a.WaveHashes[i].ToString(), b.WaveHashes[i].ToString());

            // FromJson already validated the setup hash; this states the claim outright:
            // the config, map and shapes that came back are bit-for-bit the recorded ones.
            Assert.AreEqual(a.SetupHash, ReplayFile.Hex(ReplaySetup.Hash(b.Config, b.MapData, b.Shapes, b.Towers)));

            // Both kinds of build command are in the recording and survive the trip.
            Assert.IsTrue(a.Commands.Exists(c => c.Cmd == "PlaceTower"), "towers were placed");
            Assert.IsTrue(a.Commands.Exists(c => c.Cmd == "PlaceShape"), "walls were placed");
            Assert.AreEqual(a.Towers.Length, b.Towers.Length);
            for (int i = 0; i < a.Towers.Length; i++)
            {
                Assert.AreEqual(a.Towers[i].Id, b.Towers[i].Id);
                Assert.AreEqual(a.Towers[i].DamageType, b.Towers[i].DamageType);
                Assert.AreEqual(a.Towers[i].Damage, b.Towers[i].Damage);
            }
        }

        // The replay is rewritten at every wave boundary. Save() must swap the new
        // file in whole: no half-written file, no leftover temporary.
        [Test]
        public void SavingOverAnExistingFileReplacesItAndLeavesNoTemporary()
        {
            string dir = Path.Combine(Path.GetTempPath(), "thesis-replay-save-" + System.Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "replay.json");
            try
            {
                ReplayFile first = TestEpisodes.SmallReplay(waves: 1);
                ReplayFile second = TestEpisodes.SmallReplay(waves: 2);

                first.Save(path);
                Assert.AreEqual(first.FinalHash, ReplayFile.Load(path).FinalHash, "a new file");

                second.Save(path);
                Assert.AreEqual(second.FinalHash, ReplayFile.Load(path).FinalHash, "replaced");
                CollectionAssert.AreEqual(new[] { path }, Directory.GetFiles(dir), "nothing else left in the folder");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void TickHashesRoundTripThroughBase64()
        {
            var hashes = new List<ulong> { 0UL, 1UL, 0x0123456789abcdefUL, ulong.MaxValue };
            var file = new ReplayFile { TickHashes = ReplayFile.EncodeTickHashes(hashes) };

            CollectionAssert.AreEqual(hashes, file.DecodeTickHashes());
            Assert.IsNull(new ReplayFile().DecodeTickHashes(), "no per-tick hashes recorded");
        }

        [Test]
        public void TickHashesHoldOneEntryPerTick()
        {
            ReplayFile file = TestEpisodes.SmallReplay();
            Assert.AreEqual(file.FinalTick, file.DecodeTickHashes().Length);
        }

        [Test]
        public void HexIsSixteenDigitsAndParsesBack()
        {
            Assert.AreEqual("00000000000000ff", ReplayFile.Hex(255UL));
            Assert.AreEqual(0xd88b7dfd957f0dcbUL, ReplayFile.ParseHex(ReplayFile.Hex(0xd88b7dfd957f0dcbUL)));
            Assert.Throws<InvalidDataException>(() => ReplayFile.ParseHex("not hex"));
        }

        [Test]
        public void AnEditedConfigIsRefused()
        {
            ReplayFile file = TestEpisodes.Reload(TestEpisodes.SmallReplay(waves: 1));
            file.Config.DigRate = 1.5f;

            var e = Assert.Throws<InvalidDataException>(() => file.Validate());
            StringAssert.Contains("setup hash", e.Message);
        }

        [Test]
        public void AnEditedMapOrShapeIsRefused()
        {
            ReplayFile map = TestEpisodes.Reload(TestEpisodes.SmallReplay(waves: 1));
            map.MapData.Rows[0] = "X" + map.MapData.Rows[0].Substring(1);
            Assert.Throws<InvalidDataException>(() => map.Validate());

            ReplayFile shape = TestEpisodes.Reload(TestEpisodes.SmallReplay(waves: 1));
            shape.Shapes[0].BuildCost++;
            Assert.Throws<InvalidDataException>(() => shape.Validate());
        }

        [Test]
        public void CommandsOutOfTickOrderAreRefused()
        {
            ReplayFile file = TestEpisodes.Reload(TestEpisodes.SmallReplay(waves: 2));
            Assert.Greater(file.Commands[file.Commands.Count - 1].Tick, 0, "the second build phase sent input");
            file.Commands.Add(new ReplayCommand { Tick = 0, Cmd = "Rotate" });

            var e = Assert.Throws<InvalidDataException>(() => file.Validate());
            StringAssert.Contains("tick order", e.Message);
        }

        [Test]
        public void AnUnknownCommandIsRefused()
        {
            Assert.Throws<InvalidDataException>(() => new ReplayCommand { Tick = 3, Cmd = "SellTower" }.ToCommand());
            Assert.Throws<InvalidDataException>(() => new ReplayCommand { Tick = 3, Cmd = "7" }.ToCommand());
            Assert.Throws<InvalidDataException>(() => new ReplayCommand { Tick = 3, Cmd = null }.ToCommand());
        }

        [Test]
        public void CommandsConvertBothWays()
        {
            SimCommand place = ReplayCommand.From(12, SimCommand.PlaceShape(4, 9)).ToCommand();
            Assert.AreEqual(SimCommandKind.PlaceShape, place.Kind);
            Assert.AreEqual(4, place.X);
            Assert.AreEqual(9, place.Y);

            ReplayCommand recorded = ReplayCommand.From(30, SimCommand.PlaceTower(2, 7, 5));
            Assert.AreEqual("tick 30: PlaceTower(#2 @7,5)", recorded.ToString());
            SimCommand tower = recorded.ToCommand();
            Assert.AreEqual(SimCommandKind.PlaceTower, tower.Kind);
            Assert.AreEqual(2, tower.A);
            Assert.AreEqual(7, tower.X);
            Assert.AreEqual(5, tower.Y);

            Assert.AreEqual(SimCommandKind.Rotate, ReplayCommand.From(0, SimCommand.Rotate()).ToCommand().Kind);
            Assert.AreEqual(SimCommandKind.Hold, ReplayCommand.From(0, SimCommand.Hold()).ToCommand().Kind);
            Assert.AreEqual(SimCommandKind.StartWaveNow, ReplayCommand.From(0, SimCommand.StartWaveNow()).ToCommand().Kind);
        }

        [Test]
        public void GarbageIsReportedAsABadFileNotAJsonException()
        {
            Assert.Throws<InvalidDataException>(() => ReplayFile.FromJson("{ this is not json"));
            Assert.Throws<InvalidDataException>(() => ReplayFile.FromJson("null"));
        }

        // Found in WP5: SampleScene's grid sits at Z = -0.0, and the JSON writer puts
        // that in the file as "0.0". The two zeros behave the same but hash
        // differently, so the file no longer described the run. Whatever a runtime's
        // JSON does, the sign must never be lost SILENTLY: either it survives the
        // round trip or the file is refused. (SceneMapBuilder now strips negative
        // zeros, so real maps never get here.)
        [Test]
        public void ANegativeZeroIsEitherKeptOrRefusedNeverSilentlyChanged()
        {
            ReplayFile file = TestEpisodes.SmallReplay(waves: 1);
            file.MapData.OriginZ = -0f;
            file.SetupHash = ReplayFile.Hex(ReplaySetup.Hash(file.Config, file.MapData, file.Shapes, file.Towers));

            string json = file.ToJson();
            try
            {
                ReplayFile back = ReplayFile.FromJson(json);
                Assert.AreEqual(System.BitConverter.SingleToInt32Bits(-0f), System.BitConverter.SingleToInt32Bits(back.MapData.OriginZ), "accepted, so the sign must have survived");
                TestContext.WriteLine("this runtime keeps the sign of -0.0 in JSON");
            }
            catch (InvalidDataException e)
            {
                StringAssert.Contains("setup hash", e.Message);
                TestContext.WriteLine("this runtime drops the sign of -0.0 in JSON; the file was refused");
            }
        }

        // If someone adds a tunable to SimConfig, it must change the setup hash, or a
        // replay recorded with one value would be accepted under another.
        [Test]
        public void SetupHashCoversEverySimConfigField()
        {
            MapData map = AsciiMap.Parse(TestEpisodes.SmallMap).Map;
            ShapeDef[] shapes = TestShapes.SampleSceneLibrary();
            TowerDef[] towers = TestTowers.Roster();
            ulong baseline = ReplaySetup.Hash(new SimConfig(), map, shapes, towers);

            FieldInfo[] fields = typeof(SimConfig).GetFields(BindingFlags.Public | BindingFlags.Instance);
            Assert.Greater(fields.Length, 15);
            foreach (FieldInfo f in fields)
            {
                var config = new SimConfig();
                if (f.FieldType == typeof(float)) f.SetValue(config, (float)f.GetValue(config) + 0.5f);
                else if (f.FieldType == typeof(int)) f.SetValue(config, (int)f.GetValue(config) + 1);
                else Assert.Fail("SimConfig." + f.Name + " has type " + f.FieldType.Name + ": teach ReplaySetup and this test about it.");

                Assert.AreNotEqual(baseline, ReplaySetup.Hash(config, map, shapes, towers), "SimConfig." + f.Name + " is not in the setup hash");
            }
        }

        [Test]
        public void SetupHashDependsOnShapeOrderAndMap()
        {
            MapData map = AsciiMap.Parse(TestEpisodes.SmallMap).Map;
            ShapeDef[] shapes = TestShapes.SampleSceneLibrary();
            TowerDef[] towers = TestTowers.Roster();
            ulong baseline = ReplaySetup.Hash(new SimConfig(), map, shapes, towers);

            ShapeDef[] swapped = TestShapes.SampleSceneLibrary();
            ShapeDef tmp = swapped[0];
            swapped[0] = swapped[1];
            swapped[1] = tmp;
            Assert.AreNotEqual(baseline, ReplaySetup.Hash(new SimConfig(), map, swapped, towers), "the bag shuffles in library order");

            MapData moved = AsciiMap.Parse(TestEpisodes.SmallMap).Map;
            moved.Core = new TileCoord(moved.Core.X - 1, moved.Core.Y);
            Assert.AreNotEqual(baseline, ReplaySetup.Hash(new SimConfig(), moved, shapes, towers));
        }

        // The tower roster is part of what a run is built from, and its ORDER too: a
        // PlaceTower command names a tower by its index.
        [Test]
        public void SetupHashCoversEveryTowerFieldAndTheRosterOrder()
        {
            MapData map = AsciiMap.Parse(TestEpisodes.SmallMap).Map;
            ShapeDef[] shapes = TestShapes.SampleSceneLibrary();
            ulong baseline = ReplaySetup.Hash(new SimConfig(), map, shapes, TestTowers.Roster());

            FieldInfo[] fields = typeof(TowerDef).GetFields(BindingFlags.Public | BindingFlags.Instance);
            Assert.Greater(fields.Length, 10);
            foreach (FieldInfo f in fields)
            {
                TowerDef[] towers = TestTowers.Roster();
                object value = f.GetValue(towers[1]);
                if (f.FieldType == typeof(float)) f.SetValue(towers[1], (float)value + 0.25f);
                else if (f.FieldType == typeof(int)) f.SetValue(towers[1], (int)value + 1);
                else if (f.FieldType == typeof(bool)) f.SetValue(towers[1], !(bool)value);
                else if (f.FieldType == typeof(string)) f.SetValue(towers[1], (string)value + "x");
                else if (f.FieldType == typeof(DamageType)) f.SetValue(towers[1], (DamageType)(((int)value + 1) % DamageTypes.Count));
                else if (f.FieldType == typeof(TargetingMode)) continue; // only one mode exists; covered by the enum branch of ReplaySetup when there are more
                else Assert.Fail("TowerDef." + f.Name + " has type " + f.FieldType.Name + ": teach ReplaySetup and this test about it.");

                Assert.AreNotEqual(baseline, ReplaySetup.Hash(new SimConfig(), map, shapes, towers), "TowerDef." + f.Name + " is not in the setup hash");
            }

            TowerDef[] reordered = TestTowers.Roster();
            TowerDef first = reordered[0];
            reordered[0] = reordered[1];
            reordered[1] = first;
            Assert.AreNotEqual(baseline, ReplaySetup.Hash(new SimConfig(), map, shapes, reordered), "roster order");

            Assert.AreNotEqual(baseline, ReplaySetup.Hash(new SimConfig(), map, shapes, new TowerDef[0]), "an empty roster");
        }

        [Test]
        public void AnEditedTowerIsRefused()
        {
            ReplayFile file = TestEpisodes.Reload(TestEpisodes.SmallReplay(waves: 1));
            file.Towers[0].Damage += 1f;
            var e = Assert.Throws<InvalidDataException>(() => file.Validate());
            StringAssert.Contains("setup hash", e.Message);

            ReplayFile missing = TestEpisodes.Reload(TestEpisodes.SmallReplay(waves: 1));
            missing.Towers = null;
            Assert.Throws<InvalidDataException>(() => missing.Validate());
        }

        [Test]
        public void AFileOfAnOlderSchemaIsRefused()
        {
            ReplayFile file = TestEpisodes.Reload(TestEpisodes.SmallReplay(waves: 1));
            file.Schema = 1;
            var e = Assert.Throws<InvalidDataException>(() => file.Validate());
            StringAssert.Contains("Schema 1", e.Message);
        }
    }
}
