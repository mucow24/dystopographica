using NUnit.Framework;

namespace Dystopographica.Tests
{
    public class SpecksTests
    {
        // The city plane is the band from 0.4 to 0.6. In the maps below:
        const float P = 0.5f; // plane
        const float W = 0.1f; // water, far below the band
        const float S = 0.3f; // shore, just below the band
        const float H = 0.9f; // hill, above the band

        static Heightmap Map(int width, params float[] values) => new Heightmap(width, values.Length / width, values);

        static float[] Filled(Heightmap map, int limit) => Specks.FillPlane(map, 0.4f, 0.6f, limit).Values;

        [Test]
        public void AHoleInThePlane_SmallerThanTheLimit_BecomesPlane()
        {
            Heightmap map = Map(3,
                P, P, P,
                P, W, P,
                P, P, P);

            Assert.AreEqual(new[] { P, P, P, P, 0.4f, P, P, P, P }, Filled(map, 2));
        }

        [Test]
        public void ABumpOnThePlane_IsPulledDownToTheTopOfTheBand()
        {
            Heightmap map = Map(3,
                P, P, P,
                P, H, P,
                P, P, P);

            Assert.AreEqual(0.6f, Filled(map, 2)[4]);
        }

        [Test]
        public void ASpeckAsLargeAsTheLimit_Stays()
        {
            Heightmap map = Map(4,
                P, P, P, P,
                P, W, W, P,
                P, P, P, P);

            Assert.AreEqual(map.Values, Filled(map, 2), "two pixels is not smaller than two");
            Assert.AreEqual(new[] { P, P, P, P, P, 0.4f, 0.4f, P, P, P, P, P }, Filled(map, 3));
        }

        [Test]
        public void ASpeckThatReachesTheEdgeOfTheMap_Stays()
        {
            Heightmap map = Map(3,
                P, W, P,
                P, W, P,
                P, P, P);

            Assert.AreEqual(map.Values, Filled(map, 100), "the plane does not go all the way round it");
        }

        // Otherwise a river one pixel wide, running diagonally, would be a row of
        // one-pixel specks and vanish.
        [Test]
        public void PixelsThatTouchOnlyAtACorner_AreOneSpeck()
        {
            Heightmap map = Map(5,
                P, P, P, P, P,
                P, W, P, P, P,
                P, P, W, P, P,
                P, P, P, W, P,
                P, P, P, P, P);

            Assert.AreEqual(map.Values, Filled(map, 3));
            Assert.AreEqual(0.4f, Filled(map, 4)[6]);
            Assert.AreEqual(0.4f, Filled(map, 4)[12]);
            Assert.AreEqual(0.4f, Filled(map, 4)[18]);
        }

        [Test]
        public void ADiagonalLineFromTheEdge_Stays()
        {
            Heightmap map = Map(3,
                W, P, P,
                P, W, P,
                P, P, P);

            Assert.AreEqual(map.Values, Filled(map, 100));
        }

        [Test]
        public void WaterAndTheShoreAroundIt_AreFilledAsOneSpeck()
        {
            Heightmap map = Map(5,
                P, P, P, P, P,
                P, S, S, S, P,
                P, S, W, S, P,
                P, S, S, S, P,
                P, P, P, P, P);

            Assert.AreEqual(map.Values, Filled(map, 9), "nine pixels in all");
            foreach (float value in Filled(map, 10)) Assert.That(value, Is.InRange(0.4f, 0.6f));
        }

        [Test]
        public void EachSpeckIsJudgedOnItsOwn()
        {
            Heightmap map = Map(7,
                P, P, P, P, P, P, P,
                P, W, P, P, H, H, P,
                P, P, P, P, H, H, P,
                P, P, P, P, P, P, P);

            float[] result = Filled(map, 3);

            Assert.AreEqual(0.4f, result[8], "the single pixel goes");
            Assert.AreEqual(H, result[11], "the block of four stays");
        }

        [Test]
        public void PlaneInsideOtherGround_IsLeftAlone()
        {
            Heightmap map = Map(3,
                W, W, W,
                W, P, W,
                W, W, W);

            Assert.AreEqual(map.Values, Filled(map, 100), "only holes in the plane are filled");
        }

        [TestCase(0)]
        [TestCase(1)]
        public void ALimitNothingCanBeSmallerThan_ReturnsTheSameMap(int limit)
        {
            Heightmap map = Map(1, W);

            Assert.AreSame(map, Specks.FillPlane(map, 0.4f, 0.6f, limit));
        }

        [Test]
        public void TheMapItIsGiven_IsNotChanged()
        {
            Heightmap map = Map(3,
                P, P, P,
                P, W, P,
                P, P, P);

            Specks.FillPlane(map, 0.4f, 0.6f, 2);

            Assert.AreEqual(W, map.Values[4]);
        }
    }
}
