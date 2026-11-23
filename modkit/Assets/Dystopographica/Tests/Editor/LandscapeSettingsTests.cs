using NUnit.Framework;

namespace Dystopographica.Tests
{
    public class LandscapeSettingsTests
    {
        static readonly Heightmap Square = new Heightmap(2, 2, new float[4]);

        static LandscapeSettings Settings() => new LandscapeSettings
        {
            water = 0.2f, planeStart = 0.4f, planeEnd = 0.6f, modName = "Dystopographica Test Settings",
        };

        [TestCase(LandscapeSettings.Water, 0.9f, 0.4f, TestName = "MoveCursor_Water_StopsAtThePlaneStart")]
        [TestCase(LandscapeSettings.Water, -1f, 0f, TestName = "MoveCursor_Water_StopsAtTheBottom")]
        [TestCase(LandscapeSettings.PlaneStart, 0f, 0.2f, TestName = "MoveCursor_PlaneStart_StopsAtTheWater")]
        [TestCase(LandscapeSettings.PlaneStart, 1f, 0.6f, TestName = "MoveCursor_PlaneStart_StopsAtThePlaneEnd")]
        [TestCase(LandscapeSettings.PlaneEnd, 0f, 0.4f, TestName = "MoveCursor_PlaneEnd_StopsAtThePlaneStart")]
        [TestCase(LandscapeSettings.PlaneEnd, 5f, 1f, TestName = "MoveCursor_PlaneEnd_StopsAtTheTop")]
        [TestCase(LandscapeSettings.PlaneEnd, 0.7f, 0.7f, TestName = "MoveCursor_MovesFreelyBetweenItsNeighbours")]
        public void MoveCursor(int cursor, float to, float expected)
        {
            LandscapeSettings settings = Settings();

            settings.MoveCursor(cursor, to);

            float actual = cursor == LandscapeSettings.Water ? settings.water : cursor == LandscapeSettings.PlaneStart ? settings.planeStart : settings.planeEnd;
            Assert.AreEqual(expected, actual);
        }

        [TestCase(0.05f, LandscapeSettings.Water)]
        [TestCase(0.29f, LandscapeSettings.Water)]
        [TestCase(0.38f, LandscapeSettings.PlaneStart)]
        [TestCase(0.9f, LandscapeSettings.PlaneEnd)]
        public void NearestCursor_IsTheClosestOne(float value, int expected)
        {
            Assert.AreEqual(expected, Settings().NearestCursor(value));
        }

        [TestCase(0.3f, LandscapeSettings.Water)]
        [TestCase(0.8f, LandscapeSettings.PlaneEnd)]
        public void NearestCursor_WhenCursorsOverlap_PicksOneThatCanMoveThatWay(float value, int expected)
        {
            var settings = new LandscapeSettings { water = 0.5f, planeStart = 0.5f, planeEnd = 0.5f };

            Assert.AreEqual(expected, settings.NearestCursor(value));
        }

        // A map eight build areas wide was the default once. Scaled up again for the 400%
        // size it came out sixteen wide, and nobody had asked for either.
        [Test]
        public void Defaults_MakeTheWholeMapTheBuildArea()
        {
            var settings = new LandscapeSettings();

            Assert.AreEqual(LandscapeSettings.BuildAreaSize, settings.worldSize);
            Assert.AreEqual(1f, settings.BuildAreaShare);
        }

        [TestCase(50f, 1f)]
        [TestCase(200f, 0.25f)]
        [TestCase(25f, 1f, Description = "a map smaller than the build area cannot hold more than all of it")]
        [TestCase(0f, 1f)]
        public void BuildAreaShare_IsHowMuchOfTheMapsWidthTheBuildAreaCovers(float worldSize, float expected)
        {
            Assert.AreEqual(expected, new LandscapeSettings { worldSize = worldSize }.BuildAreaShare);
        }

        [TestCase(1f, 50f)]
        [TestCase(0.25f, 200f)]
        [TestCase(5f, 50f)]
        public void BuildAreaShare_WhenSet_SizesTheWorldToMatch(float share, float expectedWorldSize)
        {
            var settings = new LandscapeSettings { BuildAreaShare = share };

            Assert.AreEqual(expectedWorldSize, settings.worldSize, 1e-4f);
        }

