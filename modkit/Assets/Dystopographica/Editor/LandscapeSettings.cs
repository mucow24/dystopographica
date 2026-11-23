using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Dystopographica
{
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

        /// <summary>Every map size the game offers.</summary>
        public static readonly int[] AllMapSizes = { 100, 200, 400 };

        public string heightmapPath = "";

        // The cursors, each from 0 (the heightmap's lowest point) to 1 (its highest).
        public float water = 0.2f;
        public float planeStart = 0.4f;
        public float planeEnd = 0.6f;

        // World units, at the 100% map size. The game's build area is then 50 x 50, so
        // by default the whole map is the build area. The two depths are measured down
        // from the city plane.
        public float worldSize = BuildAreaSize;
        public float peakHeight = 20f;
        public float lowestDepth = 2f;
        public float waterDepth = 0.7f;

        public float smoothing = 1f;
        public float knee = 0.01f;
        public bool waterPlane = true;

        public string modName = "";
        public string author = "";
        public int version = 1;
        public int minorVersion;

        public string landscapeName = "";
        public string thumbnailPath = "";
        // Whether to also make a landscape for the 200% and 400% map sizes.
        public bool size200 = true;
        public bool size400 = true;
        public bool hideDefaultGrid = true;
        public bool hideDefaultLandscape = true;

        public string Folder => "Assets/" + modName;

        /// <summary>Unity ignores folders ending in a tilde, so this one is never imported or bundled.</summary>
        public string PresetFolder => Folder + "/Dystopographica~";

        public string PresetPath => PresetFolder + "/preset.json";

        public string DisplayName => string.IsNullOrWhiteSpace(landscapeName) ? modName : landscapeName;

        /// <summary>
        /// How much of the map's width the build area covers, from 0 to 1. At 1 the whole
        /// map is the build area; less leaves scenery around it. It is the same at every
        /// map size, which is why the window offers this and not the world size.
        /// </summary>
        public float BuildAreaShare
        {
            get => worldSize > BuildAreaSize ? BuildAreaSize / worldSize : 1f;
            set => worldSize = BuildAreaSize / Mathf.Clamp(value, 0.01f, 1f);
        }

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

        /// <summary>The map sizes a landscape is made for: 100% always, and the larger ones that are ticked.</summary>
        public List<int> MapSizes()
        {
            var sizes = new List<int> { 100 };
            if (size200) sizes.Add(200);
            if (size400) sizes.Add(400);
            return sizes;
        }

        /// <summary>
        /// How much wider the build area is at a map size than at 100%. The percentage is
        /// its area, so the width grows by the square root: seen in the game, 200% is about
        /// 1.4 times as wide and 400% twice.
        /// </summary>
        public static float BuildAreaScale(int percent) => Mathf.Sqrt(percent / 100f);

        /// <summary>
        /// These settings scaled up for a larger map size. The game never scales a
        /// landscape, it only widens the build area, so the landscape for a larger size is
        /// the same map made bigger: wider, and taller and deeper to keep its shape. The
        /// water surface stays as far below the city as it was.
        /// </summary>
        public LandscapeSettings ForMapSize(int percent)
        {
            float scale = BuildAreaScale(percent);
            LandscapeSettings sized = FromJson(ToJson());
            sized.worldSize = worldSize * scale;
            sized.peakHeight = peakHeight * scale;
            sized.lowestDepth = waterPlane ? waterDepth + (lowestDepth - waterDepth) * scale : lowestDepth * scale;
            return sized;
        }

        /// <summary>The name the game lists a size's landscape under.</summary>
        public string DisplayNameFor(int percent) => MapSizes().Count > 1 ? $"{DisplayName} ({percent}%)" : DisplayName;

        /// <summary>What sets a size's asset files apart: "Landscape.prefab", "Landscape 200.prefab".</summary>
        public static string FileSuffix(int percent) => percent == 100 ? "" : " " + percent;

        /// <summary>The value below which the preview is blue. Nothing is, on a dry map.</summary>
        public float PaintedWater => waterPlane ? water : 0f;

        public HeightCurve Curve() =>
            new HeightCurve(planeStart, planeEnd, peakHeight, lowestDepth, knee, water, waterPlane ? waterDepth : 0f);

        /// <summary>
        /// Moves one cursor as far towards `value` as its neighbours allow. Cursors never
        /// cross. On a dry map the water cursor is out of use, so it does not hold the
        /// plane start back and is carried along under it.
        /// </summary>
        public void MoveCursor(int cursor, float value)
        {
            if (cursor == Water)
            {
                water = Mathf.Clamp(value, 0f, planeStart);
            }
            else if (cursor == PlaneStart)
            {
                planeStart = Mathf.Clamp(value, waterPlane ? water : 0f, planeEnd);
                water = Mathf.Min(water, planeStart);
            }
            else
            {
                planeEnd = Mathf.Clamp(value, planeStart, 1f);
            }
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

            if (worldSize <= 0f) problems.Add("World size must be more than zero.");

            if (waterPlane && waterDepth <= 0f)
                problems.Add("Water depth must be more than zero, or the water would lie level with the city.");
            else if (waterPlane && lowestDepth <= waterDepth)
                problems.Add("Lowest depth must be more than the water depth, so the sea bed is under the water.");
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

        /// <summary>True when the mod on disk was made with exactly these settings.</summary>
        public bool MatchesSavedPreset() => File.Exists(PresetPath) && File.ReadAllText(PresetPath) == ToJson();

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
