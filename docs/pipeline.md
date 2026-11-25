# Heightmap → Dystopika landscape mod

Status: design plus a first working version, last revised 2026-11-24. The tool
exists and its tests pass, including a real mod compile. In the game, Miami and
New York heightmaps show land and water and play at 400%. The custom water (§4.3)
draws in the game and mirrors what stands around it there. Buildings can stand
anywhere on the map, small ones included (§3.7), and the land and the water are
drawn at Night City's levels (§4.3).
Scope: the second half of the tool, turning an existing greyscale heightmap into an
installable Dystopika mod. The heightmap generator is out of scope here and talks to
this half through a file.

Sources are listed at the end. Facts marked **[DLL]** come from reflecting over the
ModKit's two compiled assemblies in template v1.2.2. Facts marked **[WS]** come from
inspecting the five landscape mods you are subscribed to on the Steam Workshop.

---

## 1. Summary

- The tool is a **Unity Editor package** that the user imports into their own copy of
  the Dystopika ModKit template project. It adds one window, `Dystopographica > Make
  landscape`, that loads a heightmap, previews what will be buildable, writes the
  landscape assets into a mod, and compiles and installs the mod through the ModKit's
  own public compile call. No Python, no external build orchestration.
- Audience: people who already have Unity and the ModKit installed, which is the
  game's existing modding audience. Install is "import a `.unitypackage`".
- A landscape mod is one prefab (Unity Terrain plus collider) spawned at the centre of
  the city grid, plus three small ScriptableObjects, compiled to an Addressables
  bundle with a `manifest.json`. The buildable grid is a 50 × 50 unit square centred
  on the origin at world y = 0; only surface at y = 0 is buildable.
- **Heights are remapped by a levels curve, nothing else.** Three cursors on a
  histogram of the heightmap set the shoreline and a band of input heights that
  becomes the city plane, and two numbers set the in-game height of the highest
  point and, on a dry map, the depth of the lowest. With water, the water and the
  sea bed lie at fixed levels (§4.3). The curve is monotone and applied per
  pixel, so the terrain's shape is preserved and no masks or image editing are
  needed. This is what makes real-world elevation data of mostly flat areas usable
  directly.
- One map per mod, made for one map size. The game widens the build area at 200%
  and 400% but never scales the landscape, so the map is sized against the build
  area of the size chosen in the window, 400% by default as the Workshop
  landscapes are. A map with water gets our own rippling, mirroring water; on
  request the mod holds the map a second time with normal water, and the player
  picks. Ids are generated once and live in the
  assets; re-running overwrites in place, so iterating never involves matching ids.
- Two constants still need a one-time in-game calibration: the vertical tolerance
  for placement, and whether sub-zero terrain renders. The build area's size is
  known by eye only (§3.4).
  A calibration landscape is a menu item in the same package.

## 2. Decisions

| Decision | Choice | Why |
|---|---|---|
| Where the tool lives | Editor-only C# inside the user's ModKit project | Their ModKit does the compile; nothing of Voids Within's ships in our package; the editor gives preview and touch-up for free |
| Distribution | `.unitypackage` (same mechanism the ModKit uses for updates) | Zero new concepts for modders |
| Heightmap handling | A global, monotone levels curve with a plateau (three cursors: water, plane start, plane end) plus optional input smoothing | Works on raw real-world data without an image editor; preserves terrain shape. Region-based flattening of the buildable square was rejected because what is *not* flat inside the square is the point of a custom landscape |
| Vertical scale | Two numbers, one per side of the plane: the in-game height of the highest point and the in-game depth of the lowest. With water the second gives way to a fixed sea bed | Terrain above the playfield often needs different scaling from terrain below it; typing where the extremes should land is more direct than a units-per-value factor |
| Water height | Fixed: 0.062 under the drawn land, Night City's levels (§4.3). The water cursor only picks the shoreline | In real coastal data the sea is barely lower than the land, so a height read off the curve put the water level with the city. A depth field came next. It went on 2026-11-24, once buildings could stand anywhere: the level then has to suit a building standing in the water |
| Landscapes per mod | One map at one size, chosen in the window (100%, 200% or 400%; default 400%). A map with water is one landscape, with the custom water; ticking "Also make a version with normal water" adds a second on the same terrain, "(normal water)". Re-run overwrites in place | The game does not scale a landscape for a larger map size. A landscape per size was tried on 2026-11-23 and dropped the same day: three sizes times two waters was too much to choose from, and one 400% map already works at every size, on its middle, as the Workshop maps do. The smaller sizes stay available for maps that should fill a small build area exactly. Until 2026-11-24 every map with water got both waters; the custom one is good enough now to be the default |
| Where buildings may stand | Anywhere, always: one flat collider under the whole map, with the land and the water drawn at Night City's levels, just above it | Chosen on 2026-11-24 over the ground deciding (large buildings cannot overlap a shore) and over a strip along the shore (not built). The game's small fill-in buildings spill onto the water; the Density Brush clears them, and at these levels the ones left stand in the water, which can look good. The ground-deciding layout stayed for a day as the off position of a "Build anywhere" switch and was then removed, with the water depth field only it used |
| Terrain representation | Unity Terrain (not mesh) for v1 | Documented path, example uses it, compact int16 heights, free LOD and collider |
| Rejected: Python orchestration driving batch Unity | Dropped | Unnecessary once the tool is inside the editor |
| Rejected: patching a pre-built bundle without Unity | Dropped | Relies on undeclared properties of the ModKit's output and re-serialising bundles ourselves |

---

## 3. How landscape mods work (reference)

### 3.1 Content model

