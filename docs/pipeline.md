# Heightmap → Dystopika landscape mod

Status: design plus a first working version, last revised 2026-11-23. The tool
exists and its tests pass, including a real mod compile. In the game, a Miami
heightmap now shows land and water at the 100% map size. The landscape for each
larger map size (§3.4) is built and tested but not yet seen in the game.
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
  becomes the city plane, and three numbers set the in-game height of the highest
  point, the depth of the water surface, and the depth of the lowest point. The curve is monotone and applied per
  pixel, so the terrain's shape is preserved and no masks or image editing are
  needed. This is what makes real-world elevation data of mostly flat areas usable
  directly.
- One map per mod, with a landscape for each map size: the game widens the build
  area at 200% and 400% but never scales the landscape, so each size gets the same
  map scaled up to fill it. Ids are generated once and live in the assets;
  re-running overwrites in place, so iterating never involves matching ids.
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
| Vertical scale | Two numbers, one per side of the plane: the in-game height of the highest point and the in-game depth of the lowest | Terrain above the playfield often needs different scaling from terrain below it; typing where the extremes should land is more direct than a units-per-value factor |
| Water height | Its own number, the depth of the water surface below the plane. The water cursor only picks the shoreline | In real coastal data the sea is barely lower than the land, so a height read off the curve put the water level with the city |
| Landscapes per mod | One map, with a landscape for each ticked map size (100% always, 200% and 400% optional); re-run overwrites in place | The game does not scale a landscape for a larger map size, it only widens the build area. A map sized for 100% sat in a border of bare ground at 400%, and one sized for 400% looked far too large at 100%. Chosen 2026-11-23 over one landscape sized for the largest |
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

