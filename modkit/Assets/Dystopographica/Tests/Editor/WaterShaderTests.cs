using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Dystopographica.Tests
{
    /// <summary>The custom water's shader, compiled and drawn for real by the editor.</summary>
    public class WaterShaderTests
    {
        const string ShaderName = "Dystopographica/Water";
        const int Size = 128;

        readonly List<Object> made = new List<Object>();
        AmbientMode ambientMode;
        SphericalHarmonicsL2 ambientProbe;
        float reflectionIntensity;

        // Even grey light from all round, and nothing for a shiny surface to reflect:
        // the editor's default sky would flatter the water in a way a night scene does not.
        [SetUp]
        public void LightTheSceneEvenly_WithNothingToReflect()
        {
            ambientMode = RenderSettings.ambientMode;
            ambientProbe = RenderSettings.ambientProbe;
            reflectionIntensity = RenderSettings.reflectionIntensity;
            RenderSettings.ambientMode = AmbientMode.Custom;
            SetAmbientLight(new Color(0.5f, 0.5f, 0.5f));
            RenderSettings.reflectionIntensity = 0f;
        }

        [TearDown]
        public void CleanUp()
        {
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientProbe = ambientProbe;
            RenderSettings.reflectionIntensity = reflectionIntensity;
            foreach (Object thing in made) Object.DestroyImmediate(thing);
            made.Clear();
        }

        [Test]
        public void Shader_CompilesWithoutErrors()
        {
            Shader shader = Shader.Find(ShaderName);
            Assert.IsNotNull(shader, "the shader ships with the package");
            Material material = Keep(new Material(shader));

            for (int pass = 0; pass < material.passCount; pass++) ShaderUtil.CompilePass(material, pass, true);

            string[] errors = ShaderUtil.GetShaderMessages(shader)
                .Where(m => m.severity == ShaderCompilerMessageSeverity.Error)
                .Select(m => $"{m.message} (line {m.line})").ToArray();
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.IsTrue(shader.isSupported);
        }

        // Both of the render pipeline's ways of drawing pick up a pass with this name,
        // so the water shows whether the game renders forward or deferred.
        [Test]
        public void Shader_DrawsInAPassEveryRendererPicksUp()
        {
            Shader shader = Shader.Find(ShaderName);
            Assert.IsNotNull(shader);

            var lightModes = Enumerable.Range(0, shader.passCount).Select(pass => shader.FindPassTagValue(pass, new ShaderTagId("LightMode")).name).ToArray();

            CollectionAssert.AreEqual(new[] { "UniversalForwardOnly" }, lightModes);
        }

        // What the water mirrors it looks up in the camera's picture of everything solid.
        // That picture is taken when the solid things are done, so the water has to be
        // drawn after them (queues above 2500), and ahead of the see-through things
        // (3000) so that lights and haze over the water are drawn on top of it.
        [Test]
        public void Shader_DrawsTheWaterAfterEverythingSolid()
        {
            int queue = Shader.Find(ShaderName).renderQueue;

            Assert.Greater(queue, 2500);
            Assert.Less(queue, 3000);
        }

        [Test]
        public void Water_IsDrawn_AndMovesAsTimePasses()
        {
            Color32[] early = Render(CustomWater(phase: 0f), sun: true), later = Render(CustomWater(phase: 2f), sun: true);

            Assert.IsFalse(early.Any(pixel => pixel.r > 200 && pixel.g < 60 && pixel.b > 200), "no magenta: the shader did not fall back to Unity's error shader");
            int changed = Enumerable.Range(0, early.Length).Count(i => Mathf.Abs(early[i].g - later[i].g) > 3);
            Assert.Greater(changed, early.Length / 20, "the ripples are somewhere else two seconds later");
        }

        // The first version was a dark surface that counted on reflections to be seen.
        // In the game, at night, on black terrain, there was nothing to reflect and the
        // water could not be seen at all. The water is dark again now, on purpose, but
        // with nothing to mirror it still stands out clearly from ground as black as
        // the terrain.
        [Test]
        public void Water_CanBeSeenWithNothingToMirror()
        {
            var ground = Keep(new Material(Shader.Find("Universal Render Pipeline/Lit")));
            ground.SetColor("_BaseColor", Color.black);
            ground.SetFloat("_Smoothness", 0.03f);

            float water = Brightness(Render(CustomWater(), sun: true)), land = Brightness(Render(ground, sun: true));

            Assert.Greater(water, land + 25f, $"water {water}, black ground {land}");
        }

        // The game may light a scene with little more than ambient light. The ripples
        // must still show, or the custom water is just the normal water again.
        [Test]
        public void Water_ShowsItsRipples_EvenWithNoDirectLight()
        {
            Color32[] water = WaterPixels(Render(CustomWater(), sun: false));

            float mean = (float)water.Average(pixel => pixel.g);
            float spread = Mathf.Sqrt((float)water.Average(pixel => (pixel.g - mean) * (pixel.g - mean)));
            Assert.Greater(mean, 20f, "visible under ambient light alone");
            Assert.Greater(spread, mean * 0.05f, "with lighter and darker ripples across it");
        }

        // The third version glittered with light of its own, so that it would not look
        // like the normal water in the game's dim light. In the game the glints were
        // hard, bright blobs all over the water, and very distracting. Left to itself
        // the water is now dark and calm.
        [Test]
        public void Water_HasNoBrightPatchesOfItsOwn()
        {
            SetAmbientLight(Color.black);

            float[] brightness = WaterPixels(Render(CustomWater(), sun: false)).Select(pixel => (pixel.r + pixel.g + pixel.b) / 3f).ToArray();

            Assert.Less(brightness.Max(), 90f, "nothing glows in a scene with no light and nothing to mirror");
        }

        // The point of the custom water: what stands at the water's edge shows in it.
        // A bright red block stands in the water beyond the middle of the picture, and
        // its mirror image has to lie on the water in front of it, and nowhere else.
        [Test]
        public void Water_MirrorsWhatStandsBehindIt()
        {
            SetAmbientLight(Color.black);
            Color32[] empty = Render(CustomWater(), sun: false);

            Block(Color.red);
            Color32[] mirroring = Render(CustomWater(), sun: false);

            Assert.Less(MeanRed(empty, InFrontOfTheBlock), 40f, "nothing red on the water to begin with");
            Assert.Greater(MeanRed(mirroring, InFrontOfTheBlock), 100f, "the block, upside down on the water in front of it");
            Assert.Less(MeanRed(mirroring, OffToOneSide), 40f, "and none of it where there is no block to mirror");
        }

        // In the game only the brightest things showed in the water: the mirror image
        // was added to the water's own colour, and adding something dark changes
        // nothing. A mirror shows a dark thing as dark. So the image takes the place
        // of the water's colour, and a black block shows as a dark shape on lit water.
        // Taking nearly all of its place was too much, though: seen in the game, about
        // two thirds of that is right, so some of the water's colour shows through.
        [Test]
        public void Water_MirrorsDarkThingsToo_ButKeepsSomeOfItsOwnColour()
        {
            Block(Color.black);

            Color32[] water = Render(CustomWater(), sun: false);

            // Measured as light, not as the numbers in the picture, which are not in proportion to it.
            Assert.Greater(MeanBrightness(water, OffToOneSide), 30f, "the water itself is lit");
            float lit = MeanLight(water, OffToOneSide), mirroring = MeanLight(water, InFrontOfTheBlock);
            Assert.Less(mirroring, lit * 0.5f, $"the block shows, dark: {mirroring} against {lit}");
            Assert.Greater(mirroring, lit * 0.25f, $"but not as in a perfect mirror: {mirroring} against {lit}");
        }

        // Looking out across the water, most of what a mirror would show is sky. The
        // sky is in the camera's picture too, so the water takes its colour from there:
        // red water under a red sky, and none of it under a black one.
        [Test]
        public void Water_MirrorsTheSkyTowardsTheHorizon()
        {
            SetAmbientLight(Color.black);

            Color32[] underBlack = Render(CustomWater(), sun: false, view: View.Level);
            Color32[] underRed = Render(CustomWater(), sun: false, sky: Color.red, view: View.Level);

            Assert.Less(MeanRed(underBlack, JustBelowTheHorizon), 40f);
            Assert.Greater(MeanRed(underRed, JustBelowTheHorizon), 120f);
        }

        // Seen in the game from high above, the water looked like a canyon: its bed and
        // the cliffs down to it showed through. Mirroring the sky had gone wrong. A
        // mirrored line of sight that meets nothing was given the colour the picture
        // shows where the line ends, on the reasoning that this is the sky beyond it.
        // Seen from above, the line comes straight back up at the camera, and what the
        // picture shows where it ends is the ground under the water.
        [Test]
        public void Water_SeenFromHighAbove_DoesNotShowWhatLiesUnderIt()
        {
            SetAmbientLight(Color.black);
            GameObject bed = Keep(GameObject.CreatePrimitive(PrimitiveType.Plane));
            bed.transform.position = new Vector3(0f, -2f, 0f);
            bed.transform.localScale = new Vector3(40f, 1f, 40f);
            var red = Keep(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            red.SetColor("_BaseColor", Color.red);
            bed.GetComponent<MeshRenderer>().sharedMaterial = red;

            Color32[] water = Render(CustomWater(), sun: false, view: View.HighAbove);

            // From 150 units up it shows in the middle third of the picture. Higher up, in more of it.
            Assert.Less(MeanRed(water, new RectInt(52, 52, 24, 24)), 40f, "none of the red bed under it");
        }

        // The mirror image is looked up in two pictures the camera keeps of the scene.
        // A game need not keep them. With the picture of the colours missing the water
        // must simply mirror nothing: Unity hands a shader plain grey in its place, and
        // mirroring that would paint a glowing grey ghost of every building.
        [Test]
        public void Water_MirrorsNothing_WhenTheCameraKeepsNoPictureOfTheScene()
        {
            SetAmbientLight(Color.black);
            Block(Color.red);
            Shader.SetGlobalTexture("_CameraOpaqueTexture", Texture2D.grayTexture);

            Color32[] water = Render(CustomWater(), sun: false, sceneColour: false);

            Assert.Less(Mathf.Abs(MeanBrightness(water, InFrontOfTheBlock) - MeanBrightness(water, OffToOneSide)), 10f);
            Assert.Less(MeanBrightness(water, InFrontOfTheBlock), 60f);
        }

        // Seen in the game next to Night City's water, ours read as turquoise and light
        // where that one is navy. So in plain grey light it has to come out blue, not
        // blue-green. The first navy was then too dark, and the answer was to split the
        // difference: as light to the eye as halfway between that navy and the teal
        // before it.
        [Test]
        public void Water_IsNavy_NotTurquoise_AndHalfwayBetweenTheTealThatWasTooLightAndTheNavyThatWasTooDark()
        {
            Material tooLight = CustomWater(), tooDark = CustomWater();
            tooLight.SetColor("_BaseColor", new Color(0.195f, 0.5f, 0.47f));
            tooLight.SetColor("_SheenColor", new Color(0.07f, 0.19f, 0.2f));
            tooDark.SetColor("_BaseColor", new Color(0.11f, 0.19f, 0.32f));
            tooDark.SetColor("_SheenColor", new Color(0.05f, 0.09f, 0.15f));

            Color32[] picture = Render(CustomWater(), sun: true);
            Color32[] ours = WaterPixels(picture);
            float red = (float)ours.Average(pixel => (float)pixel.r), green = (float)ours.Average(pixel => (float)pixel.g), blue = (float)ours.Average(pixel => (float)pixel.b);
            float light = Lightness(Render(tooLight, sun: true)), dark = Lightness(Render(tooDark, sun: true));

            Assert.Greater(blue, green * 1.25f, $"red {red}, green {green}, blue {blue}");
            Assert.Greater(green, red, $"red {red}, green {green}, blue {blue}");
            Assert.That(Mathf.InverseLerp(dark, light, Lightness(picture)), Is.InRange(0.4f, 0.6f),
                $"ours {Lightness(picture)}, too dark {dark}, too light {light}");
        }

        // Where nothing on the screen is there to mirror, the water mirrors the scene's
        // surroundings: its sky, or whatever a reflection probe has captured. Here those
        // are plain bright red, and the water has to show them strongly: a real water
        // surface reflects only a few percent, which is lost in the game's fog.
        [Test]
        public void Water_MirrorsTheSurroundings_WhereNothingOnScreenIsThereToMirror()
        {
            SetAmbientLight(Color.black);
            float without = WaterPixels(Render(CustomWater(), sun: false)).Average(pixel => (float)pixel.r);

            var surroundings = Keep(new Cubemap(16, TextureFormat.RGBA32, false));
            foreach (CubemapFace face in new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ })
                surroundings.SetPixels(Enumerable.Repeat(Color.red, 16 * 16).ToArray(), face);
            surroundings.Apply();
            var probe = Keep(new GameObject("Probe")).AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Custom;
            probe.customBakedTexture = surroundings;
            probe.size = new Vector3(1000f, 1000f, 1000f);
            RenderSettings.reflectionIntensity = 1f;

            float with = WaterPixels(Render(CustomWater(), sun: false)).Average(pixel => (float)pixel.r);

            Assert.Greater(with, without + 100f, "red all over the water, where there was next to none");
        }

        /// <summary>The same light arriving from every direction. The probe is what the renderer reads.</summary>
        static void SetAmbientLight(Color colour)
        {
            var probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(colour);
            RenderSettings.ambientProbe = probe;
        }

        /// <summary>The custom water's material as the tool makes it: the shader's own defaults and a ripple texture.</summary>
        Material CustomWater(float phase = 0f)
        {
            var ripples = Keep(new Texture2D(64, 64, TextureFormat.RGBA32, true, true));
            ripples.SetPixels32(Ripples.NormalMap(64));
            ripples.Apply();
            Material material = Keep(new Material(Shader.Find(ShaderName)));
            material.SetTexture("_Ripples", ripples);
            material.SetFloat("_Phase", phase);
            return material;
        }

        /// <summary>A block of one plain colour, twice as tall as it is wide, standing in the water beyond the middle of the picture.</summary>
        void Block(Color colour)
        {
            GameObject block = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));
            block.transform.position = new Vector3(0f, 1f, 3f);
            block.transform.localScale = new Vector3(1f, 2f, 0.5f);
            var plain = Keep(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            plain.SetColor("_BaseColor", colour);
            block.GetComponent<MeshRenderer>().sharedMaterial = plain;
        }

        // Parts of the picture, as columns and rows counted from its bottom left corner.
        // The block's foot is at row 87 in the middle 18 columns. Its mirror image
        // reaches from there down to row 55.
        static readonly RectInt InFrontOfTheBlock = new RectInt(60, 62, 8, 18);
        static readonly RectInt OffToOneSide = new RectInt(12, 62, 20, 18);

        // With the camera looking level the horizon is row 64. These rows are water
        // 10 to 40 units away, seen at a shallow angle.
        static readonly RectInt JustBelowTheHorizon = new RectInt(40, 48, 48, 12);

        static IEnumerable<Color32> Part(Color32[] picture, RectInt part) =>
            Enumerable.Range(0, picture.Length).Where(i => part.Contains(new Vector2Int(i % Size, i / Size))).Select(i => picture[i]);

        static float MeanRed(Color32[] picture, RectInt part) => (float)Part(picture, part).Average(pixel => (float)pixel.r);

        static float MeanBrightness(Color32[] picture, RectInt part) => (float)Part(picture, part).Average(pixel => (pixel.r + pixel.g + pixel.b) / 3f);

        /// <summary>How much light a part of the picture gives off, from 0 to 1. A picture's numbers are not in proportion to light: half the number is about a fifth of the light.</summary>
        static float MeanLight(Color32[] picture, RectInt part) => (float)Part(picture, part).Average(pixel =>
            (Mathf.GammaToLinearSpace(pixel.r / 255f) + Mathf.GammaToLinearSpace(pixel.g / 255f) + Mathf.GammaToLinearSpace(pixel.b / 255f)) / 3f);

        /// <summary>
        /// A picture of a stretch of water with the given material, seen from just above and to one side:
        /// close enough that ripples a fraction of a unit long are several pixels each. The camera keeps
        /// a picture of the scene's depths for the water to look things up in and, unless told not to,
        /// one of its colours, under a black sky or one of the given colour.
        /// </summary>
        Color32[] Render(Material material, bool sun, bool sceneColour = true, Color? sky = null, View view = View.Near)
        {
            GameObject water = Keep(GameObject.CreatePrimitive(PrimitiveType.Plane));
            water.transform.localScale = new Vector3(40f, 1f, 40f);
            water.GetComponent<MeshRenderer>().sharedMaterial = material;
            GameObject sunObject = null;
            if (sun)
            {
                sunObject = Keep(new GameObject("Sun"));
                var light = sunObject.AddComponent<Light>();
                light.type = LightType.Directional;
                sunObject.transform.rotation = Quaternion.Euler(50f, 150f, 0f);
            }
            var camera = Keep(new GameObject("Camera")).AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 1.5f, -3f);
            if (view == View.HighAbove)
            {
                camera.transform.position = new Vector3(0f, 150f, 0f);
                camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
            else
            {
                camera.transform.LookAt(view == View.Level ? new Vector3(0f, 1.5f, 0f) : Vector3.zero);
            }
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = sky ?? Color.black;
            UniversalAdditionalCameraData pictures = camera.GetUniversalAdditionalCameraData();
            pictures.requiresDepthOption = CameraOverrideOption.On;
            pictures.requiresColorOption = sceneColour ? CameraOverrideOption.On : CameraOverrideOption.Off;
            var target = Keep(new RenderTexture(Size, Size, 24));
            var picture = Keep(new Texture2D(Size, Size, TextureFormat.RGBA32, false));
            // The scene the tests run in may have a light of its own. Only ours should count.
            Light[] others = Object.FindObjectsOfType<Light>().Where(light => light.enabled && light.gameObject != sunObject).ToArray();
            foreach (Light light in others) light.enabled = false;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                picture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                return picture.GetPixels32();
            }
            finally
            {
                RenderTexture.active = null;
                camera.targetTexture = null;
                foreach (Light light in others) light.enabled = true;
                // Only one sun and one sheet of water at a time.
                Object.DestroyImmediate(water);
                if (sunObject != null) Object.DestroyImmediate(sunObject);
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        /// <summary>
        /// Where the camera is: just above the water, looking down at the water in front
        /// of it; in the same place, looking level, out to the horizon; or 150 units up,
        /// looking straight down.
        /// </summary>
        enum View
        {
            Near,
            Level,
            HighAbove,
        }

        /// <summary>The lower half of a picture, which the water fills completely.</summary>
        static Color32[] WaterPixels(Color32[] picture) => picture.Take(Size * Size / 2).ToArray();

        static float Brightness(Color32[] picture) => (float)WaterPixels(picture).Average(pixel => (pixel.r + pixel.g + pixel.b) / 3f);

        /// <summary>
        /// How light the water looks, on a scale that is even to the eye. Green light
        /// counts for most and blue for least, and the eye judges by the cube root of
        /// the light: eight times the light looks twice as light.
        /// </summary>
        static float Lightness(Color32[] picture) => (float)WaterPixels(picture).Average(pixel => Mathf.Pow(
            0.2126f * Mathf.GammaToLinearSpace(pixel.r / 255f) + 0.7152f * Mathf.GammaToLinearSpace(pixel.g / 255f) + 0.0722f * Mathf.GammaToLinearSpace(pixel.b / 255f), 1f / 3f));

        T Keep<T>(T thing) where T : Object
        {
            made.Add(thing);
            return thing;
        }
    }
}
