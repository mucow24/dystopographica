using NUnit.Framework;

namespace Dystopographica.Tests
{
    public class PaintedMapTests
    {
        const float Blue = 0, Green = 1, MidGrey = 2;

        static LandscapeSettings Settings() => new LandscapeSettings
        {
            water = 0.3f, planeStart = 0.5f, planeEnd = 0.7f, peakHeight = 20f, lowestDepth = 4f, knee = 0f, smoothing = 0f,
        };

        static Heightmap Row(params float[] values) => new Heightmap(values.Length, 1, values);

        static float Channel(ushort[] picture, int pixel, int channel) => picture[pixel * 3 + channel] / 65535f;

        static bool IsGrey(ushort[] picture, int pixel) =>
            picture[pixel * 3] == picture[pixel * 3 + 1] && picture[pixel * 3] == picture[pixel * 3 + 2];

        /// <summary>A picture one row high, read the way a file is: each pixel is three numbers, red, green and blue.</summary>
        static Heightmap Read(params float[] rgb)
        {
            int width = rgb.Length / 3;
            float[] red = new float[width], green = new float[width], blue = new float[width];
            for (int i = 0; i < width; i++)
            {
                red[i] = rgb[i * 3];
                green[i] = rgb[i * 3 + 1];
                blue[i] = rgb[i * 3 + 2];
            }
            return PaintedMap.Read(width, 1, red, green, blue);
        }

        /// <summary>Writes a map as a painted picture and reads the picture back.</summary>
        static Heightmap ThereAndBack(Heightmap map, LandscapeSettings settings)
        {
            ushort[] picture = PaintedMap.Paint(map, settings);
            float[] red = new float[map.Values.Length], green = new float[map.Values.Length], blue = new float[map.Values.Length];
            for (int i = 0; i < red.Length; i++)
            {
                red[i] = Channel(picture, i, 0);
                green[i] = Channel(picture, i, 1);
                blue[i] = Channel(picture, i, 2);
            }
            return PaintedMap.Read(map.Width, map.Height, red, green, blue);
        }

        [Test]
        public void Paint_WaterIsBlueAndThePlaneIsGreen()
        {
            ushort[] picture = PaintedMap.Paint(Row(0.1f, 0.5f, 0.6f, 0.7f), Settings());

            Assert.Greater(Channel(picture, 0, 2), Channel(picture, 0, 0) + 0.3f, "under water: far more blue than red");
            Assert.Greater(Channel(picture, 0, 2), Channel(picture, 0, 1) + 0.3f, "under water: far more blue than green");
            for (int pixel = 1; pixel <= 3; pixel++)
            {
                Assert.Greater(Channel(picture, pixel, 1), Channel(picture, pixel, 0) + 0.3f, $"pixel {pixel}, on the plane: far more green than red");
                Assert.Greater(Channel(picture, pixel, 1), Channel(picture, pixel, 2) + 0.3f, $"pixel {pixel}, on the plane: far more green than blue");
            }
        }

        [Test]
        public void Paint_HillsAreGreyFromMidGreyAtThePlaneToWhiteAtThePeak()
        {
            ushort[] picture = PaintedMap.Paint(Row(0.85f, 1f), Settings());

            Assert.IsTrue(IsGrey(picture, 0));
            Assert.AreEqual(0.75f, Channel(picture, 0, 0), 1e-4f, "halfway up");
            Assert.IsTrue(IsGrey(picture, 1));
            Assert.AreEqual(1f, Channel(picture, 1, 0), "the peak");
        }

        [Test]
        public void Paint_TheShoreIsGreyFromBlackAtTheWaterToMidGreyAtThePlane()
        {
            ushort[] picture = PaintedMap.Paint(Row(0.3f, 0.4f), Settings());

            Assert.IsTrue(IsGrey(picture, 0));
            Assert.AreEqual(0f, Channel(picture, 0, 0), "at the waterline");
            Assert.IsTrue(IsGrey(picture, 1));
            Assert.AreEqual(0.25f, Channel(picture, 1, 0), 1e-4f, "halfway up the shore");
        }

