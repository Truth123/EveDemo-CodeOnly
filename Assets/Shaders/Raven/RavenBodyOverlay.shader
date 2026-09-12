Shader "Project EVE/Raven Body Overlay"
{
    Properties
    {
        _WorkflowMode("Workflow Mode", Float) = 1
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        _Smoothness("Smoothness", Range(0, 1)) = 0.5
        _SmoothnessTextureChannel("Smoothness texture channel", Float) = 0
        _Metallic("Metallic", Range(0, 1)) = 0
        _MetallicGlossMap("Metallic Map", 2D) = "white" {}
        _SpecColor("Specular Color", Color) = (0.2, 0.2, 0.2, 1)
        _SpecGlossMap("Specular Map", 2D) = "white" {}
        [ToggleOff] _SpecularHighlights("Specular Highlights", Float) = 1
        [ToggleOff] _EnvironmentReflections("Environment Reflections", Float) = 1
        _BumpScale("Normal Scale", Float) = 1
        _BumpMap("Normal Map", 2D) = "bump" {}
        _Parallax("Height Scale", Range(0.005, 0.08)) = 0.005
        _ParallaxMap("Height Map", 2D) = "black" {}
        _OcclusionStrength("Occlusion Strength", Range(0, 1)) = 1
        _OcclusionMap("Occlusion Map", 2D) = "white" {}
        _ClearCoatMask("Clear Coat Mask", Range(0, 1)) = 0
        _ClearCoatSmoothness("Clear Coat Smoothness", Range(0, 1)) = 0
        [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 0)
        _EmissionMap("Emission Map", 2D) = "white" {}
        _DetailMask("Detail Mask", 2D) = "white" {}
        _DetailAlbedoMapScale("Detail Albedo Map Scale", Range(0, 2)) = 1
        _DetailAlbedoMap("Detail Albedo Map", 2D) = "linearGrey" {}
        _DetailNormalMapScale("Detail Normal Map Scale", Range(0, 2)) = 1
        [Normal] _DetailNormalMap("Detail Normal Map", 2D) = "bump" {}

        [PerRendererData][HDR] _BodyColorOverlay("Body Color Overlay", Color) = (0, 0, 0, 0)
        [PerRendererData] _BodyVisibility("Body Visibility", Range(0, 1)) = 1

        [ToggleOff] _ReceiveShadows("Receive Shadows", Float) = 1
        [ToggleOff] _ScreenSpaceReflections("Screen Space Reflections", Float) = 1
        [ToggleOff] _ScreenSpaceReflectionsContributeTransparent("Screen Space Reflections Contribute Transparent", Float) = 1
        [HideInInspector] _Surface("__surface", Float) = 0
        [HideInInspector] _Blend("__blend", Float) = 0
        [HideInInspector] _Cull("__cull", Float) = 2
        [ToggleUI] [HideInInspector] _AlphaClip("__clip", Float) = 0
        [HideInInspector] _SrcBlend("__src", Float) = 1
        [HideInInspector] _DstBlend("__dst", Float) = 0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0
        [HideInInspector] _ZWrite("__zw", Float) = 1
        [HideInInspector] _BlendModePreserveSpecular("_BlendModePreserveSpecular", Float) = 1
        [HideInInspector] _AlphaToMask("__alphaToMask", Float) = 0
        [HideInInspector] _AddPrecomputedVelocity("_AddPrecomputedVelocity", Float) = 0
        [HideInInspector] _XRMotionVectorsPass("_XRMotionVectorsPass", Float) = 1
        [HideInInspector] _QueueOffset("Queue offset", Float) = 0
        [HideInInspector] _QueueControl("Queue control", Float) = -1

        [HideInInspector] _MainTex("BaseMap", 2D) = "white" {}
        [HideInInspector] _Color("Base Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _GlossMapScale("Smoothness", Float) = 0
        [HideInInspector] _Glossiness("Smoothness", Float) = 0
        [HideInInspector] _GlossyReflections("Environment Reflections", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "Queue" = "Geometry"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Blend [_SrcBlend] [_DstBlend], [_SrcBlendAlpha] [_DstBlendAlpha]
            ZWrite [_ZWrite]
            Cull [_Cull]
            AlphaToMask [_AlphaToMask]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex LitPassVertex
            #pragma fragment RavenBodyLitPassFragment

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _PARALLAXMAP
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
            #pragma shader_feature_local _ _DETAIL_MULX2 _DETAIL_SCALED
            #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _ _ALPHAPREMULTIPLY_ON _ALPHAMODULATE_ON
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local_fragment _OCCLUSIONMAP
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
            #pragma shader_feature_local_fragment _SPECULAR_SETUP

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #define LitPassFragment RavenOriginalLitPassFragment
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
            #undef LitPassFragment

            half4 _BodyColorOverlay;
            half _BodyVisibility;

            half RavenVisibilityDitherThreshold(float2 positionCS)
            {
                float2 pixel = fmod(floor(positionCS), 4.0);
                half4 row;
                if (pixel.y < 1.0)
                {
                    row = half4(0.5, 8.5, 2.5, 10.5);
                }
                else if (pixel.y < 2.0)
                {
                    row = half4(12.5, 4.5, 14.5, 6.5);
                }
                else if (pixel.y < 3.0)
                {
                    row = half4(3.5, 11.5, 1.5, 9.5);
                }
                else
                {
                    row = half4(15.5, 7.5, 13.5, 5.5);
                }

                half threshold = pixel.x < 1.0
                    ? row.x
                    : pixel.x < 2.0
                        ? row.y
                        : pixel.x < 3.0
                            ? row.z
                            : row.w;
                return threshold * (1.0h / 16.0h);
            }

            void RavenBodyLitPassFragment(
                Varyings input,
                out half4 outColor : SV_Target0
                #ifdef _WRITE_RENDERING_LAYERS
                    , out uint outRenderingLayers : SV_Target1
                #endif
            )
            {
                // Screen-door fade keeps the body in the opaque/cutout depth path.
                // Visibility=1 retains every pixel; lower values keep a stable subset
                // of a 4x4 Bayer pattern instead of alpha-blending layered hair cards.
                clip(saturate(_BodyVisibility) - RavenVisibilityDitherThreshold(input.positionCS.xy));

                RavenOriginalLitPassFragment(
                    input,
                    outColor
                    #ifdef _WRITE_RENDERING_LAYERS
                        , outRenderingLayers
                    #endif
                );

                outColor.rgb = lerp(
                    outColor.rgb,
                    _BodyColorOverlay.rgb,
                    saturate(_BodyColorOverlay.a));
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
        UsePass "Universal Render Pipeline/Lit/Meta"
        UsePass "Universal Render Pipeline/Lit/Universal2D"
        UsePass "Universal Render Pipeline/Lit/MotionVectors"

    }

    // The wrapped URP internal Forward pass is rejected by Unity's D3D12 shader
    // importer. Preserve the original material textures and lighting instead of
    // rendering the magenta error shader on that graphics API.
    FallBack "Universal Render Pipeline/Lit"
    CustomEditor "UnityEditor.Rendering.Universal.ShaderGUI.LitShader"
}
