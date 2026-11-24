using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Dystopographica.Tests
{
    public class ModKitBridgeTests
    {
        const string ModName = "Dystopographica Test Compile";

        string scratch;

        [SetUp]
        public void MakeScratchFolder()
        {
            scratch = Path.Combine(Path.GetTempPath(), "dystopographica-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
        }

        [TearDown]
        public void CleanUp()
        {
            Directory.Delete(scratch, true);
            AssetDatabase.DeleteAsset("Assets/" + ModName);
            string compiled = "Assets/Export~/" + ModName;
            if (Directory.Exists(compiled)) Directory.Delete(compiled, true);
        }

        [Test]
        public void ModsFolder_IsWhereDystopikaLoadsLocalMods()
        {
            string folder = ModKitBridge.ModsFolder.Replace('\\', '/');

            StringAssert.EndsWith("/AppData/LocalLow/Voids Within/Dystopika/Mods", folder);
        }

        [Test]
        public void Install_ReplacesWhatWasInstalledBefore()
        {
            string compiled = Path.Combine(scratch, "compiled"), installed = Path.Combine(scratch, "Mods", "My Mod");
            Directory.CreateDirectory(Path.Combine(compiled, "StandaloneWindows64"));
            File.WriteAllText(Path.Combine(compiled, "manifest.json"), "new");
            File.WriteAllText(Path.Combine(compiled, "StandaloneWindows64", "content.bundle"), "bundle");
            Directory.CreateDirectory(installed);
            File.WriteAllText(Path.Combine(installed, "manifest.json"), "old");
            File.WriteAllText(Path.Combine(installed, "stale.png"), "stale");

            ModKitBridge.Install(compiled, installed);

            Assert.AreEqual("new", File.ReadAllText(Path.Combine(installed, "manifest.json")));
            Assert.AreEqual("bundle", File.ReadAllText(Path.Combine(installed, "StandaloneWindows64", "content.bundle")));
            Assert.IsFalse(File.Exists(Path.Combine(installed, "stale.png")));
            Assert.IsTrue(File.Exists(Path.Combine(compiled, "manifest.json")), "the compiled copy stays");
        }

        [Test]
        public void Install_WithNothingCompiled_LeavesTheInstalledModAlone()
        {
            string installed = Path.Combine(scratch, "Mods", "My Mod");
            Directory.CreateDirectory(installed);
            File.WriteAllText(Path.Combine(installed, "manifest.json"), "old");

            Assert.Throws<DirectoryNotFoundException>(() => ModKitBridge.Install(Path.Combine(scratch, "missing"), installed));

            Assert.AreEqual("old", File.ReadAllText(Path.Combine(installed, "manifest.json")));
        }

        [Test]
        public void CompileAndInstall_ProducesAModTheGameCanLoad()
        {
            var settings = new LandscapeSettings
            {
                modName = ModName, author = "Tester", version = 2, landscapeName = "Compiled Valley",
                water = 0.1f, planeStart = 0.4f, planeEnd = 0.6f, smoothing = 0f,
                // Both kinds of water, to see each of them through the compile.
                normalWater = true,
            };
            var values = new float[33 * 33];
            for (int i = 0; i < values.Length; i++) values[i] = i % 33 / 32f;
            string installed = Path.Combine(scratch, "Mods", ModName);

            // A flat landscape made earlier with these very settings must not be what gets
            // compiled. The tool used to skip the remake when the settings had not changed,
            // and then a newer tool, or a newer heightmap, never reached the game.
            LandscapeBuilder.Make(settings, new Heightmap(33, 33, new float[33 * 33]));

            LandscapeBuilder.CompileAndInstall(settings, new Heightmap(33, 33, values), installed);

            string manifest = File.ReadAllText(Path.Combine(installed, "manifest.json"));
            StringAssert.Contains("\"Name\": \"" + ModName + "\"", manifest);
            StringAssert.Contains("\"Author\": \"Tester\"", manifest);
            // One landscape per kind of water, both made for the 400% map size.
            StringAssert.Contains("\"Name\": \"Compiled Valley\"", manifest);
            StringAssert.Contains("\"Name\": \"Compiled Valley (normal water)\"", manifest);
            StringAssert.Contains("\"AllowLargeMapSizes\": true", manifest);
            StringAssert.DoesNotContain("\"AllowLargeMapSizes\": false", manifest);
            StringAssert.Contains("\"Id\": \"compiled-valley-", manifest);
            Assert.IsTrue(File.Exists(Path.Combine(installed, "preview.png")));
            Assert.AreEqual(2, Directory.GetFiles(installed, "compiled-valley-*.png").Length, "a thumbnail for each landscape");
            Assert.IsNotEmpty(Directory.GetFiles(Path.Combine(installed, "StandaloneWindows64"), "*.bundle"));
            Assert.IsNotEmpty(Directory.GetFiles(Path.Combine(installed, "StandaloneWindows64"), "catalog*.json"));

            // The game finds the prefab through the catalog, by the id the landscape definition holds.
            string content = Path.Combine(installed, "StandaloneWindows64");
            string prefabGuid = AssetDatabase.AssetPathToGUID("Assets/" + ModName + "/Landscape.prefab");
            var catalog = JsonUtility.FromJson<Catalog>(File.ReadAllText(Directory.GetFiles(content, "catalog*.json")[0]));
            StringAssert.Contains(prefabGuid, Encoding.ASCII.GetString(Convert.FromBase64String(catalog.m_KeyDataString)));

            // And the bundles hold a whole landscape: terrain, heights, materials, shader, water.
            var bundles = new List<AssetBundle>();
            try
            {
                foreach (string file in Directory.GetFiles(content, "shaders_*.bundle")) bundles.Add(AssetBundle.LoadFromFile(file));
                bundles.Add(AssetBundle.LoadFromFile(Directory.GetFiles(content, "modcontent*.bundle")[0]));
                var prefab = bundles[bundles.Count - 1].LoadAsset<GameObject>("Assets/" + ModName + "/Landscape.prefab");

                var terrain = prefab.GetComponentInChildren<Terrain>();
                Assert.AreEqual(33, terrain.terrainData.heightmapResolution);
                Assert.Greater(terrain.terrainData.GetHeight(32, 16), terrain.terrainData.GetHeight(0, 16), "the heights came along");
                Assert.AreEqual("Universal Render Pipeline/Lit", terrain.materialTemplate.shader.name);
                Transform water = prefab.transform.Find("Water");
                Assert.IsNotNull(water.GetComponent<MeshFilter>().sharedMesh);
                Assert.AreEqual("Universal Render Pipeline/Lit", water.GetComponent<MeshRenderer>().sharedMaterial.shader.name);

                // The custom water brings its own shader and ripple texture, from outside the mod folder.
                var custom = bundles[bundles.Count - 1].LoadAsset<GameObject>("Assets/" + ModName + "/Landscape Custom Water.prefab");
                Assert.AreSame(terrain.terrainData, custom.GetComponentInChildren<Terrain>().terrainData, "one terrain for both");
                Material moving = custom.transform.Find("Water").GetComponent<MeshRenderer>().sharedMaterial;
                Assert.AreEqual("Dystopographica/Water", moving.shader.name);
                Assert.IsTrue(moving.shader.isSupported);
                Assert.IsNotNull(moving.GetTexture("_Ripples"));
            }
            finally
            {
                foreach (AssetBundle bundle in bundles) bundle.Unload(true);
            }
        }

        [Serializable]
        class Catalog
        {
            public string m_KeyDataString;
        }
    }
}
