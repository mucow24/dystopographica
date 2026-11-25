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

        // The water cursor used to stop the plane start from passing it. To put the plane
        // start just above a river, the water cursor had to be shoved down out of the
        // way first, and was then left far below the river, which came out as dry land.
        [Test]
        public void MoveCursor_PlaneStartDraggedBelowTheWater_TakesTheWaterWithIt()
        {
            LandscapeSettings settings = Settings();

            settings.MoveCursor(LandscapeSettings.PlaneStart, 0.1f);

            Assert.AreEqual(0.1f, settings.planeStart);
            Assert.AreEqual(0.1f, settings.water);
        }

        [Test]
        public void MoveCursor_PlaneStartMovedUp_LeavesSeparateWaterWhereItIs()
        {
            LandscapeSettings settings = Settings();

            settings.MoveCursor(LandscapeSettings.PlaneStart, 0.5f);

            Assert.AreEqual(0.5f, settings.planeStart);
            Assert.AreEqual(0.2f, settings.water);
        }

        // Water right at the plane start means "everything lower than the city is
        // water", which is what a map usually wants. It stays that way as the plane
        // start moves, until the water cursor itself is pulled away.
        [Test]
        public void MoveCursor_WaterSittingAtThePlaneStart_FollowsItUpAndDown()
        {
            var settings = new LandscapeSettings { water = 0.4f, planeStart = 0.4f, planeEnd = 0.6f };

            settings.MoveCursor(LandscapeSettings.PlaneStart, 0.5f);
            Assert.AreEqual(0.5f, settings.water);

            settings.MoveCursor(LandscapeSettings.PlaneStart, 0.3f);
            Assert.AreEqual(0.3f, settings.water);

            settings.MoveCursor(LandscapeSettings.Water, 0.1f);
            settings.MoveCursor(LandscapeSettings.PlaneStart, 0.35f);
            Assert.AreEqual(0.1f, settings.water, "pulled away, it stays put");
        }

        // Water switched on with nothing under it gives a map with no water at all, which
        // nobody wants. The window calls this when the cursors are at rest, and the
        // water cursor goes back to the plane start by itself.
        [Test]
        public void RaiseWaterOutOfTheDry_WhenNothingIsUnderWater_PutsTheWaterAtThePlaneStart()
        {
            LandscapeSettings settings = Settings();

            Assert.IsTrue(settings.RaiseWaterOutOfTheDry(waterFraction: 0f));

            Assert.AreEqual(settings.planeStart, settings.water);
        }

        [Test]
        public void RaiseWaterOutOfTheDry_TreatsAFewStrayPixelsAsNothing()
        {
            Assert.IsTrue(Settings().RaiseWaterOutOfTheDry(waterFraction: 0.00003f), "29 pixels in a million is not a body of water");
            Assert.IsFalse(Settings().RaiseWaterOutOfTheDry(waterFraction: 0.001f), "a small pond is");
        }

        [Test]
        public void RaiseWaterOutOfTheDry_LeavesAloneWhatIsFine()
        {
            LandscapeSettings wet = Settings();
            Assert.IsFalse(wet.RaiseWaterOutOfTheDry(waterFraction: 0.2f));
            Assert.AreEqual(0.2f, wet.water);

            LandscapeSettings dry = Settings();
            dry.waterPlane = false;
            Assert.IsFalse(dry.RaiseWaterOutOfTheDry(waterFraction: 0f), "a dry map has no water to show");
            Assert.AreEqual(0.2f, dry.water);

            var alreadyThere = new LandscapeSettings { water = 0.4f, planeStart = 0.4f };
            Assert.IsFalse(alreadyThere.RaiseWaterOutOfTheDry(waterFraction: 0f), "nothing is lower than the city, so there is nowhere better to put it");
        }

        [Test]
        public void Defaults_PutTheWaterCursorAtThePlaneStart()
        {
            var settings = new LandscapeSettings();

            Assert.AreEqual(settings.planeStart, settings.water);
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

        // Presets saved by earlier versions of the tool carry settings that are no
        // longer choices: whether buildings may stand anywhere, how deep the water
        // lies, whether the game's own grid and mountains are hidden. What a preset
        // says about those is ignored, and dropped the next time it is saved.
        [Test]
        public void APresetFromAnEarlierTool_StillLoads_AndWhatItSaysAboutSettingsThatAreGoneIsIgnored()
        {
            LandscapeSettings settings = LandscapeSettings.FromJson(
                "{\"water\": 0.2, \"peakHeight\": 33.0, \"buildAnywhere\": false, \"waterDepth\": 0.7, \"hideDefaultGrid\": false, \"hideDefaultLandscape\": false}");

            Assert.AreEqual(33f, settings.peakHeight);
            Assert.AreEqual(0.18f, settings.GroundLift, 1e-6f, "buildings stand anywhere, at Night City's levels");
            Assert.AreEqual(0.118f, settings.GroundLift - settings.WaterDrop, 1e-6f);
            Assert.AreEqual(new[] { WaterKind.Custom }, settings.Landscapes(), "and the map gets our own water, like any other");
            foreach (string gone in new[] { "buildAnywhere", "waterDepth", "hideDefaultGrid", "hideDefaultLandscape" })
                StringAssert.DoesNotContain(gone, settings.ToJson());
        }

        // The game stands every building at y = 0, and lets it stand anywhere. Some will
        // stand over the water, and with the land drawn at 0 and the water below it they
        // hover. The Night City mod draws its ground a little above 0 and its water
        // between the two, so a building over the water stands in it. These are its
        // levels, measured from its meshes: roads at 0.18, water at 0.118.
        [Test]
        public void AMapWithWater_DrawsTheLandAndTheWaterJustAboveTheLevelBuildingsStandOn()
        {
            var settings = new LandscapeSettings { water = 0.2f, knee = 0f };

            Assert.AreEqual(0.18f, settings.GroundLift, 1e-6f);
            Assert.AreEqual(0.118f, settings.GroundLift - settings.WaterDrop, 1e-6f, "the water");
            Assert.AreEqual(-settings.WaterDrop, settings.Curve().Evaluate(0.2f), 1e-5f, "the shore comes down to the water");

            // The sea bed lies just under the water, as Night City's does. Two units
            // down, seen from above, the water looked like a canyon.
            settings.lowestDepth = 2f;
            Assert.AreEqual(-0.08f, settings.GroundLift - settings.BedDrop, 1e-6f, "the sea bed, whatever the lowest depth says: that is for dry maps");
            Assert.AreEqual(-settings.BedDrop, settings.Curve().Evaluate(0.1f), 1e-5f);
        }

        [Test]
        public void ADryMap_LeavesTheLandWhereBuildingsStand()
        {
            var settings = new LandscapeSettings { waterPlane = false, lowestDepth = 3f };

            Assert.AreEqual(0f, settings.GroundLift, "with no water to stand in, sinking the buildings gains nothing");
            Assert.AreEqual(3f, settings.BedDrop, "and its valleys are as deep as the lowest depth says");
        }

        // The game never scales a landscape, it only widens the build area. So the mod
        // holds one map, sized against the build area of the map size it is made for.
        // By default that is 400%, the way the Workshop landscapes are built: at
        // smaller sizes the player builds on the middle of it.
        [Test]
        public void Defaults_MakeTheMapFillThe400PercentBuildArea()
        {
            var settings = new LandscapeSettings();

            Assert.AreEqual(400, settings.mapSize);
            Assert.AreEqual(1f, settings.buildAreaShare);
            Assert.AreEqual(100f, settings.WorldSize);
        }

        [TestCase(400, 1f, 100f)]
        [TestCase(400, 0.5f, 200f)]
        [TestCase(200, 1f, 70.7107f)]
        [TestCase(100, 1f, 50f)]
        [TestCase(100, 0.25f, 200f)]
        [TestCase(400, 3f, 100f, Description = "the build area cannot cover more than the whole map")]
        public void WorldSize_FitsTheBuildAreaOfTheChosenMapSizeIntoItsShareOfTheMap(int mapSize, float share, float expected)
        {
            Assert.AreEqual(expected, new LandscapeSettings { mapSize = mapSize, buildAreaShare = share }.WorldSize, 1e-3f);
        }

        [Test]
        public void WorldSize_WithAShareOfNothing_IsStillFinite()
        {
            Assert.That(new LandscapeSettings { buildAreaShare = 0f }.WorldSize, Is.InRange(100f, 100000f));
        }

        // The game's flag is all or nothing: a map made for 200% cannot refuse 400%.
        [TestCase(100, false)]
        [TestCase(200, true)]
        [TestCase(400, true)]
        public void AllowsLargerMapSizes_UnlessTheMapOnlyFillsThe100PercentArea(int mapSize, bool expected)
        {
            Assert.AreEqual(expected, new LandscapeSettings { mapSize = mapSize }.AllowsLargerMapSizes);
        }

        [Test]
        public void Problems_MapSizeTheGameDoesNotOffer()
        {
            LandscapeSettings settings = Settings();
            settings.mapSize = 300;

            StringAssert.Contains("Map size", string.Join(" ", settings.Problems(Square)));
        }

        // The percentage is the build area's area: seen in game, 200% is about 1.4 times as wide and 400% twice.
        [TestCase(100, 1f)]
        [TestCase(200, 1.41421f)]
        [TestCase(400, 2f)]
        public void BuildAreaScale_IsHowMuchWiderTheBuildAreaGets(int percent, float expected)
        {
            Assert.AreEqual(expected, LandscapeSettings.BuildAreaScale(percent), 1e-4f);
        }

        // A map with water is one landscape, with our own water. The same map with
        // normal water is added only when it is asked for, second and under a longer
        // name. A dry map is a single landscape either way.
        [Test]
        public void Landscapes_AreOneWithOurOwnWater_PlusOneWithNormalWaterOnRequest_OrASingleDryOne()
        {
            var settings = new LandscapeSettings { landscapeName = "Miami" };

            Assert.AreEqual(new[] { WaterKind.Custom }, settings.Landscapes());
            Assert.AreEqual("Miami", settings.DisplayNameFor(WaterKind.Custom));

            settings.normalWater = true;
            Assert.AreEqual(new[] { WaterKind.Custom, WaterKind.Normal }, settings.Landscapes());
            Assert.AreEqual("Miami", settings.DisplayNameFor(WaterKind.Custom));
            Assert.AreEqual("Miami (normal water)", settings.DisplayNameFor(WaterKind.Normal));

            settings.waterPlane = false;
            Assert.AreEqual(new[] { WaterKind.Normal }, settings.Landscapes(), "asked for or not, a dry map has no water of either kind");
            Assert.AreEqual("Miami", settings.DisplayNameFor(WaterKind.Normal));
        }

        [Test]
        public void WriteThenRead_BringsTheSettingsBack()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dystopographica-settings-" + System.Guid.NewGuid().ToString("N") + ".json");
            try
            {
                LandscapeSettings settings = Settings();
                settings.buildAreaShare = 0.3f;

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
        public void Curve_PutsTheWaterCursorAtTheWaterSurface_OnlyWhenTheMapHasWater()
        {
            LandscapeSettings settings = Settings();
            settings.knee = 0f;
            settings.lowestDepth = 2f;

            Assert.AreEqual(-0.062f, settings.Curve().Evaluate(settings.water), 1e-5f, "the water lies 0.062 under the land");

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

        // The water and the sea bed lie at fixed levels, so nothing entered can put the
        // sea bed above the water. The lowest depth only shapes a dry map, and any will do.
        [Test]
        public void Problems_NoLowestDepthIsOne()
        {
            LandscapeSettings settings = Settings();
            settings.lowestDepth = 0f;
            Assert.IsEmpty(settings.Problems(Square));

            settings.waterPlane = false;
            Assert.IsEmpty(settings.Problems(Square));
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
        public void For_AHeightmap_IsTheSettingsThemselves()
        {
            LandscapeSettings settings = Settings();

            Assert.AreSame(settings, settings.For(Square));
            Assert.AreSame(settings, settings.For(null));
        }

        // A painted map says for itself what is water and what is plane.
        [Test]
        public void For_APaintedMap_PutsTheCursorsAtThePaintedLevels()
        {
            LandscapeSettings settings = Settings();
            settings.water = 0.05f;
            settings.planeStart = 0.1f;
            settings.planeEnd = 0.9f;
            settings.peakHeight = 33f;
            var painted = new Heightmap(1, 1, new[] { 0.5f }, painted: true, paintedWater: true);

            LandscapeSettings used = settings.For(painted);

            Assert.AreEqual(PaintedMap.Water, used.water);
            Assert.AreEqual(PaintedMap.PlaneStart, used.planeStart);
            Assert.AreEqual(PaintedMap.PlaneEnd, used.planeEnd);
            Assert.AreEqual(33f, used.peakHeight, "everything else is kept");
            Assert.AreEqual(0.1f, settings.planeStart, "and the settings themselves are untouched, for the next heightmap");
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void For_APaintedMap_HasWaterWhenSomethingIsPaintedBlue(bool paintedWater, bool waterSwitch)
        {
            LandscapeSettings settings = Settings();
            settings.waterPlane = waterSwitch;
            var painted = new Heightmap(1, 1, new[] { 0.5f }, painted: true, paintedWater: paintedWater);

            Assert.AreEqual(paintedWater, settings.For(painted).waterPlane);
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
