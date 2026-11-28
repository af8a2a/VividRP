Shader "VividRP/Samples/TMP_SDF_Unlit"
{
    Properties
    {
        [MainTexture] _MainTex("Font Atlas", 2D) = "white" {}
        _FaceColor("Face Color", Color) = (1, 1, 1, 1)
        _OutlineColor("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineWidth("Outline Width", Range(0, 0.4)) = 0.04
        _FaceDilate("Face Dilate", Range(-0.5, 0.5)) = 0.04
        _Softness("Edge Softness", Range(0.0, 0.2)) = 0.02
        _Brightness("Linear Brightness", Range(0.1, 4.0)) = 1.15
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "VividRenderPipeline"
            "Queue" = "AlphaTest"
            "RenderType" = "TransparentCutout"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "VividForward"
            Tags { "LightMode" = "VividForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertTmpSdf
            #pragma fragment FragTmpSdf

            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/AutoExposure.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _FaceColor;
                float4 _OutlineColor;
                float _OutlineWidth;
                float _FaceDilate;
                float _Softness;
                float _Brightness;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv0        : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            Varyings VertTmpSdf(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color * _FaceColor;
                output.uv = input.uv0 * _MainTex_ST.xy + _MainTex_ST.zw;
                return output;
            }

            float4 FragTmpSdf(Varyings input) : SV_Target
            {
                // TMP LiberationSans SDF stores signed distance in Alpha (0.5 = glyph contour).
                float sdf = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                float aa = max(fwidth(sdf) * 0.75f, 0.008f) + _Softness;

                float faceEdge = 0.5f - _FaceDilate * 0.5f;
                float faceMask = smoothstep(faceEdge - aa, faceEdge + aa, sdf);

                float outlineEdge = faceEdge - _OutlineWidth * 0.5f;
                float totalAlpha = smoothstep(outlineEdge - aa, outlineEdge + aa, sdf) * input.color.a;
                clip(totalAlpha - 0.01f);

                float3 col = lerp(_OutlineColor.rgb, input.color.rgb, faceMask) * _Brightness;
                return float4(col * VividGetPreExposure(), totalAlpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
