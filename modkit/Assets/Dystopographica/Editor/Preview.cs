using UnityEngine;

namespace Dystopographica
{
    /// <summary>The picture of the heightmap shown in the window.</summary>
    public static class Preview
    {
        static readonly Color WaterTint = new Color(0.10f, 0.35f, 0.85f);
        static readonly Color PlaneTint = new Color(0.15f, 0.75f, 0.25f);
        static readonly Color FilledTint = new Color(0.85f, 1f, 0.35f);

        /// <summary>
        /// Colours a normalized map: blue below the water cursor, green inside the city
        /// plane band, grey elsewhere, all lighter with height. The result is in Unity's
        /// texture order, bottom row first. Given the same map as it was before the speck
        /// filter ran, the pixels the filter pulled into the plane are a brighter,
        /// yellower green, so its effect can be seen.
        /// </summary>
        public static Color32[] Paint(Heightmap normalized, float water, float planeStart, float planeEnd, Heightmap beforeFilling = null)
        {
            int width = normalized.Width, height = normalized.Height;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float value = normalized.Values[y * width + x];
                    float shade = 0.12f + 0.8f * Mathf.Clamp01(value);
                    var colour = new Color(shade, shade, shade);
                    if (value < water)
                    {
                        colour = Color.Lerp(colour, WaterTint, 0.65f);
                    }
                    else if (value >= planeStart && value <= planeEnd)
                    {
                        bool filled = beforeFilling != null && beforeFilling.Values[y * width + x] != value;
                        colour = Color.Lerp(colour, filled ? FilledTint : PlaneTint, 0.65f);
                    }
                    pixels[(height - 1 - y) * width + x] = colour;
                }
            }
            return pixels;
        }

        /// <summary>
        /// Scales a picture until it covers outWidth x outHeight and keeps the middle,
        /// the way a thumbnail is cut from a photo.
        /// </summary>
        public static Color32[] Crop(Color32[] source, int width, int height, int outWidth, int outHeight)
        {
            float scale = Mathf.Max(outWidth / (float)width, outHeight / (float)height);
            float left = (width - outWidth / scale) / 2, top = (height - outHeight / scale) / 2;
            var result = new Color32[outWidth * outHeight];
            for (int y = 0; y < outHeight; y++)
            {
                int sourceY = Mathf.Clamp((int)(top + (y + 0.5f) / scale), 0, height - 1);
                for (int x = 0; x < outWidth; x++)
                {
                    int sourceX = Mathf.Clamp((int)(left + (x + 0.5f) / scale), 0, width - 1);
                    result[y * outWidth + x] = source[sourceY * width + sourceX];
                }
            }
            return result;
        }

        /// <summary>How much of the map is under water, from 0 to 1: the share of it below the water cursor.</summary>
        public static float WaterFraction(Heightmap normalized, float water)
        {
            int under = 0;
            foreach (float value in normalized.Values)
                if (value < water) under++;
            return normalized.Values.Length == 0 ? 0f : under / (float)normalized.Values.Length;
        }

        /// <summary>
        /// How much of the centred square is on the city plane, from 0 to 1.
        /// `squareFraction` is the square's edge as a fraction of the map's edge.
        /// </summary>
        public static float PlaneFraction(Heightmap normalized, float planeStart, float planeEnd, float squareFraction)
        {
            float half = Mathf.Clamp01(squareFraction) / 2;
            int width = normalized.Width, height = normalized.Height;
            int inside = 0, onPlane = 0;
            for (int y = 0; y < height; y++)
            {
                if (Mathf.Abs((y + 0.5f) / height - 0.5f) > half) continue;
                for (int x = 0; x < width; x++)
                {
                    if (Mathf.Abs((x + 0.5f) / width - 0.5f) > half) continue;
                    inside++;
                    float value = normalized.Values[y * width + x];
                    if (value >= planeStart && value <= planeEnd) onPlane++;
                }
            }
            return inside == 0 ? 0f : onPlane / (float)inside;
        }
    }
}
