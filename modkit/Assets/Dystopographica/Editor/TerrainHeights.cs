using UnityEngine;

namespace Dystopographica
{
    /// <summary>What a Unity Terrain needs to reproduce a height curve.</summary>
    public sealed class TerrainShape
    {
        /// <summary>Heights from 0 to 1 for TerrainData.SetHeights, indexed [z, x] from the south-west corner.</summary>
        public float[,] Heights;

        /// <summary>The terrain's height range in world units (TerrainData.size.y).</summary>
        public float SizeY;

        /// <summary>The world y of the terrain object, which is where height 0 sits.</summary>
        public float BaseY;
    }

    public static class TerrainHeights
    {
        /// <summary>Unity stores each terrain height as a whole number of steps from 0 to this.</summary>
        public const int Steps = 32766;

        /// <summary>
        /// Runs a normalized heightmap through the curve. The city plane is put on a whole
        /// step and the terrain is shifted to match, so the plane sits on world y = 0
        /// exactly and not just to the nearest step.
        /// </summary>
        public static TerrainShape Build(Heightmap normalized, HeightCurve curve, float peakHeight, float lowestDepth)
        {
            float sizeY = Mathf.Max(peakHeight + lowestDepth, 1f);
            float plane = Mathf.Round(lowestDepth / sizeY * Steps) / Steps;

            int width = normalized.Width, height = normalized.Height;
            var heights = new float[height, width];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float worldY = curve.Evaluate(normalized.Values[y * width + x]);
                    // Image rows run north to south, terrain rows south to north.
                    heights[height - 1 - y, x] = worldY == 0f ? plane : Mathf.Clamp01(plane + worldY / sizeY);
                }
            }
            return new TerrainShape { Heights = heights, SizeY = sizeY, BaseY = -plane * sizeY };
        }
    }
}
