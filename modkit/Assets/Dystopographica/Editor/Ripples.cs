using UnityEngine;

namespace Dystopographica
{
    /// <summary>
    /// The ripple pattern of the custom water, made from scratch so the tool needs no
    /// texture files of its own. It is a sum of sine waves. Each wave fits a whole
    /// number of times across the tile in both directions, so the pattern repeats from
    /// one tile to the next without a seam.
    /// </summary>
    public static class Ripples
    {
        // Waves per tile along u and along v, height, and where in its cycle each starts.
        static readonly (int u, int v, float height, float phase)[] Waves =
        {
            (3, 1, 1.00f, 0.0f), (-2, 4, 0.80f, 1.7f), (5, -3, 0.55f, 4.1f), (1, 6, 0.45f, 2.9f),
            (-7, 2, 0.30f, 5.3f), (4, 7, 0.25f, 0.8f), (8, -5, 0.18f, 3.6f),
        };

        /// <summary>How far the surface leans from level on average, as a slope.</summary>
        const float TypicalLean = 0.3f;

        /// <summary>The height of the water at a point. One tile runs from 0 to 1 in u and in v.</summary>
        public static float Height(float u, float v)
        {
            float height = 0f;
            foreach (var wave in Waves) height += wave.height * Mathf.Sin(2 * Mathf.PI * (wave.u * u + wave.v * v) + wave.phase);
            return height;
        }

        /// <summary>
        /// The pattern as a normal map: each pixel holds the direction the surface faces
        /// there, the usual way, with 128 for zero and blue pointing up.
        /// </summary>
        public static Color32[] NormalMap(int size)
        {
            // The steepness of the summed waves, on average, so it can be scaled to the typical lean.
            float meanSquareSlope = 0f;
            foreach (var wave in Waves)
                meanSquareSlope += wave.height * wave.height * (wave.u * wave.u + wave.v * wave.v) * 2 * Mathf.PI * Mathf.PI;
            float scale = TypicalLean / Mathf.Sqrt(meanSquareSlope);

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                    float slopeU = 0f, slopeV = 0f;
                    foreach (var wave in Waves)
                    {
                        float slope = wave.height * 2 * Mathf.PI * Mathf.Cos(2 * Mathf.PI * (wave.u * u + wave.v * v) + wave.phase);
                        slopeU += slope * wave.u;
                        slopeV += slope * wave.v;
                    }
                    Vector3 normal = new Vector3(-slopeU * scale, -slopeV * scale, 1f).normalized;
                    pixels[y * size + x] = new Color32(Channel(normal.x), Channel(normal.y), Channel(normal.z), 255);
                }
            }
            return pixels;
        }

        static byte Channel(float value) => (byte)Mathf.RoundToInt((value * 0.5f + 0.5f) * 255f);
    }
}