        [Test]
        public void Paint_OnADryMap_NothingIsBlueAndBlackIsTheLowestPoint()
        {
            LandscapeSettings settings = Settings();
            settings.waterPlane = false;

            ushort[] picture = PaintedMap.Paint(Row(0f, 0.25f), settings);

            Assert.IsTrue(IsGrey(picture, 0));
            Assert.AreEqual(0f, Channel(picture, 0, 0));
            Assert.AreEqual(0.25f, Channel(picture, 1, 0), 1e-4f, "halfway down from the plane");
        }

        [Test]
        public void Read_APictureWithNothingBlueOrGreen_IsNotAPaintedMap()
        {
            // Grey, and a faint colour cast such as a scan or a JPEG leaves.
            Assert.IsNull(Read(0.2f, 0.2f, 0.2f, 0.8f, 0.8f, 0.8f, 0.5f, 0.53f, 0.56f));
        }

        [Test]
        public void Read_BlueIsUnderWaterAndGreenIsOnThePlane()
        {
            Heightmap map = Read(0.1f, 0.35f, 0.85f, 0.15f, 0.75f, 0.25f, 0f, 0f, 1f, 0.3f, 1f, 0.3f);

            Assert.IsTrue(map.IsPainted);
            Assert.IsTrue(map.HasPaintedWater);
            Assert.Less(map.Values[0], PaintedMap.Water);
            Assert.That(map.Values[1], Is.InRange(PaintedMap.PlaneStart, PaintedMap.PlaneEnd));
            Assert.Less(map.Values[2], PaintedMap.Water, "any blue will do");
            Assert.That(map.Values[3], Is.InRange(PaintedMap.PlaneStart, PaintedMap.PlaneEnd), "any green will do");
        }

        [Test]
        public void Read_GreyIsHeight_WithMidGreyLevelWithThePlane()
        {
            Heightmap map = Read(0f, 0f, 1f, 1f, 1f, 1f, 0.75f, 0.75f, 0.75f, 0.25f, 0.25f, 0.25f, 0f, 0f, 0f);

            Assert.AreEqual(1f, map.Values[1], "white is the peak");
            Assert.AreEqual((PaintedMap.PlaneEnd + 1f) / 2, map.Values[2], 1e-5f, "light grey is halfway up");
            Assert.AreEqual((PaintedMap.Water + PaintedMap.PlaneStart) / 2, map.Values[3], 1e-5f, "dark grey is halfway down the shore");
            Assert.AreEqual(PaintedMap.Water, map.Values[4], 1e-5f, "black is at the waterline");
        }

        [Test]
        public void Read_WithNothingBlue_IsADryMapAndBlackIsItsLowestPoint()
        {
            Heightmap map = Read(0.15f, 0.75f, 0.25f, 0f, 0f, 0f, 0.25f, 0.25f, 0.25f);

            Assert.IsTrue(map.IsPainted);
            Assert.IsFalse(map.HasPaintedWater);
            Assert.AreEqual(0f, map.Values[1]);
            Assert.AreEqual(PaintedMap.PlaneStart / 2, map.Values[2], 1e-5f);
        }

        // A soft brush leaves pixels that are part paint and part what was under it.
        [Test]
        public void Read_HalfBlueHalfGrey_IsStillWater()
        {
            Heightmap map = Read(0.3f, 0.42f, 0.67f, 0.15f, 0.75f, 0.25f);

            Assert.Less(map.Values[0], PaintedMap.Water);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PaintThenRead_GivesTheSameHeights(bool water)
        {
            LandscapeSettings settings = Settings();
            settings.waterPlane = water;
            var map = new Heightmap(11, 1, new[] { 0f, 0.1f, 0.3f, 0.35f, 0.45f, 0.5f, 0.6f, 0.7f, 0.75f, 0.9f, 1f });

            Heightmap read = ThereAndBack(map, settings);

            HeightCurve before = settings.Curve(), after = settings.For(read).Curve();
            for (int i = 0; i < map.Values.Length; i++)
                Assert.AreEqual(before.Evaluate(map.Values[i]), after.Evaluate(read.Values[i]), 1e-3f, $"pixel {i}, value {map.Values[i]}");
        }
    }
}
