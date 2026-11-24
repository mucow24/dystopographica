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
        const int LargestSpeckSize = 5000;
        const float BarWidth = 64f;
        const float BarLabelWidth = 80f;
        const float BuildAreaSize = LandscapeSettings.BuildAreaSize;

        static readonly Color WaterColour = new Color(0.35f, 0.6f, 1f);
        static readonly Color PlaneColour = new Color(0.35f, 0.85f, 0.45f);
        static readonly string[] CursorNames = { "water", "plane start", "plane end" };
        static readonly GUIContent[] MapSizeLabels = { new GUIContent("100%"), new GUIContent("200%"), new GUIContent("400%") };

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
        [NonSerialized] float waterFraction;
        [NonSerialized] bool waterWasRaised;
        [NonSerialized] bool showWaterNote;
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
                // Only while nothing is being dragged or typed, so a cursor never jumps out from under the pointer.
                bool atRest = GUIUtility.hotControl == 0 && !EditorGUIUtility.editingTextField;
                if (atRest && source != null && previewTexture != null && !previewStale && settings.RaiseWaterOutOfTheDry(waterFraction))
                {
                    previewStale = true;
                    waterWasRaised = true;
                }
                showWaterNote = waterWasRaised;
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
            EditorGUILayout.LabelField(
                "Blue is under water and green is the city plane. Drag the three cursors on the bar."
                + (settings.waterPlane ? $" Water covers {waterFraction:P0} of the map." : "")
                + (settings.speckSize > 0 ? " Brighter green is what the speck filter filled." : ""),
                EditorStyles.wordWrappedMiniLabel);
            if (showWaterNote)
                EditorGUILayout.HelpBox(
                    "Nothing on the map was below the water cursor, so the landscape would have had no water. "
                    + "The cursor has been moved up to Plane start: everything lower than the city is now water.",
                    MessageType.Info);
            EditorGUILayout.LabelField(
                $"The yellow square is the {settings.mapSize}% build area, and any fainter squares inside it are the smaller map sizes. The plane covers {planeFraction:P0} of it. The Build area slider sets how much of the map it takes in.",
                EditorStyles.wordWrappedMiniLabel);
        }

        void PaintPreview()
        {
            if (previewTexture == null || previewTexture.width != previewMap.Width || previewTexture.height != previewMap.Height)
            {
                DestroyImmediate(previewTexture);
                previewTexture = new Texture2D(previewMap.Width, previewMap.Height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            }
            Heightmap shown = LandscapeBuilder.WithoutSpecks(previewMap, source.Width, settings);
            previewTexture.SetPixels32(Preview.Paint(shown, settings.PaintedWater, settings.planeStart, settings.planeEnd, previewMap));
            previewTexture.Apply();
            planeFraction = Preview.PlaneFraction(shown, settings.planeStart, settings.planeEnd, BuildAreaFraction());
            waterFraction = Preview.WaterFraction(shown, settings.PaintedWater);
            previewStale = false;
        }

        float BuildAreaFraction() => Mathf.Clamp(settings.buildAreaShare, 0.01f, 1f);

        /// <summary>
        /// Outlines the build area the map is made for, in the middle of the map, and
        /// more faintly inside it the build areas of the smaller map sizes.
        /// </summary>
        void DrawBuildArea(Rect image)
        {
            foreach (int size in LandscapeSettings.MapSizes)
            {
                if (size > settings.mapSize) continue;
                float edge = image.width * BuildAreaFraction() * LandscapeSettings.BuildAreaScale(size) / LandscapeSettings.BuildAreaScale(settings.mapSize);
                var square = new Rect(image.center.x - edge / 2, image.center.y - edge / 2, edge, edge);
                Color colour = size == settings.mapSize ? Color.yellow : new Color(1f, 0.92f, 0.016f, 0.4f);
                EditorGUI.DrawRect(new Rect(square.x, square.y, square.width, 1f), colour);
                EditorGUI.DrawRect(new Rect(square.x, square.yMax - 1f, square.width, 1f), colour);
                EditorGUI.DrawRect(new Rect(square.x, square.y, 1f, square.height), colour);
                EditorGUI.DrawRect(new Rect(square.xMax - 1f, square.y, 1f, square.height), colour);
            }
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
            waterWasRaised = false;
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
                CursorSlider(LandscapeSettings.Water, settings.water, "Water level", "Where the shoreline is: everything lower than this is under water. Put it right under plane start for a quay, lower for a sloping shore.");

            settings.mapSize = EditorGUILayout.IntPopup(
                new GUIContent("Map size", "The game's map size this landscape is made for. The game never scales a landscape, it only widens the build area, so the map is sized to fill the build area of one size. A 400% map also works at 100% and 200%, where the city sits on the middle of it. A 100% map is offered at 100% only. A 200% map can be chosen at 400% too, but will show bare ground at its edges there."),
                settings.mapSize, MapSizeLabels, LandscapeSettings.MapSizes);
            float share = BuildAreaFraction() * 100f;
            float newShare = EditorGUILayout.Slider(new GUIContent("Build area (% of map)", "How much of the map's width the build area covers. At 100 the whole map is the build area. Lower it to leave scenery around the city: at 50 the build area is the middle half."), share, 5f, 100f);
            if (newShare != share) settings.buildAreaShare = newShare / 100f;
            EditorGUILayout.LabelField(" ", $"In the game the map is {settings.WorldSize:0.#} units wide, and the {settings.mapSize}% build area is {settings.BuildArea:0.#}.", EditorStyles.wordWrappedMiniLabel);
            settings.peakHeight = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Peak height", "How far the heightmap's highest point rises above the city plane, in units."), settings.peakHeight));
            // Only a dry map has one. With water, the sea bed lies at a fixed level just under it.
            if (!settings.waterPlane)
                settings.lowestDepth = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Lowest depth", "How far the heightmap's lowest point drops below the city plane, in units."), settings.lowestDepth));
            settings.speckSize = EditorGUILayout.IntSlider(new GUIContent("Fill specks under (px)", "Fills small holes in the city plane without blurring anything. Any patch of water, shore or hill smaller than this many heightmap pixels, with plane all the way round it, becomes plane. The preview shows what it filled in a brighter green. 0 is off. The preview is a smaller picture, so it shows the effect only roughly for limits under about 30."), settings.speckSize, 0, LargestSpeckSize);
            settings.smoothing = EditorGUILayout.Slider(new GUIContent("Smoothing", "Blurs the heightmap by this many pixels before mapping it. Clears specks of noise off the city plane. 0 is off."),settings.smoothing, 0f, 10f);
            settings.knee = EditorGUILayout.Slider(new GUIContent("Edge softness", "How gently terrain leaves the city plane, as a share of the heightmap's range. 0 gives a hard crease."), settings.knee, 0f, 0.25f);

            if (EditorGUI.EndChangeCheck())
            {
                previewStale = true;
                waterWasRaised = false;
            }
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

            using (new EditorGUI.DisabledScope(!settings.waterPlane))
                settings.normalWater = EditorGUILayout.ToggleLeft(
                    new GUIContent("Also make a version with normal water", $"The map's water ripples and mirrors what stands around it. Tick this and the mod holds the same map a second time, with flat, still water like the game's example landscape. The game lists it as \"{settings.DisplayName} (normal water)\", and the player picks."),
                    settings.normalWater);
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
                    if (GUILayout.Button(new GUIContent("Compile and install", "Makes the landscape, compiles the mod, and copies it into the game's Mods folder."), GUILayout.Height(30f)))
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
