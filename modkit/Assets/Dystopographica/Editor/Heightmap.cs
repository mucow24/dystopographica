using System;

namespace Dystopographica
{
    /// <summary>
    /// A grid of heights. Values are stored row by row, and row 0 is the top of the image
    /// (north). A map straight from a file runs from 0 (black) to 1 (white).
    /// </summary>
    public sealed class Heightmap
    {
        public Heightmap(int width, int height, float[] values, bool painted = false, bool paintedWater = false)
        {
            IsPainted = painted;
            HasPaintedWater = painted && paintedWater;
            if (values.Length != width * height)
                throw new ArgumentException($"A {width} x {height} heightmap needs {width * height} values, not {values.Length}.");
            Width = width;
            Height = height;
            Values = values;
            if (values.Length == 0) return;

            Min = Max = values[0];
            foreach (float value in values)
            {
                if (value < Min) Min = value;
                if (value > Max) Max = value;
            }
        }

        public int Width { get; }
        public int Height { get; }
        public float[] Values { get; }
        public float Min { get; }
        public float Max { get; }

        /// <summary>
        /// Whether the map was read from a painted picture (see PaintedMap). Its values
        /// then stand for water, plane and heights at fixed levels, not for heights alone.
        /// </summary>
        public bool IsPainted { get; }

        /// <summary>Whether anything on a painted map is painted as water.</summary>
        public bool HasPaintedWater { get; }

        public float this[int x, int y] => Values[y * Width + x];

        /// <summary>
        /// The same map stretched so its lowest point is 0 and its highest is 1. A painted
        /// map is left as it is: stretching it would move its fixed levels.
        /// </summary>
        public Heightmap Normalized()
        {
            if (IsPainted) return this;

            float range = Max - Min;
            var values = new float[Values.Length];
            if (range > 0f)
                for (int i = 0; i < values.Length; i++) values[i] = (Values[i] - Min) / range;
            return new Heightmap(Width, Height, values);
        }
    }
}
