Shader "RPG/JumpText/SDF Instanced"
{
    Properties
    {
        [NoScaleOffset] _JumpTextAtlas ("跳字 SDF Atlas", 2D) = "white" {}
        [HideInInspector] _JumpTextScreenRect ("跳字屏幕像素矩形", Vector) = (0, 0, 1, 1)
        [HideInInspector] _JumpTextUVRect ("跳字 Atlas UV 矩形", Vector) = (0, 0, 1, 1)
        [HideInInspector] _JumpTextColor ("跳字文字颜色", Color) = (1, 1, 1, 1)
        [HideInInspector] _JumpTextOutline ("跳字描边参数", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+100"
        }

        Pass
        {
            Name "JumpText"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_JumpTextAtlas);
            SAMPLER(sampler_JumpTextAtlas);

            UNITY_INSTANCING_BUFFER_START(JumpTextPerInstance)
                UNITY_DEFINE_INSTANCED_PROP(float4, _JumpTextScreenRect)
                UNITY_DEFINE_INSTANCED_PROP(float4, _JumpTextUVRect)
                UNITY_DEFINE_INSTANCED_PROP(float4, _JumpTextColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _JumpTextOutline)
            UNITY_INSTANCING_BUFFER_END(JumpTextPerInstance)

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : TEXCOORD1;
                float4 outline : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);

                Varyings output;
                float3 worldAnchor = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                float4 positionCS = TransformWorldToHClip(worldAnchor);
                float4 screenRect = UNITY_ACCESS_INSTANCED_PROP(
                    JumpTextPerInstance, _JumpTextScreenRect);
                float2 pixelOffset = screenRect.xy + input.positionOS.xy * screenRect.zw;
                float2 safeScreenSize = max(_ScreenParams.xy, float2(1.0, 1.0));

    // 屏幕像素偏移需要乘裁剪空间 w，才能在透视相机下保持固定像素大小。
    // URP 投影翻转时 _ProjectionParams.x 为 -1；屏幕 Y 需要乘该符号才能保持视觉方向一致。
    float2 clipPixelOffset = pixelOffset;
    clipPixelOffset.y *= _ProjectionParams.x;
    positionCS.xy += clipPixelOffset * (2.0 / safeScreenSize) * positionCS.w;
                output.positionCS = positionCS;

                float4 uvRect = UNITY_ACCESS_INSTANCED_PROP(
                    JumpTextPerInstance, _JumpTextUVRect);
    // 使用 TMP GlyphRect 的标准 UV 方向；投影翻转由裁剪空间 Y 偏移统一处理。
    output.uv = uvRect.xy + input.uv * uvRect.zw;
                output.color = UNITY_ACCESS_INSTANCED_PROP(
                    JumpTextPerInstance, _JumpTextColor);
                output.outline = UNITY_ACCESS_INSTANCED_PROP(
                    JumpTextPerInstance, _JumpTextOutline);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // LiberationSans SDF 使用 Alpha8 Atlas，距离场数据存储在 Alpha 通道。
                float sdf = SAMPLE_TEXTURE2D(_JumpTextAtlas, sampler_JumpTextAtlas, input.uv).a;
                float smoothing = max(fwidth(sdf), 0.0001);
                float fillAlpha = smoothstep(0.5 - smoothing, 0.5 + smoothing, sdf);
                float outlineThreshold = 0.5 - saturate(input.outline.a);
                float outlineAlpha = smoothstep(
                    outlineThreshold - smoothing,
                    outlineThreshold + smoothing,
                    sdf);
                float visibleAlpha = max(fillAlpha, outlineAlpha) * input.color.a;
                float3 visibleColor = lerp(input.outline.rgb, input.color.rgb, fillAlpha);
                return half4(visibleColor, visibleAlpha);
            }
            ENDHLSL
        }
    }
}
