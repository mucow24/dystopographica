using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Dystopographica.Tests
{
    public class RipplesTests
    {
        // A normal map stores a direction in each pixel: 128 is zero, 255 is one.
        static Vector3 Direction(Color32 pixel) => new Vector3(pixel.r, pixel.g, pixel.b) / 127.5f - Vector3.one;

        [Test]
        public void NormalMap_HasOnePixelPerTexel()
        {
            Assert.AreEqual(64 * 64, Ripples.NormalMap(64).Length);
        }

        [Test]
        public void NormalMap_EveryPixelIsAUnitDirectionLeaningUpward()
        {
            foreach (Color32 pixel in Ripples.NormalMap(64))
            {
                Vector3 direction = Direction(pixel);
                Assert.AreEqual(1f, direction.magnitude, 0.03f);
                Assert.Greater(direction.z, 0.6f, "a ripple, not a breaking wave");
                Assert.AreEqual(255, pixel.a, "opaque, so every way of unpacking a normal map reads it the same");
            }
        }

        [Test]
        public void NormalMap_HasRipplesInIt_LeaningEvenlyAllWays()
        {
            Vector3[] directions = Ripples.NormalMap(64).Select(Direction).ToArray();

            Assert.Greater(directions.Max(d => Mathf.Abs(d.x)), 0.15f);
            Assert.Greater(directions.Max(d => Mathf.Abs(d.y)), 0.15f);
            Assert.AreEqual(0f, directions.Average(d => d.x), 0.01f);
            Assert.AreEqual(0f, directions.Average(d => d.y), 0.01f);
        }

        // The texture is laid edge to edge across the water, so it has to repeat without a seam.
        [TestCase(0.1f, 0.2f)]
        [TestCase(0.77f, 0.31f)]
        [TestCase(0f, 0.999f)]
        public void Height_RepeatsFromOneTileToTheNext(float u, float v)
        {
            Assert.AreEqual(Ripples.Height(u, v), Ripples.Height(u + 1f, v), 1e-4f);
            Assert.AreEqual(Ripples.Height(u, v), Ripples.Height(u, v + 1f), 1e-4f);
            Assert.AreNotEqual(Ripples.Height(u, v), Ripples.Height(u + 0.37f, v + 0.21f));
        }

        [Test]
        public void NormalMap_IsTheSameEveryTime()
        {
            Assert.AreEqual(Ripples.NormalMap(16), Ripples.NormalMap(16));
        }
    }
}
