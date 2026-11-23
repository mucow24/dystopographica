using System;
using NUnit.Framework;

namespace Dystopographica.Tests
{
    public class HeightmapTests
    {
        [Test]
        public void MinAndMax_AreTheExtremes()
        {
            var map = new Heightmap(2, 2, new[] { 0.3f, 0.9f, 0.1f, 0.5f });

            Assert.AreEqual(0.1f, map.Min);
            Assert.AreEqual(0.9f, map.Max);
        }

        [Test]
        public void Indexer_ReadsColumnThenRow()
        {
            var map = new Heightmap(2, 2, new[] { 0.3f, 0.9f, 0.1f, 0.5f });

            Assert.AreEqual(0.9f, map[1, 0]);
            Assert.AreEqual(0.1f, map[0, 1]);
        }

        [Test]
        public void Constructor_RejectsTheWrongNumberOfValues()
        {
            Assert.Throws<ArgumentException>(() => new Heightmap(2, 2, new float[3]));
        }

        [Test]
        public void Normalized_StretchesTheRangeToZeroOne()
        {
            var map = new Heightmap(3, 1, new[] { 0.2f, 0.3f, 0.6f }).Normalized();

            Assert.AreEqual(0f, map.Values[0], 1e-6f);
            Assert.AreEqual(0.25f, map.Values[1], 1e-6f);
            Assert.AreEqual(1f, map.Values[2], 1e-6f);
        }

        [Test]
        public void Normalized_FlatImage_IsAllZero()
        {
            var map = new Heightmap(2, 1, new[] { 0.4f, 0.4f }).Normalized();

            Assert.AreEqual(new[] { 0f, 0f }, map.Values);
        }
    }
}
