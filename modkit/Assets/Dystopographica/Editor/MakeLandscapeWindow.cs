using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dystopographica
{
    /// <summary>
    /// The Make landscape window. It only lays out controls and passes input along:
    /// the work is in the classes it calls, which is where the tests are.
    /// </summary>
    public class MakeLandscapeWindow : EditorWindow
    {
        const int PreviewSize = 512;
        const int HistogramBins = 256;
        const float BarWidth = 64f;
        const float BarLabelWidth = 80f;
        const float BuildAreaSize = LandscapeSettings.BuildAreaSize;

        static readonly Color WaterColour = new Color(0.35f, 0.6f, 1f);
        static readonly Color PlaneColour = new Color(0.35f, 0.85f, 0.45f);
        static readonly string[] CursorNames = { "water", "plane start", "plane end" };

        [SerializeField] LandscapeSettings settings = new LandscapeSettings();
        [SerializeField] Vector2 scroll;
        [SerializeField] string status = "";
        [SerializeField] bool statusIsError;

        // Rebuilt from the settings whenever Unity reloads its scripts.
        [NonSerialized] string loadedPath;
        [NonSerialized] Heightmap source;
        [NonSerialized] string heightmapNote = "";
        [NonSerialized] bool heightmapFailed;
        [NonSerialized] int[] histogram;
        [NonSerialized] Heightmap previewMap;
        [NonSerialized] float previewSmoothing;
        [NonSerialized] Texture2D previewTexture;
        [NonSerialized] bool previewStale;
        [NonSerialized] float planeFraction;
        [NonSerialized] int draggedCursor;
        [NonSerialized] List<string> problems;
        [NonSerialized] GUIStyle cursorLabel;

        [MenuItem("Dystopographica/Make landscape")]
        public static void Open()
        {
            GetWindow<MakeLandscapeWindow>("Make landscape").minSize = new Vector2(430f, 480f);
        }

        // Unity only remembers a window's fields while the window stays open. The settings
        // are kept in the project's UserSettings folder as well, so closing the window or
        // restarting Unity does not put everything back to its defaults.
        const string RememberedSettings = "UserSettings/Dystopographica.json";

        void OnEnable()
        {
            settings = LandscapeSettings.Read(RememberedSettings) ?? settings;
        }

        void OnDisable()
        {
            settings.Write(RememberedSettings);
            DestroyImmediate(previewTexture);
        }

        void OnGUI()
        {
            // IMGUI runs OnGUI once to lay controls out and again to draw them, and both
            // passes must see the same controls. So anything that decides which controls
            // exist is only refreshed on the layout pass.
            if (Event.current.type == EventType.Layout || problems == null)
            {
                LoadHeightmap();
                problems = settings.Problems(source);
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawHeightmap();
            if (source != null) DrawPreview();
            DrawMapping();
            DrawMod();
            DrawLandscape();
            DrawActions();
            EditorGUILayout.EndScrollView();
        }

        void DrawHeightmap()
        {
            EditorGUILayout.LabelField("Heightmap", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(settings.heightmapPath == "" ? "No file chosen" : settings.heightmapPath, EditorStyles.textField);
                if (GUILayout.Button("Browse", GUILayout.Width(70f)))
                {
                    string chosen = EditorUtility.OpenFilePanel("Choose a greyscale PNG heightmap", FolderOf(settings.heightmapPath), "png");
                    if (chosen != "") settings.heightmapPath = chosen;
                    GUIUtility.ExitGUI();
                }
            }

            if (heightmapNote != "") EditorGUILayout.HelpBox(heightmapNote, heightmapFailed ? MessageType.Error : MessageType.None);
        }

        void LoadHeightmap()
        {
            if (settings.heightmapPath == loadedPath) return;

            loadedPath = settings.heightmapPath;
            source = null;
            previewMap = null;
            heightmapNote = "";
            heightmapFailed = false;
            if (loadedPath == "") return;

            try
            {
                byte[] file = File.ReadAllBytes(loadedPath);
                Heightmap raw = HeightmapFile.Decode(file);
                int bits = HeightmapFile.BitDepth(file);
                int resolution = Resample.TerrainResolution(raw.Width);

                source = raw.Normalized();
                histogram = Histogram.Compute(source, HistogramBins);
                heightmapNote = $"{raw.Width} x {raw.Height} pixels, {bits}-bit.";
                if (resolution != raw.Width) heightmapNote += $" The terrain will be resampled to {resolution} x {resolution}.";
                if (bits == 8) heightmapNote += " An 8-bit file has only 256 height levels, so flat land can come out terraced. Use a 16-bit PNG if you can.";
            }
            catch (Exception e)
            {
                heightmapNote = e.Message;
                heightmapFailed = true;
            }
        }

        void DrawPreview()
        {
            if (previewMap == null || previewSmoothing != settings.smoothing)
            {
                previewMap = LandscapeBuilder.PreviewMap(source, settings.smoothing, PreviewSize);
                previewSmoothing = settings.smoothing;
                previewStale = true;
            }
            if (previewStale || previewTexture == null) PaintPreview();

            float size = Mathf.Clamp(position.width - BarWidth - BarLabelWidth - 44f, 128f, PreviewSize);
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect image = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
                GUI.DrawTexture(image, previewTexture, ScaleMode.StretchToFill);
                DrawBuildArea(image);
                GUILayout.Space(12f);
                DrawLevels(GUILayoutUtility.GetRect(BarWidth + BarLabelWidth, size, GUILayout.Width(BarWidth + BarLabelWidth), GUILayout.Height(size)));
            }
            EditorGUILayout.LabelField("Blue is under water and green is the city plane. Drag the three cursors on the bar.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField(
                $"The yellow square is the build area, at every map size. The plane covers {planeFraction:P0} of it. The Build area slider sets how much of the map it takes in.",
                EditorStyles.wordWrappedMiniLabel);
        }

        void PaintPreview()
        {
            if (previewTexture == null || previewTexture.width != previewMap.Width || previewTexture.height != previewMap.Height)
            {
                DestroyImmediate(previewTexture);
                previewTexture = new Texture2D(previewMap.Width, previewMap.Height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            }
            previewTexture.SetPixels32(Preview.Paint(previewMap, settings.PaintedWater, settings.planeStart, settings.planeEnd));
            previewTexture.Apply();
            planeFraction = Preview.PlaneFraction(previewMap, settings.planeStart, settings.planeEnd, BuildAreaFraction());
            previewStale = false;
        }

        float BuildAreaFraction() => settings.BuildAreaShare;

        /// <summary>Outlines the game's starting 50 x 50 build area in the middle of the map.</summary>
        void DrawBuildArea(Rect image)
        {
            float edge = image.width * BuildAreaFraction();
            var square = new Rect(image.center.x - edge / 2, image.center.y - edge / 2, edge, edge);
            EditorGUI.DrawRect(new Rect(square.x, square.y, square.width, 1f), Color.yellow);
            EditorGUI.DrawRect(new Rect(square.x, square.yMax - 1f, square.width, 1f), Color.yellow);
            EditorGUI.DrawRect(new Rect(square.x, square.y, 1f, square.height), Color.yellow);
            EditorGUI.DrawRect(new Rect(square.xMax - 1f, square.y, 1f, square.height), Color.yellow);
        }

        /// <summary>
        /// The levels bar: the heightmap's histogram on its side, lowest values at the
        /// bottom, with the water, plane start and plane end cursors across it.
        /// </summary>
        void DrawLevels(Rect area)
        {
            var bar = new Rect(area.x, area.y, BarWidth, area.height);
            float[] cursors = { settings.water, settings.planeStart, settings.planeEnd };
            float Y(float value) => bar.yMax - value * bar.height;

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(bar, new Color(0.1f, 0.1f, 0.1f));
                if (settings.waterPlane) EditorGUI.DrawRect(Rect.MinMaxRect(bar.x, Y(settings.water), bar.xMax, bar.yMax), Faded(WaterColour));
                EditorGUI.DrawRect(Rect.MinMaxRect(bar.x, Y(settings.planeEnd), bar.xMax, Y(settings.planeStart)), Faded(PlaneColour));

                int tallest = 1;
                foreach (int count in histogram) tallest = Mathf.Max(tallest, count);
                float binHeight = bar.height / histogram.Length;
                for (int i = 0; i < histogram.Length; i++)
                {
                    // Square root, so one huge flat area does not flatten everything else.
                    float length = Mathf.Sqrt(histogram[i] / (float)tallest) * bar.width;
                    EditorGUI.DrawRect(new Rect(bar.x, bar.yMax - (i + 1) * binHeight, length, Mathf.Max(binHeight, 1f)), new Color(0.8f, 0.8f, 0.8f, 0.75f));
                }

                // Top cursor first, pushing each label below the one before so they never overlap.
                if (cursorLabel == null) cursorLabel = new GUIStyle(EditorStyles.miniLabel);
                float nextLabelTop = float.MinValue;
                for (int i = cursors.Length - 1; i >= (settings.waterPlane ? LandscapeSettings.Water : LandscapeSettings.PlaneStart); i--)
                {
                    Color colour = i == LandscapeSettings.Water ? WaterColour : PlaneColour;
                    float y = Y(cursors[i]);
                    EditorGUI.DrawRect(new Rect(bar.x - 3f, y - 1f, bar.width + 10f, 2f), colour);

                    float labelTop = Mathf.Max(y - 8f, nextLabelTop);
                    nextLabelTop = labelTop + 13f;
                    cursorLabel.normal.textColor = colour;
                    GUI.Label(new Rect(bar.xMax + 10f, labelTop, BarLabelWidth - 10f, 16f), CursorNames[i], cursorLabel);
                }
            }

            EditorGUIUtility.AddCursorRect(area, MouseCursor.ResizeVertical);
            int control = GUIUtility.GetControlID(FocusType.Passive);
            Event e = Event.current;
            float pointed = Mathf.InverseLerp(bar.yMax, bar.y, e.mousePosition.y);
            switch (e.GetTypeForControl(control))
            {
                case EventType.MouseDown when e.button == 0 && area.Contains(e.mousePosition):
                    GUIUtility.hotControl = control;
                    draggedCursor = settings.NearestCursor(pointed);
                    MoveDraggedCursor(pointed);
                    e.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == control:
                    MoveDraggedCursor(pointed);
                    e.Use();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == control:
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
            }
        }

        void MoveDraggedCursor(float value)
        {
            settings.MoveCursor(draggedCursor, value);
            previewStale = true;
            Repaint();
        }

        static Color Faded(Color colour) => new Color(colour.r, colour.g, colour.b, 0.35f);

        void DrawMapping()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Mapping", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();

            CursorSlider(LandscapeSettings.PlaneEnd, settings.planeEnd, "Plane end", "Heights between plane start and plane end are flattened into the city plane.");
            CursorSlider(LandscapeSettings.PlaneStart, settings.planeStart, "Plane start", "Heights between plane start and plane end are flattened into the city plane.");

            settings.waterPlane = EditorGUILayout.Toggle(new GUIContent("Water", "Whether the map has water. Off makes a dry map: no water surface, and the water cursor is ignored."), settings.waterPlane);
            using (new EditorGUI.DisabledScope(!settings.waterPlane))
            {
                CursorSlider(LandscapeSettings.Water, settings.water, "Water level", "Where the shoreline is: everything lower than this is under water. Put it right under plane start for a quay, lower for a sloping shore.");
                settings.waterDepth = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Water depth", "How far the water surface lies below the city plane, in units. A small building is about 1 unit tall."), settings.waterDepth));
            }

            float share = settings.BuildAreaShare * 100f;
            float newShare = EditorGUILayout.Slider(new GUIContent("Build area (% of map)", "How much of the map's width the build area covers. At 100 the whole map is the build area. Lower it to leave scenery around the city: at 50 the build area is the middle half."), share, 5f, 100f);
            if (newShare != share) settings.BuildAreaShare = newShare / 100f;
            EditorGUILayout.LabelField(" ", "In the game the map is " + string.Join(", ", settings.MapSizes().ConvertAll(size => $"{settings.ForMapSize(size).worldSize:0.#} units wide at {size}%")) + ".", EditorStyles.wordWrappedMiniLabel);
            settings.peakHeight = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Peak height", "How far the heightmap's highest point rises above the city plane, in units, at the 100% map size. The larger landscapes are scaled up, so theirs is higher."), settings.peakHeight));
            settings.lowestDepth = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Lowest depth", "On a dry map, how far the heightmap's lowest point drops below the city plane, in units. With water, this is the sea bed: everything under water lies this far down. Keep it well over half a unit, or the game marks the water with red crosses as ground you cannot build on."), settings.lowestDepth));
            settings.smoothing = EditorGUILayout.Slider(new GUIContent("Smoothing", "Blurs the heightmap by this many pixels before mapping it. Clears specks of noise off the city plane. 0 is off."), settings.smoothing, 0f, 10f);
            settings.knee = EditorGUILayout.Slider(new GUIContent("Edge softness", "How gently terrain leaves the city plane, as a share of the heightmap's range. 0 gives a hard crease."), settings.knee, 0f, 0.25f);

            if (EditorGUI.EndChangeCheck()) previewStale = true;
        }

        void CursorSlider(int cursor, float value, string label, string tooltip)
        {
            float changed = EditorGUILayout.Slider(new GUIContent(label, tooltip + " 0 is the heightmap's lowest point, 1 its highest."), value, 0f, 1f);
            if (changed != value) settings.MoveCursor(cursor, changed);
        }

        void DrawMod()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Mod", EditorStyles.boldLabel);
            settings.modName = EditorGUILayout.TextField(new GUIContent("Mod name", "Also the name of the mod's folder, in Assets here and in the game's Mods folder."), settings.modName);
            settings.author = EditorGUILayout.TextField("Author", settings.author);
            settings.version = Mathf.Max(0, EditorGUILayout.IntField("Version", settings.version));
            settings.minorVersion = Mathf.Max(0, EditorGUILayout.IntField("Minor version", settings.minorVersion));

            using (new EditorGUI.DisabledScope(!File.Exists(settings.PresetPath)))
            {
                if (GUILayout.Button(new GUIContent("Load this mod's saved settings", "Brings back the settings the mod was last made with.")))
                {
                    settings = LandscapeSettings.Load(settings.modName) ?? settings;
                    previewStale = true;
                    GUI.FocusControl(null);
                }
            }
        }

        void DrawLandscape()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Landscape", EditorStyles.boldLabel);
            settings.landscapeName = EditorGUILayout.TextField(new GUIContent("Display name", "The name in the game's landscape list. Leave empty to use the mod name."), settings.landscapeName);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(new GUIContent("Thumbnail", "The picture in the game's landscape list, 1024 x 512."));
                EditorGUILayout.LabelField(settings.thumbnailPath == "" ? "Made from the preview" : settings.thumbnailPath, EditorStyles.textField);
                if (GUILayout.Button("Browse", GUILayout.Width(70f)))
                {
                    string chosen = EditorUtility.OpenFilePanel("Choose a 1024 x 512 PNG thumbnail", FolderOf(settings.thumbnailPath), "png");
                    if (chosen != "") settings.thumbnailPath = chosen;
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("Clear", GUILayout.Width(50f))) settings.thumbnailPath = "";
            }

            const string sizeTip = "The game does not scale a landscape for a larger map size, it only widens the build area. "
                + "So each size gets a landscape of its own: the same map, scaled up to fill that build area.";
            settings.size200 = EditorGUILayout.Toggle(new GUIContent("200% landscape", sizeTip), settings.size200);
            settings.size400 = EditorGUILayout.Toggle(new GUIContent("400% landscape", sizeTip), settings.size400);
            if (settings.size200 || settings.size400)
                EditorGUILayout.LabelField(
                    $"The game will list {settings.MapSizes().Count} landscapes, named like \"{settings.DisplayNameFor(100)}\". Pick the one that matches the city's map size.",
                    EditorStyles.wordWrappedMiniLabel);
            settings.hideDefaultGrid = EditorGUILayout.Toggle(new GUIContent("Hide default grid", "Hides the game's own flat grid plane."), settings.hideDefaultGrid);
            settings.hideDefaultLandscape = EditorGUILayout.Toggle(new GUIContent("Hide default mountains", "Hides the mountains the game normally generates around the city."), settings.hideDefaultLandscape);
        }

        void DrawActions()
        {
            EditorGUILayout.Space();
            foreach (string problem in problems) EditorGUILayout.HelpBox(problem, MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(problems.Count > 0))
                {
                    if (GUILayout.Button(new GUIContent("Make landscape", "Writes the landscape into this project, replacing the last one made for this mod."), GUILayout.Height(30f)))
                        Run(install: false);
                    if (GUILayout.Button(new GUIContent("Compile and install", "Makes the landscape if anything changed, compiles the mod, and copies it into the game's Mods folder."), GUILayout.Height(30f)))
                        Run(install: true);
                }
                if (GUILayout.Button("Open Mods folder", GUILayout.Height(30f), GUILayout.Width(120f))) ModKitBridge.OpenModsFolder();
            }
            if (status != "") EditorGUILayout.HelpBox(status, statusIsError ? MessageType.Error : MessageType.Info);
        }

        void Run(bool install)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Dystopographica", install ? "Compiling and installing the mod" : "Making the landscape", 0.5f);
                if (install)
                {
                    string installFolder = ModKitBridge.InstallFolder(settings.modName);
                    LandscapeBuilder.CompileAndInstall(settings, source, installFolder);
                    status = $"Installed in {installFolder}.";
                }
                else
                {
                    LandscapeBuilder.Make(settings, source);
                    status = $"Made the landscape in {settings.Folder}.";
                }
                statusIsError = false;
                settings.Write(RememberedSettings);
            }
            catch (Exception e)
            {
                status = e.Message;
                statusIsError = true;
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            GUIUtility.ExitGUI();
        }

        static string FolderOf(string path) => path == "" ? "" : Path.GetDirectoryName(path);
    }
}
