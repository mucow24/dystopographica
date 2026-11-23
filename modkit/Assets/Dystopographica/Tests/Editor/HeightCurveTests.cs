using NUnit.Framework;

namespace Dystopographica.Tests
{
    public class HeightCurveTests
    {
        const float Peak = 20f;
        const float Depth = 2f;

        // The city plane is the band from 0.4 to 0.6.
        static HeightCurve Curve(float knee) => new HeightCurve(0.4f, 0.6f, Peak, Depth, knee);

        [TestCase(0.4f)]
        [TestCase(0.5f)]
        [TestCase(0.6f)]
        public void Band_IsFlatAtZero(float value)
        {
            Assert.AreEqual(0f, Curve(0f).Evaluate(value));
            Assert.AreEqual(0f, Curve(0.05f).Evaluate(value));
        }

        [TestCase(0f)]
        [TestCase(0.05f)]
        [TestCase(0.9f)]
        public void Extremes_LandOnPeakHeightAndLowestDepth(float knee)
        {
            Assert.AreEqual(Peak, Curve(knee).Evaluate(1f), 1e-4f);
            Assert.AreEqual(-Depth, Curve(knee).Evaluate(0f), 1e-4f);
        }

        [Test]
        public void WithoutKnee_EachSideIsAStraightLine()
        {
            Assert.AreEqual(Peak / 2, Curve(0f).Evaluate(0.8f), 1e-4f);
            Assert.AreEqual(-Depth / 2, Curve(0f).Evaluate(0.2f), 1e-4f);
        }

        [Test]
        public void WithKnee_TerrainLeavesTheBandGently()
        {
            float sharp = Curve(0f).Evaluate(0.61f);
            float soft = Curve(0.1f).Evaluate(0.61f);

            Assert.Greater(soft, 0f);
            Assert.Less(soft, sharp / 5);

            sharp = Curve(0f).Evaluate(0.39f);
            soft = Curve(0.1f).Evaluate(0.39f);

            Assert.Less(soft, 0f);
            Assert.Greater(soft, sharp / 5);
        }

        [TestCase(0f)]
        [TestCase(0.1f)]
        [TestCase(0.9f)]
        public void Curve_NeverDecreasesAndHasNoJumps(float knee)
        {
            HeightCurve curve = Curve(knee);
            float previous = curve.Evaluate(0f);

            for (int i = 1; i <= 1000; i++)
            {
                float y = curve.Evaluate(i / 1000f);
                Assert.GreaterOrEqual(y, previous);
                Assert.Less(y - previous, 0.2f);
                previous = y;
            }
        }

        [Test]
        public void BandTouchingAnEnd_LeavesThatSideFlat()
        {
            Assert.AreEqual(0f, new HeightCurve(0.4f, 1f, Peak, Depth, 0.05f).Evaluate(1f));
            Assert.AreEqual(0f, new HeightCurve(0f, 0.6f, Peak, Depth, 0.05f).Evaluate(0f));
        }

        // The same curve on a map with water: the water cursor is at 0.2 and the water
        // surface is half a unit below the city plane.
        const float WaterDepth = 0.5f;

        static HeightCurve Wet(float knee, float water = 0.2f) => new HeightCurve(0.4f, 0.6f, Peak, Depth, knee, water, WaterDepth);

        [TestCase(0f)]
        [TestCase(0.05f)]
        [TestCase(0.9f)]
        public void WithWater_TerrainAtTheWaterCursorIsAtTheWaterSurface(float knee)
        {
            Assert.AreEqual(-WaterDepth, Wet(knee).Evaluate(0.2f), 1e-5f);
        }

        [Test]
        public void WithWater_TheShoreClimbsFromTheWaterToThePlane()
        {
            Assert.AreEqual(-WaterDepth / 2, Wet(0f).Evaluate(0.3f), 1e-5f);
        }

        // The game marks ground that is only a little below the city with red crosses, as
        // land that cannot be built on. A sea bed that followed the heightmap got them
        // wherever the water was shallow. The water is opaque, so the bed's shape is
        // never seen: everything under water goes straight to the full depth.
        [TestCase(0f)]
        [TestCase(0.1f)]
        [TestCase(0.199f)]
        public void WithWater_ShallowWaterIsAsDeepAsDeepWater(float value)
        {
            Assert.AreEqual(-Depth, Wet(0f).Evaluate(value));
            Assert.AreEqual(-Depth, Wet(0.05f).Evaluate(value));
        }

        [Test]
        public void WithWater_BandAndHillsAreUnchanged()
        {
            Assert.AreEqual(0f, Wet(0.05f).Evaluate(0.5f));
            Assert.AreEqual(Curve(0.05f).Evaluate(0.8f), Wet(0.05f).Evaluate(0.8f));
        }

        [Test]
        public void WithWater_CursorsTogether_MakeAWallFromThePlaneToTheBed()
        {
            HeightCurve curve = Wet(0.05f, water: 0.4f);

            Assert.AreEqual(0f, curve.Evaluate(0.4f));
            Assert.AreEqual(-Depth, curve.Evaluate(0.399f));
        }

        [TestCase(0f)]
        [TestCase(0.1f)]
        public void WithWater_CurveNeverDecreases(float knee)
        {
            HeightCurve curve = Wet(knee);
            float previous = curve.Evaluate(0f);

            for (int i = 1; i <= 1000; i++)
            {
                float y = curve.Evaluate(i / 1000f);
                Assert.GreaterOrEqual(y, previous);
                previous = y;
            }
        }

        [Test]
        public void WithWater_LowestDepthAboveTheWater_StillKeepsTheBedUnderIt()
        {
            var curve = new HeightCurve(0.4f, 0.6f, Peak, 0.1f, 0f, 0.2f, WaterDepth);

            Assert.LessOrEqual(curve.Evaluate(0f), -WaterDepth);
        }

        // The settings that first showed the problem: a coastline, with the water cursor
        // right under the plane. The sea came out a hair below the city and looked flat.
        [Test]
        public void WithWater_ACursorRightUnderThePlane_StillPutsTheSeaWellBelowIt()
        {
            var curve = new HeightCurve(0.588f, 1f, 20f, 50f, 0.25f, 0.587f, 0.7f);

            Assert.AreEqual(0f, curve.Evaluate(0.588f));
            Assert.LessOrEqual(curve.Evaluate(0.586f), -0.7f);
        }

        [Test]
        public void ValuesOutsideZeroOne_AreClamped()
        {
            Assert.AreEqual(Peak, Curve(0f).Evaluate(1.5f), 1e-4f);
            Assert.AreEqual(-Depth, Curve(0f).Evaluate(-1f), 1e-4f);
        }
    }
}
