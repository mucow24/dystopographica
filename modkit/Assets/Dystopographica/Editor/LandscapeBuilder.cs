using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dystopographica
{
    /// <summary>Turns settings and a heightmap into the assets of a landscape mod.</summary>
    public static class LandscapeBuilder
    {
        const int ThumbnailWidth = 1024, ThumbnailHeight = 512;
        static readonly Color WaterColour = new Color(0.23f, 0.59f, 0.55f);

        /// <summary>
        /// A smoothed copy of the heightmap, small enough to repaint while a cursor is
        /// dragged. Smoothing is in pixels of the full-size map.
        /// </summary>
        public static Heightmap PreviewMap(Heightmap normalized, float smoothing, int maxSize)
        {
            int factor = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(normalized.Width, normalized.Height) / (float)maxSize));
            return Smoothing.Gaussian(Resample.Shrink(normalized, factor), smoothing / factor);
        }

        /// <summary>The smoothed heightmap at the resolution the terrain will have.</summary>
        public static Heightmap TerrainMap(Heightmap normalized, float smoothing)
        {
            int resolution = Resample.TerrainResolution(normalized.Width);
            return Smoothing.Gaussian(Resample.Bilinear(normalized, resolution), smoothing * resolution / normalized.Width);
        }

        /// <summary>
        /// Writes the mod into Assets/[mod name], or rewrites it in place. Asset files
        /// and ids stay the same from one run to the next.
        /// </summary>
        public static void Make(LandscapeSettings settings, Heightmap source)
        {
            List<string> problems = settings.Problems(source);
            if (problems.Count > 0) throw new InvalidOperationException(string.Join(" ", problems));

            // The preset folder goes in first. It marks the mod folder as ours to overwrite.
            Directory.CreateDirectory(settings.PresetFolder);
            AssetDatabase.Refresh();

            string folder = settings.Folder;
            Heightmap normalized = source.Normalized();
            Heightmap terrainMap = TerrainMap(normalized, settings.smoothing);
            Material terrainMaterial = FindOrCreateMaterial(folder + "/Terrain.mat", Color.black, 0.03f);
            Material waterMaterial = FindOrCreateMaterial(folder + "/Water.mat", WaterColour, 0.07f);

            // One landscape per map size: the game widens the build area but never scales
            // the landscape, so each size needs the map at its own scale.
            var prefabs = new Dictionary<int, GameObject>();
            foreach (int percent in LandscapeSettings.AllMapSizes)
            {
                string suffix = LandscapeSettings.FileSuffix(percent);
                string terrainPath = $"{folder}/Terrain Data{suffix}.asset", prefabPath = $"{folder}/Landscape{suffix}.prefab";
                if (!settings.MapSizes().Contains(percent))
                {
                    // Left over from a run that had this size ticked. Everything in the folder is bundled.
                    if (File.Exists(prefabPath)) AssetDatabase.DeleteAsset(prefabPath);
                    if (File.Exists(terrainPath)) AssetDatabase.DeleteAsset(terrainPath);
                    continue;
                }

                LandscapeSettings sized = settings.ForMapSize(percent);
                TerrainShape shape = TerrainHeights.Build(terrainMap, sized.Curve(), sized.peakHeight, sized.lowestDepth);
                TerrainData terrainData = WriteTerrainData(terrainPath, shape, sized.worldSize);
                prefabs[percent] = WritePrefab(prefabPath, sized, shape, terrainData, terrainMaterial, waterMaterial);
            }

            Texture2D thumbnail = WriteThumbnail(folder + "/Thumbnail.png", settings, normalized);
            ModKitBridge.WriteMod(folder, settings, prefabs, thumbnail);

            AssetDatabase.SaveAssets();
            settings.Save();
        }

        /// <summary>
        /// Compiles the mod and copies it into installFolder. The landscape is made first
        /// if it does not exist yet or the settings have changed since it was made.
        /// </summary>
        public static void CompileAndInstall(LandscapeSettings settings, Heightmap source, string installFolder)
        {
            if (!settings.MatchesSavedPreset() || !ModKitBridge.HasMod(settings.Folder)) Make(settings, source);
            ModKitBridge.Install(ModKitBridge.Compile(settings.Folder), installFolder);
        }

        static TerrainData WriteTerrainData(string path, TerrainShape shape, float worldSize)
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            bool isNew = data == null;
            if (isNew) data = new TerrainData();

            // Resolution goes first: setting it resets the size.
            data.heightmapResolution = shape.Heights.GetLength(0);
            data.size = new Vector3(worldSize, shape.SizeY, worldSize);
            data.SetHeights(0, 0, shape.Heights);

            if (isNew) AssetDatabase.CreateAsset(data, path);
            else EditorUtility.SetDirty(data);
            return data;
        }

        /// <summary>Materials are made once and then left alone, so the user's edits to them survive.</summary>
        static Material FindOrCreateMaterial(string path, Color colour, float smoothness)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>The same layout as the ModKit's example landscape: a root at the origin, the terrain and a water plane under it.</summary>
        static GameObject WritePrefab(string path, LandscapeSettings settings, TerrainShape shape, TerrainData terrainData, Material terrainMaterial, Material waterMaterial)
        {
            var root = new GameObject("Landscape");
            try
            {
                var ground = new GameObject("Terrain");
                ground.transform.SetParent(root.transform, false);
                ground.transform.localPosition = new Vector3(-settings.worldSize / 2, shape.BaseY, -settings.worldSize / 2);
                var terrain = ground.AddComponent<Terrain>();
                terrain.terrainData = terrainData;
                terrain.materialTemplate = terrainMaterial;
                terrain.shadowCastingMode = ShadowCastingMode.TwoSided;
                ground.AddComponent<TerrainCollider>().terrainData = terrainData;

                if (settings.waterPlane)
                {
                    GameObject water = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    water.name = "Water";
                    water.transform.SetParent(root.transform, false);
                    water.transform.localPosition = new Vector3(0f, -settings.waterDepth, 0f);
                    // Unity's plane is 10 units wide.
                    water.transform.localScale = new Vector3(settings.worldSize / 10, 1f, settings.worldSize / 10);
                    water.GetComponent<MeshRenderer>().sharedMaterial = waterMaterial;
                }
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The picture the game shows for the landscape and the mod: the user's own file,
        /// or else the preview. The ModKit reads it back at compile time, so it has to be
        /// imported readable.
        /// </summary>
        static Texture2D WriteThumbnail(string path, LandscapeSettings settings, Heightmap normalized)
        {
            if (File.Exists(settings.thumbnailPath))
            {
                File.Copy(settings.thumbnailPath, path, true);
            }
            else
            {
                Heightmap map = PreviewMap(normalized, settings.smoothing, ThumbnailWidth);
                Color32[] painted = Preview.Paint(map, settings.PaintedWater, settings.planeStart, settings.planeEnd);
                var picture = new Texture2D(ThumbnailWidth, ThumbnailHeight, TextureFormat.RGBA32, false);
                picture.SetPixels32(Preview.Crop(painted, map.Width, map.Height, ThumbnailWidth, ThumbnailHeight));
                File.WriteAllBytes(path, picture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(picture);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.isReadable = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
