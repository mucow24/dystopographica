using System;
using System.IO;
using Neo3.ModKit.Public.Collections;
using Neo3.ModKit.Public.Components;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dystopographica.Tests
{
    /// <summary>Builds a real mod folder inside the project, then deletes it.</summary>
    public class LandscapeBuilderTests
    {
        const string ModName = "Dystopographica Test Mod";
        const string Folder = "Assets/" + ModName;

        static LandscapeSettings Settings() => new LandscapeSettings
        {
            modName = ModName, author = "Tester", version = 3, minorVersion = 2, landscapeName = "Test Valley",
            water = 0.1f, planeStart = 0.4f, planeEnd = 0.6f,
            mapSize = 100, buildAreaShare = 0.25f, peakHeight = 10f, lowestDepth = 2f, smoothing = 0f, knee = 0f,
        };

        // A map with water is in the first file. The second is the same map with normal
        // water, when that is asked for, or else a dry map.
        const string Custom = "Landscape Custom Water.prefab", Plain = "Landscape.prefab";

        // Climbs from 0 on the west edge to 1 on the east edge.
        static Heightmap Ramp(int size = 33)
        {
            var values = new float[size * size];
            for (int i = 0; i < values.Length; i++) values[i] = i % size / (size - 1f);
            return new Heightmap(size, size, values);
        }

        static T Load<T>(string file) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(Folder + "/" + file);

        /// <summary>
        /// The height at which something dropped onto a landscape at (x, z) comes to rest:
        /// the highest of the landscape's own colliders there.
        /// </summary>
        static float LandsAt(GameObject landscape, float x, float z)
        {
            Physics.SyncTransforms();
            float highest = float.NegativeInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(x, 100f, z), Vector3.down, 200f))
                if (hit.transform.IsChildOf(landscape.transform)) highest = Mathf.Max(highest, hit.point.y);
            Assert.AreNotEqual(float.NegativeInfinity, highest, $"something to land on at ({x}, {z})");
            return highest;
        }

        [TearDown]
        public void DeleteTheMod()
        {
            AssetDatabase.DeleteAsset(Folder);
        }

        [Test]
        public void Make_OnADryMap_BuildsATerrainThatFollowsTheCurve()
        {
            LandscapeSettings settings = Settings();
            settings.waterPlane = false;

            LandscapeBuilder.Make(settings, Ramp());

            var prefab = Load<GameObject>(Plain);
            var terrain = prefab.GetComponentInChildren<Terrain>();
            TerrainData data = terrain.terrainData;
            Vector3 corner = terrain.transform.localPosition;

            Assert.AreEqual(Vector3.zero, prefab.transform.position);
            Assert.AreEqual(33, data.heightmapResolution);
            Assert.AreEqual(new Vector3(200f, 12f, 200f), data.size);
            Assert.AreEqual(-100f, corner.x);
            Assert.AreEqual(-100f, corner.z);
            Assert.AreEqual(-2f, corner.y + data.GetHeight(0, 16), 1e-3f, "west edge, the lowest point");
            Assert.AreEqual(10f, corner.y + data.GetHeight(32, 16), 1e-3f, "east edge, the highest point");
            Assert.AreSame(Load<TerrainData>("Terrain Data.asset"), data);
            Assert.AreSame(Load<Material>("Terrain.mat"), terrain.materialTemplate);
        }

        // With no water for a building to stand in, the land is drawn at y = 0, the
        // level the game stands buildings on.
        [Test]
        public void Make_OnADryMap_PutsTheWholeBandWhereBuildingsStand()
        {
            LandscapeSettings settings = Settings();
            settings.waterPlane = false;

            LandscapeBuilder.Make(settings, Ramp());

            var terrain = Load<GameObject>(Plain).GetComponentInChildren<Terrain>();

            // Columns 13 to 19 hold the values from 0.40625 to 0.59375.
            for (int x = 13; x <= 19; x++)
                Assert.AreEqual(0f, terrain.transform.localPosition.y + terrain.terrainData.GetHeight(x, 5), 1e-5f, $"column {x}");
            Assert.AreEqual(5f, terrain.heightmapPixelError, "with no shallow coast to keep sharp, Unity's usual detail will do");
        }

        [Test]
        public void Make_ResamplesTheHeightmapToASizeUnityAccepts()
        {
            LandscapeBuilder.Make(Settings(), Ramp(40));

            Assert.AreEqual(33, Load<TerrainData>("Terrain Data.asset").heightmapResolution);
        }

        [Test]
        public void Make_FillsInTheModKitAssets()
        {
            LandscapeBuilder.Make(Settings(), Ramp());

            var manifest = Load<ModContentManifest>("Manifest.asset");
            Assert.AreEqual(ModName, manifest.Name);
            Assert.AreEqual("Tester", manifest.Author);
            Assert.AreEqual(3, manifest.Version);
            Assert.AreEqual(2, manifest.MinorVersion);
            Assert.IsTrue(Guid.TryParse(manifest.Id, out _), "the manifest id is a GUID");
            Assert.AreEqual(1, manifest.Includes.Count, "the base content include");
            Assert.IsNotNull(manifest.Includes[0]);

            Assert.AreEqual(1, manifest.LandscapeCollection.Landscapes.Length);
            ModLandscapeDefinition landscape = manifest.LandscapeCollection.Landscapes[0];
            Assert.AreEqual("Test Valley", landscape.Name);
            StringAssert.StartsWith("test-valley-", landscape.Id);
            // The landscape takes the place of the game's own grid and mountains, always.
            Assert.IsTrue(landscape.DisableDefaultGridMesh);
            Assert.IsTrue(landscape.DisableDefaultLandscape);
            Assert.AreEqual(AssetDatabase.AssetPathToGUID(Folder + "/" + Custom), landscape.LandscapeResource.AssetGUID);

            // The ModKit reads both pictures back when it compiles.
            Assert.AreSame(landscape.Thumbnail, manifest.PreviewImage);
            Assert.IsTrue(landscape.Thumbnail.isReadable);
            Assert.AreEqual(1024, landscape.Thumbnail.width);
            Assert.AreEqual(512, landscape.Thumbnail.height);
        }

        // A map with water is one landscape, with our own rippling, mirroring water.
        [Test]
        public void Make_GivesAMapWithWaterOneLandscape_WithOurOwnWater()
        {
            LandscapeBuilder.Make(Settings(), Ramp());

            Material water = Load<GameObject>(Custom).transform.Find("Water").GetComponent<MeshRenderer>().sharedMaterial;
            Assert.AreSame(Load<Material>("Custom Water.mat"), water);
            Assert.AreEqual("Dystopographica/Water", water.shader.name);
            Texture ripples = water.GetTexture("_Ripples");
            Assert.AreSame(Load<Texture2D>("Ripples.png"), ripples);
            Assert.AreEqual(TextureImporterType.NormalMap, ((TextureImporter)AssetImporter.GetAtPath(Folder + "/Ripples.png")).textureType);
            Assert.AreEqual(TextureWrapMode.Repeat, ripples.wrapMode);

            Assert.IsNull(Load<GameObject>(Plain), "no landscape with normal water unless it is asked for");
            Assert.IsNull(Load<ModLandscapeDefinition>("Landscape Definition.asset"));
            Assert.IsNull(Load<Material>("Water.mat"));
        }

        // On request the mod holds the same map a second time, with flat, still water
        // drawn by Unity's own shader. The player picks, as with the Workshop maps that
        // offer both. Ours comes first and has the plain name.
        [Test]
        public void Make_WithNormalWaterAskedFor_AddsTheSameMapWithNormalWater()
        {
            LandscapeSettings settings = Settings();
            settings.normalWater = true;

            LandscapeBuilder.Make(settings, Ramp());

            GameObject normal = Load<GameObject>(Plain), custom = Load<GameObject>(Custom);
            Assert.AreSame(normal.GetComponentInChildren<Terrain>().terrainData, custom.GetComponentInChildren<Terrain>().terrainData);
            Assert.AreEqual(normal.transform.Find("Water").localPosition, custom.transform.Find("Water").localPosition);
            Assert.AreEqual(normal.transform.Find("Water").localScale, custom.transform.Find("Water").localScale);
            Material still = normal.transform.Find("Water").GetComponent<MeshRenderer>().sharedMaterial;
            Assert.AreSame(Load<Material>("Water.mat"), still);
            Assert.AreEqual("Universal Render Pipeline/Lit", still.shader.name);

            ModLandscapeDefinition[] landscapes = Load<ModContentManifest>("Manifest.asset").LandscapeCollection.Landscapes;
            Assert.AreEqual(2, landscapes.Length);
            Assert.AreEqual("Test Valley", landscapes[0].Name);
            Assert.AreEqual("Test Valley (normal water)", landscapes[1].Name);
            Assert.AreEqual(landscapes[1].Id + "-custom-water", landscapes[0].Id);
            Assert.AreEqual(AssetDatabase.AssetPathToGUID(Folder + "/" + Custom), landscapes[0].LandscapeResource.AssetGUID);
            Assert.AreEqual(AssetDatabase.AssetPathToGUID(Folder + "/" + Plain), landscapes[1].LandscapeResource.AssetGUID);
        }

        // The Night City mod's way. The game judges where a building may stand by what
        // a landscape's colliders say, so there the terrain says nothing and one flat,
        // unseen sheet at city level spans the map: large buildings can stand over the
        // water and hard against a shore.
        [Test]
        public void Make_LetsBuildingsStandAnywhere_OnOneFlatSheetAtCityLevel()
        {
            LandscapeSettings settings = Settings();
            settings.normalWater = true;

            LandscapeBuilder.Make(settings, Ramp());

            foreach (string file in new[] { Custom, Plain })
            {
                GameObject landscape = UnityEngine.Object.Instantiate(Load<GameObject>(file));
                try
                {
                    Assert.AreEqual(0f, LandsAt(landscape, -95f, 0f), 1e-4f, file + ": over the water");
                    Assert.AreEqual(0f, LandsAt(landscape, 0f, 30f), 1e-4f, file + ": on the city plane");
                    Assert.AreEqual(0f, LandsAt(landscape, 95f, -60f), 1e-4f, file + ": inside the hill");

                    Collider[] colliders = landscape.GetComponentsInChildren<Collider>(true);
                    Assert.AreEqual(1, colliders.Length, file + ": the sheet is the only thing in the way");
                    Assert.AreEqual("Buildable Plane", colliders[0].name);
                    Assert.IsNull(colliders[0].GetComponent<Renderer>(), "nothing of it is drawn");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(landscape);
                }
            }
        }

        // Night City's levels (docs, section 3.7). The game stands every building at
        // y = 0, on the sheet. The land is drawn a little above that, and the water
        // just under the land but still above the sheet, so a building that stands
        // over the water stands in it and not in the air above it.
        [Test]
        public void Make_DrawsLandAndWaterAtNightCitysLevels()
        {
            LandscapeSettings settings = Settings();
            settings.normalWater = true;
            settings.water = 0.125f;

            LandscapeBuilder.Make(settings, Ramp());

            foreach (string file in new[] { Custom, Plain })
            {
                GameObject prefab = Load<GameObject>(file);
                var terrain = prefab.GetComponentInChildren<Terrain>();
                float Ground(int column) => terrain.transform.localPosition.y + terrain.terrainData.GetHeight(column, 16);

                Assert.AreEqual(0f, prefab.transform.Find("Buildable Plane").localPosition.y, file + ": the level buildings stand on");
                for (int x = 13; x <= 19; x++) Assert.AreEqual(0.18f, Ground(x), 1e-4f, $"{file}: the city plane, column {x}");
                Assert.AreEqual(0.118f, prefab.transform.Find("Water").localPosition.y, 1e-6f, file + ": the water");
                Assert.AreEqual(20f, prefab.transform.Find("Water").localScale.x, file + ": as wide as the map (Unity's plane is 10 units wide)");
                // Column 4 holds the value 0.125, the water cursor: the shoreline.
                Assert.AreEqual(0.118f, Ground(4), 1e-3f, file + ": the shoreline");
                Assert.AreEqual(-0.08f, Ground(3), 1e-3f, file + ": the sea bed, just under the water");
                // Unity draws distant terrain with fewer triangles while that moves nothing up or
                // down by more than this many pixels. A shore that is a step of a quarter of a
                // unit is under five pixels from quite near, and blurring the step moves the
                // waterline a long way sideways: the coast went blocky. So the finest setting.
                Assert.AreEqual(1f, terrain.heightmapPixelError, file + ": the coast stays sharp from far away");
                Assert.AreEqual(0.18f + 10f, Ground(32), 1e-3f, file + ": and the peak its height above it");
            }
        }

        [Test]
        public void Make_OnADryMap_LetsBuildingsStandAnywhereToo()
        {
            LandscapeSettings settings = Settings();
            settings.waterPlane = false;

            LandscapeBuilder.Make(settings, Ramp());

            GameObject landscape = UnityEngine.Object.Instantiate(Load<GameObject>(Plain));
            try
            {
                Assert.AreEqual(0f, LandsAt(landscape, -95f, 0f), 1e-4f, "over the valley");
                Assert.AreEqual(0f, LandsAt(landscape, 95f, -60f), 1e-4f, "inside the hill");
                Assert.AreEqual(1, landscape.GetComponentsInChildren<Collider>(true).Length, "the sheet is the only thing in the way");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(landscape);
            }
        }

        // The first try at mirrored buildings was a reflection probe. In the game it
        // showed nothing, and it drew part of the scene again every frame.
        [Test]
        public void Make_PutsNoReflectionProbeInTheLandscape()
        {
            LandscapeBuilder.Make(Settings(), Ramp());

            Assert.IsNull(Load<GameObject>(Custom).GetComponentInChildren<ReflectionProbe>(true));
        }

        [Test]
        public void Make_OnADryMap_BuildsOneLandscapeWithNoWater()
        {
            LandscapeSettings settings = Settings();
            settings.waterPlane = false;

            LandscapeBuilder.Make(settings, Ramp());

            Assert.IsNull(Load<GameObject>(Plain).transform.Find("Water"));
            Assert.IsNull(Load<GameObject>(Custom));
            ModLandscapeDefinition[] landscapes = Load<ModContentManifest>("Manifest.asset").LandscapeCollection.Landscapes;
            Assert.AreEqual(1, landscapes.Length);
            Assert.AreEqual("Test Valley", landscapes[0].Name);
        }

        [Test]
        public void MakeAgain_AfterTheWaterIsSwitchedOff_RemovesTheCustomWaterLandscape()
        {
            LandscapeSettings settings = Settings();
            LandscapeBuilder.Make(settings, Ramp());
            Assert.IsNotNull(Load<GameObject>("Landscape Custom Water.prefab"));

            settings.waterPlane = false;
            LandscapeBuilder.Make(settings, Ramp());

            Assert.IsNull(Load<GameObject>("Landscape Custom Water.prefab"));
            Assert.IsNull(Load<ModLandscapeDefinition>("Landscape Definition Custom Water.asset"));
            Assert.AreEqual(1, Load<ModContentManifest>("Manifest.asset").LandscapeCollection.Landscapes.Length);
        }

        // Everything in the mod folder is bundled, so a landscape that is no longer
        // asked for must not linger. The one that stays keeps its id, and the one that
        // went gets its old id back if it is asked for again: saved cities refer to them.
        [Test]
        public void MakeAgain_AfterNormalWaterIsSwitchedOff_RemovesItsLandscape_AndBringsItBackUnchanged()
        {
            LandscapeSettings settings = Settings();
            settings.normalWater = true;
            LandscapeBuilder.Make(settings, Ramp());
            string customId = Load<ModLandscapeDefinition>("Landscape Definition Custom Water.asset").Id;
            string normalId = Load<ModLandscapeDefinition>("Landscape Definition.asset").Id;

            settings.normalWater = false;
            LandscapeBuilder.Make(settings, Ramp());

            Assert.IsNull(Load<GameObject>(Plain));
            Assert.IsNull(Load<ModLandscapeDefinition>("Landscape Definition.asset"));
            ModLandscapeDefinition[] landscapes = Load<ModContentManifest>("Manifest.asset").LandscapeCollection.Landscapes;
            Assert.AreEqual(1, landscapes.Length);
            Assert.AreEqual(customId, landscapes[0].Id);

            settings.normalWater = true;
            LandscapeBuilder.Make(settings, Ramp());

            Assert.AreEqual(normalId, Load<ModLandscapeDefinition>("Landscape Definition.asset").Id);
            Assert.AreEqual(customId, Load<ModLandscapeDefinition>("Landscape Definition Custom Water.asset").Id);
        }

        // The map is sized against the build area of the map size it is made for, and
        // tagged for the game to match.
        [TestCase(100, 200f, false)]
        [TestCase(200, 282.843f, true)]
        [TestCase(400, 400f, true)]
        public void Make_SizesAndTagsTheMapForItsMapSize(int mapSize, float expectedWidth, bool allowsLarger)
        {
            LandscapeSettings settings = Settings();
            settings.mapSize = mapSize;
            settings.normalWater = true;

            LandscapeBuilder.Make(settings, Ramp());

            GameObject prefab = Load<GameObject>(Custom);
            var terrain = prefab.GetComponentInChildren<Terrain>();
            Assert.AreEqual(expectedWidth, terrain.terrainData.size.x, 1e-2f);
            Assert.AreEqual(-expectedWidth / 2, terrain.transform.localPosition.x, 1e-2f);
            Assert.AreEqual(expectedWidth / 10, prefab.transform.Find("Water").localScale.x, 1e-3f);
            Assert.AreEqual(10.26f, terrain.terrainData.size.y, 1e-4f, "heights are the same at every size: the peak height, and the sea bed 0.26 under the land");
            ModLandscapeDefinition[] landscapes = Load<ModContentManifest>("Manifest.asset").LandscapeCollection.Landscapes;
            Assert.AreEqual(2, landscapes.Length);
            foreach (ModLandscapeDefinition landscape in landscapes)
                Assert.AreEqual(allowsLarger, landscape.AllowLargerMapSizes, landscape.Name);
        }

        // Mods made before this had a landscape per map size. Everything in the mod
        // folder is bundled, so those must not linger.
        [Test]
        public void Make_RemovesTheLandscapesOfTheOldOnePerSizeScheme()
        {
            LandscapeBuilder.Make(Settings(), Ramp());
            foreach (string leftover in new[] { "Landscape 200.prefab", "Landscape 400.prefab", "Terrain Data 400.asset", "Landscape Definition 400.asset" })
                Assert.IsTrue(AssetDatabase.CopyAsset(Folder + (leftover.EndsWith(".prefab") ? "/" + Custom : leftover.StartsWith("Terrain") ? "/Terrain Data.asset" : "/Landscape Definition Custom Water.asset"), Folder + "/" + leftover), leftover);

            LandscapeBuilder.Make(Settings(), Ramp());

            foreach (string leftover in new[] { "Landscape 200.prefab", "Landscape 400.prefab", "Terrain Data 400.asset", "Landscape Definition 400.asset" })
                Assert.IsFalse(File.Exists(Folder + "/" + leftover), leftover);
        }

        [Test]
        public void MakeAgain_KeepsEveryIdSoSavedCitiesStillFindTheLandscape()
        {
            LandscapeBuilder.Make(Settings(), Ramp());
            string manifestId = Load<ModContentManifest>("Manifest.asset").Id;
            string landscapeId = Load<ModLandscapeDefinition>("Landscape Definition Custom Water.asset").Id;
            string prefabGuid = AssetDatabase.AssetPathToGUID(Folder + "/" + Custom);
            string terrainGuid = AssetDatabase.AssetPathToGUID(Folder + "/Terrain Data.asset");

            LandscapeSettings changed = Settings();
            changed.landscapeName = "Renamed";
            changed.peakHeight = 30f;
            LandscapeBuilder.Make(changed, Ramp());

            Assert.AreEqual(manifestId, Load<ModContentManifest>("Manifest.asset").Id);
            Assert.AreEqual(landscapeId, Load<ModLandscapeDefinition>("Landscape Definition Custom Water.asset").Id);
            Assert.AreEqual("Renamed", Load<ModLandscapeDefinition>("Landscape Definition Custom Water.asset").Name);
            Assert.AreEqual(prefabGuid, AssetDatabase.AssetPathToGUID(Folder + "/" + Custom));
            Assert.AreEqual(terrainGuid, AssetDatabase.AssetPathToGUID(Folder + "/Terrain Data.asset"));
            Assert.AreEqual(30.26f, Load<TerrainData>("Terrain Data.asset").size.y, 1e-4f);
        }

        [Test]
        public void MakeAgain_LeavesMaterialsTheUserHasEdited()
        {
            LandscapeSettings settings = Settings();
            settings.normalWater = true;
            LandscapeBuilder.Make(settings, Ramp());
            Load<Material>("Water.mat").SetColor("_BaseColor", Color.red);

            LandscapeBuilder.Make(settings, Ramp());

            Assert.AreEqual(Color.red, Load<Material>("Water.mat").GetColor("_BaseColor"));
        }

        // The custom water is the exception. Its look belongs to the tool, which is
        // still being tuned against the game, and a material keeps its own copy of
        // every setting. So each Make puts the material back to the shader's settings,
        // and a mod first made with an older tool gets today's water.
        [Test]
        public void MakeAgain_BringsTheCustomWaterUpToDate()
        {
            LandscapeBuilder.Make(Settings(), Ramp());
            string guid = AssetDatabase.AssetPathToGUID(Folder + "/Custom Water.mat");
            Material water = Load<Material>("Custom Water.mat");
            water.SetColor("_BaseColor", Color.red);
            water.SetFloat("_RippleSize", 10f);
            EditorUtility.SetDirty(water);
            AssetDatabase.SaveAssets();

            LandscapeBuilder.Make(Settings(), Ramp());

            var asShipped = new Material(Shader.Find("Dystopographica/Water"));
            water = Load<Material>("Custom Water.mat");
            Assert.AreEqual(asShipped.GetColor("_BaseColor"), water.GetColor("_BaseColor"));
            Assert.AreEqual(asShipped.GetFloat("_RippleSize"), water.GetFloat("_RippleSize"));
            Assert.AreSame(Load<Texture2D>("Ripples.png"), water.GetTexture("_Ripples"), "it keeps its ripples");
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(Folder + "/Custom Water.mat"), "and is the same asset, so the prefab still points at it");
            UnityEngine.Object.DestroyImmediate(asShipped);
        }

        [Test]
        public void Make_SavesTheSettingsWithTheMod()
        {
            LandscapeSettings settings = Settings();
            Assert.IsNull(LandscapeSettings.Load(ModName));

            LandscapeBuilder.Make(settings, Ramp());

            Assert.AreEqual(settings.ToJson(), LandscapeSettings.Load(ModName).ToJson());
        }

        [Test]
        public void Make_WithProblems_RefusesAndWritesNothing()
        {
            LandscapeSettings settings = Settings();
            settings.mapSize = 300;

            var error = Assert.Throws<InvalidOperationException>(() => LandscapeBuilder.Make(settings, Ramp()));

            StringAssert.Contains("Map size", error.Message);
            Assert.IsFalse(Directory.Exists(Folder));
        }

        [Test]
        public void Make_FillsSmallHolesInThePlane()
        {
            // Flat plane, one pit in the middle, one peak in a corner so the values span 0 to 1.
            var values = new float[33 * 33];
            for (int i = 0; i < values.Length; i++) values[i] = 0.5f;
            values[16 * 33 + 16] = 0f;
            values[0] = 1f;
            LandscapeSettings settings = Settings();
            settings.speckSize = 2;

            LandscapeBuilder.Make(settings, new Heightmap(33, 33, values));

            var terrain = Load<GameObject>(Custom).GetComponentInChildren<Terrain>();
            Assert.AreEqual(0.18f, terrain.transform.localPosition.y + terrain.terrainData.GetHeight(16, 16), 1e-4f, "the pit is gone: level with the land around it");
            Assert.AreEqual(10.18f, terrain.transform.localPosition.y + terrain.terrainData.GetHeight(0, 32), 1e-3f, "the peak at the map's edge is not");
        }

        // The limit is an area in pixels of the heightmap file. The preview and the
        // terrain are other sizes, so there it is converted.
        [Test]
        public void WithoutSpecks_CountsTheLimitInPixelsOfTheOriginalHeightmap()
        {
            // A 4 x 4 map standing in for an 8 x 8 original: each pixel here is four there.
            var values = new float[16];
            for (int i = 0; i < values.Length; i++) values[i] = 0.5f;
            values[5] = 0.1f;
            var half = new Heightmap(4, 4, values);
            var settings = new LandscapeSettings { planeStart = 0.4f, planeEnd = 0.6f };

            settings.speckSize = 8;
            Assert.AreEqual(0.4f, LandscapeBuilder.WithoutSpecks(half, 8, settings).Values[5], "four original pixels is under eight");

            settings.speckSize = 4;
            Assert.AreEqual(0.1f, LandscapeBuilder.WithoutSpecks(half, 8, settings).Values[5], "but not under four");
        }

        [TestCase(2000, 500)]
        [TestCase(512, 512)]
        [TestCase(100, 100)]
        public void PreviewMap_IsShrunkToFitThePreview(int size, int expected)
        {
            Heightmap map = LandscapeBuilder.PreviewMap(new Heightmap(size, size, new float[size * size]), 1f, 512);

            Assert.AreEqual(expected, map.Width);
        }
    }
}
