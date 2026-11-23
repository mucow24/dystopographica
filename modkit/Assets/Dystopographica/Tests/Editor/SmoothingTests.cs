using System.Linq;
using NUnit.Framework;

namespace Dystopographica.Tests
{
    public class SmoothingTests
    {
        [Test]
        public void Gaussian_ZeroSigma_ReturnsTheSameMap()
        {
            var map = new Heightmap(2, 1, new[] { 0f, 1f });

            Assert.AreSame(map, Smoothing.Gaussian(map, 0f));
        }

        [Test]
        public void Gaussian_FlatMap_StaysFlatRightToTheEdges()
        {
            var map = new Heightmap(5, 4, Enumerable.Repeat(0.3f, 20).ToArray());

            Heightmap result = Smoothing.Gaussian(map, 2f);

            foreach (float value in result.Values) Assert.AreEqual(0.3f, value, 1e-6f);
        }

        [Test]
        public void Gaussian_SinglePeak_SpreadsEvenlyAndKeepsItsTotal()
        {
            var values = new float[21 * 21];
            values[10 * 21 + 10] = 1f;

            Heightmap result = Smoothing.Gaussian(new Heightmap(21, 21, values), 1.5f);

            Assert.AreEqual(1f, result.Values.Sum(), 1e-4f);
            Assert.AreEqual(result.Max, result[10, 10]);
            Assert.Less(result[10, 10], 0.5f);
            Assert.Greater(result[11, 10], 0f);
            Assert.AreEqual(result[8, 10], result[12, 10], 1e-7f);
            Assert.AreEqual(result[10, 8], result[12, 10], 1e-7f);
        }
    }
}
