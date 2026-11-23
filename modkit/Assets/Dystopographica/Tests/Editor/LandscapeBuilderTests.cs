using System;
using System.IO;
using Neo3.ModKit.Public.Collections;
using Neo3.ModKit.Public.Components;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

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
            worldSize = 200f, peakHeight = 10f, lowestDepth = 2f, smoothing = 0f, knee = 0f,
            size200 = false, size400 = false,
        };

        // Climbs from 0 on the west edge to 1 on the east edge.
        static Heightmap Ramp(int size = 33)
        {
            var values = new float[size * size];
            for (int i = 0; i < values.Length; i++) values[i] = i % size / (size - 1f);
            return new Heightmap(size, size, values);
        }

        static T Load<T>(string file) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(Folder + "/" + file);

        [TearDown]
        public void DeleteTheMod()
        {
            AssetDatabase.DeleteAsset(Folder);
        }

        [Test]
        public void Make_BuildsATerrainThatFollowsTheCurve()
        {
            LandscapeBuilder.Make(Settings(), Ramp());

            var prefab = Load<GameObject>("Landscape.prefab");
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
            Assert.AreSame(data, terrain.GetComponent<TerrainCollider>().terrainData);
            Assert.AreSame(Load<TerrainData>("Terrain Data.asset"), data);
            Assert.AreSame(Load<Material>("Terrain.mat"), terrain.materialTemplate);
        }

        [Test]
        public void Make_PutsTheWholeBandOnWorldZero()
        {
            LandscapeBuilder.Make(Settings(), Ramp());

            var terrain = Load<GameObject>("Landscape.prefab").GetComponentInChildren<Terrain>();

            // Columns 13 to 19 hold the values from 0.40625 to 0.59375.
            for (int x = 13; x <= 19; x++)
                Assert.AreEqual(0f, terrain.transform.localPosition.y + terrain.terrainData.GetHeight(x, 5), 1e-5f, $"column {x}");
        }

        [Test]
        public void Make_PutsTheWaterAtTheWaterDepth_WithItsShoreAtTheWaterCursor()
        {
            LandscapeSettings settings = Settings();
            settings.water = 0.125f;
            settings.waterDepth = 0.5f;

            LandscapeBuilder.Make(settings, Ramp());

            GameObject prefab = Load<GameObject>("Landscape.prefab");
            Transform water = prefab.transform.Find("Water");
            var terrain = prefab.GetComponentInChildren<Terrain>();
            float Ground(int column) => terrain.transform.localPosition.y + terrain.terrainData.GetHeight(column, 16);

            Assert.AreEqual(-0.5f, water.localPosition.y);
            // Column 4 holds the value 0.125, the water cursor: the shoreline.
            Assert.AreEqual(-0.5f, Ground(4), 1e-3f);
            Assert.AreEqual(-2f, Ground(3), 1e-3f, "under water, at the lowest depth");
            Assert.Greater(Ground(5), -0.5f, "dry shore");
            Assert.AreEqual(20f, water.localScale.x, "Unity's plane is 10 units wide");
            Assert.AreSame(Load<Material>("Water.mat"), water.GetComponent<MeshRenderer>().sharedMaterial);
        }

        [Test]
        public void Make_WithoutAWaterPlane_LeavesItOut()
        {
            LandscapeSettings settings = Settings();
            settings.waterPlane = false;

            LandscapeBuilder.Make(settings, Ramp());

            Assert.IsNull(Load<GameObject>("Landscape.prefab").transform.Find("Water"));
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
            Assert.IsTrue(landscape.DisableDefaultGridMesh);
            Assert.IsTrue(landscape.DisableDefaultLandscape);
            Assert.IsFalse(landscape.AllowLargerMapSizes, "this landscape only fills the 100% build area");
            Assert.AreEqual(AssetDatabase.AssetPathToGUID(Folder + "/Landscape.prefab"), landscape.LandscapeResource.AssetGUID);

            // The ModKit reads both pictures back when it compiles.
            Assert.AreSame(landscape.Thumbnail, manifest.PreviewImage);
            Assert.IsTrue(landscape.Thumbnail.isReadable);
            Assert.AreEqual(1024, landscape.Thumbnail.width);
            Assert.AreEqual(512, landscape.Thumbnail.height);
        }

        // The game never scales a landscape: a larger map size only widens the build area.
        // So each ticked size gets a landscape of its own, scaled up to fill that area.
        [Test]
        public void Make_BuildsAScaledUpLandscapeForEachTickedMapSize()
        {
            LandscapeSettings settings = Settings();
            settings.size400 = true;

            LandscapeBuilder.Make(settings, Ramp());

            // The 400% build area is twice as wide, so that landscape is twice the size.
            var prefab = Load<GameObject>("Landscape 400.prefab");
            var terrain = prefab.GetComponentInChildren<Terrain>();
            Assert.AreEqual(400f, terrain.terrainData.size.x);
            Assert.AreEqual(-200f, terrain.transform.localPosition.x);
            Assert.AreEqual(20f, terrain.transform.localPosition.y + terrain.terrainData.GetHeight(32, 16), 2e-3f, "the peak is twice as high");
            Assert.AreEqual(-0.7f, prefab.transform.Find("Water").localPosition.y, "the water stays as far below the city");
            Assert.AreEqual(40f, prefab.transform.Find("Water").localScale.x);
            Assert.AreSame(Load<TerrainData>("Terrain Data 400.asset"), terrain.terrainData);

            Assert.AreEqual(200f, Load<GameObject>("Landscape.prefab").GetComponentInChildren<Terrain>().terrainData.size.x);
            Assert.IsNull(Load<GameObject>("Landscape 200.prefab"), "200% was not ticked");

            ModLandscapeDefinition[] landscapes = Load<ModContentManifest>("Manifest.asset").LandscapeCollection.Landscapes;
            Assert.AreEqual(2, landscapes.Length);
            Assert.AreEqual("Test Valley (100%)", landscapes[0].Name);
            Assert.AreEqual("Test Valley (400%)", landscapes[1].Name);
            Assert.AreEqual(landscapes[0].Id + "-400", landscapes[1].Id);
            Assert.IsFalse(landscapes[0].AllowLargerMapSizes);
            Assert.IsTrue(landscapes[1].AllowLargerMapSizes);
            Assert.AreEqual(AssetDatabase.AssetPathToGUID(Folder + "/Landscape 400.prefab"), landscapes[1].LandscapeResource.AssetGUID);
        }

        [Test]
        public void MakeAgain_WithAMapSizeUnticked_RemovesThatLandscape()
        {
            LandscapeSettings settings = Settings();
            settings.size200 = true;
            LandscapeBuilder.Make(settings, Ramp());
            Assert.IsNotNull(Load<GameObject>("Landscape 200.prefab"));

            settings.size200 = false;
            LandscapeBuilder.Make(settings, Ramp());

            Assert.IsNull(Load<GameObject>("Landscape 200.prefab"));
            Assert.IsNull(Load<TerrainData>("Terrain Data 200.asset"));
            Assert.IsNull(Load<ModLandscapeDefinition>("Landscape Definition 200.asset"));
            Assert.AreEqual(1, Load<ModContentManifest>("Manifest.asset").LandscapeCollection.Landscapes.Length);
            Assert.AreEqual("Test Valley", Load<ModLandscapeDefinition>("Landscape Definition.asset").Name);
        }

        [Test]
        public void MakeAgain_KeepsEveryIdSoSavedCitiesStillFindTheLandscape()
        {
            LandscapeBuilder.Make(Settings(), Ramp());
            string manifestId = Load<ModContentManifest>("Manifest.asset").Id;
            string landscapeId = Load<ModLandscapeDefinition>("Landscape Definition.asset").Id;
            string prefabGuid = AssetDatabase.AssetPathToGUID(Folder + "/Landscape.prefab");
            string terrainGuid = AssetDatabase.AssetPathToGUID(Folder + "/Terrain Data.asset");

            LandscapeSettings changed = Settings();
            changed.landscapeName = "Renamed";
            changed.peakHeight = 30f;
            LandscapeBuilder.Make(changed, Ramp());

            Assert.AreEqual(manifestId, Load<ModContentManifest>("Manifest.asset").Id);
            Assert.AreEqual(landscapeId, Load<ModLandscapeDefinition>("Landscape Definition.asset").Id);
            Assert.AreEqual("Renamed", Load<ModLandscapeDefinition>("Landscape Definition.asset").Name);
            Assert.AreEqual(prefabGuid, AssetDatabase.AssetPathToGUID(Folder + "/Landscape.prefab"));
            Assert.AreEqual(terrainGuid, AssetDatabase.AssetPathToGUID(Folder + "/Terrain Data.asset"));
            Assert.AreEqual(32f, Load<TerrainData>("Terrain Data.asset").size.y);
        }

        [Test]
        public void MakeAgain_LeavesMaterialsTheUserHasEdited()
        {
            LandscapeBuilder.Make(Settings(), Ramp());
            Load<Material>("Water.mat").SetColor("_BaseColor", Color.red);

            LandscapeBuilder.Make(Settings(), Ramp());

            Assert.AreEqual(Color.red, Load<Material>("Water.mat").GetColor("_BaseColor"));
        }

        [Test]
        public void Make_SavesTheSettingsWithTheMod()
        {
            LandscapeSettings settings = Settings();
            Assert.IsFalse(settings.MatchesSavedPreset());

            LandscapeBuilder.Make(settings, Ramp());

            Assert.IsTrue(settings.MatchesSavedPreset());
            Assert.AreEqual(settings.ToJson(), LandscapeSettings.Load(ModName).ToJson());
            settings.peakHeight += 1f;
            Assert.IsFalse(settings.MatchesSavedPreset());
        }

        [Test]
        public void Make_WithProblems_RefusesAndWritesNothing()
        {
            LandscapeSettings settings = Settings();
            settings.worldSize = 0f;

            var error = Assert.Throws<InvalidOperationException>(() => LandscapeBuilder.Make(settings, Ramp()));

            StringAssert.Contains("World size", error.Message);
            Assert.IsFalse(Directory.Exists(Folder));
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
