using NUnit.Framework;
using UnityEngine;

namespace Dystopographica.Tests
{
    public class PreviewTests
    {
        const float Water = 0.2f;
        const float PlaneStart = 0.4f;
        const float PlaneEnd = 0.6f;

        static bool IsBlue(Color32 c) => c.b > c.r && c.b > c.g;
        static bool IsGreen(Color32 c) => c.g > c.r && c.g > c.b;
        static bool IsGrey(Color32 c) => c.r == c.g && c.g == c.b;

        [Test]
        public void Paint_ColoursWaterBlue_PlaneGreen_AndTheRestGrey()
        {
            var map = new Heightmap(5, 1, new[] { 0.1f, 0.3f, 0.5f, 0.6f, 0.9f });

            Color32[] pixels = Preview.Paint(map, Water, PlaneStart, PlaneEnd);

            Assert.IsTrue(IsBlue(pixels[0]), "below the water cursor");
            Assert.IsTrue(IsGrey(pixels[1]), "shore, between water and plane");
            Assert.IsTrue(IsGreen(pixels[2]), "inside the band");
            Assert.IsTrue(IsGreen(pixels[3]), "on the band's edge");
            Assert.IsTrue(IsGrey(pixels[4]), "hills");
        }

        // A filled speck simply turning green is easy to miss among all the other green.
        // Given the map as it was before the speck filter, the preview marks what changed.
        [Test]
        public void Paint_MarksWhatTheSpeckFilterFilled_InABrighterGreen()
        {
            var before = new Heightmap(3, 1, new[] { 0.5f, 0.1f, 0.5f });
            Heightmap after = Specks.FillPlane(new Heightmap(3, 3, new[] { 0.5f, 0.5f, 0.5f, 0.5f, 0.1f, 0.5f, 0.5f, 0.5f, 0.5f }), PlaneStart, PlaneEnd, 2);
            var filledRow = new Heightmap(3, 1, new[] { after[0, 1], after[1, 1], after[2, 1] });

            Color32[] marked = Preview.Paint(filledRow, Water, PlaneStart, PlaneEnd, before);
            Color32[] unmarked = Preview.Paint(filledRow, Water, PlaneStart, PlaneEnd);

            Assert.IsTrue(IsGreen(marked[1]), "still plane");
            Assert.Greater(marked[1].r + marked[1].g + marked[1].b, marked[0].r + marked[0].g + marked[0].b + 100, "but clearly brighter than plane that was always there");
            Assert.AreEqual(unmarked[0], marked[0], "untouched pixels look the same either way");
            Assert.IsTrue(IsGreen(unmarked[1]) && unmarked[1].r < marked[1].r, "and nothing is marked unless asked");
        }

        [Test]
        public void Paint_HigherGroundIsLighter()
        {
            var map = new Heightmap(2, 1, new[] { 0.7f, 0.9f });

            Color32[] pixels = Preview.Paint(map, Water, PlaneStart, PlaneEnd);

            Assert.Less(pixels[0].r, pixels[1].r);
        }

        [Test]
        public void Paint_PutsTheTopImageRowLast_AsUnityTexturesExpect()
        {
            var map = new Heightmap(1, 2, new[] { 0.1f, 0.9f });

            Color32[] pixels = Preview.Paint(map, Water, PlaneStart, PlaneEnd);

            Assert.IsTrue(IsGrey(pixels[0]));
            Assert.IsTrue(IsBlue(pixels[1]));
        }

        // Each pixel's red channel holds its row and its green channel its column.
        static Color32[] Grid(int width, int height)
        {
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32((byte)(i / width), (byte)(i % width), 0, 255);
            return pixels;
        }

        [Test]
        public void Crop_OfATallerPicture_KeepsTheMiddleRows()
        {
            Color32[] result = Preview.Crop(Grid(2, 4), 2, 4, 2, 2);

            Assert.AreEqual(4, result.Length);
            Assert.AreEqual(new[] { 1, 1, 2, 2 }, new[] { (int)result[0].r, result[1].r, result[2].r, result[3].r });
            Assert.AreEqual(new[] { 0, 1, 0, 1 }, new[] { (int)result[0].g, result[1].g, result[2].g, result[3].g });
        }

        [Test]
        public void Crop_ToALargerSize_ScalesUpToFillIt()
        {
            Color32[] result = Preview.Crop(Grid(4, 4), 4, 4, 8, 4);

            Assert.AreEqual(32, result.Length);
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    Assert.AreEqual(1 + y / 2, result[y * 8 + x].r, $"row of {x}, {y}");
                    Assert.AreEqual(x / 2, result[y * 8 + x].g, $"column of {x}, {y}");
                }
            }
        }

        // A map can be made with its water cursor under everything, so that a river in
        // the heightmap comes out as dry ground a hair below the city. The window uses
        // this to say so.
        [Test]
        public void WaterFraction_IsTheShareOfTheMapBelowTheWaterCursor()
        {
            var map = new Heightmap(4, 1, new[] { 0.1f, 0.3f, 0.5f, 0.9f });

            Assert.AreEqual(0.25f, Preview.WaterFraction(map, 0.2f));
            Assert.AreEqual(0.5f, Preview.WaterFraction(map, 0.4f));
            Assert.AreEqual(0f, Preview.WaterFraction(map, 0.1f), "a pixel exactly at the cursor is the shoreline, not water");
            Assert.AreEqual(0f, Preview.WaterFraction(map, 0f));
        }

        [Test]
        public void PlaneFraction_CountsBandPixelsInsideTheCentredSquare()
        {
            var map = new Heightmap(4, 4, new[]
            {
                0.9f, 0.9f, 0.9f, 0.9f,
                0.9f, 0.5f, 0.5f, 0.9f,
                0.9f, 0.5f, 0.9f, 0.9f,
                0.9f, 0.9f, 0.9f, 0.9f,
            });

            Assert.AreEqual(0.75f, Preview.PlaneFraction(map, PlaneStart, PlaneEnd, 0.5f), 1e-6f);
            Assert.AreEqual(3 / 16f, Preview.PlaneFraction(map, PlaneStart, PlaneEnd, 1f), 1e-6f);
            Assert.AreEqual(3 / 16f, Preview.PlaneFraction(map, PlaneStart, PlaneEnd, 4f), 1e-6f);
        }
    }
}
