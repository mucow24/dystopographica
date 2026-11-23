using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

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
            LandscapeSettings settings = LandscapeSettings.FromJson(File.ReadAllText(Argument("preset")));
            Heightmap map = LandscapeBuilder.PreviewMap(HeightmapFile.Load(settings.heightmapPath).Normalized(), settings.smoothing, 512);

            var picture = new Texture2D(map.Width, map.Height, TextureFormat.RGBA32, false);
            picture.SetPixels32(Preview.Paint(map, settings.PaintedWater, settings.planeStart, settings.planeEnd));
            File.WriteAllBytes(Argument("out"), picture.EncodeToPNG());

            float onPlane = Preview.PlaneFraction(map, settings.planeStart, settings.planeEnd, LandscapeSettings.BuildAreaSize / settings.worldSize);
            Debug.Log($"[Dystopographica] Wrote the preview. The city plane covers {onPlane:P0} of the starting build area.");
        }

        /// <summary>
        /// Photographs a made landscape from above and to the south, so its shape can be
        /// checked without starting the game. The terrain is painted light grey in place
        /// of its black material; the water keeps its own. Arguments: mod, out, and
        /// optionally size, the map size whose landscape to show (100, 200 or 400), and
        /// distance, how far back the camera stands in units (default 120).
        /// </summary>
        public static void RenderLandscape()
        {
            int size = int.Parse(Argument("size", "100"), CultureInfo.InvariantCulture);
            float buildArea = LandscapeSettings.BuildAreaSize * LandscapeSettings.BuildAreaScale(size);
            string path = $"Assets/{Argument("mod")}/Landscape{LandscapeSettings.FileSuffix(size)}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new FileNotFoundException("There is no made landscape at " + path);
            float distance = float.Parse(Argument("distance", "120"), CultureInfo.InvariantCulture);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject landscape = UnityEngine.Object.Instantiate(prefab);
            var grey = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            grey.SetColor("_BaseColor", new Color(0.7f, 0.7f, 0.7f));
            var terrain = landscape.GetComponentInChildren<Terrain>();
            terrain.materialTemplate = grey;

            // The game's starting build area, 50 x 50 around the origin, for scale.
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
            sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(35f, 40f, 0f);

            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, distance * 0.6f, -distance);
            camera.transform.LookAt(Vector3.zero);
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

        static string Argument(string name, string fallback = null)
        {
            foreach (string argument in Environment.GetCommandLineArgs())
                if (argument.StartsWith(name + "=")) return argument.Substring(name.Length + 1);
            return fallback ?? throw new ArgumentException($"Missing command line argument {name}=<value>.");
        }
    }
}
