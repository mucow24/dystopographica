using UnityEngine;

namespace Dystopographica
{
    /// <summary>
    /// Maps a normalized heightmap value (0 = the image's lowest point, 1 = its highest)
    /// to a world height. Values inside the band become the city plane at y = 0. Above
    /// it the terrain climbs to the peak height. Below it:
    ///
    /// - on a dry map, the terrain drops to the lowest depth;
    /// - on a map with water, it drops to the water surface at the water cursor (the
    ///   shore), and everything lower than the cursor lies at the lowest depth (the bed).
    ///
    /// The water surface gets a depth of its own because coastlines need it. In real
    /// elevation data the sea is only just lower than the land beside it, so a surface
    /// placed by the cursor alone ends up a hair below the city and looks like ground.
    ///
    /// The bed is flat and deep for the game's sake. Dystopika marks ground that is a
    /// little below the city with red crosses, as land that cannot be built on, and a
    /// bed that followed the heightmap got them wherever the water was shallow. The
    /// water is opaque, so the bed's shape is never seen.
    /// </summary>
    public sealed class HeightCurve
    {
        readonly float planeStart, planeEnd, water, waterDepth, lowestDepth;
        readonly Side below, above;

        /// <summary>A water depth of zero makes a dry map, and the water cursor is then ignored.</summary>
        public HeightCurve(float planeStart, float planeEnd, float peakHeight, float lowestDepth, float knee, float water = 0f, float waterDepth = 0f)
        {
            this.planeStart = planeStart;
            this.planeEnd = planeEnd;
            this.waterDepth = waterDepth;
            above = new Side(1f - planeEnd, peakHeight, knee);

            if (waterDepth > 0f)
            {
                this.water = Mathf.Min(water, planeStart);
                // The bed can never be above the water that covers it.
                this.lowestDepth = Mathf.Max(lowestDepth, waterDepth);
                below = new Side(planeStart - this.water, waterDepth, knee);
            }
            else
            {
                below = new Side(planeStart, lowestDepth, knee);
            }
        }

        public float Evaluate(float value)
        {
            value = Mathf.Clamp01(value);
            if (value > planeEnd) return above.Height(value - planeEnd);
            if (value >= planeStart) return 0f;
            if (value >= water) return -below.Height(planeStart - value);
            return -lowestDepth;
        }

        /// <summary>
        /// The terrain on one side of the band. It leaves the band with no slope, curves
        /// through the knee, then runs straight to `height` at `length` from the band.
        /// </summary>
        readonly struct Side
        {
            readonly float knee, slope;

            public Side(float length, float height, float knee)
            {
                this.knee = Mathf.Clamp(knee, 0f, Mathf.Max(length, 0f));
                slope = length > 0f ? height / (length - this.knee / 2) : 0f;
            }

            public float Height(float distance) =>
                distance < knee ? slope * distance * distance / (2 * knee) : slope * (distance - knee / 2);
        }
    }
}
