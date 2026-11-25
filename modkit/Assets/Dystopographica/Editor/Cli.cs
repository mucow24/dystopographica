using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Dystopographica
{
    /// <summary>
    /// Entry points for headless runs, for example:
    /// pwsh tools/dev.ps1 run Dystopographica.Cli.Make preset=C:\maps\preset.json
    /// Arguments are name=value pairs. A preset is the settings file the window saves
    /// with each mod, in [mod folder]/Dystopographica~/preset.json.
    /// </summary>
    public static class Cli
    {
        /// <summary>Logs what the tool sees of the ModKit. A quick check that an install is wired up.</summary>
        public static void Info()
        {
            Debug.Log("[Dystopographica] Mods folder: " + ModKitBridge.ModsFolder);
        }

        /// <summary>Makes the landscape a preset describes. Arguments: preset.</summary>
        public static void Make()
        {
            LandscapeSettings settings = LandscapeSettings.FromJson(File.ReadAllText(Argument("preset")));

            LandscapeBuilder.Make(settings, HeightmapFile.Load(settings.heightmapPath));

            Debug.Log("[Dystopographica] Made the landscape in " + settings.Folder);
        }

        /// <summary>
        /// Makes, compiles and installs the mod a preset describes. Arguments: preset, and
        /// optionally install, a folder to copy the mod to in place of the game's Mods folder.
        /// </summary>
        public static void CompileAndInstall()
        {
            LandscapeSettings settings = LandscapeSettings.FromJson(File.ReadAllText(Argument("preset")));
            string installFolder = Argument("install", ModKitBridge.InstallFolder(settings.modName));

            LandscapeBuilder.CompileAndInstall(settings, HeightmapFile.Load(settings.heightmapPath), installFolder);

            Debug.Log("[Dystopographica] Installed the mod in " + installFolder);
        }

        /// <summary>Writes the window's preview of a preset to a PNG file. Arguments: preset, out.</summary>
        public static void RenderPreview()
        {
            LandscapeSettings asEntered = LandscapeSettings.FromJson(File.ReadAllText(Argument("preset")));
            Heightmap source = HeightmapFile.Load(asEntered.heightmapPath).Normalized();
            LandscapeSettings settings = asEntered.For(source);
            Heightmap unfilled = LandscapeBuilder.PreviewMap(source, settings.smoothing, 512);
            Heightmap map = LandscapeBuilder.WithoutSpecks(unfilled, source.Width, settings);

            var picture = new Texture2D(map.Width, map.Height, TextureFormat.RGBA32, false);
            picture.SetPixels32(Preview.Paint(map, settings.PaintedWater, settings.planeStart, settings.planeEnd, unfilled));
            File.WriteAllBytes(Argument("out"), picture.EncodeToPNG());

            float onPlane = Preview.PlaneFraction(map, settings.planeStart, settings.planeEnd, settings.buildAreaShare);
            Debug.Log($"[Dystopographica] Wrote the preview. The city plane covers {onPlane:P0} of the {settings.mapSize}% build area.");
        }

        /// <summary>
        /// Photographs a made landscape from above and to the south, so its shape can be
        /// checked without starting the game. The terrain is painted light grey in place
        /// of its black material; the water keeps its own. Arguments: mod, out, and
        /// optionally water, "normal" to show the version with normal water, where the
        /// mod has one; terrain, "own" to keep the terrain black as the game shows it;
        /// light, "night" for dim,
        /// even light with nothing to reflect, closer to the game than a sunny day;
        /// distance, how far back the camera stands in units (default 120); height, how
        /// high it stands (default 0.6 of the distance; much more looks down from above); and city,
        /// "yes" to stand a few lit towers on the shore nearest the middle of the map and
        /// point the camera at them, to see what the water makes of a city.
        /// </summary>
        public static void RenderLandscape()
        {
            LandscapeSettings made = LandscapeSettings.Load(Argument("mod")) ?? new LandscapeSettings();
            float buildArea = made.BuildArea;
            // The map with our own water, unless the other is asked for or the map is dry.
            // A dry map has no such file. The saved settings cannot say: on a painted
            // map the picture decides whether there is water.
            string customWater = $"Assets/{Argument("mod")}/Landscape{LandscapeSettings.FileSuffix(WaterKind.Custom)}.prefab";
            WaterKind kind = Argument("water", "custom") == "normal" || !File.Exists(customWater) ? WaterKind.Normal : WaterKind.Custom;
            string path = $"Assets/{Argument("mod")}/Landscape{LandscapeSettings.FileSuffix(kind)}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new FileNotFoundException("There is no made landscape at " + path);
            float distance = float.Parse(Argument("distance", "120"), CultureInfo.InvariantCulture);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject landscape = UnityEngine.Object.Instantiate(prefab);
            var grey = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            grey.SetColor("_BaseColor", new Color(0.7f, 0.7f, 0.7f));
            var terrain = landscape.GetComponentInChildren<Terrain>();
            if (Argument("terrain", "grey") != "own") terrain.materialTemplate = grey;

            // The build area the map was made for, around the origin, for scale.
            var yellow = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            yellow.SetColor("_BaseColor", Color.yellow);
            for (int side = 0; side < 4; side++)
            {
                GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bool alongX = side < 2;
                float offset = side % 2 == 0 ? -buildArea / 2 : buildArea / 2;
                bar.transform.position = alongX ? new Vector3(0f, 0.2f, offset) : new Vector3(offset, 0.2f, 0f);
                bar.transform.localScale = alongX ? new Vector3(buildArea, 0.4f, 0.4f) : new Vector3(0.4f, 0.4f, buildArea);
                bar.GetComponent<MeshRenderer>().sharedMaterial = yellow;
            }

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            bool night = Argument("light", "day") == "night";
            sun.intensity = night ? 0.05f : 1.3f;
            if (night)
            {
                var ambient = new SphericalHarmonicsL2();
                ambient.AddAmbientLight(new Color(0.06f, 0.06f, 0.08f));
                RenderSettings.ambientMode = AmbientMode.Custom;
                RenderSettings.ambientProbe = ambient;
                RenderSettings.reflectionIntensity = 0f;
            }
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(35f, 40f, 0f);

            Vector3 subject = Argument("city", "no") == "yes" ? BuildTowersOnTheShore(terrain, landscape.transform.Find("Buildable Plane") != null) : Vector3.zero;

            var camera = new GameObject("Camera").AddComponent<Camera>();
            float height = float.Parse(Argument("height", (distance * 0.6f).ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
            camera.transform.position = subject + new Vector3(0f, height, -distance);
            camera.transform.LookAt(subject);
            // The custom water looks things up in the camera's pictures of the scene.
            UniversalAdditionalCameraData pictures = camera.GetUniversalAdditionalCameraData();
            pictures.requiresColorOption = CameraOverrideOption.On;
            pictures.requiresDepthOption = CameraOverrideOption.On;
            camera.fieldOfView = 40f;
            camera.farClipPlane = 5000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.06f, 0.08f);

            var target = new RenderTexture(1280, 720, 24);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var picture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            picture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            File.WriteAllBytes(Argument("out"), picture.EncodeToPNG());
            RenderTexture.active = null;

            TerrainData data = terrain.terrainData;
            Debug.Log($"[Dystopographica] Rendered {path}. Terrain size {data.size}, resolution {data.heightmapResolution}, "
                + $"corner at {terrain.transform.position}, bounds {data.bounds.size}.");
        }

        /// <summary>
        /// Stands a row of dark towers with lit windows where the game would stand them,
        /// at y = 0, at the place nearest the middle of the map that has water just south
        /// of it (towards the camera). Returns that place. Stand-ins for the player's
        /// buildings. When the landscape lets buildings stand anywhere, a few small ones
        /// are stood over the water as well, as the game's own small buildings will be.
        /// </summary>
        static Vector3 BuildTowersOnTheShore(Terrain terrain, bool anywhere)
        {
            // The city plane is the commonest height of the terrain near the middle.
            float Ground(float x, float z) => terrain.transform.position.y + terrain.SampleHeight(new Vector3(x, 0f, z));
            var counts = new System.Collections.Generic.Dictionary<int, int>();
            for (float z = -20f; z <= 20f; z += 1f)
                for (float x = -20f; x <= 20f; x += 1f)
                {
                    int step = Mathf.RoundToInt(Ground(x, z) * 100f);
                    counts[step] = counts.TryGetValue(step, out int count) ? count + 1 : 1;
                }
            float plane = 0f;
            int most = 0;
            foreach (var entry in counts)
                if (entry.Value > most)
                {
                    most = entry.Value;
                    plane = entry.Key / 100f;
                }
            bool Shore(float x, float z) => Mathf.Abs(Ground(x, z) - plane) < 0.01f && Ground(x, z - 2f) < plane - 0.1f && Ground(x, z - 4f) < plane - 0.1f;

            float reach = terrain.terrainData.size.x * 0.4f;
            Vector3 shore = Vector3.zero;
            float nearest = float.MaxValue;
            for (float z = -reach; z <= reach; z += 0.5f)
            {
                for (float x = -reach; x <= reach; x += 0.5f)
                {
                    if (x * x + z * z >= nearest || !Shore(x, z)) continue;
                    nearest = x * x + z * z;
                    shore = new Vector3(x, 0f, z);
                }
            }

            // Windows: a small picture of lit and unlit squares, glowing.
            var windows = new Texture2D(16, 32, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var random = new System.Random(7);
            for (int y = 0; y < windows.height; y++)
                for (int x = 0; x < windows.width; x++)
                    windows.SetPixel(x, y, x % 2 == 1 && y % 2 == 1 && random.Next(3) > 0
                        ? (random.Next(4) == 0 ? new Color(1f, 0.25f, 0.6f) : new Color(1f, 0.85f, 0.5f)) : Color.black);
            windows.Apply();
            var wall = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            wall.SetTexture("_BaseMap", windows);

            float[] heights = { 5f, 9f, 3f, 12f, 6f };
            for (int i = 0; i < heights.Length; i++)
            {
                GameObject tower = GameObject.CreatePrimitive(PrimitiveType.Cube);
                float x = shore.x + (i - 2) * 2.2f;
                tower.transform.position = new Vector3(x, heights[i] / 2, shore.z + 0.8f + (i % 2) * 1.5f);
                tower.transform.localScale = new Vector3(1.6f, heights[i], 1.6f);
                tower.GetComponent<MeshRenderer>().sharedMaterial = wall;
            }
            for (int i = 0; anywhere && i < 4; i++)
            {
                GameObject small = GameObject.CreatePrimitive(PrimitiveType.Cube);
                float height = 0.5f + 0.2f * (i % 3);
                small.transform.position = new Vector3(shore.x + (i - 1.5f) * 1.3f, height / 2, shore.z - 2.5f - (i % 2) * 1.2f);
                small.transform.localScale = new Vector3(0.7f, height, 0.7f);
                small.GetComponent<MeshRenderer>().sharedMaterial = wall;
            }
            return shore;
        }

        static string Argument(string name, string fallback = null)
        {
            foreach (string argument in Environment.GetCommandLineArgs())
                if (argument.StartsWith(name + "=")) return argument.Substring(name.Length + 1);
            return fallback ?? throw new ArgumentException($"Missing command line argument {name}=<value>.");
        }
    }
}
