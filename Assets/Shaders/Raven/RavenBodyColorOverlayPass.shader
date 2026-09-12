// 文件说明：在 Raven 官方 URP Lit 身体之后绘制纯色透明覆盖，不依赖 URP Lit 的内部实现文件。
// 所属模块：Boss 动画表现。
// 运行影响：只在颜色覆盖存在时由专用 SkinnedMeshRenderer 绘制；不写深度、不投射阴影。

Shader "Project EVE/Raven Body Color Overlay Pass"
{
    Properties
    {
        [PerRendererData][HDR] _OverlayColor("Overlay Color", Color) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
        }

        Pass
        {
            Name "BodyColorOverlay"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Equal
            // Hair02/Hair01 原材质为双面；深度相等测试会阻止覆盖层染到主体未绘制的表面。
            Cull Off
            ColorMask RGB

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex OverlayVertex
            #pragma fragment OverlayFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _OverlayColor;
            CBUFFER_END

            Varyings OverlayVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 OverlayFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                clip(_OverlayColor.a - 0.0001h);
                return half4(_OverlayColor.rgb, saturate(_OverlayColor.a));
            }
            ENDHLSL
        }
    }

    FallBack Off
}
