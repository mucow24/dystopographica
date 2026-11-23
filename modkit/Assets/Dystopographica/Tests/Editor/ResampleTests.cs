using NUnit.Framework;

namespace Dystopographica.Tests
{
    public class ResampleTests
    {
        [Test]
        public void Bilinear_SameSize_ChangesNothing()
        {
            var map = new Heightmap(2, 2, new[] { 0.1f, 0.2f, 0.3f, 0.4f });

            Assert.AreEqual(map.Values, Resample.Bilinear(map, 2).Values);
        }

        [Test]
        public void Bilinear_Enlarging_InterpolatesBetweenPixels()
        {
            var ramp = new Heightmap(3, 3, new[] { 0f, 0.5f, 1f, 0f, 0.5f, 1f, 0f, 0.5f, 1f });

            Heightmap result = Resample.Bilinear(ramp, 5);

            Assert.AreEqual(5, result.Width);
            Assert.AreEqual(5, result.Height);
            for (int y = 0; y < 5; y++)
                for (int x = 0; x < 5; x++)
                    Assert.AreEqual(x * 0.25f, result[x, y], 1e-6f);
        }

        [Test]
        public void Bilinear_Shrinking_KeepsTheCorners()
        {
            var values = new float[25];
            values[0] = 0.1f;
            values[4] = 0.2f;
            values[20] = 0.3f;
            values[24] = 0.4f;

            Heightmap result = Resample.Bilinear(new Heightmap(5, 5, values), 3);

            Assert.AreEqual(0.1f, result[0, 0], 1e-6f);
            Assert.AreEqual(0.2f, result[2, 0], 1e-6f);
            Assert.AreEqual(0.3f, result[0, 2], 1e-6f);
            Assert.AreEqual(0.4f, result[2, 2], 1e-6f);
        }

        [Test]
        public void Shrink_AveragesEachBlock()
        {
            var map = new Heightmap(4, 2, new[]
            {
                0.0f, 0.2f, 1.0f, 1.0f,
                0.4f, 0.6f, 0.0f, 0.0f,
            });

            Heightmap result = Resample.Shrink(map, 2);

            Assert.AreEqual(2, result.Width);
            Assert.AreEqual(1, result.Height);
            Assert.AreEqual(0.3f, result[0, 0], 1e-6f);
            Assert.AreEqual(0.5f, result[1, 0], 1e-6f);
        }

        [Test]
        public void Shrink_DropsPixelsThatDoNotFillABlock()
        {
            Heightmap result = Resample.Shrink(new Heightmap(5, 5, new float[25]), 2);

            Assert.AreEqual(2, result.Width);
            Assert.AreEqual(2, result.Height);
        }

        [Test]
        public void Shrink_ByOne_ReturnsTheSameMap()
        {
            var map = new Heightmap(1, 1, new[] { 0.5f });

            Assert.AreSame(map, Resample.Shrink(map, 1));
        }

        [TestCase(1280, 1025)]
        [TestCase(2000, 2049)]
        [TestCase(2049, 2049)]
        [TestCase(513, 513)]
        [TestCase(10, 33)]
        [TestCase(8129, 4097)]
        public void TerrainResolution_IsTheNearestSizeUnityAccepts(int imageSize, int expected)
        {
            Assert.AreEqual(expected, Resample.TerrainResolution(imageSize));
        }
    }
}