From the ModKit docs page "Creating New Landscapes" (a PDF export is kept locally in
`docs/reference/`, which is not committed because the page is Voids Within's)
and the DLL field lists:

| Asset | Type | Fields **[DLL]** |
|---|---|---|
| Landscape prefab | Prefab | Root GameObject at (0,0,0). Child with `Terrain` + `TerrainCollider` (the TerrainData asset must live in the mod folder), or a mesh with `MeshCollider`. Anything else you like (water plane, props). |
| Landscape Definition | `ModLandscapeDefinition` | `id`, `name`, `thumbnail` (Texture2D, docs say 1024×512 PNG), `landscapeResource` (Addressables `AssetReference` → prefab GUID), `disableDefaultGridMesh`, `disableDefaultLandscape`, `allowLargerMapSizes` |
| Landscape Collection | `ModLandscapeCollection` | `landscapes[]` |
| Content Manifest | `ModContentManifest` | `Id` (GUID string), `ModKitVersion`, `Name`, `Author`, `Version`, `MinorVersion`, `PreviewImage`, the collection references, `includes[]` (must contain `Dystopika.BaseContent`) |

`disableDefaultGridMesh` hides the game's flat grid plane; `disableDefaultLandscape`
suppresses the procedural mountains. The example landscape sets both.

### 3.2 Grid and coordinate conventions

- Grid is **50 × 50 units** at 100% (dev, "Adding Custom Landscapes" video). Ground
  tiles in the template have mesh extents of about ±1 unit, so one unit is roughly one
  small building.
- Buildable surface must be at **world y = 0** ("the buildable space always has to be
  right at world 0"; docs: tiles cannot be placed where the landscape is "too far
  above or too far below" the grid plane). The tolerance is not published.
- Game known issue, v1.7.0 (Dec 2025): "Custom Landscapes with valleys below y:0 are
  not displaying correctly and being cut off due to a change in the world grid setup."
  No later patch note says it was fixed. Needs a test.
- Camera is not fenced to the landscape; on big maps you can look under the geometry
  (Pandaemonium mod notes).

### 3.3 The example landscape, measured

Template `ExampleMod/Example Landscape/Example Internal Landscape` ("River Valley"),
parsed with UnityPy:

| Property | Value |
|---|---|
| Terrain footprint | 100 × 100 units (2× the grid per axis), placed at (−50, −1, −50) under a root at (0,0,0) |
| Heightmap | 2049 × 2049, 0.0488 units/cell |
| Height scale (terrain `size.y`) | 600 (Unity default; int16 step = 600/32766 ≈ 0.018 units) |
| Plateau | raw value 55 → world y = **+0.0071** (not exactly 0, and it works); 71.5% of the 50×50 grid |
| River | down to world −1.0; 13% of the grid |
| Hills | up to +6.6 inside the grid, +17.9 on the terrain |
| Water | Unity built-in Plane scaled ×10 at y = **−0.69**, URP/Lit, teal (0.23, 0.59, 0.55), smoothness 0.07, opaque |
| Terrain material | `Black.mat`, URP/Lit, black; no terrain layers |
| Thumbnail | 800 × 450 PNG (docs say 1024×512), Read/Write enabled |

The "Dystopika look" for landscapes is near-black geometry under fog; texture detail
barely reads (the dev says so in the video). A black URP/Lit material is the default.

### 3.4 Map sizes (100 / 200 / 400%)

- Added 2025-12-24: "select 100%, 200%, and 400% map size to increase the buildable
  area." The player chooses at city creation; the landscape only declares support.
- ModKit 1.1.1 added `Allow Larger Map Sizes`: "ensure your landscape fills the entire
  buildable space at those sizes otherwise it may have adverse effects on gameplay."
- Volumetric light budget went 4096 → 16384 for the large sizes (v1.7.7), i.e. 4×,
  which fits 400% meaning 4× the *area* (100 × 100 units).
- **[WS]** Night City's terrain mesh was "fully resized" for 400%: its bundle holds
  the old and new meshes, 192 and 384 units across (exactly 2×). Other 400%-capable
  landscapes are 450–550 units across (Isla Salvación: 3×3 tiles of 150; Post
  Nuclear: 550; Triple River: 500, 99% flat).
- **Seen in the game, 2026-11-23:** a landscape exactly 50 units wide fills the 100%
  build area. At 200% and 400% the same landscape stays the same size and the build
  area grows around it, to about 1.4 times and about 2 times its width. So the
  percentage is the area: 50, 70.7 and 100 units across. These are by eye; the
  calibration landscape (§6) would make them exact.
- The game never scales a landscape. A landscape either covers the larger build
  area or leaves bare ground around itself.

### 3.5 Compiled package format

**[WS]** every subscribed mod has this layout:

```
<Mods>/<ModName>/
  manifest.json
  preview.png                          # mod thumbnail
  <landscapeId>.png                    # one per landscape definition
  StandaloneWindows64/
    catalog_1.json, catalog_1.hash     # Addressables content catalog
    modcontent_.bundle                 # everything labelled mod_content (LZ4)
    shaders_<manifestId>_unitybuiltinshaders.bundle   # only if package/built-in shaders are referenced
```

`manifest.json` (Triple River, verbatim):

```json
{
 "Id": "ea7c8428-7aed-4fb9-be4e-98058f6d460b",
 "Name": "Triple River",
 "Author": "DF",
 "Version": 1,
 "MinorVersion": 0,
 "Timestamp": "2026-7-17--14-36-08",
 "ModKitVersion": 4,
 "Features": { "Landscapes": [ { "Id": "triple-river", "Name": "Double River", "AllowLargeMapSizes": true } ] },
 "LocalInstallPath": ""
}
```

Catalog internal ids are `{Dystopika.ModKit.Root}\<ModName>\StandaloneWindows64\...`,
so the installed folder name equals the mod Name used at compile time. Saves refer to
the manifest GUID and landscape ids; keep them stable.

### 3.6 What "Compile" and "Export" do **[DLL]**

`Neo3.ModKit.Public.Editor.ModKitCompiler.Compile(ModKitAuthorConfig config, ModContentManifest manifest)`
is `public static`. In order:

1. Activates the `Dystopika Modding` Addressables profile and sets its `ModName`
   variable to `manifest.Name`.
2. If `Assets/Export~/<Name>` exists and `config.IgnoreOverwriteWarnings` is false,
   shows an overwrite dialog. The shipped `Default ModKit Author Config.asset` has
   `IgnoreOverwriteWarnings: 1`.
3. `PreCompileValidate` over the manifest tree; on problems shows
   `EditorUtility.DisplayDialog("Compiler Validation Failed!", ..., "Compile Anyway", "Cancel")`.
4. Clears `Assets/Export~/<Name>`.
5. `AddressableAssetSettings.BuildPlayerContent(out result)`; logs "Successfully
   Compiled Mod!" or "Mod Build failed! Error: ...".
6. Writes `manifest.json` (ModKitVersion const = 4, timestamp `yyyy-M-dd--HH-mm-ss`,
   `Features.Landscapes` from the collection; each landscape thumbnail is read with
   `Texture2D.EncodeToPNG`, so thumbnails must be readable).
7. Writes `preview.png` and one `<landscapeId>.png` per landscape.
8. Restores the previous profile.

Done by the wizard *before* Compile, so it is our job too, `ConfigureAddressableState`:
set `ShaderBundleNaming = Custom` and `ShaderBundleCustomNaming = $"shaders_{manifest.Id}"`;
remove every entry from every group; ensure the `Dystopika.BaseContent` include is in
`manifest.includes`; `CreateOrMoveEntry(folderGuid, DefaultGroup)` + `SetLabel("mod_content", true)`
for the manifest's folder and each include's folder.

Export is a folder copy: clear `<Mods>/<Name>`, copy `Assets/Export~/<Name>` into it.
Public helpers in `ModKitPathHelper`: `GetLocalInstallationPath()`,
`GetModCompileLocation(name)`, `GetModInstallLocation(name)`;
`ModKitEditorHelper.ShowLocalModInstallLocation()` opens the Mods folder.

Addressables in the template: profile `Dystopika Modding`,
`Local.BuildPath = [Application.dataPath]/Export~/[ModName]/[BuildTarget]`,
`Local.LoadPath = {Dystopika.ModKit.Root}/[ModName]/[BuildTarget]`, remote catalog on,
one group "Mod Content", pack-together-by-label, LZ4, label `mod_content`.

### 3.7 How the Night City mod does its water

From the mod's bundle (Cyberpunk District v1.38), opened with Unity's own tools on
2026-11-23:

- The water is a low-poly mesh about 370 × 319 units at y = 0.13, drawn with
  "UberStylizedWater", a third-party asset-pack shader compiled into the mod. It is
  a Shader Graph shader. A scrolling ripple texture, two very low waves, scrolling
  foam and a distortion noise make it move. So a custom shader in a mod bundle runs
  in the game.
- The mirrored buildings come from a script. An object named "PlanarReflection"
  carries `PlanarReflectionVolume`, a class from the same pack's own assembly, which
  renders the scene a second time, mirrored. A mod cannot carry scripts, and the
  reflections are real in the game, so the game's own build must contain that class.
  Nothing documents this. Reaching it from our tool would mean shipping the pack or
  a look-alike of its class, and we do neither.
- There are no reflection probes in the mod.
- The water's material has the shader's refraction, caustics and depth colouring
  switched on, and sits in render queue 2950, among the see-through things. Those
  features read the camera's picture of the scene's colours and of its depths. The
  author would hardly have left them on if the game kept neither picture, so the
  game very likely keeps both. Our own mirror image (§4.3) rests on that.
- Where a building may stand is decided by colliders, not by what is drawn. The
  terrain's collider is switched off, the water's too, and a separate invisible flat
  plane at y = 0, 340 units wide ("Buildable Plane"), takes their place. In the
  first of the mod's three landscapes that plane is the only collider switched on;
  the other two also switch on the collider of a flat concrete slab under the city.
- The game applies one rule to large buildings and to the small ones it fills in by
  itself (its "ground tiles"): both go wherever a collider is at city level. Seen on
  our own map with the same flat plane (2026-11-24): large buildings can be placed
  anywhere, and the small ones spill off the land and stand in the air over the
  water. The mod's change notes on Steam tell the same story from the author's side:
  - v1.3.2 (2025-07): "Optimized buildable areas using collider zones on concrete
    layers". Land only.
  - v1.3.5 (2025-09-21): "Enabled building on both water and land". The flat plane.
    The same update "slightly increased water height".
  - v1.3.6 (2025-09-27): "Disable building on water for NCT3 to prevent ground tiles
    from appearing on water". The plane switched off again in one landscape.
  - v1.38 (2026-05): the plane is back in all three, and the notes say "Use Ground
    Building Remover Tool for cleaner water areas".
  So the mod has no trick that keeps the small buildings on land. Its author let them
  spawn on the water and tells players to clear them by hand. The tool meant is very
  likely the game's Density Brush (v1.7.0), which paints the small procedural
  buildings up or down.
- Everything the mod draws in the city lies a little above the build level, which
  is why its water can too. Measured from the meshes (2026-11-24): the collider
  sheet is at y = 0.000 exactly; the concrete slab under the city runs from 0.000
  to 0.100, so its visible top is 0.10 up; the roads are mostly at 0.18 and higher;
  the water is at 0.118, about level with the slab and under the roads. The terrain
  mesh below all that wanders between about −0.1 and +0.15. So every building,
  which the game stands on the sheet at 0, is sunk 0.1 to 0.2 into what is drawn,
  on land and on water alike. Ours draws the land exactly at 0 and the water 0.7
  below it: small buildings on the mod's water stand in the water, on ours they
  hover.

### 3.8 Toolchain and locations

| Item | Value |
|---|---|
| ModKit | v1.2.2 (2026-08-04); template `ProjectVersion.txt` says Unity 2022.3.62f2 |
| Unity on this machine | `C:\Program Files\Unity 2022.3.62f2\Editor\Unity.exe`. The game is built with 2022.3.62f3. |
| Packages | Addressables 1.22.3, URP 14.0.12 |
| ModKit code | two DLLs in `Assets/Plugins`, no sources. EULA forbids redistributing the ModKit; this repo never vendors it. |
| Game | `D:\SteamLibrary\steamapps\common\Dystopika` (app 2379910) |
| Installed mods | `%USERPROFILE%\AppData\LocalLow\Voids Within\Dystopika\Mods\<Name>` |
| Workshop mods | `D:\SteamLibrary\steamapps\workshop\content\2379910\<id>` |
| Workshop upload | the "Dystopika Mod Manager" app shipped with the template (manual, Steam running) |

---

## 4. The tool: a Unity Editor package

### 4.1 Install procedure (for anyone)

Prerequisites, which the ModKit already demands: Unity Hub with a signed-in account
and Personal licence; Unity 2022.3.62 installed from the archive; the ModKit template
downloaded from the modding wiki, unzipped somewhere not cloud-synced, opened once;
Dystopika run once so the Mods folder exists.

Then:

1. Download the release `.unitypackage`.
2. In the template project: `Assets > Import Package > Custom Package`, import all.
   Everything lands in `Assets/Dystopographica/`.
3. `Dystopographica > Make landscape` appears in the menu bar.

Updating is importing the newer package over the old one.

### 4.2 The window

```
┌ Make landscape ──────────────────────────────────────────────────────┐
│ Heightmap                                                            │
│ [ C:/maps/miami.png                                     ] [Browse]   │
│ 2000 x 2000 pixels, 16-bit. The terrain will be resampled to 2049.   │
│ ┌────────────────────────────┐   ┃▁▂▃▂▁                              │
│ │                            │   ┃▂▃▅▇█▇▅▃    ━━ plane end           │
│ │  preview, with the         │   ┃▃▅▇████▇▅   ━━ plane start         │
│ │  starting build area       │   ┃▂▃▅▇▇▅▃▂    ━━ water               │
│ │  outlined in yellow        │   ┃▁▂▃▂▁                              │
│ └────────────────────────────┘   histogram, lowest values at bottom  │
│ Blue is under water and green is the city plane. The plane covers    │
│ 67% of the starting build area.                                      │
│ [ Export as a painted map ]                                          │
│ Mapping                                                              │
│   Plane end, Plane start                      sliders, 0 to 1        │
│   [x] Water   Water level (slider)                                   │
│   Map size [400% ▾]   Build area (% of map) [100]                    │
│   Peak height [20]   Lowest depth [2] (on a dry map only)            │
│   Fill specks under (px) [0]   Smoothing [1]   Edge softness [0.01]  │
│ Mod                                                                  │
│   Mod name [ ]   Author [ ]   Version [1]   Minor version [0]        │
│   [ Load this mod's saved settings ]                                 │
│ Landscape                                                            │
│   Display name [ ]   Thumbnail [made from the preview] [Browse][Clear]│
│   [ ] Also make a version with normal water                          │
│ ⚠ anything that stops the landscape being made, one line each        │
│ [ Make landscape ]  [ Compile and install ]  [ Open Mods folder ]    │
└──────────────────────────────────────────────────────────────────────┘
```

**Heightmap input.** An 8-bit or 16-bit PNG, read straight from disk and never through
the texture importer, which would resize, compress and gamma-correct it. The decoder
is our own, about a hundred lines. Unity's `Texture2D.LoadImage` cuts 16-bit PNGs
down to 8 bits (a test caught this), and 256 levels is too few for flat land: in the
Miami sample the whole city spans about 6% of the value range. Greyscale is expected;
a colour image contributes its red channel, unless it is a painted map (below). Interlaced, indexed and lower-bit-depth
PNGs are refused with a message that says how to re-save them. Square images only for
now. Unity Terrain accepts resolutions of 2^n + 1 (513, 1025, 2049, 4097); any other
size is resampled bilinearly to the nearest one and the window says so. Unity's RAW
format is not read yet.

**Painted map.** The cursors divide the map by height alone, which cannot say
"this low ground is city, that low ground is river". For that, "Export as a painted
map" saves the map the preview shows (smoothed, specks filled, at the heightmap's
full size) as a 16-bit colour PNG to paint on in an image editor:

- blue is under water;
- green is the city plane;
- grey is everything else, and its lightness is its height. Mid-grey is level with
  the city plane, white is the peak, and black is as low as land goes: the
  waterline, or the lowest depth on a map with nothing blue.

The painted file is chosen with the same Browse button as a heightmap. A PNG with
any blue or green in it is a painted map (a channel has to lead the other two by
0.1, so a soft brush edge counts and a grey picture's faint colour cast does not);
any other PNG is a heightmap, as before. The picture says what is water and what
is plane, so for a painted map the window hides the levels bar, the three cursor
sliders and the Water switch: there is water if anything is blue. Peak height,
lowest depth, smoothing, the speck filter and edge softness still apply. The
cursors keep their positions in the settings, for when the heightmap is chosen
again.

Inside, a painted map is turned into an ordinary map of values with water, plane
and heights at fixed levels (blue 0, shore 0.2 to 0.4, green 0.5, hills 0.6 to 1),
and the cursors are set to those levels for it (`LandscapeSettings.For`). From
there it is made like any heightmap, so nothing downstream knows the difference.
Exported and made again untouched, a map gives the same terrain, which a test
checks; with smoothing on it is smoothed a second time. The round trip is lossy
by design: 8-bit editors leave 128 shades of grey on each side of the plane, and a
painted map cannot be re-divided with the cursors. The window reads the file again
whenever it is saved, so painting and looking can alternate.

**Mapping: the levels bar.** A vertical bar spans the heightmap's value range, lowest
at the bottom, with the image's histogram drawn inside it so the mass of flat land
shows as a spike. Three cursors slide along it and cannot cross:

- `w`, the lowest, marks the shoreline: everything below it is under water;
- `a`, the middle, marks the start of the city-plane band;
- `b`, the top, marks its end.

Three numbers set the vertical scale. The first is a field in the window, the
second is fixed, and the third is a field on a dry map only:

- **Peak height** `P`: the in-game height of the heightmap's highest point;
- **Water depth** `W`: how far the water surface lies below the city plane. Fixed
  at 0.062 (§4.3). It was a field while the ground decided where buildings stand;
- **Lowest depth** `D`: on a dry map, the in-game depth of the heightmap's lowest
  point. With water it is the depth of the sea bed, fixed at 0.26 (§4.3), and the
  window shows no field for it.

The image is first stretched so its lowest value is 0 and its highest is 1. Cursor
positions and the formulas below are on that scale, with `t` a pixel's value:

```
t in [a, b]   →  y = 0                                the city plane
t > b         →  y = +P · (t − b) / (1 − b)           hills; the highest point lands on +P
w ≤ t < a     →  y = −W · (a − t) / (a − w)           the shore, from the plane down to the water
t < w         →  y = −D                                the bed: everything under water, flat
water plane   →  y_w = −W                             a flat quad; terrain is not modified
```

With "Water" switched off the map is dry: there is no water plane, the water cursor
is ignored, and everything below the band is one slope, `y = −D · (a − t) / a`.

The sides scale independently because real-world data usually wants that: hills
compressed until they are scenery, a river two metres deep exaggerated until it reads
as water. Moving a cursor changes the slopes, never where the extremes land. If the
band reaches the top or bottom of the range, that side has no terrain.

The curve is monotone and applied per pixel, so contour lines stay where they are and
the band collapses to a plateau wherever it occurs on the map. The shoreline is the
contour where the heightmap equals `w`. Land between `w` and `a` is shore: cursors
together give a quay wall, apart give a beach slope. The bar keeps `w ≤ a ≤ b`,
and `a` leads: `w` cannot rise past it, but `a` pushes `w` down ahead of it, and a
`w` that sits right at `a` stays with it both ways. They start together, which means
"everything lower than the city is water". At first `w` blocked `a` instead. To put
`a` just above a river, `w` had to be shoved down out of the way, and was then left
far below the river, which came out as dry ground: two New York exports had no
water at all for that reason.

The water surface has a depth of its own because of coastlines. In real elevation
data the sea is only just lower than the land beside it, so the natural place for the
water cursor is right under `a`. The first version read the water's height off the
curve at `w`, and the first in-game test, Miami, came out with the water 0.2 mm
below the city: a flat sheet.

The bed is flat, and at first it was deep as well, for the game's sake. Dystopika
marks ground that is a little below the plane with a lattice of blinking red
crosses: it looks buildable and is not. A bed that followed the heightmap got those
crosses wherever the water was shallow. In the Miami test the bay north of a
causeway lay about 0.45 units down and was covered in them; south of it the bay lay
0.47 and deeper and was clean, as was the ocean. The water is opaque, so the bed's
shape is never seen, and putting all of it two units down kept every stretch of
water clear of the marks. The marks only arise where the terrain carries a
collider. It no longer does (§4.3), and the bed now lies just under the water.

**Knee ("Edge softness").** A width `k`, as a share of the value range. Over the first
`k` past a band edge the height follows a quadratic that leaves the band with no
slope; beyond that it is a straight line. The line's slope is scaled so the extremes
still land on `+P` and `−D`. With `k = 0` the curve is exactly the formulas above and
terrain meets the plateau in a crease.

**Input smoothing.** Real-world data is noisy, and pixels just outside the band inside
the city become bumps and pits (grey and blue specks in the preview). A Gaussian blur
of user-set sigma (in pixels of the original image, default one) runs on the heights
before the curve. Gaussian rather than median because elevation noise is broadband
rather than impulsive, and a separable Gaussian is cheap even at 4097². The knee
catches most residual specks; widening the band catches the rest. Zero disables it.
Smoothing lowers the highest point a little, so with it on the peak lands just under
`P`.

**Speck filter ("Fill specks under").** Smoothing strong enough to clear every
speck off the plane also rounds off the coast. The speck filter fills holes in the
plane and touches nothing else. A speck is a connected group of pixels outside the
band, water, shore and hill alike. One that is smaller than the limit and has
plane all the way round it is pulled into the band, so it comes out at y = 0. A
speck that reaches the edge of the map is not surrounded and stays. Pixels that
touch only at a corner count as one speck, or a river one pixel wide running
diagonally would be a row of specks and vanish. The limit is an area in pixels of
the heightmap file and zero switches the filter off. It runs after smoothing and
before the curve. The preview applies it to its own smaller picture with the
limit converted, so for limits under about 30 pixels the preview is only a rough
guide. It works one way only: small patches of plane inside water are left alone.
The window offers the limit as a slider from 0 to 5000, and the preview paints what
the filter filled in a brighter, yellower green, because a speck that simply turns
green is hard to spot among the rest.

**Map size and build area share.** "Map size" is the game's map size the landscape
is made for: 100%, 200% or 400%, default 400%. The slider below it says how much of
the map's width that size's build area covers, from 5% to 100%. The preview draws
the answer as a yellow square, with the build areas of any smaller sizes fainter
inside it. At 100%, the default, the whole map is the build area; lower values
leave scenery around the city. The map's width in the game follows from the two:
`S = (50 · √(size / 100)) / share`, so 100 units by default. A map made for 400%
also works at 100% and 200%, where the city sits on the middle of it. The window
first asked for `S` directly, with a default of 400, which made the map eight build
areas wide without anyone choosing it. The map is centred on the origin, and there
is no vertical offset control; the band defines the plane.

**Remembered settings.** Unity keeps a window's fields only while the window stays
open, so the window also writes its settings to `UserSettings/Dystopographica.json`
in the project and reads them back when it opens.

**Preview.** A copy of the heightmap shrunk to at most 512 pixels, smoothed, and
coloured: grey that lightens with height, blue below the water cursor, green inside
the band. The 50 × 50 starting build area is outlined, and a readout gives the share
of it that is on the plane. Repainted on every cursor change; the full-resolution
pass only runs on Make. Not built yet: hillshading, a separate colour for ground
above the plane inside the build area, and the larger square for 200% and 400%,
which waits for §6.

**No-water warning.** Only what lies below the water cursor is under water. A river
in the heightmap that sits between the water cursor and plane start comes out as
dry ground just below the city, above the water surface, and the landscape shows no
water at all. This happened on a New York map. The note under the preview gives the
share of the map that is under water. When it is nothing, and no cursor is being
dragged or typed, the window moves the water cursor up to plane start by itself
and says so: water switched on with nothing under it is never wanted.

**Problems.** Listed above the buttons, which stay disabled until the list is empty:
no heightmap, a heightmap that is not square, a mod name that cannot be a folder
name, a mod folder this tool did not make, a map size the game does not offer. Advisory checks
(coverage, asset size, the ModKit's own validation messages) are not built yet.

### 4.3 What "Make landscape" does

Everything goes into one flat folder with fixed file names:

```
Assets/<ModName>/
  Manifest.asset                # ModContentManifest
  Landscape Collection.asset    # ModLandscapeCollection, one definition per landscape
  Landscape Definition Custom Water.asset, Landscape Custom Water.prefab
                                # a map with water: ModLandscapeDefinition and prefab
  Landscape Definition.asset, Landscape.prefab
                                # a dry map; or the same map with normal water, if asked for
  Terrain Data.asset            # one terrain, shared by both prefabs
  Terrain.mat
  Custom Water.mat, Ripples.png # the custom water's material and its generated ripple texture
  Water.mat                     # the normal water's material, if asked for
  Thumbnail.png
  Dystopographica~/preset.json  # the window's settings
```

1. **Mod folder.** Created if missing, with `Dystopographica~` inside it first. Unity
   ignores folders ending in a tilde, so the preset is neither imported nor bundled,
   and the folder doubles as the mark that this tool owns the mod: a folder without
   it is someone else's and is never written to.
2. **Remap and exactness.** Resample to the terrain resolution, smooth, apply the
   curve, giving world heights from `−D` to `+P`. The terrain's `size.y = P + D`.
   Terrain heights are int16, `h = k / 32766`, world `y = y0 + h · size.y`, so the
   plane sits at `h_p = D / (P + D)`. The tool rounds `k_p = round(h_p · 32766)` and
   then sets the terrain's position to `y0 = −(k_p / 32766) · size.y`, so every cell
   in the band lands on y = 0 to float precision instead of the example's
   seven-millimetre miss; every other cell moves by at most half a step. A test reads
   the heights back from a real TerrainData to confirm it.
3. **TerrainData**: `heightmapResolution = R`, `size = (S, P + D, S)`, `SetHeights`.
   Written in place if it already exists (same asset path, same GUID). Image rows run
   north to south and terrain rows south to north, so the top of the image is +z.
4. **Materials**: a black URP/Lit for the terrain and, if the normal water is asked
   for, an opaque teal one for it, with the example's values. Those two are created once and then left
   alone, so edits to them survive a re-run. The custom water gets a material that
   uses our own shader (below) and a ripple texture generated into the mod folder.
   That material is put back to the shader's own settings on every run: its look
   belongs to the tool and is still being tuned against the game, and a material
   keeps its own copy of every setting, so one left alone would keep the look of
   whichever version of the tool first made it.
5. **Prefabs**, laid out like the example: a root at the origin; child `Terrain` at
   `(−S/2, y0, −S/2)`, two-sided shadows; child `Water`, Unity's built-in Plane
   scaled `S/10` at `y_w`. A map with water gets a prefab with the custom water
   and, if the normal water is asked for, a second that differs only in the water's
   material; a dry map gets one with no water. Saved over the previous prefabs. A
   prefab that is no longer asked for is deleted, because everything in the folder
   is bundled.

   Colliders decide where the game lets a building stand. Ours is the Night City
   layout (§3.7): the terrain and the water carry no collider, and a child
   `Buildable Plane`, Unity's Plane again with nothing left but its collider, lies
   at y = 0 under the whole map. Large buildings can then stand over the water,
   hard against a shore and inside a hill. The example's layout was the tool's
   first: the terrain has its `TerrainCollider` and the water keeps its own, so a
   building needs level city plane under all of it, which makes shores awkward: the
   game refuses a building whose footprint reaches past the edge. It stayed for a
   day as the off position of a "Build anywhere" switch and was removed on
   2026-11-24. The price of the sheet, seen in the game: the small buildings the
   game fills in by itself follow the same rule as the large ones, so they spill
   off the land and stand over the water (§3.7). The game's Density Brush clears
   them. Tests drop a ray on water, plane and hill.

   The levels are Night City's (§3.7). The game stands every
   building on the sheet, at y = 0. Drawn exactly there, with the water below, the
   land would leave anything over the water hovering. So everything drawn is raised:
   the city plane to y = 0.18, where Night City has its roads, and the water to
   y = 0.118, where Night City has its water. Every building is then sunk 0.18 into
   the land and 0.118 into the water, and one over the water stands in it. The
   water has to stay under the land, since ours is one sheet that runs under the
   whole map (Night City's concrete, at 0.10, is lower than its water), so the
   shore is a step of 0.062. The peaks keep their height above the city plane. A
   dry map is not raised.

   The sea bed, too, is where Night City's terrain lies under its water: at
   y = −0.08, a fifth of a unit under the surface. It used to lie two units down,
   and seen in the game from above the water looked like a canyon. Our water is
   drawn late, to mirror what is drawn before it (below), so the camera's picture
   of the scene's depths holds the sea bed and not the water, and whatever the game
   draws from that picture, its ground fog for one, is shaped like the bed. With
   the bed just under the surface the two are nearly the same shape. The deep bed
   was there to keep the game's red crosses off the water (§4.2), which only arise
   when the terrain carries a collider; under the sheet it does not.

   So shallow a shore needs finer terrain. Unity draws distant terrain with fewer
   triangles as long as nothing moves up or down by more than
   `heightmapPixelError` pixels, 5 by default. A step of a quarter of a unit is
   under 5 pixels from quite near, and blurring the step moves the waterline a
   long way sideways: at the default the coast went blocky from above. These
   landscapes set it to 1, the finest.

   `LandscapeSettings.GroundLift`, `WaterDrop` and `BedDrop` hold the three levels.
   They are constants, not window fields. Only a dry map has a lowest depth to enter.
6. **Thumbnail**: the user's PNG if one is chosen, otherwise the preview cropped to
   1024 × 512. Imported readable, uncompressed, without mipmaps, because the ModKit
   reads it back at compile time. It serves as both the landscape thumbnail and the
   mod's preview image.
7. **ModKit assets.** The manifest gets its GUID `Id` once, then name, author,
   version numbers, preview image, the landscape collection, and the BaseContent
   include from the project's ModKit Author Config. The definition gets its `id` once,
   the display name's slug plus the first eight characters of the manifest id (a new
   definition otherwise carries the ModKit's placeholder `custom-landscape-definition`,
   the same in every mod), then name, thumbnail, `landscapeResource`, and the three
   flags. The two that hide the game's own grid and mountains are always on: they
   were window toggles until 2026-11-24, and every mod made had left them on.
   `allowLargerMapSizes` is off for a 100% map and on for the others.
   Seen in the game: with the flag off, the create-city screen does not offer 200% or
   400%, and with it on it offers both, so a 200% map cannot refuse 400% and shows
   bare ground at its edges there. A map with water is listed under its plain
   name. With the normal water asked for there is a second definition, listed after
   it as "Name (normal water)". The custom water's id is the other's plus
   `-custom-water`, whether or not the other exists, so ticking and unticking the
   box never changes an id. Private fields are set through `SerializedObject`, the way
   the Inspector sets them. Each object is filled in completely before its file is
   written: creating the file first leaves a moment when it holds default values,
   and a test caught Unity reloading it in that moment and losing the real ones.
8. **Preset**: the window's settings are saved to `Dystopographica~/preset.json`.
   "Load this mod's saved settings" brings them back; the generator can write the
   same file to hand over its parameters.

Re-running overwrites in place. Ids live in the assets (manifest `Id`, definition
`id`), so renaming the landscape's display name never changes them and saves keep
resolving. The mod name is the folder name, so a new mod name is a new mod. Files
left by the short-lived landscape-per-size scheme are deleted on the next Make.

**The custom water.** A mod cannot carry scripts, so everything is in a shader,
`Dystopographica/Water`, which ships in the package and is pulled into the mod by
the compile because the material uses it. Two copies of one ripple texture slide
across each other in different directions, in world units, and tilt the surface the
light bounces off. The surface is navy and smooth, and the
side of each ripple facing one fixed direction is tinted lighter and the other
darker, so the ripples show even in flat light. The ripple tile is 2 units: at 10
the pattern was far too large against the buildings. Each copy of the texture is
turned by a different odd angle, so a small tile does not show as a repeating grid.
The ripple texture is generated from a sum of sine waves that each fit the tile a
whole number of times, so it repeats without a seam and the package needs no image
files. The lighting is URP's own, with the same keywords as Unity's Lit shader, so
the water is lit, shadowed and fogged like everything else.

What it took to get here, each step seen in the game:

1. A dark mirror that relied on reflections to be seen. With nothing to reflect it
   was all but black. A test now holds the water to about the normal water's
   brightness in a scene with no reflections.
2. Lit, but with no light of its own. It looked exactly like the normal water: the
   game's scenes are dim and evenly lit.
3. Glints of its own light on the ripples, and a reflection probe in the prefab. The
   glints were hard bright blobs all over the water and very distracting, and the
   probe showed nothing. Both are gone. A test holds the water to having no bright
   patches in a dark scene, and another to the prefab holding no probe.
4. A calm dark surface with a faint sheen towards the horizon, with the mirror
   image described below added to its own colour. Much better, but only the
   brightest things showed: adding something dark changes nothing. And the water
   read as bright against the sky and the land.
5. The mirror image takes the place of the water's colour (85% of it, all of it
   towards the horizon), so dark walls show as dark shapes; the sky is mirrored
   too; and the water's colour is the normal water's, 15% darker. Very close, but
   beside Night City's water the mirror was a bit too strong and the water still
   turquoise and light.
6. The image takes 60% of the colour's place, rising to 84% towards the horizon,
   and the water is a dark navy, (0.11, 0.19, 0.32). Colour and mirror right, but
   the navy a little too dark.
7. Now: the same blue, as light to the eye as halfway between that navy and the
   teal of step 5: (0.195, 0.315, 0.51). A test renders all three and holds ours
   to the middle fifth of the way between the other two.

The mirror image is found on the screen (screen-space reflection). By the time the
water is drawn, the camera has a picture of every solid thing in view and of how
far away each is. For each point of the water the shader follows the mirrored line
of sight across those pictures, in 28 strides that start at 0.08 units and grow by
22% each (about 100 units in all), until the line passes behind something; six
halvings then home in on the place, and the colour drawn there is the mirror image.
The line leans with the ripples, at 6% of their lean: at 25% the image was scattered
into speckle. So buildings appear upside down on the water in front of them, exactly
where a mirror would show them, wobbling. A line that runs its whole length and
meets nothing ends somewhere on the screen, and what the picture shows there is the
sky or something far off: that is mirrored too, but only where the camera is looking
along the line (within about 25 degrees). The picture shows, at any place, what the
camera sees past it. Seen from high above, the mirrored line comes back up towards
the camera and ends in front of the ground it started from, and the water showed
its own bed until this was caught. What this cannot do: mirror anything
that is off the screen or hidden behind something else. A tower whose top is out of
view is mirrored only as far up as the view goes, and the image fades out towards
the screen's edges. Where the line leaves the view the water shows its own colour,
its sheen and the scene's surroundings (an environment reflection) instead.

Three things follow from looking at the camera's pictures:

- The water is drawn in the queue of the see-through things (2900), after the
  pictures are taken, though nothing shows through it. It has no depth passes of
  its own, so depth-based effects see the sea bed under it. That is why the sea
  bed lies just under the surface when buildings may stand anywhere (step 5).
- The game has to keep both pictures (URP's "Opaque Texture" and "Depth Texture").
  Night City's water needs them too (§3.7), so it very likely does. If it keeps
  neither, the line never finds anything; if only the depths, Unity hands the
  shader plain grey for the colours, which would paint a grey ghost of every
  building, so the shader checks that both pictures are of a real size and
  otherwise mirrors nothing. Tests cover the mirror image of a red block, its
  absence to one side, a black block showing dark on lit water, a red sky on the
  water towards the horizon, and the missing-picture case.
- A reflection is only as good as what the game draws as solid. Lights and signs
  that the game draws see-through are not in the picture and are not mirrored.

Tests also compile the shader, draw it in the editor at two moments and check that
the picture changed, and check it arrives in the bundle.

### 4.4 Compile and install

"Compile and install" always runs Make first: skipping it when the settings looked
unchanged let a newer tool go uncompiled. Then:

1. The `ConfigureAddressableState` steps from §3.6.
2. `manifest.PreCompileValidate(...)`; on an error it stops and points at the Console,
   so the ModKit's modal dialog is never reached.
3. `ModKitCompiler.Compile(config, manifest)` with the project's
   `Default ModKit Author Config` asset. The manifest is held in a static field
   meanwhile: Unity unloads assets that only a local variable refers to, the
   Addressables build triggers that, and the ModKit then writes a `manifest.json`
   whose `Landscapes` list is empty. The compile test caught this.
4. Check that `Assets/Export~/<Name>` now holds a `manifest.json` and a bundle.
5. Replace `<Mods>/<Name>` with a copy of it.

The generated mod is a normal mod in the wizard's eyes, so the Creation Wizard's own
Compile and Export buttons work on it too. Assets the user adds to the folder are
bundled and survive re-runs; the prefab itself is regenerated each time.

### 4.5 Iteration loop

Adjust → Compile and install → reload the city in the game. Cities store the
landscape id, not the geometry, so an existing test city should show the new terrain
on reload (the Night City author tells subscribers to re-subscribe to receive
landscape updates). Confirm on the first real build. Buildings standing on ground
that moved will float or snap, which is fine for testing.

### 4.6 Headless entry points

The window and a static `Dystopographica.Cli` class call the same code, so everything
the window does can also run without it, in Unity's batch mode, from a preset file:

```bash
pwsh tools/dev.ps1 run Dystopographica.Cli.Make preset=<preset.json>
pwsh tools/dev.ps1 run Dystopographica.Cli.CompileAndInstall preset=<preset.json> install=<folder>
pwsh tools/dev.ps1 run Dystopographica.Cli.RenderPreview preset=<preset.json> out=<file.png>
pwsh tools/dev.ps1 run Dystopographica.Cli.RenderLandscape "mod=<mod name>" out=<file.png>
```

Arguments are `name=value` pairs, because PowerShell would take a dash-prefixed
argument as one of the script's own. `install` is optional and defaults to the game's
Mods folder. `RenderPreview` writes the picture the window would show.
`RenderLandscape` photographs a made landscape in 3D, with the terrain painted grey
so its shape shows: a look at the result without starting the game. Its optional
arguments are `water=normal` (the version with normal water, where the mod has
one), `terrain=own` (keep the black terrain), `light=night`,
`distance=<units>`, `height=<units>` (how high the camera stands; a large value
looks down from above) and `city=yes`, which stands a few lit towers on the shore
nearest the middle of the map and points the camera at them, to see what the water
mirrors.

The script wraps `Unity.exe -batchmode -quit -projectPath modkit -executeMethod ...`.
Batch-mode notes: dialogs cannot show, which is why §4.4 pre-validates and relies on
`IgnoreOverwriteWarnings`; the first open of the template imports everything (three
to four minutes here), later runs take 10 to 25 seconds, and a compile adds 10 to 40;
the licence is the one Unity Hub activated.

### 4.7 Package structure, dependencies and testing

```
Assets/Dystopographica/
  Editor/
    Dystopographica.Editor.asmdef
    Heightmap.cs            # a grid of heights, and normalising it
    HeightmapFile.cs        # the PNG decoder, and the encoder for painted maps
    PaintedMap.cs           # §4.2: a map as a picture to paint on, and read back
    Resample.cs             # bilinear resize, block shrink, terrain resolutions
    Smoothing.cs            # Gaussian blur
    Specks.cs               # fills small holes in the city plane
    Ripples.cs              # generates the custom water's ripple texture
    Histogram.cs
    HeightCurve.cs          # §4.2: band, two sides, knee
    TerrainHeights.cs       # §4.3 step 2: quantisation and the exact plane
    Preview.cs              # preview colours, plane coverage, thumbnail crop
    LandscapeSettings.cs    # the window's fields, cursor rules, problems, preset
    LandscapeBuilder.cs     # §4.3: TerrainData, materials, prefab, thumbnail
    ModKitBridge.cs         # §4.3 step 7 and §4.4: everything that touches the ModKit
    MakeLandscapeWindow.cs  # the window: layout and input only
    Cli.cs                  # §4.6
  Shaders/
    Water.shader            # the custom water (§4.3)
  Tests/Editor/
    Dystopographica.Editor.Tests.asmdef
    *Tests.cs
```

Two assembly definitions, both editor-only: the tool and its tests. Unity's test
runner needs tests in their own assembly, and an assembly of our own means a change
recompiles only our code. The ModKit DLLs are referenced automatically; the
Addressables assemblies are named in the asmdef.

ModKit API surface used, all of it in `ModKitBridge`: `ModKitCompiler.Compile`,
`ModKitAuthorConfig`, `ModKitPathHelper`, `ModKitEditorHelper.ShowLocalModInstallLocation`,
`ModKitConsts`, `ModKitVersion`, the three ScriptableObject types, and
`PreCompileValidate`. If a ModKit update changes any of these, that file stops
compiling and the console says so.

**Testing** follows the rules in `CLAUDE.md`: test-first, all nontrivial logic covered.
The tests are Unity edit-mode tests (the Unity Test Framework ships in the template)
and run headlessly with `pwsh tools/dev.ps1 test`.

- Pure logic is unit-tested: the curve and its knee, the histogram, smoothing,
  resampling, quantisation and the y = 0 exactness, preview colouring, the cursor
  rules, and PNG decoding against small files that use every bit depth, colour type
  and row filter the decoder accepts.
- Asset generation is integration-tested inside the editor: build a tiny landscape
  into a scratch mod folder, read the terrain heights and ModKit assets back, build
  it again and check that every id survived.
- The ModKit bridge is integration-tested by compiling that mod for real and reading
  the exported `manifest.json`. Installing is tested against a temporary folder,
  never the game's Mods folder.
- The window is the stated exclusion. It holds layout and input only, and is checked
  by eye.

---

## 5. Tuning map

*Status*: **known** (documented or measured), **inferred** (good evidence,
unconfirmed), **unknown** (needs §6).

| # | Knob | What it controls | Evidence / default | Status | How to pin down |
|---|---|---|---|---|---|
| 1 | Grid extent at 100% | Where the plane must be | 50 × 50 centred on origin (dev video) | known | confirm with calibration rings |
| 2 | Grid extent at 200% / 400% | How wide the map is made for each map size | seen in game: about 1.4× and 2× the 100% width, so the percentage is the area (70.7 and 100 units); agrees with the 4× light budget and Night City's 2× terrain | known by eye | calibration, for exact figures |
| 3 | Datum height | Buildable surface | world y = 0 (docs, video, example +0.007) | known | — |
| 4 | Plane tolerance | What still accepts tiles, and what the game marks as near-miss ground | example plateau at +0.007 works; hills at +2 do not. Seen in game: ground down to about 0.45 below the plane gets red crosses (looks buildable, is not), ground about 0.47 and deeper gets none | partly known | terrace staircase ±0.02 … ±2 |
| 5 | Below-zero rendering | Whether rivers draw correctly | v1.7.0 known issue "cut off"; example river at −1.0 | unknown | trench in calibration mod |
| 6 | Map size and build area share | Which build area the map is sized against, and how much of the map it covers | defaults 400% and 100%: a 100-unit map that is all build area at 400%; the example is 100 units for a 50-unit area; community maps for 400% are 384–550 units | known | — |
| 7 | Resolution `R` | Detail vs size | example 2049 over 100 u; 4097 over 400 u = 0.1 u/cell; buildings ≈ 1 u | known | — |
| 8 | Peak height `P`, lowest depth `D` | In-game height of the heightmap's highest point and depth of its lowest; each side of the plane scales independently; quantisation step = (P + D)/32766 | example: hills to +18, river to −1; defaults 20 and 2; `D` is a field on a dry map only, and fixed with water (14b) | known | — |
| 9 | Plane band `a`, `b` | Which input heights become the city plane | cursors on the histogram; no default, data-dependent | known | — |
| 9a | Knee width ("Edge softness") | Shoulder between plateau and terrain | quadratic ease out of the band, then a straight line; default 1% of the value range | design choice | eyeball in game |
| 9b | Input smoothing | Speckle removal on real-world data | Gaussian sigma in pixels, default ≈ 1, 0 = off | design choice | red/blue specks in the preview |
| 9c | Speck limit | Holes in the plane smaller than this are filled | area in heightmap pixels, default 0 = off | design choice | specks inside green in the preview |
| 10 | Orientation | Which image edge is +z | top of image = +z; no flip control yet | design choice | compare preview to in-game view |
| 11 | Water cursor `w` / material | The shoreline (`w`) and how far the water surface lies below the plane (`W`, fixed at 0.062: 14b) | example −0.69 world; opaque teal URP/Lit; must sit below the plane band by the tolerance; custom water shaders misbehave in "Clear" weather (Isla notes) | known | — |
| 12 | Terrain material | The black look | `Black` URP/Lit; snow uses a stencil only on the game's PBR shader, so terrain gets no snow | known | check weather presets |
| 13 | Default grid mesh / mountains flags | Fallback plane, procedural mountains | example disables both; always disabled, and no longer window fields | known | — |
| 14 | Kind of water | Normal (flat, Unity's Lit shader) or custom (rippling and mirroring, our shader) | custom by default; "Also make a version with normal water" adds the normal one as a second landscape | the custom water and its mirror image are seen in the game | look at it under each weather preset |
| 14a | Where buildings may stand | One flat sheet at city level under the whole map, not the ground itself | always, the Night City layout (§3.7, §4.3); the ground-deciding layout and its "Build anywhere" switch are removed | seen in the game: large buildings go anywhere, and so do the small fill-in ones | — |
| 14b | Drawn levels on a map with water | How far buildings are sunk into the land and into the water, and how deep the sea bed is | land 0.18, water 0.118 and sea bed −0.08 above the level buildings stand on, Night City's measured levels | land and water seen in the game; the shallow sea bed only in the editor | look at the water from above: no canyon, and a sharp coast |
| 15 | Thumbnails | Landscape picker, mod card | 1024 × 512 PNG, readable; 800 × 450 works | known | — |
| 16 | Ids and names | Save identity, folder name, shader bundle name | manifest Id = GUID, landscape id kebab-case, Name = folder name | known | — |
| 17 | Pixel error / LOD | Popping, perf, and how sharp a shallow coast stays from far away | example `heightmapPixelError` 5; 1 at Night City's levels (§4.3) | known | — |
| 18 | Bundle size | Load time, Workshop limits | terrain-only mods 4–17 MB | known | — |
| 19 | Fog / edge | Hiding the terrain edge | unknown fog distance; community uses 400–550 u | unknown | look at the horizon at 400% |

Knobs 2, 4, 5 and 19 are game behaviour to observe once and then hard-code. Knobs
13, 14a, 14b and 17 are fixed in the code. The rest are window fields with the
defaults above.

---

## 6. Calibration landscape (menu item)

`Dystopographica > Make calibration landscape` generates, through the same code path,
a synthetic landscape that answers the unknowns in one game session:

- A flat plane at y = 0 across 600 × 600 units with a 5-unit checker and labelled
  rings at half-widths 25, 35.4, 50, 70.7, 100, 141, 200. Load at 100/200/400% and
  read where the Out-of-Bounds shading starts → knobs 1, 2.
- A staircase of terraces along +X at ±0.02, ±0.05, ±0.1, ±0.25, ±0.5, ±1, ±2;
  try placing a tile and a prop on each → knob 4.
- A trench along +Z from 0 to −3 units and a ridge along −Z → knob 5.
- Distance markers every 50 units → knob 19.

It is also the end-to-end smoke test for the package.

---

## 7. Risks and open questions

- **The custom water is on undocumented ground.** The modding wiki covers the
  game's own shader and Unity's built-in materials, not custom shaders. Two Workshop
  mods ship one and it works, but one of them reports glitches under some weather
  presets. Ours is written against the ModKit project's render settings, not the
  game's, which we cannot see. Its mirror image in particular needs the game's
  camera to keep pictures of the scene's colours and depths (§4.3); without them
  the water still draws but mirrors only the sky. The normal-water landscape, one
  tick in the window, is the fallback.
- **Building anywhere has a price.** The game has one rule for large buildings and
  for the small ones it fills in itself, so a map cannot let the first stand over
  the water and keep the second off it (§3.7). Small buildings spill onto the
  water until the player brushes them away. The layout that kept them off, the
  ground deciding, could not let a large building overlap a shore, and is gone. A
  sheet that covers the land and only a strip of water along the shore would sit
  between the two; it is not built.
- **Sunk buildings on bare terrain.** On a map with water, every building
  stands 0.18 below the drawn land. Night City covers that join with concrete and
  roads. Ours has black terrain, and whatever the game draws at ground level under
  its small buildings is buried by the same 0.18. How that looks is unseen.
- **ModKit API drift.** A ModKit update that changes `Compile`'s signature or the
  asset types breaks our package at compile time. Visible, fixable, but it couples
  releases to theirs.
- **Game updates.** River Valley users saw the river go flat after an update; v1.7.0
  cut off sub-zero terrain. Re-run the calibration landscape after game patches.
- **Validation is a black box.** `PreCompileValidate` rules live in the DLL; we can
  surface the messages but not read the rules.
- **Existing-city reload.** §4.5 assumes a city picks up a rebuilt landscape by id.
  Verify on the first build; if not, iteration means creating a new city each time.
- **PNG decoding** is our own code, because Unity's loader drops 16-bit files to 8
  bits. It is tested against small files of every kind it accepts, but not yet
  against a wide range of real exports.
- **A deep outlier sets the depth scale.** `D` is where the single lowest pixel
  lands. In the Manhattan sample the rivers are a flat surface with only a few pits
  below it, so the river bed ends up at a small fraction of `D`. If that proves
  awkward, the bottom of the bar could be a low percentile and not the minimum.
- **Placement quirks** on custom terrain (placement sensitivity on small pads, Isla
  notes; v1.5.2 fixed buildings snapping back). Keep the plane contiguous where you
  want a city.
- **Multiple terrains** for very large high-resolution maps are possible (Isla
  Salvación uses nine) but out of scope for v1.
- **Workshop publishing** stays manual through the Mod Manager.

---

## 8. Repo layout and next steps

```
dystopographica/
  modkit/                       # the Unity project: an unzipped ModKit template, git-ignored
    Assets/Dystopographica/     # our package, the only tracked part of modkit/
  tools/dev.ps1                 # headless Unity: compile + test, or run one method
  docs/pipeline.md              # this document
  docs/reference/               # the landscape docs page you exported; local only, git-ignored
  CLAUDE.md                     # development rules, layout, commands, first-time setup
```

The ModKit template is never committed. The package lives inside the project rather
than being linked in from elsewhere: Unity only compiles what is in the project, and
this keeps setup to one unzip with no folder links to create or maintain.

### Development workflow

Unity has no separate build step for editor code: it compiles every script under
`Assets/` whenever a file changes, and an editor script takes effect as soon as that
compile finishes. Only one Unity instance can have a project open, so the two ways of
working take turns:

- **Headless.** `pwsh tools/dev.ps1 test` opens the project without a window,
  compiles, runs the edit-mode tests, prints compiler errors and failures, and exits.
  This is the red-green loop, and it needs the Unity editor to be closed.
- **Interactive.** Open `modkit/` from Unity Hub (Add > Add project from disk). The
  tool is under the `Dystopographica` menu. After a code change, click into the Unity
  window and it recompiles by itself; errors appear in the Console window. Tests can
  also be run from Window > General > Test Runner.

Unity creates a `.meta` file beside every file and folder in `Assets/`. They carry the
ids Unity uses to link assets, so each one is committed with its file.

### Next steps

Done: the pure logic, the window, `LandscapeBuilder`, `ModKitBridge`, and the
headless entry points. Next:

1. Look at the lighter navy and the shallow sea bed in the game, under each
   weather preset.
2. Look at the new levels in the game: buildings standing in the water, and
   buildings sunk 0.18 into the land.
3. The calibration landscape; make knobs 1 and 2 exact and fill in 4, 5, 19.
4. Export the `.unitypackage`; write the install page.
5. Then the generator half, which hands over a 16-bit heightmap and optionally a `preset.json`.

---

## 9. Sources

- ModKit docs (Confluence, space DYSMOD): [Creating New Landscapes](https://voidswithin.atlassian.net/wiki/spaces/DYSMOD/pages/170950658), [ModKit Release Archive](https://voidswithin.atlassian.net/wiki/spaces/DYSMOD/pages/102989870), [Unity Asset Bundles](https://voidswithin.atlassian.net/wiki/spaces/DYSMOD/pages/105381907), [Mod Content Manifest](https://voidswithin.atlassian.net/wiki/spaces/DYSMOD/pages/105250852), [Distributing Your Mod](https://voidswithin.atlassian.net/wiki/spaces/DYSMOD/pages/105218060), [Mod Manager](https://voidswithin.atlassian.net/wiki/spaces/DYSMOD/pages/105054261), [Getting Started](https://voidswithin.atlassian.net/wiki/spaces/DYSMOD/pages/102924696), [Your First Mod](https://voidswithin.atlassian.net/wiki/spaces/DYSMOD/pages/104824845), [Legal](https://voidswithin.atlassian.net/wiki/spaces/DYSMOD/pages/231800833), [Textures and Materials](https://voidswithin.atlassian.net/wiki/spaces/DYSMOD/pages/105545802).
- Video tutorial "Adding Custom Landscapes" (Loom, 20 min, Oct 2025): https://www.loom.com/share/ab750e1851d5481c9aa3820e22a80fd0 (source of the 50×50 grid size and the y=0 rule).
- Steam news (Steam news API, app 2379910): "v1.5 Meditations" (2025-06-21, landscapes beta), "v1.6 Expanded Mod Support" (2025-10-09), "v1.7.0" (2025-12-03, sub-zero known issue), "New! Larger Map Sizes" (2025-12-24), "v1.7.7 ModKit Improvements" (2026-02-24), "v1.7.8" (2026-04-16, Mod Debug Tools), "v1.7.11" (2026-08-03, ModKit 1.2.2).
- Workshop landscape mods inspected on disk: [Cyberpunk District / Night City](https://steamcommunity.com/sharedfiles/filedetails/?id=3481319601), [Triple River](https://steamcommunity.com/sharedfiles/filedetails/?id=3766383201), [Davy Jones](https://steamcommunity.com/sharedfiles/filedetails/?id=3548824423), [Nueva Isla Salvación](https://steamcommunity.com/sharedfiles/filedetails/?id=3670290023), [Post Nuclear](https://steamcommunity.com/sharedfiles/filedetails/?id=3676046488); descriptions also read for [River Valley](https://steamcommunity.com/workshop/filedetails/?id=3481713968), [Pandaemonium](https://steamcommunity.com/sharedfiles/filedetails/?id=3665751575), [Hamster Off-Grid](https://steamcommunity.com/sharedfiles/filedetails/?id=3610175803).
- Cyberpunk District change notes, read 2026-11-24 for the history of its colliders (§3.7): https://steamcommunity.com/sharedfiles/filedetails/changelog/3481319601
- ModKit template v1.2.2 (your copy): `Assets/Plugins/Neo3.ModKit.Public*.dll` reflected with System.Reflection.Metadata; example landscape parsed with UnityPy.
- Unity: [EditorUtility.DisplayDialog](https://docs.unity3d.com/ScriptReference/EditorUtility.DisplayDialog.html).