        [Test]
        public void WriteThenRead_BringsTheSettingsBack()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dystopographica-settings-" + System.Guid.NewGuid().ToString("N") + ".json");
            try
            {
                LandscapeSettings settings = Settings();
                settings.worldSize = 123f;

                settings.Write(path);

                Assert.AreEqual(settings.ToJson(), LandscapeSettings.Read(path).ToJson());
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Test]
        public void Read_OfAMissingOrUnreadableFile_IsNothing()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dystopographica-settings-" + System.Guid.NewGuid().ToString("N") + ".json");
            Assert.IsNull(LandscapeSettings.Read(path));

            try
            {
                System.IO.File.WriteAllText(path, "not json at all");
                Assert.IsNull(LandscapeSettings.Read(path));
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Test]
        public void MapSizes_Are100AndTheLargerOnesThatAreTicked()
        {
            Assert.AreEqual(new[] { 100, 400 }, new LandscapeSettings { size200 = false, size400 = true }.MapSizes());
            Assert.AreEqual(new[] { 100 }, new LandscapeSettings { size200 = false, size400 = false }.MapSizes());
            Assert.AreEqual(new[] { 100, 200, 400 }, new LandscapeSettings().MapSizes(), "all three unless told otherwise");
        }

        // The percentage is the build area's area: seen in game, 200% is about 1.4 times as wide and 400% twice.
        [TestCase(100, 1f)]
        [TestCase(200, 1.41421f)]
        [TestCase(400, 2f)]
        public void BuildAreaScale_IsHowMuchWiderTheBuildAreaGets(int percent, float expected)
        {
            Assert.AreEqual(expected, LandscapeSettings.BuildAreaScale(percent), 1e-4f);
        }

        [Test]
        public void ForMapSize_ScalesTheMapAndItsHeights_ButLeavesTheWaterWhereItIs()
        {
            var settings = new LandscapeSettings { worldSize = 50f, peakHeight = 10f, waterDepth = 0.5f, lowestDepth = 2f, water = 0.2f };

            LandscapeSettings large = settings.ForMapSize(400);

            Assert.AreEqual(100f, large.worldSize);
            Assert.AreEqual(20f, large.peakHeight);
            Assert.AreEqual(0.5f, large.waterDepth);
            Assert.AreEqual(3.5f, large.lowestDepth, 1e-5f, "the bed is twice as far below the water");
            Assert.AreEqual(0.2f, large.water);
            Assert.AreEqual(50f, settings.worldSize, "the original is untouched");
        }

        [Test]
        public void ForMapSize_OnADryMap_ScalesTheWholeDepth()
        {
            var settings = new LandscapeSettings { waterPlane = false, lowestDepth = 2f };

            Assert.AreEqual(4f, settings.ForMapSize(400).lowestDepth, 1e-5f);
        }

        [Test]
        public void ForMapSize_100_ChangesNothing()
        {
            LandscapeSettings settings = Settings();

            Assert.AreEqual(settings.ToJson(), settings.ForMapSize(100).ToJson());
        }

        [Test]
        public void DisplayNameFor_NamesTheMapSizeOnlyWhenThereIsMoreThanOneVersion()
        {
            var settings = new LandscapeSettings { landscapeName = "Miami", size200 = false, size400 = true };
            Assert.AreEqual("Miami (100%)", settings.DisplayNameFor(100));
            Assert.AreEqual("Miami (400%)", settings.DisplayNameFor(400));

            settings.size400 = false;
            Assert.AreEqual("Miami", settings.DisplayNameFor(100));
        }

        [Test]
        public void DryMap_ClicksNeverPickUpTheWaterCursor()
        {
            LandscapeSettings settings = Settings();
            settings.waterPlane = false;

            Assert.AreEqual(LandscapeSettings.PlaneStart, settings.NearestCursor(0.05f));
        }

        [Test]
        public void DryMap_PlaneStartMovesFreely_AndTakesTheUnusedWaterCursorWithIt()
        {
            LandscapeSettings settings = Settings();
            settings.waterPlane = false;

            settings.MoveCursor(LandscapeSettings.PlaneStart, 0.1f);

            Assert.AreEqual(0.1f, settings.planeStart);
            Assert.LessOrEqual(settings.water, settings.planeStart);
        }

