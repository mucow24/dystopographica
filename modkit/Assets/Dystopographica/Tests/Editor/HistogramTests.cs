using NUnit.Framework;

namespace Dystopographica.Tests
{
    public class HistogramTests
    {
        [Test]
        public void Compute_CountsEachPixelInItsBin()
        {
            var map = new Heightmap(6, 1, new[] { 0f, 0.24f, 0.26f, 0.5f, 0.99f, 1f });

            Assert.AreEqual(new[] { 2, 1, 1, 2 }, Histogram.Compute(map, 4));
        }

        [Test]
        public void Compute_ValuesOutsideZeroOne_GoInTheEndBins()
        {
            var map = new Heightmap(2, 1, new[] { -0.5f, 1.5f });

            Assert.AreEqual(new[] { 1, 0, 1 }, Histogram.Compute(map, 3));
        }
    }
}
