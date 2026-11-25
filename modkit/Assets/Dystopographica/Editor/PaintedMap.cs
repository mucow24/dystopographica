using UnityEngine;

namespace Dystopographica
{
    /// <summary>
    /// A map as a picture to paint on, and such a picture read back as a map.
    ///
    /// - Blue is under water.
    /// - Green is the city plane.
    /// - Grey is everything else, and how light it is gives its height. Mid-grey is
    ///   level with the city plane, white is the peak, and black is as low as land
    ///   goes: the waterline, or on a map with no water the lowest point.
    ///
    /// The picture says what is water and what is plane, so the three cursors have no
    /// part in it. Read back, it becomes a map whose values put water, plane and
    /// heights at fixed levels of their own, which LandscapeSettings.For sets the
    /// cursors to. From there on it is made like any heightmap.
    /// </summary>
    public static class PaintedMap
    {
        /// <summary>
        /// Where the cursors stand for a map read from a picture. Blue is 0, below the
        /// water cursor; dark greys fill the shore between it and the plane start;
        /// green is the middle of the band; light greys fill the rest, up to 1. On a
        /// picture with nothing blue the dark greys run all the way down to 0.
        /// </summary>
        public const float Water = 0.2f, PlaneStart = 0.4f, PlaneEnd = 0.6f;

        const float MidGrey = 0.5f;

        // How much stronger than the other two a pixel's blue or green has to be. Low
        // enough that the soft edge of a brush stroke still counts, high enough that
        // the faint cast of a scanned or compressed grey picture does not.
        const float Tint = 0.1f;

        static readonly Color WaterColour = new Color(0.10f, 0.35f, 0.85f);
        static readonly Color PlaneColour = new Color(0.15f, 0.75f, 0.25f);

        /// <summary>
        /// Paints a normalized map as the settings' cursors divide it. The result holds
        /// three numbers for each pixel, red, green and blue, row by row from the top.
        /// </summary>
        public static ushort[] Paint(Heightmap normalized, LandscapeSettings settings)
        {
            float lowest = settings.PaintedWater, planeStart = settings.planeStart, planeEnd = settings.planeEnd;
            var picture = new ushort[normalized.Values.Length * 3];
            for (int i = 0; i < normalized.Values.Length; i++)
            {
                float value = normalized.Values[i];
                Color colour;
                if (value < lowest) colour = WaterColour;
                else if (value >= planeStart && value <= planeEnd) colour = PlaneColour;
                else
                {
                    float grey = value > planeEnd
                        ? Mathf.Lerp(MidGrey, 1f, Mathf.InverseLerp(planeEnd, 1f, value))
                        : Mathf.Lerp(0f, MidGrey, Mathf.InverseLerp(lowest, planeStart, value));
                    colour = new Color(grey, grey, grey);
                }
                picture[i * 3] = Sixteen(colour.r);
                picture[i * 3 + 1] = Sixteen(colour.g);
                picture[i * 3 + 2] = Sixteen(colour.b);
            }
            return picture;
        }

        static ushort Sixteen(float value) => (ushort)Mathf.RoundToInt(Mathf.Clamp01(value) * 65535f);

        /// <summary>
        /// Reads a picture as a painted map. Null when nothing in it is blue or green:
        /// such a picture is a plain heightmap.
        /// </summary>
        public static Heightmap Read(int width, int height, float[] red, float[] green, float[] blue)
        {
            bool anyWater = false, anyPlane = false;
            for (int i = 0; i < red.Length && !(anyWater && anyPlane); i++)
            {
                if (IsWater(red[i], green[i], blue[i])) anyWater = true;
                else if (IsPlane(red[i], green[i], blue[i])) anyPlane = true;
            }
            if (!anyWater && !anyPlane) return null;

            float lowest = anyWater ? Water : 0f;
            var values = new float[red.Length];
            for (int i = 0; i < values.Length; i++)
            {
                float grey = (red[i] + green[i] + blue[i]) / 3;
                values[i] =
                    IsWater(red[i], green[i], blue[i]) ? 0f :
                    IsPlane(red[i], green[i], blue[i]) ? (PlaneStart + PlaneEnd) / 2 :
                    grey >= MidGrey ? Mathf.Lerp(PlaneEnd, 1f, Mathf.InverseLerp(MidGrey, 1f, grey)) :
                    Mathf.Lerp(lowest, PlaneStart, Mathf.InverseLerp(0f, MidGrey, grey));
            }
            return new Heightmap(width, height, values, painted: true, paintedWater: anyWater);
        }

        static bool IsWater(float red, float green, float blue) => blue - Mathf.Max(red, green) > Tint;

        static bool IsPlane(float red, float green, float blue) => green - Mathf.Max(red, blue) > Tint;
    }
}