        [Test]
        public void Curve_PutsTheWaterCursorAtTheWaterDepth_OnlyWhenTheMapHasWater()
        {
            LandscapeSettings settings = Settings();
            settings.knee = 0f;
            settings.lowestDepth = 2f;
            settings.waterDepth = 0.5f;

            Assert.AreEqual(-0.5f, settings.Curve().Evaluate(settings.water), 1e-5f);

            settings.waterPlane = false;
            Assert.AreEqual(-1f, settings.Curve().Evaluate(settings.water), 1e-5f, "halfway down a dry slope to the lowest depth");
        }

        [Test]
        public void PaintedWater_IsNothingOnADryMap()
        {
            LandscapeSettings settings = Settings();
            Assert.AreEqual(0.2f, settings.PaintedWater);

            settings.waterPlane = false;
            Assert.AreEqual(0f, settings.PaintedWater);
        }

        [Test]
        public void Problems_LowestDepthThatIsNotBelowTheWater()
        {
            LandscapeSettings settings = Settings();
            settings.waterDepth = 0.7f;
            settings.lowestDepth = 0.7f;

            StringAssert.Contains("Lowest depth", string.Join(" ", settings.Problems(Square)));

            settings.waterPlane = false;
            Assert.IsEmpty(settings.Problems(Square), "a dry map has no water to be below");
        }

        [Test]
        public void Problems_WaterWithNoDepth()
        {
            LandscapeSettings settings = Settings();
            settings.waterDepth = 0f;

            StringAssert.Contains("Water depth", string.Join(" ", settings.Problems(Square)));
        }

        [Test]
        public void Problems_AreEmptyWhenTheSettingsAreUsable()
        {
            Assert.IsEmpty(Settings().Problems(Square));
        }

        [Test]
        public void Problems_MissingHeightmap()
        {
            StringAssert.Contains("heightmap", string.Join(" ", Settings().Problems(null)));
        }

        [Test]
        public void Problems_HeightmapThatIsNotSquare()
        {
            StringAssert.Contains("square", string.Join(" ", Settings().Problems(new Heightmap(2, 1, new float[2]))));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("Up/Down")]
        [TestCase("What?")]
        [TestCase("Trailing dot.")]
        public void Problems_ModNameThatCannotBeAFolder(string name)
        {
            LandscapeSettings settings = Settings();
            settings.modName = name;

            StringAssert.Contains("mod name", string.Join(" ", settings.Problems(Square)));
        }

        [Test]
        public void Problems_FolderThatThisToolDidNotMake()
        {
            LandscapeSettings settings = Settings();
            settings.modName = "ExampleMod";

            StringAssert.Contains("already exists", string.Join(" ", settings.Problems(Square)));
        }

        [Test]
        public void Problems_WorldThatHasNoSize()
        {
            LandscapeSettings settings = Settings();
            settings.worldSize = 0f;

            StringAssert.Contains("World size", string.Join(" ", settings.Problems(Square)));
        }

        [Test]
        public void Json_RoundTrips()
        {
            LandscapeSettings settings = Settings();
            settings.peakHeight = 33f;
            settings.heightmapPath = "C:/maps/miami.png";

            LandscapeSettings copy = LandscapeSettings.FromJson(settings.ToJson());

            Assert.AreEqual(33f, copy.peakHeight);
            Assert.AreEqual("C:/maps/miami.png", copy.heightmapPath);
            Assert.AreEqual(settings.ToJson(), copy.ToJson());
        }

        [TestCase("Miami Beach", "miami-beach")]
        [TestCase("  --Old__Town 2  ", "old-town-2")]
        [TestCase("!!!", "landscape")]
        public void Slug_IsSafeToUseAsAnIdAndAFileName(string name, string expected)
        {
            Assert.AreEqual(expected, LandscapeSettings.Slug(name));
        }

        [Test]
        public void DisplayName_FallsBackToTheModName()
        {
            LandscapeSettings settings = Settings();

            Assert.AreEqual("Dystopographica Test Settings", settings.DisplayName);
            settings.landscapeName = "Valley";
            Assert.AreEqual("Valley", settings.DisplayName);
        }
    }
}
