using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Neo3.ModKit;
using Neo3.ModKit.Public;
using Neo3.ModKit.Public.Collections;
using Neo3.ModKit.Public.Components;
using Neo3.ModKit.Public.Editor;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Dystopographica
{
    /// <summary>
    /// The only place that talks to the Dystopika ModKit. If a ModKit update changes
    /// its API, this is the file that stops compiling.
    /// </summary>
    public static class ModKitBridge
    {
        const string ManifestFile = "/Manifest.asset";

        // Unity unloads assets that only a local variable refers to, and the Addressables
        // build triggers that. Without this the ModKit finds the manifest's landscape
        // collection gone when it writes manifest.json, and lists no landscapes.
        static ModContentManifest keptLoaded;

        /// <summary>The folder Dystopika loads locally installed mods from.</summary>
        public static string ModsFolder => ModKitPathHelper.GetLocalInstallationPath();

        public static string InstallFolder(string modName) => ModKitPathHelper.GetModInstallLocation(modName);

        public static void OpenModsFolder() => ModKitEditorHelper.ShowLocalModInstallLocation();

        public static bool HasMod(string folder) => AssetDatabase.LoadAssetAtPath<ModContentManifest>(folder + ManifestFile) != null;

        /// <summary>
        /// Creates or updates the ModKit assets that describe the mod: its manifest, its
        /// landscape collection, and a landscape definition for each map size that has a
        /// prefab. `prefabs` maps a map size (100, 200, 400) to its landscape prefab.
        /// </summary>
        public static void WriteMod(string folder, LandscapeSettings settings, IReadOnlyDictionary<int, GameObject> prefabs, Texture2D thumbnail)
        {
            var manifest = FindOrCreate<ModContentManifest>(folder + ManifestFile, out _);
            var collection = FindOrCreate<ModLandscapeCollection>(folder + "/Landscape Collection.asset", out _);

            // Ids are made once and never change, because saved cities refer to them.
            if (string.IsNullOrEmpty(manifest.Id)) manifest.Id = Guid.NewGuid().ToString();
            manifest.ModKitVersion = ModKitVersion.Version;
            manifest.Name = settings.modName;
            manifest.Author = settings.author;
            manifest.Version = settings.version;
            manifest.MinorVersion = settings.minorVersion;
            manifest.PreviewImage = thumbnail;
            EditorUtility.SetDirty(manifest);

            // The rest are private fields, so they are set the way the Inspector sets them.
            var manifestFields = new SerializedObject(manifest);
            manifestFields.FindProperty("landscapeCollection").objectReferenceValue = collection;
            SerializedProperty includes = manifestFields.FindProperty("includes");
            includes.arraySize = 1;
            includes.GetArrayElementAtIndex(0).objectReferenceValue = AuthorConfig().baseContentInclude;
            manifestFields.ApplyModifiedPropertiesWithoutUndo();

            var definitions = new List<ModLandscapeDefinition>();
            foreach (int percent in LandscapeSettings.AllMapSizes)
            {
                string path = $"{folder}/Landscape Definition{LandscapeSettings.FileSuffix(percent)}.asset";
                if (!prefabs.TryGetValue(percent, out GameObject prefab))
                {
                    // Left over from a run that had this size ticked.
                    if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
                    continue;
                }

                var landscape = FindOrCreate<ModLandscapeDefinition>(path, out bool isNew);
                var fields = new SerializedObject(landscape);
                // A new definition starts with the ModKit's placeholder id, the same in every mod.
                if (isNew)
                {
                    string id = LandscapeSettings.Slug(settings.DisplayName) + "-" + manifest.Id.Substring(0, 8);
                    fields.FindProperty("id").stringValue = percent == 100 ? id : id + "-" + percent;
                }
                fields.FindProperty("name").stringValue = settings.DisplayNameFor(percent);
                fields.FindProperty("thumbnail").objectReferenceValue = thumbnail;
                fields.FindProperty("landscapeResource.m_AssetGUID").stringValue = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));
                fields.FindProperty("disableDefaultGridMesh").boolValue = settings.hideDefaultGrid;
                fields.FindProperty("disableDefaultLandscape").boolValue = settings.hideDefaultLandscape;
                // The 100% landscape only fills the 100% build area. A larger one cannot
                // refuse sizes above its own: the flag is all or nothing.
                fields.FindProperty("allowLargerMapSizes").boolValue = percent > 100;
                fields.ApplyModifiedPropertiesWithoutUndo();
                definitions.Add(landscape);
            }

            var collectionFields = new SerializedObject(collection);
            SerializedProperty landscapes = collectionFields.FindProperty("landscapes");
            landscapes.arraySize = definitions.Count;
            for (int i = 0; i < definitions.Count; i++) landscapes.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            collectionFields.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Compiles the mod in `folder` the way the ModKit's own wizard does, and returns
        /// the folder the compiled mod was written to.
        /// </summary>
        public static string Compile(string folder)
        {
            var manifest = AssetDatabase.LoadAssetAtPath<ModContentManifest>(folder + ManifestFile);
            if (manifest == null) throw new InvalidOperationException($"There is no mod in {folder}. Make the landscape first.");

            ConfigureAddressables(manifest);
            AssetDatabase.SaveAssets();

            // Checked here first so the ModKit's own "compile anyway?" dialog never opens.
            if (manifest.PreCompileValidate(manifest) == PreCompileResults.Error)
                throw new InvalidOperationException("The ModKit found errors in the mod. They are listed in the Console.");

            keptLoaded = manifest;
            try
            {
                ModKitCompiler.Compile(AuthorConfig(), manifest);
            }
            finally
            {
                keptLoaded = null;
            }

            string compiled = ModKitPathHelper.GetModCompileLocation(manifest.Name);
            bool hasManifest = File.Exists(Path.Combine(compiled, ModKitConsts.ModManifestFileName));
            if (!hasManifest || Directory.GetFiles(compiled, "*.bundle", SearchOption.AllDirectories).Length == 0)
                throw new InvalidOperationException("The ModKit did not produce a compiled mod. The Console says why.");
            return compiled;
        }

        /// <summary>Replaces installFolder with a copy of the compiled mod.</summary>
        public static void Install(string compiledFolder, string installFolder)
        {
            if (!Directory.Exists(compiledFolder)) throw new DirectoryNotFoundException($"There is no compiled mod at {compiledFolder}.");

            if (Directory.Exists(installFolder)) Directory.Delete(installFolder, true);
            foreach (string file in Directory.GetFiles(compiledFolder, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(installFolder, file.Substring(compiledFolder.Length).TrimStart('\\', '/'));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target);
            }
        }

        /// <summary>
        /// What the ModKit wizard does before every compile: the mod's folder and the
        /// folders of its includes become the only Addressables content, labelled as mod
        /// content, and the shader bundle is named after the mod.
        /// </summary>
        static void ConfigureAddressables(ModContentManifest manifest)
        {
            AddressableAssetSettings addressables = AddressableAssetSettingsDefaultObject.Settings;
            addressables.ShaderBundleNaming = ShaderBundleNaming.Custom;
            addressables.ShaderBundleCustomNaming = "shaders_" + manifest.Id;

            foreach (AddressableAssetGroup group in addressables.groups)
                foreach (AddressableAssetEntry entry in group.entries.ToList())
                    group.RemoveAssetEntry(entry);

            foreach (UnityEngine.Object asset in new UnityEngine.Object[] { manifest }.Concat(manifest.Includes))
            {
                string assetFolder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(asset)).Replace('\\', '/');
                AddressableAssetEntry entry = addressables.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(assetFolder), addressables.DefaultGroup);
                entry.SetLabel(ModKitConsts.ModContentLabel, true);
            }
        }

        static ModKitAuthorConfig AuthorConfig()
        {
            string guid = AssetDatabase.FindAssets("t:" + nameof(ModKitAuthorConfig)).FirstOrDefault();
            var config = guid == null ? null : AssetDatabase.LoadAssetAtPath<ModKitAuthorConfig>(AssetDatabase.GUIDToAssetPath(guid));
            if (config == null) throw new InvalidOperationException("This project has no ModKit Author Config asset. Is it a Dystopika ModKit project?");
            return config;
        }

        static T FindOrCreate<T>(string path, out bool isNew) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            isNew = asset == null;
            if (!isNew) return asset;

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
