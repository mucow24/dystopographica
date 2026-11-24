using System.Collections.Generic;
using UnityEngine;

namespace Dystopographica
{
    /// <summary>Cleans small specks of noise out of a heightmap without blurring the rest of it.</summary>
    public static class Specks
    {
        /// <summary>
        /// Fills small holes in the city plane. A speck is a connected group of pixels
        /// outside the band from planeStart to planeEnd: water, shore and hill alike.
        /// One that is smaller than `limit` pixels and has plane all the way round it is
        /// pulled into the band, each pixel to the nearer edge. A speck touching the edge
        /// of the map is not surrounded and stays. Pixels that touch only at a corner
        /// belong to the same speck, so a thin diagonal river is not taken for a row of
        /// specks.
        /// </summary>
        public static Heightmap FillPlane(Heightmap map, float planeStart, float planeEnd, int limit)
        {
            if (limit <= 1) return map;

            int width = map.Width, height = map.Height;
            var values = (float[])map.Values.Clone();
            var seen = new bool[values.Length];
            var toVisit = new int[values.Length];
            var speck = new List<int>();

            for (int start = 0; start < values.Length; start++)
            {
                if (seen[start] || InBand(values[start])) continue;

                // Walk the whole speck from this pixel. Only the first `limit` pixels are
                // remembered: a speck with more than that is too big to fill anyway.
                int waiting = 0, size = 0;
                bool atMapEdge = false;
                speck.Clear();
                toVisit[waiting++] = start;
                seen[start] = true;
                while (waiting > 0)
                {
                    int pixel = toVisit[--waiting];
                    int x = pixel % width, y = pixel / width;
                    if (++size < limit) speck.Add(pixel);
                    if (x == 0 || y == 0 || x == width - 1 || y == height - 1) atMapEdge = true;

                    for (int ny = Mathf.Max(y - 1, 0); ny <= Mathf.Min(y + 1, height - 1); ny++)
                    {
                        for (int nx = Mathf.Max(x - 1, 0); nx <= Mathf.Min(x + 1, width - 1); nx++)
                        {
                            int neighbour = ny * width + nx;
                            if (seen[neighbour] || InBand(values[neighbour])) continue;
                            seen[neighbour] = true;
                            toVisit[waiting++] = neighbour;
                        }
                    }
                }

                if (size < limit && !atMapEdge)
                    foreach (int pixel in speck) values[pixel] = Mathf.Clamp(values[pixel], planeStart, planeEnd);
            }
            return new Heightmap(width, height, values);

            bool InBand(float value) => value >= planeStart && value <= planeEnd;
        }
    }
}
