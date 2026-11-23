using System;

namespace Dystopographica
{
    /// <summary>Takes the noise out of a heightmap before it is mapped to world heights.</summary>
    public static class Smoothing
    {
        /// <summary>A Gaussian blur. Sigma is in pixels, and zero or less leaves the map alone.</summary>
        public static Heightmap Gaussian(Heightmap source, float sigma)
        {
            if (sigma <= 0f) return source;

            int radius = (int)Math.Ceiling(sigma * 3);
            var kernel = new float[radius * 2 + 1];
            float total = 0f;
            for (int i = -radius; i <= radius; i++) total += kernel[i + radius] = (float)Math.Exp(-i * i / (2.0 * sigma * sigma));
            for (int i = 0; i < kernel.Length; i++) kernel[i] /= total;

            int width = source.Width, height = source.Height;
            float[] across = new float[width * height], result = new float[width * height];

            // The blur is separable: once along the rows, then once along the columns.
            // Pixels past an edge repeat the edge pixel.
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float sum = 0f;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int sx = x + k;
                        if (sx < 0) sx = 0; else if (sx >= width) sx = width - 1;
                        sum += source.Values[y * width + sx] * kernel[k + radius];
                    }
                    across[y * width + x] = sum;
                }
            }
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float sum = 0f;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int sy = y + k;
                        if (sy < 0) sy = 0; else if (sy >= height) sy = height - 1;
                        sum += across[sy * width + x] * kernel[k + radius];
                    }
                    result[y * width + x] = sum;
                }
            }
            return new Heightmap(width, height, result);
        }
    }
}