### 3.7 Toolchain and locations

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
│ Mapping                                                              │
│   Plane end, Plane start                      sliders, 0 to 1        │
│   [x] Water   Water level (slider)   Water depth [0.7]               │
│   Build area (% of map) [100]   Peak height [20]   Lowest depth [2]  │
│   Smoothing [1]   Edge softness [0.01]                               │
│ Mod                                                                  │
│   Mod name [ ]   Author [ ]   Version [1]   Minor version [0]        │
│   [ Load this mod's saved settings ]                                 │
│ Landscape                                                            │
│   Display name [ ]   Thumbnail [made from the preview] [Browse][Clear]│
│   [x] 200% landscape  [x] 400% landscape                             │
│   [x] Hide default grid  [x] Hide default mountains                  │
│ ⚠ anything that stops the landscape being made, one line each        │
│ [ Make landscape ]  [ Compile and install ]  [ Open Mods folder ]    │
└──────────────────────────────────────────────────────────────────────┘
```

**Heightmap input.** An 8-bit or 16-bit PNG, read straight from disk and never through
the texture importer, which would resize, compress and gamma-correct it. The decoder
is our own, about a hundred lines. Unity's `Texture2D.LoadImage` cuts 16-bit PNGs
down to 8 bits (a test caught this), and 256 levels is too few for flat land: in the
Miami sample the whole city spans about 6% of the value range. Greyscale is expected;
a colour image contributes its red channel. Interlaced, indexed and lower-bit-depth
PNGs are refused with a message that says how to re-save them. Square images only for
now. Unity Terrain accepts resolutions of 2^n + 1 (513, 1025, 2049, 4097); any other
size is resampled bilinearly to the nearest one and the window says so. Unity's RAW
format is not read yet.

**Mapping: the levels bar.** A vertical bar spans the heightmap's value range, lowest
at the bottom, with the image's histogram drawn inside it so the mass of flat land
shows as a spike. Three cursors slide along it and cannot cross:

- `w`, the lowest, marks the shoreline: everything below it is under water;
- `a`, the middle, marks the start of the city-plane band;
- `b`, the top, marks its end.

Three fields set the vertical scale:

- **Peak height** `P`: the in-game height of the heightmap's highest point;
- **Water depth** `W`: how far the water surface lies below the city plane;
- **Lowest depth** `D`: on a dry map, the in-game depth of the heightmap's lowest
  point; with water, the depth of the sea bed. It must be more than `W`.

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
together give a quay wall, apart give a beach slope. The bar keeps `w ≤ a ≤ b`.

The water surface has a depth of its own because of coastlines. In real elevation
data the sea is only just lower than the land beside it, so the natural place for the
water cursor is right under `a`. The first version read the water's height off the
curve at `w`, and the first in-game test, Miami, came out with the water 0.2 mm
below the city: a flat sheet.

The bed is flat and deep for the game's sake. Dystopika marks ground that is a
little below the plane with a lattice of blinking red crosses: it looks buildable
and is not. A bed that followed the heightmap got those crosses wherever the water
was shallow. In the Miami test the bay north of a causeway lay about 0.45 units
down and was covered in them; south of it the bay lay 0.47 and deeper and was
clean, as was the ocean. The water is opaque, so the bed's shape is never seen,
and putting all of it at `D` keeps every stretch of water clear of the marks.

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

**Build area share.** The window asks how much of the map's width the build area
covers, from 5% to 100%, and draws the answer as the yellow square. At 100%, the
default, the whole map is the build area; lower values leave scenery around the
city. The share is the same at every map size. Internally it is still a world size
`S`, the width of the map at the 100% size: `S = 50 / share`. The window used to ask
for `S` directly, with a default of 400. That made the map eight build areas wide
without anyone choosing it, and sixteen wide at 400%. The map is centred on the
origin, and there is no vertical offset control; the band defines the plane.

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

**Problems.** Listed above the buttons, which stay disabled until the list is empty:
no heightmap, a heightmap that is not square, a mod name that cannot be a folder
name, a mod folder this tool did not make, a world size of zero. Advisory checks
(coverage, asset size, the ModKit's own validation messages) are not built yet.

### 4.3 What "Make landscape" does

Everything goes into one flat folder with fixed file names:

```
Assets/<ModName>/
  Manifest.asset                # ModContentManifest
  Landscape Collection.asset    # ModLandscapeCollection, one definition per map size
  Landscape Definition.asset    # ModLandscapeDefinition, the 100% landscape
  Landscape.prefab
  Terrain Data.asset
  Landscape Definition 200.asset, Landscape 200.prefab, Terrain Data 200.asset
  Landscape Definition 400.asset, Landscape 400.prefab, Terrain Data 400.asset
  Terrain.mat, Water.mat
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
4. **Materials**: a black URP/Lit for the terrain and an opaque teal one for the
   water, with the example's values. They are created once and then left alone, so
   edits to them survive a re-run.
5. **Prefab**, laid out like the example: a root at the origin; child `Terrain` with
   `Terrain` + `TerrainCollider` at `(−S/2, y0, −S/2)`, two-sided shadows; optional
   child `Water`, Unity's built-in Plane scaled `S/10` at `y_w`, collider kept as in
   the example. Saved over the previous prefab.
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
   flags. The collection holds one definition per map size. Private fields are set
   through `SerializedObject`, the way the Inspector sets them.

**Map sizes.** Steps 2, 3, 5 and 7 run once for each ticked map size. The landscape
for a larger size is the same map scaled by the build area's growth in width, √2 at
200% and 2 at 400%: world size, peak height, and the bed's depth below the water
all scale, so the terrain keeps its shape. The water depth does not, so a quay is
the same height against the buildings at every size. The larger landscapes take
the 100% id plus `-200` or `-400`, and when there is more than one the game lists
them as "Name (100%)", "Name (200%)", "Name (400%)". `allowLargerMapSizes` is
off for the 100% landscape and on for the others. Seen in the game: with the flag
off, the create-city screen does not offer 200% or 400%, and with it on it offers
both, so a 200% landscape cannot refuse 400%. Unticking a size deletes its three assets,
since everything in the folder is bundled.
8. **Preset**: the window's settings are saved to `Dystopographica~/preset.json`.
   "Load this mod's saved settings" brings them back; the generator can write the
   same file to hand over its parameters.

Re-running overwrites in place. Ids live in the assets (manifest `Id`, definition
`id`), so renaming the landscape's display name never changes them and saves keep
resolving. The mod name is the folder name, so a new mod name is a new mod.

### 4.4 Compile and install

"Compile and install" first runs Make if the mod does not exist yet or the settings
differ from its saved preset. Then:

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
so its shape shows: a look at the result without starting the game.

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
    HeightmapFile.cs        # the PNG decoder
    Resample.cs             # bilinear resize, block shrink, terrain resolutions
    Smoothing.cs            # Gaussian blur
    Histogram.cs
    HeightCurve.cs          # §4.2: band, two sides, knee
    TerrainHeights.cs       # §4.3 step 2: quantisation and the exact plane
    Preview.cs              # preview colours, plane coverage, thumbnail crop
    LandscapeSettings.cs    # the window's fields, cursor rules, problems, preset
    LandscapeBuilder.cs     # §4.3: TerrainData, materials, prefab, thumbnail
    ModKitBridge.cs         # §4.3 step 7 and §4.4: everything that touches the ModKit
    MakeLandscapeWindow.cs  # the window: layout and input only
    Cli.cs                  # §4.6
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
| 2 | Grid extent at 200% / 400% | How far each larger landscape is scaled up | seen in game: about 1.4× and 2× the 100% width, so the percentage is the area (70.7 and 100 units); agrees with the 4× light budget and Night City's 2× terrain | known by eye | calibration, for exact figures |
| 3 | Datum height | Buildable surface | world y = 0 (docs, video, example +0.007) | known | — |
| 4 | Plane tolerance | What still accepts tiles, and what the game marks as near-miss ground | example plateau at +0.007 works; hills at +2 do not. Seen in game: ground down to about 0.45 below the plane gets red crosses (looks buildable, is not), ground about 0.47 and deeper gets none | partly known | terrace staircase ±0.02 … ±2 |
| 5 | Below-zero rendering | Whether rivers draw correctly | v1.7.0 known issue "cut off"; example river at −1.0 | unknown | trench in calibration mod |
| 6 | Build area share (world size `S` = 50 / share) | How much of the map is city and how much scenery | default 100%, the whole map is the build area (`S` = 50); the example is 50% (`S` = 100); community maps for 400% are 384–550 units | known | — |
| 7 | Resolution `R` | Detail vs size | example 2049 over 100 u; 4097 over 400 u = 0.1 u/cell; buildings ≈ 1 u | known | — |
| 8 | Peak height `P`, lowest depth `D` | In-game height of the heightmap's highest point and depth of its lowest; each side of the plane scales independently; quantisation step = (P + D)/32766 | example: hills to +18, river to −1; defaults 20 and 2 | known | — |
| 9 | Plane band `a`, `b` | Which input heights become the city plane | cursors on the histogram; no default, data-dependent | known | — |
| 9a | Knee width ("Edge softness") | Shoulder between plateau and terrain | quadratic ease out of the band, then a straight line; default 1% of the value range | design choice | eyeball in game |
| 9b | Input smoothing | Speckle removal on real-world data | Gaussian sigma in pixels, default ≈ 1, 0 = off | design choice | red/blue specks in the preview |
| 10 | Orientation | Which image edge is +z | top of image = +z; no flip control yet | design choice | compare preview to in-game view |
| 11 | Water cursor `w` / material | The shoreline (`w`) and how far the water surface lies below the plane (`W`, default 0.7) | example −0.69 world; opaque teal URP/Lit; must sit below the plane band by the tolerance; custom water shaders misbehave in "Clear" weather (Isla notes) | known | — |
| 12 | Terrain material | The black look | `Black` URP/Lit; snow uses a stencil only on the game's PBR shader, so terrain gets no snow | known | check weather presets |
| 13 | Default grid mesh / mountains flags | Fallback plane, procedural mountains | example disables both; defaults on | known | — |
| 14 | 200% and 400% landscapes | Whether the mod carries a scaled-up landscape for each larger map size | both on by default; scale = √(percent/100) | known | — |
| 15 | Thumbnails | Landscape picker, mod card | 1024 × 512 PNG, readable; 800 × 450 works | known | — |
| 16 | Ids and names | Save identity, folder name, shader bundle name | manifest Id = GUID, landscape id kebab-case, Name = folder name | known | — |
| 17 | Pixel error / LOD | Popping, perf | example `heightmapPixelError` 5 | known | — |
| 18 | Bundle size | Load time, Workshop limits | terrain-only mods 4–17 MB | known | — |
| 19 | Fog / edge | Hiding the terrain edge | unknown fog distance; community uses 400–550 u | unknown | look at the horizon at 400% |

Knobs 2, 4, 5 and 19 are game behaviour to observe once and then hard-code. The rest
are window fields with the defaults above.

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

1. Open the window in Unity and judge the layout and the cursor feel by hand.
2. Compile and install a real heightmap and look at it in the game. First check of
   everything the tests cannot see: the look, the water, whether tiles place.
3. The calibration landscape; make knobs 1 and 2 exact and fill in 4, 5, 19.
4. Export the `.unitypackage`; write the install page.
5. Then the generator half, which hands over a 16-bit heightmap and optionally a `preset.json`.

---

## 9. Sources

- ModKit docs (Confluence, space DYSMOD): [Creating New Landscapes](https://voidswithin.atlassian.net/wiki/spaces/DYSMOD/pages/170950658), [ModKit Release Archive](https://voidswithin.atlassian.net/wiki/spaces/DYSMOD/pages/102989870), [Unity Asset Bundles](https://voidswithin.atlassian.net/wiki/spaces/DYSMOD/pages/105381907), [Mod Content Manifest](https://voidswithin.atlassian.net/wiki/spaces/DYSMOD/pages/105250852), [Distributing Your Mod](https://voidswithin.atlassian.net/wiki/spaces/DYSMOD/pages/105218060), [Mod Manager](https://voidswithin.atlassian.net/wiki/spaces/DYSMOD/pages/105054261), [Getting Started](https://voidswithin.atlassian.net/wiki/spaces/DYSMOD/pages/102924696), [Your First Mod](https://voidswithin.atlassian.net/wiki/spaces/DYSMOD/pages/104824845), [Legal](https://voidswithin.atlassian.net/wiki/spaces/DYSMOD/pages/231800833), [Textures and Materials](https://voidswithin.atlassian.net/wiki/spaces/DYSMOD/pages/105545802).
- Video tutorial "Adding Custom Landscapes" (Loom, 20 min, Oct 2025): https://www.loom.com/share/ab750e1851d5481c9aa3820e22a80fd0 (source of the 50×50 grid size and the y=0 rule).
- Steam news (Steam news API, app 2379910): "v1.5 Meditations" (2025-06-21, landscapes beta), "v1.6 Expanded Mod Support" (2025-10-09), "v1.7.0" (2025-12-03, sub-zero known issue), "New! Larger Map Sizes" (2025-12-24), "v1.7.7 ModKit Improvements" (2026-02-24), "v1.7.8" (2026-04-16, Mod Debug Tools), "v1.7.11" (2026-08-03, ModKit 1.2.2).
- Workshop landscape mods inspected on disk: [Cyberpunk District / Night City](https://steamcommunity.com/sharedfiles/filedetails/?id=3481319601), [Triple River](https://steamcommunity.com/sharedfiles/filedetails/?id=3766383201), [Davy Jones](https://steamcommunity.com/sharedfiles/filedetails/?id=3548824423), [Nueva Isla Salvación](https://steamcommunity.com/sharedfiles/filedetails/?id=3670290023), [Post Nuclear](https://steamcommunity.com/sharedfiles/filedetails/?id=3676046488); descriptions also read for [River Valley](https://steamcommunity.com/workshop/filedetails/?id=3481713968), [Pandaemonium](https://steamcommunity.com/sharedfiles/filedetails/?id=3665751575), [Hamster Off-Grid](https://steamcommunity.com/sharedfiles/filedetails/?id=3610175803).
- ModKit template v1.2.2 (your copy): `Assets/Plugins/Neo3.ModKit.Public*.dll` reflected with System.Reflection.Metadata; example landscape parsed with UnityPy.
- Unity: [EditorUtility.DisplayDialog](https://docs.unity3d.com/ScriptReference/EditorUtility.DisplayDialog.html).
