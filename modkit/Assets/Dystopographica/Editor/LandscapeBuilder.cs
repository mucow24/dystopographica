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
        const string WaterShader = "Dystopographica/Water";
        const int RippleTextureSize = 256;
        static readonly Color WaterColour = new Color(0.23f, 0.59f, 0.55f);

        // Mods made while each map size had a landscape of its own still hold these.
        static readonly string[] FilesOfTheOldScheme =
        {
            "Landscape 200.prefab", "Landscape 400.prefab", "Terrain Data 200.asset", "Terrain Data 400.asset",
            "Landscape Definition 200.asset", "Landscape Definition 400.asset",
        };

        /// <summary>
        /// A smoothed copy of the heightmap, small enough to repaint while a cursor is
        /// dragged. Smoothing is in pixels of the full-size map.
        /// </summary>
        public static Heightmap PreviewMap(Heightmap normalized, float smoothing, int maxSize)
        {
            int factor = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(normalized.Width, normalized.Height) / (float)maxSize));
            return Smoothing.Gaussian(Resample.Shrink(normalized, factor), smoothing / factor);
        }

        /// <summary>
        /// A map with the small holes in its city plane filled. The size limit in the
        /// settings is an area in pixels of the heightmap file; `map` may be a resized
        /// copy of that file, originally `originalWidth` wide, so the limit is converted.
        /// </summary>
        public static Heightmap WithoutSpecks(Heightmap map, int originalWidth, LandscapeSettings settings)
        {
            float scale = map.Width / (float)originalWidth;
            int limit = Mathf.RoundToInt(settings.speckSize * scale * scale);
            return Specks.FillPlane(map, settings.planeStart, settings.planeEnd, limit);
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
        public static void Make(LandscapeSettings asEntered, Heightmap source)
        {
            List<string> problems = asEntered.Problems(source);
            if (problems.Count > 0) throw new InvalidOperationException(string.Join(" ", problems));

            // The preset folder goes in first. It marks the mod folder as ours to overwrite.
            Directory.CreateDirectory(asEntered.PresetFolder);
            AssetDatabase.Refresh();

            // A painted map brings its own cursors and its own answer to "is there water".
            LandscapeSettings settings = asEntered.For(source);

            string folder = settings.Folder;
            Heightmap normalized = source.Normalized();
            Heightmap terrainMap = WithoutSpecks(TerrainMap(normalized, settings.smoothing), normalized.Width, settings);
            TerrainShape shape = TerrainHeights.Build(terrainMap, settings.Curve(), settings.peakHeight, settings.BedDrop);
            TerrainData terrainData = WriteTerrainData(folder + "/Terrain Data.asset", shape, settings.WorldSize);
            Material terrainMaterial = FindOrCreateMaterial(folder + "/Terrain.mat", Color.black, 0.03f);

            // One terrain, and a landscape for each kind of water asked for on it.
            var prefabs = new Dictionary<WaterKind, GameObject>();
            foreach (WaterKind kind in new[] { WaterKind.Normal, WaterKind.Custom })
            {
                string path = $"{folder}/Landscape{LandscapeSettings.FileSuffix(kind)}.prefab";
                if (!settings.Landscapes().Contains(kind))
                {
                    // Left over from a run that asked for it. Everything in the folder is bundled.
                    if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
                    continue;
                }

                Material water = !settings.waterPlane ? null
                    : kind == WaterKind.Custom ? WriteCustomWater(folder)
                    : FindOrCreateMaterial(folder + "/Water.mat", WaterColour, 0.07f);
                prefabs[kind] = WritePrefab(path, settings, shape, terrainData, terrainMaterial, water);
            }
            foreach (string leftover in FilesOfTheOldScheme)
                if (File.Exists(folder + "/" + leftover)) AssetDatabase.DeleteAsset(folder + "/" + leftover);

            Texture2D thumbnail = WriteThumbnail(folder + "/Thumbnail.png", settings, normalized);
            ModKitBridge.WriteMod(folder, settings, prefabs, thumbnail);

            AssetDatabase.SaveAssets();
            asEntered.Save();
        }

        /// <summary>
        /// Writes the map as a picture to paint on: blue under water, green on the city
        /// plane, grey elsewhere (see PaintedMap). It is the map the preview shows,
        /// smoothed and with its specks filled, at the heightmap's full size. Chosen as
        /// the heightmap, the picture is made into a landscape as it is painted.
        /// </summary>
        public static void ExportPaintedMap(LandscapeSettings asEntered, Heightmap source, string path)
        {
            LandscapeSettings settings = asEntered.For(source);
            Heightmap normalized = source.Normalized();
            Heightmap map = WithoutSpecks(Smoothing.Gaussian(normalized, settings.smoothing), normalized.Width, settings);
            File.WriteAllBytes(path, HeightmapFile.EncodeRgb16(map.Width, map.Height, PaintedMap.Paint(map, settings)));
        }

        /// <summary>
        /// Makes the landscape, compiles the mod and copies it into installFolder. It is
        /// always made afresh: skipping that when the settings looked unchanged let a newer
        /// tool, or a newer heightmap under the same name, go uncompiled.
        /// </summary>
        public static void CompileAndInstall(LandscapeSettings settings, Heightmap source, string installFolder)
        {
            Make(settings, source);
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

            // Saved at once. A change that waits in memory can be lost if Unity reloads the file first.
            if (isNew)
            {
                AssetDatabase.CreateAsset(data, path);
            }
            else
            {
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssetIfDirty(data);
            }
            return data;
        }

        /// <summary>The terrain's and the normal water's materials are made once and then left alone, so the user's edits to them survive.</summary>
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

        /// <summary>
        /// The custom water's material and its ripple texture. Unlike the other materials
        /// this one is put back to the shader's own settings on every run: the water's
        /// look belongs to the tool, and a material keeps a copy of every setting, so one
        /// left alone would keep the look of whichever version of the tool first made
        /// it. The shader itself stays in the package; the compile pulls it into the mod
        /// because the material uses it.
        /// </summary>
        static Material WriteCustomWater(string folder)
        {
            Shader shader = Shader.Find(WaterShader);
            if (shader == null) throw new InvalidOperationException($"The shader \"{WaterShader}\" is missing. It is part of the Dystopographica package.");
            var ripples = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/Ripples.png");
            if (ripples == null) ripples = WriteRipples(folder + "/Ripples.png");

            string path = folder + "/Custom Water.mat";
            var asShipped = new Material(shader);
            asShipped.SetTexture("_Ripples", ripples);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                AssetDatabase.CreateAsset(asShipped, path);
                return asShipped;
            }

            material.shader = shader;
            material.CopyPropertiesFromMaterial(asShipped);
            UnityEngine.Object.DestroyImmediate(asShipped);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        static Texture2D WriteRipples(string path)
        {
            var picture = new Texture2D(RippleTextureSize, RippleTextureSize, TextureFormat.RGBA32, false);
            picture.SetPixels32(Ripples.NormalMap(RippleTextureSize));
            File.WriteAllBytes(path, picture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(picture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.NormalMap;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// The same layout as the ModKit's example landscape: a root at the origin, the
        /// terrain and a water plane under it.
        ///
        /// The game lets a building stand where a landscape's colliders are at city
        /// level. In the example the terrain and the water carry colliders of their
        /// own, so the ground decides, and a large building cannot overlap a shore.
        /// Here, as in the Night City mod, they carry none and one flat sheet at city
        /// level takes their place: buildings can stand anywhere.
        /// </summary>
        static GameObject WritePrefab(string path, LandscapeSettings settings, TerrainShape shape, TerrainData terrainData, Material terrainMaterial, Material waterMaterial)
        {
            var root = new GameObject("Landscape");
            try
            {
                var ground = new GameObject("Terrain");
                ground.transform.SetParent(root.transform, false);
                // Everything drawn is raised by the ground lift. The sheet below is not.
                ground.transform.localPosition = new Vector3(-settings.WorldSize / 2, shape.BaseY + settings.GroundLift, -settings.WorldSize / 2);
                var terrain = ground.AddComponent<Terrain>();
                terrain.terrainData = terrainData;
                terrain.materialTemplate = terrainMaterial;
                terrain.shadowCastingMode = ShadowCastingMode.TwoSided;
                // Unity draws distant terrain with fewer triangles, as long as that moves
                // nothing up or down by more than this many pixels. At Night City's levels
                // the shore is a step of a quarter of a unit, which is under Unity's usual
                // five pixels from quite near, and blurring the step moves the waterline a
                // long way sideways. So there the terrain gets the finest setting.
                if (settings.GroundLift > 0f) terrain.heightmapPixelError = 1f;

                if (waterMaterial != null)
                {
                    GameObject water = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    water.name = "Water";
                    water.transform.SetParent(root.transform, false);
                    water.transform.localPosition = new Vector3(0f, settings.GroundLift - settings.WaterDrop, 0f);
                    // Unity's plane is 10 units wide.
                    water.transform.localScale = new Vector3(settings.WorldSize / 10, 1f, settings.WorldSize / 10);
                    water.GetComponent<MeshRenderer>().sharedMaterial = waterMaterial;
                    UnityEngine.Object.DestroyImmediate(water.GetComponent<Collider>());
                }

                // Unity's plane again, with nothing left of it but its collider, at y = 0.
                GameObject sheet = GameObject.CreatePrimitive(PrimitiveType.Plane);
                sheet.name = "Buildable Plane";
                sheet.transform.SetParent(root.transform, false);
                sheet.transform.localScale = new Vector3(settings.WorldSize / 10, 1f, settings.WorldSize / 10);
                UnityEngine.Object.DestroyImmediate(sheet.GetComponent<MeshRenderer>());
                UnityEngine.Object.DestroyImmediate(sheet.GetComponent<MeshFilter>());
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
                Heightmap map = WithoutSpecks(PreviewMap(normalized, settings.smoothing, ThumbnailWidth), normalized.Width, settings);
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
