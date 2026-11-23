using System;

namespace Dystopographica
{
    /// <summary>Changes the pixel size of a heightmap.</summary>
    public static class Resample
    {
        /// <summary>
        /// Stretches a map onto a size x size grid. The outermost pixels stay on the
        /// edges and everything between is interpolated.
        /// </summary>
        public static Heightmap Bilinear(Heightmap source, int size)
        {
            var values = new float[size * size];
            double stepX = size > 1 ? (source.Width - 1) / (double)(size - 1) : 0;
            double stepY = size > 1 ? (source.Height - 1) / (double)(size - 1) : 0;
            float[] from = source.Values;

            for (int y = 0; y < size; y++)
            {
                double sourceY = y * stepY;
                int y0 = Math.Min((int)sourceY, source.Height - 1);
                int y1 = Math.Min(y0 + 1, source.Height - 1);
                float fy = (float)(sourceY - y0);

                for (int x = 0; x < size; x++)
                {
                    double sourceX = x * stepX;
                    int x0 = Math.Min((int)sourceX, source.Width - 1);
                    int x1 = Math.Min(x0 + 1, source.Width - 1);
                    float fx = (float)(sourceX - x0);

                    float top = from[y0 * source.Width + x0] * (1 - fx) + from[y0 * source.Width + x1] * fx;
                    float bottom = from[y1 * source.Width + x0] * (1 - fx) + from[y1 * source.Width + x1] * fx;
                    values[y * size + x] = top * (1 - fy) + bottom * fy;
                }
            }
            return new Heightmap(size, size, values);
        }

        /// <summary>
        /// Averages factor x factor blocks into single pixels. Pixels left over at the
        /// right and bottom edges are dropped.
        /// </summary>
        public static Heightmap Shrink(Heightmap source, int factor)
        {
            factor = Math.Min(factor, Math.Min(source.Width, source.Height));
            if (factor <= 1) return source;

            int width = source.Width / factor, height = source.Height / factor;
            var values = new float[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double sum = 0;
                    for (int dy = 0; dy < factor; dy++)
                        for (int dx = 0; dx < factor; dx++)
                            sum += source.Values[(y * factor + dy) * source.Width + x * factor + dx];
                    values[y * width + x] = (float)(sum / (factor * factor));
                }
            }
            return new Heightmap(width, height, values);
        }

        /// <summary>
        /// The Unity Terrain resolution closest to an image size. Unity accepts 33, 65,
        /// 129 and so on up to 4097.
        /// </summary>
        public static int TerrainResolution(int imageSize)
        {
            int best = 33;
            for (int candidate = 33; candidate <= 4097; candidate = (candidate - 1) * 2 + 1)
                if (Math.Abs(candidate - imageSize) <= Math.Abs(best - imageSize)) best = candidate;
            return best;
        }
    }
}
