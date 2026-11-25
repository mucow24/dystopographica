using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Dystopographica
{
    /// <summary>
    /// The water a landscape has. A map with water gets the custom kind. On request the
    /// mod holds the same map a second time with the normal kind, and the player picks.
    /// </summary>
    public enum WaterKind
    {
        /// <summary>A flat, still surface drawn with Unity's own shader. Also stands for a dry map's one landscape.</summary>
        Normal,

        /// <summary>A rippling, reflective surface drawn with our shader.</summary>
        Custom,
    }

    /// <summary>
    /// Everything the Make landscape window asks for. A copy is saved with each mod as
    /// its preset, so the window can pick up where it left off.
    /// </summary>
    [Serializable]
    public class LandscapeSettings
    {
        /// <summary>The three cursors on the levels bar, lowest first.</summary>
        public const int Water = 0, PlaneStart = 1, PlaneEnd = 2;

        /// <summary>The width of the game's build area at the 100% map size, in units.</summary>
        public const float BuildAreaSize = 50f;

        /// <summary>The map sizes the game offers, as percentages.</summary>
        public static readonly int[] MapSizes = { 100, 200, 400 };

        public string heightmapPath = "";

        // The cursors, each from 0 (the heightmap's lowest point) to 1 (its highest).
        // The water starts at the plane start: everything lower than the city is water.
        public float water = 0.4f;
        public float planeStart = 0.4f;
        public float planeEnd = 0.6f;

        // The map size the landscape is made for, and how much of the map's width that
        // size's build area covers. The game never scales a landscape, it only widens
        // the build area, so the map is sized to suit one map size: by default the
        // largest, which also works at the smaller ones, on the middle of the map.
        public int mapSize = 400;
        public float buildAreaShare = 1f;

        // World units, measured up and down from the city plane. The lowest depth shapes
        // a dry map only: with water, the sea bed lies at a fixed level. See BedDrop.
        public float peakHeight = 20f;
        public float lowestDepth = 2f;

        public float smoothing = 1f;
        public int speckSize;
        public float knee = 0.01f;
        public bool waterPlane = true;

        // A map with water is one landscape, with our own rippling, mirroring water.
        // On: the mod also holds the same map with flat, still water, and the player picks.
        public bool normalWater;

        public string modName = "";
        public string author = "";
        public int version = 1;
        public int minorVersion;

        public string landscapeName = "";
        public string thumbnailPath = "";

        /// <summary>
        /// Where the Night City mod draws its roads and its water, and where its terrain
        /// lies under that water, in units above the level the game stands buildings on
        /// (y = 0). Measured from its meshes.
        /// </summary>
        public const float NightCityGround = 0.18f, NightCityWater = 0.118f, NightCitySeaBed = -0.08f;

        /// <summary>
        /// How far above the level buildings stand on the city plane is drawn. The game
        /// lets a building stand wherever a landscape's colliders are at that level, and
        /// ours has one flat sheet there under the whole map, as Night City has. So
        /// some buildings stand over the water, and with the land drawn at their level
        /// and the water below it those hover. The land is drawn a little higher, as
        /// Night City draws it, and the water between the two: every building is then
        /// sunk a little into what is drawn, and one over the water stands in it. A dry
        /// map has no water to stand in and is left alone.
        /// </summary>
        public float GroundLift => waterPlane ? NightCityGround : 0f;

        /// <summary>How far below the drawn city plane the water lies: the little that Night City's levels leave.</summary>
        public float WaterDrop => NightCityGround - NightCityWater;

        /// <summary>
        /// How far below the drawn city plane the sea bed lies: the little that Night
        /// City's levels leave. Nothing is built on the terrain, so the bed has no need
        /// to be deep, and a deep one made the water look like a canyon from above. On
        /// a dry map this is the heightmap's lowest point, at the lowest depth.
        /// </summary>
        public float BedDrop => waterPlane ? NightCityGround - NightCitySeaBed : lowestDepth;

        public string Folder => "Assets/" + modName;

        /// <summary>Unity ignores folders ending in a tilde, so this one is never imported or bundled.</summary>
        public string PresetFolder => Folder + "/Dystopographica~";

        public string PresetPath => PresetFolder + "/preset.json";

        public string DisplayName => string.IsNullOrWhiteSpace(landscapeName) ? modName : landscapeName;

        /// <summary>
        /// How much wider the build area is at a map size than at 100%. The percentage is
        /// its area, so the width grows by the square root: seen in the game, 200% is about
        /// 1.4 times as wide and 400% twice.
        /// </summary>
        public static float BuildAreaScale(int percent) => Mathf.Sqrt(percent / 100f);

        /// <summary>The width of the build area the map is made for, in units.</summary>
        public float BuildArea => BuildAreaSize * BuildAreaScale(mapSize);

        /// <summary>
        /// The width of the whole map in the game, in units: wide enough that the build
        /// area is its share of it.
        /// </summary>
        public float WorldSize => BuildArea / Mathf.Clamp(buildAreaShare, 0.01f, 1f);

        /// <summary>
        /// What the landscapes tell the game. The flag is all or nothing: a map made for
        /// 200% has to allow 400% as well, and will show bare ground at its edges there.
        /// </summary>
        public bool AllowsLargerMapSizes => mapSize > 100;

        /// <summary>
        /// The landscapes the mod holds, in the order the game lists them: the map with
        /// our own water, then with normal water if that is asked for. Or a single dry one.
        /// </summary>
        public List<WaterKind> Landscapes() =>
            !waterPlane ? new List<WaterKind> { WaterKind.Normal }
            : normalWater ? new List<WaterKind> { WaterKind.Custom, WaterKind.Normal }
            : new List<WaterKind> { WaterKind.Custom };

        /// <summary>The name the game lists a landscape under. Only the extra one with normal water says which it is.</summary>
        public string DisplayNameFor(WaterKind kind) =>
            waterPlane && kind == WaterKind.Normal ? DisplayName + " (normal water)" : DisplayName;

        /// <summary>What sets a landscape's asset files apart: "Landscape.prefab", "Landscape Custom Water.prefab".</summary>
        public static string FileSuffix(WaterKind kind) => kind == WaterKind.Custom ? " Custom Water" : "";

        public void Write(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, ToJson());
        }

        /// <summary>The settings in a file, or null when there is no such file or it cannot be read.</summary>
        public static LandscapeSettings Read(string path)
        {
            try
            {
                return File.Exists(path) ? FromJson(File.ReadAllText(path)) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The value below which the preview is blue. Nothing is, on a dry map.</summary>
        public float PaintedWater => waterPlane ? water : 0f;

        /// <summary>
        /// The settings to make a map with. For a heightmap, these. A painted map says
        /// for itself what is water and what is plane, so for one of those it is a copy
        /// with the cursors at the painted map's fixed levels, and with water if
        /// something is painted blue. The settings themselves keep their cursors, for
        /// the next heightmap.
        /// </summary>
        public LandscapeSettings For(Heightmap source)
        {
            if (source == null || !source.IsPainted) return this;

            LandscapeSettings copy = FromJson(ToJson());
            copy.water = source.HasPaintedWater ? PaintedMap.Water : 0f;
            copy.planeStart = PaintedMap.PlaneStart;
            copy.planeEnd = PaintedMap.PlaneEnd;
            copy.waterPlane = source.HasPaintedWater;
            return copy;
        }

        public HeightCurve Curve() =>
            new HeightCurve(planeStart, planeEnd, peakHeight, BedDrop, knee, water, waterPlane ? WaterDrop : 0f);

        /// <summary>
        /// Moves one cursor towards `value`. Cursors never cross. The plane start is the
        /// one that leads: the water cursor cannot rise past it, but it pushes the water
        /// cursor down ahead of it, and water that sits right at the plane start stays
        /// with it both ways. That keeps "everything lower than the city is water", the
        /// usual wish, until the water cursor itself is pulled lower.
        /// </summary>
        public void MoveCursor(int cursor, float value)
        {
            if (cursor == Water)
            {
                water = Mathf.Clamp(value, 0f, planeStart);
            }
            else if (cursor == PlaneStart)
            {
                bool together = water == planeStart;
                planeStart = Mathf.Clamp(value, 0f, planeEnd);
                water = together ? planeStart : Mathf.Min(water, planeStart);
            }
            else
            {
                planeEnd = Mathf.Clamp(value, planeStart, 1f);
            }
        }

        /// <summary>The share of the map under water below which there is, in effect, none: a few stray pixels.</summary>
        public const float NoWaterToSpeakOf = 0.0001f;

        /// <summary>
        /// Water switched on with nothing under it gives a map with no water at all, which
        /// nobody wants. Given the share of the map that is under water, this puts a water
        /// cursor that has nothing below it back at the plane start, where everything
        /// lower than the city is water. It says whether it moved the cursor.
        /// </summary>
        public bool RaiseWaterOutOfTheDry(float waterFraction)
        {
            if (!waterPlane || water >= planeStart || waterFraction >= NoWaterToSpeakOf) return false;
            water = planeStart;
            return true;
        }

        /// <summary>The cursor a click at `value` should pick up.</summary>
        public int NearestCursor(float value)
        {
            float[] cursors = { water, planeStart, planeEnd };
            int nearest = waterPlane ? Water : PlaneStart;
            for (int i = nearest + 1; i < cursors.Length; i++)
            {
                float distance = Mathf.Abs(value - cursors[i]), best = Mathf.Abs(value - cursors[nearest]);
                // Overlapping cursors are equally near. Above them, only the upper one can follow the pointer.
                if (distance < best || (distance == best && value > cursors[i])) nearest = i;
            }
            return nearest;
        }

        /// <summary>Reasons the landscape cannot be made yet, in words for the user. Empty when it can.</summary>
        public List<string> Problems(Heightmap source)
        {
            var problems = new List<string>();

            if (source == null) problems.Add("Choose a heightmap.");
            else if (source.Width != source.Height)
                problems.Add($"The heightmap is {source.Width} x {source.Height}. Only square heightmaps work for now.");

            if (!IsFolderName(modName))
                problems.Add("Enter a mod name. It becomes a folder name, so it cannot contain \\ / : * ? \" < > | or end in a dot.");
            else if (Directory.Exists(Folder) && !Directory.Exists(PresetFolder))
                problems.Add($"{Folder} already exists and was not made by this tool. Choose another mod name.");

            if (Array.IndexOf(MapSizes, mapSize) < 0) problems.Add("Map size must be 100, 200 or 400, the sizes the game offers.");
            return problems;
        }

        static bool IsFolderName(string name) =>
            !string.IsNullOrWhiteSpace(name)
            && name == name.Trim()
            && !name.EndsWith(".")
            && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

        public string ToJson() => JsonUtility.ToJson(this, true);

        public static LandscapeSettings FromJson(string json) => JsonUtility.FromJson<LandscapeSettings>(json);

        public void Save()
        {
            Directory.CreateDirectory(PresetFolder);
            File.WriteAllText(PresetPath, ToJson());
        }

        /// <summary>The settings a mod was last made with, or null when it has none.</summary>
        public static LandscapeSettings Load(string modName)
        {
            string path = new LandscapeSettings { modName = modName }.PresetPath;
            return File.Exists(path) ? FromJson(File.ReadAllText(path)) : null;
        }

        /// <summary>Lower-case letters, digits and single dashes: safe as a landscape id and as a file name.</summary>
        public static string Slug(string name)
        {
            var slug = new StringBuilder();
            foreach (char c in name.ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) slug.Append(c);
                else if (slug.Length > 0 && slug[slug.Length - 1] != '-') slug.Append('-');
            }
            string result = slug.ToString().TrimEnd('-');
            return result.Length > 0 ? result : "landscape";
        }
    }
}
