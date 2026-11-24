// The custom water of a Dystopographica landscape: a dark, smooth surface whose
// ripples slide across it, and which mirrors what stands around it.
//
// A mod cannot carry scripts, so everything here happens in the shader. Two copies
// of one ripple texture drift across each other in different directions; added
// together they never repeat visibly and never look like a sheet being dragged.
// The lighting is Unity's own (URP's physically based lighting), with the same
// keywords as Unity's Lit shader, so the water is lit, shadowed and fogged like
// everything else in the game.
//
// The mirror image is found on the screen. By the time the water is drawn the
// camera has a picture of every solid thing in view and of how far away each is.
// For each point of the water the shader follows the mirrored line of sight across
// those pictures until it passes behind something, and shows the colour found
// there in place of its own. So the water mirrors what is in view, dark things as
// well as bright ones and the sky behind them, exactly where a mirror would show
// them, and cannot mirror what is off the screen or hidden. This needs the game to
// keep those two pictures; where it does not, the water mirrors only the scene's
// surroundings.
Shader "Dystopographica/Water"
{
    Properties
    {
        // Navy. The normal water's teal, and then that teal 15% darker, both read as
        // turquoise and light in the game beside Night City's water. The first navy,
        // (0.11, 0.19, 0.32), was too dark. This is the same blue, as light to the eye
        // as halfway between that navy and the darker teal.
        _BaseColor ("Colour", Color) = (0.195, 0.315, 0.51, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.85
        [NoScaleOffset] [Normal] _Ripples ("Ripples", 2D) = "bump" {}
        _RippleSize ("Ripple tile size (units)", Float) = 2
        _RippleStrength ("Ripple strength", Range(0, 2)) = 0.6
        _RippleContrast ("Ripple contrast", Range(0, 2)) = 0.8
        [HDR] _SheenColor ("Sheen colour", Color) = (0.08, 0.13, 0.21, 1)
        _Mirror ("Mirror strength", Range(0, 1)) = 0.6
        _MirrorWobble ("Mirror wobble", Range(0, 1)) = 0.06
        _RippleSpeed ("Ripple speed (units per second)", Float) = 0.25
        _Phase ("Clock offset (seconds)", Float) = 0
    }

    SubShader
    {
        // Nothing shows through the water, but it is drawn in the queue of the
        // see-through things. The camera's pictures of the scene are taken when the
        // solid things are done, so only what is drawn later can look at them. It goes
        // early in that queue, so lights and haze over the water are drawn on top.
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" "RenderPipeline" = "UniversalPipeline" }

        // The pass that draws the water. "UniversalForwardOnly" is picked up by both
        // of URP's renderers, forward and deferred.
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForwardOnly" }

            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Smoothness;
                float _RippleSize;
                half _RippleStrength;
                half _RippleContrast;
                half4 _SheenColor;
                half _Mirror;
                half _MirrorWobble;
                float _RippleSpeed;
                float _Phase;
            CBUFFER_END

            TEXTURE2D(_Ripples);
            SAMPLER(sampler_Ripples);

            // Unity fills these in with the sizes of the camera's two pictures of the scene.
            float4 _CameraOpaqueTexture_TexelSize;
            float4 _CameraDepthTexture_TexelSize;

            // How the mirrored line of sight is followed: in strides that start short and
            // grow, so the first units are searched finely and the search still reaches
            // about a hundred units. Then the place where it passed behind something is
            // homed in on by halving.
            #define MIRROR_STRIDES 28
            #define MIRROR_FIRST_STRIDE 0.08
            #define MIRROR_STRIDE_GROWTH 1.22
            #define MIRROR_HOMING_STEPS 6

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD2;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                output.shadowCoord = GetShadowCoord(position);
                #endif
                return output;
            }

            // One copy of the ripple texture, turned by an angle (given as its cosine and
            // sine), scaled, and slid along. Returns how far the surface leans along the
            // world's x and z there, and how level it is. Each copy is turned by a
            // different odd angle, so the copies never line up into a repeating grid
            // however small the texture's tile is.
            half3 Ripple(float2 uv, half2 turn, float scale, float2 slide, half strength)
            {
                float2 turned = float2(turn.x * uv.x - turn.y * uv.y, turn.y * uv.x + turn.x * uv.y) * scale + slide;
                half3 lean = UnpackNormalScale(SAMPLE_TEXTURE2D(_Ripples, sampler_Ripples, turned), strength);
                return half3(turn.x * lean.x + turn.y * lean.y, turn.x * lean.y - turn.y * lean.x, lean.z);
            }

            // How far a point in the world lies behind whatever solid thing the camera
            // drew at the same place on the screen, in units: negative when it is in
            // front. Also gives that place, from 0 to 1 across and up the screen, or
            // somewhere outside that when the point is behind the camera.
            float BehindTheScene(float3 positionWS, out float2 uv)
            {
                float4 positionCS = TransformWorldToHClip(positionWS);
                if (positionCS.w <= 0.0)
                {
                    uv = float2(-1.0, -1.0);
                    return -1.0;
                }
                uv = float2(positionCS.x, positionCS.y * _ProjectionParams.x) / positionCS.w * 0.5 + 0.5;
                float drawn = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, UnityStereoTransformScreenSpaceTex(uv), 0).r;
                return positionCS.w - LinearEyeDepth(drawn, _ZBufferParams);
            }

            // The mirror image at one point of the water: the colour of what a line from
            // there in the given direction runs into, and how much to trust it, from 0
            // (it left the view, so there is no telling) to 1.
            half4 MirrorImage(float3 from, float3 direction)
            {
                // A camera that keeps no picture of the scene leaves a small blank
                // texture in its place. There is nothing to find in that.
                if (min(_CameraOpaqueTexture_TexelSize.z, _CameraDepthTexture_TexelSize.z) < 16.0) return half4(0, 0, 0, 0);

                float2 uv = float2(0, 0);
                float before = 0.0, at = 0.0, stride = MIRROR_FIRST_STRIDE;
                bool found = false, inView = true;
                for (int i = 0; i < MIRROR_STRIDES; i++)
                {
                    at = before + stride;
                    float behind = BehindTheScene(from + direction * at, uv);
                    if (any(uv < 0.0) || any(uv > 1.0))
                    {
                        inView = false;
                        break;
                    }
                    if (behind > 0.0)
                    {
                        found = true;
                        break;
                    }
                    before = at;
                    stride *= MIRROR_STRIDE_GROWTH;
                }
                if (!inView) return half4(0, 0, 0, 0);

                if (found)
                {
                    // Somewhere in the last stride the line passed behind something.
                    for (int j = 0; j < MIRROR_HOMING_STEPS; j++)
                    {
                        float middle = (before + at) * 0.5;
                        if (BehindTheScene(from + direction * middle, uv) > 0.0) at = middle;
                        else before = middle;
                    }
                    float depth = BehindTheScene(from + direction * at, uv);

                    // Still well behind it after homing in: the line did not run into
                    // the thing, it passed behind something nearer the camera. That
                    // hides whatever the line really meets, so there is no telling.
                    if (depth > 0.2 + 0.15 * stride) return half4(0, 0, 0, 0);
                }

                // The image fades out towards the edges of the screen, where what it
                // should show next is out of view.
                float2 fromEdge = min(uv, 1.0 - uv);
                half trust = saturate(min(fromEdge.x, fromEdge.y) * 12.0);

                if (!found)
                {
                    // The line ran its whole length and met nothing. What lies on beyond
                    // its end is the sky or something far off, and the picture shows that
                    // at the place where the line ended, but only if the camera is looking
                    // along the line. What the picture shows at any place is what the
                    // camera sees past it, and a line that comes back towards the camera
                    // ends in front of the very ground it started from: seen from high
                    // above, the water showed its own bed.
                    float3 end = from + direction * at;
                    trust *= smoothstep(0.8, 0.95, dot(direction, normalize(end - GetCameraPositionWS())));
                    if (trust <= 0.0) return half4(0, 0, 0, 0);
                }
                half3 colour = SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, UnityStereoTransformScreenSpaceTex(uv), 0).rgb;
                return half4(colour, trust);
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // The ripples are laid out in world units, so they are the same size
                // however large the water is. Two copies drift across each other.
                float2 uv = input.positionWS.xz / _RippleSize;
                float drift = (_Time.y + _Phase) * _RippleSpeed / _RippleSize;
                half3 first = Ripple(uv, half2(1.0, 0.0), 1.0, drift * float2(1.0, 0.35), _RippleStrength);
                half3 second = Ripple(uv, half2(0.799, 0.602), 0.77, drift * float2(0.6, -0.8), _RippleStrength);
                half3 normalWS = normalize(half3(first.x + second.x, first.z * second.z, first.y + second.y));

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                inputData.shadowCoord = input.shadowCoord;
                #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #else
                inputData.shadowCoord = float4(0, 0, 0, 0);
                #endif
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
                inputData.vertexLighting = half3(0, 0, 0);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                // The water has a colour of its own, like the normal water, so it can be
                // seen when there is nothing for it to mirror: the first version was a
                // dark mirror, and at night on black terrain it was invisible. The side
                // of each ripple facing one fixed direction is tinted lighter and the
                // other side darker, so the ripples also show in flat, even light.
                half facing = dot(normalWS.xz, half2(0.6, 0.8));

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = _BaseColor.rgb * max(0.0, 1.0 + _RippleContrast * facing);
                surface.smoothness = _Smoothness;

                // The mirror. It leans with the ripples, but far less than the ripples
                // themselves lean, or the image would be scattered beyond recognition.
                half3 mirrorNormal = normalize(half3(normalWS.x * _MirrorWobble, normalWS.y, normalWS.z * _MirrorWobble));

                // A faint sheen of the water's own grows towards the horizon, the way a
                // mirrored sky does. It follows the calm mirror, not the ripples: on the
                // ripples it lit up every crest that happened to lean away from the eye.
                half sheen = pow(1.0 - saturate(dot(mirrorNormal, inputData.viewDirectionWS)), 4.0);

                // The mirrored line of sight never points down into the water.
                float3 away = reflect(-inputData.viewDirectionWS, mirrorNormal);
                away = normalize(float3(away.x, max(away.y, 0.02), away.z));

                // Where the mirrored line leaves the view there is no image, and the water
                // shows its own colour, its sheen, and the scene's surroundings: its sky,
                // or what a reflection probe holds.
                half3 surroundings = GlossyEnvironmentReflection(away, input.positionWS, 1.0 - _Smoothness, 1.0, inputData.normalizedScreenSpaceUV);
                surface.emission = _SheenColor.rgb * sheen + surroundings * lerp(0.6, 1.0, sheen);
                surface.occlusion = 1.0;
                surface.alpha = 1.0;
                surface.normalTS = half3(0, 0, 1);
                half4 colour = UniversalFragmentPBR(inputData, surface);

                // Where there is an image it takes the place of most of that, as in a
                // mirror: adding it to the water's colour instead showed only the brightest
                // things, since adding something dark changes nothing. A real water surface
                // mirrors only a few percent except near the horizon, and in the game's fog
                // that is lost, so this one mirrors over half from every angle and more
                // towards the horizon. Mirroring nearly everything was too much.
                half4 image = MirrorImage(input.positionWS, away);
                colour.rgb = lerp(colour.rgb, image.rgb, image.a * saturate(_Mirror * lerp(1.0, 1.4, sheen)));

                colour.rgb = MixFog(colour.rgb, inputData.fogCoord);
                return half4(colour.rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
