namespace Dystopographica
{
    public static class Histogram
    {
        /// <summary>
        /// Counts how many pixels of a normalized map fall in each of `bins` equal slices
        /// of the range 0 to 1.
        /// </summary>
        public static int[] Compute(Heightmap normalized, int bins)
        {
            var counts = new int[bins];
            foreach (float value in normalized.Values)
            {
                int bin = (int)(value * bins);
                if (bin < 0) bin = 0; else if (bin >= bins) bin = bins - 1;
                counts[bin]++;
            }
            return counts;
        }
    }
}
