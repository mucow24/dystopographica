using NUnit.Framework;
using UnityEngine;

namespace Dystopographica.Tests
{
    public class TerrainHeightsTests
    {
        const float Peak = 20f;
        const float Depth = 3f;

        // Top row: lowest point, two cells in the band, highest point. Bottom row: all in the band.
        static readonly Heightmap Map = new Heightmap(4, 2, new[]
        {
            0.0f, 0.4f, 0.6f, 1.0f,
            0.5f, 0.5f, 0.5f, 0.5f,
        });

        static TerrainShape Build() => TerrainHeights.Build(Map, new HeightCurve(0.4f, 0.6f, Peak, Depth, 0f), Peak, Depth);

        static float WorldY(TerrainShape shape, int z, int x) => shape.BaseY + shape.Heights[z, x] * shape.SizeY;

        [Test]
        public void SizeY_SpansFromLowestDepthToPeakHeight()
        {
            Assert.AreEqual(Peak + Depth, Build().SizeY);
        }

        [Test]
        public void Heights_AreIndexedSouthToNorth_SoTheTopImageRowComesLast()
        {
            TerrainShape shape = Build();

            Assert.AreEqual(2, shape.Heights.GetLength(0));
            Assert.AreEqual(4, shape.Heights.GetLength(1));
            Assert.Less(WorldY(shape, 1, 0), -1f);
            Assert.Greater(WorldY(shape, 1, 3), 1f);
        }

        [Test]
        public void BandCells_SitOnAWholeTerrainStep_AtWorldZero()
        {
            TerrainShape shape = Build();
            float plane = shape.Heights[0, 0];

            Assert.AreEqual(Mathf.Round(plane * TerrainHeights.Steps), plane * TerrainHeights.Steps, 1e-2f);
            foreach (var cell in new[] { (0, 1), (0, 2), (0, 3), (1, 1), (1, 2) })
            {
                Assert.AreEqual(plane, shape.Heights[cell.Item1, cell.Item2]);
                Assert.AreEqual(0f, WorldY(shape, cell.Item1, cell.Item2), 1e-5f);
            }
        }

        [Test]
        public void Extremes_AreWithinHalfATerrainStepOfWhereTheCurvePutsThem()
        {
            TerrainShape shape = Build();
            float halfStep = shape.SizeY / TerrainHeights.Steps / 2 + 1e-5f;

            Assert.AreEqual(-Depth, WorldY(shape, 1, 0), halfStep);
            Assert.AreEqual(Peak, WorldY(shape, 1, 3), halfStep);
            foreach (float height in shape.Heights) Assert.That(height, Is.InRange(0f, 1f));
        }

        [Test]
        public void FlatTerrain_StillHasAUsableHeightRange()
        {
            TerrainShape shape = TerrainHeights.Build(Map, new HeightCurve(0f, 1f, 0f, 0f, 0f), 0f, 0f);

            Assert.Greater(shape.SizeY, 0f);
            Assert.AreEqual(0f, WorldY(shape, 0, 0), 1e-5f);
        }
    }
}
