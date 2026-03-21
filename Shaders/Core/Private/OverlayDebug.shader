Shader "Hidden/VividRP/OverlayDebug"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "OverlayDebug"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"

            #define VIVID_OVERLAY_VISUALIZATION_AUTO 0
            #define VIVID_OVERLAY_VISUALIZATION_COLOR 1
            #define VIVID_OVERLAY_VISUALIZATION_DEPTH 2
            #define VIVID_OVERLAY_VISUALIZATION_MOTION_VECTORS 3
            #define VIVID_OVERLAY_DEPTHMODE_RAW 0
            #define VIVID_OVERLAY_DEPTHMODE_LINEAR01 1

            TEXTURE2D(_SourceTexture);
            SAMPLER(sampler_SourceTexture);
            TEXTURE2D(_DebugTexture);
            SAMPLER(sampler_DebugTexture);
            TEXTURE2D_ARRAY(_DebugTextureArray);
            SAMPLER(sampler_DebugTextureArray);

            float4 _SourceTextureScaleBias;
            float4 _DebugTextureScaleBias;
            float4 _OverlayRect;
            float4 _OverlayScreenSize;
            int _DebugTextureAvailable;
            int _DebugTextureIsArray;
            int _DebugSlice;
            int _VisualizationMode;
            int _DepthMode;
            float _DebugExposure;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float2 ApplyScaleBias(float2 uv, float4 scaleBias)
            {
                return uv * scaleBias.xy + scaleBias.zw;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float4 EvaluateDebugColor(float4 sampleColor)
            {
                float exposureMultiplier = exp2(_DebugExposure);

                if (_VisualizationMode == VIVID_OVERLAY_VISUALIZATION_DEPTH)
                {
                    float depthValue = sampleColor.r;
                    if (_DepthMode == VIVID_OVERLAY_DEPTHMODE_LINEAR01)
                        depthValue = Linear01Depth(depthValue, _ZBufferParams);

                    return float4(depthValue.xxx * exposureMultiplier, 1.0);
                }

                if (_VisualizationMode == VIVID_OVERLAY_VISUALIZATION_MOTION_VECTORS)
                {
                    float2 motion = sampleColor.xy;
                    float magnitude = saturate(length(motion) * 8.0);
                    return float4((float3(motion * 0.5 + 0.5, magnitude)) * exposureMultiplier, 1.0);
                }

                return float4(sampleColor.rgb * exposureMultiplier, 1.0);
            }

            float4 SampleDebugTexture(float2 uv)
            {
                float2 debugUv = ApplyScaleBias(uv, _DebugTextureScaleBias);
                float4 sampleColor = _DebugTextureIsArray != 0
                    ? SAMPLE_TEXTURE2D_ARRAY(_DebugTextureArray, sampler_DebugTextureArray, debugUv, (float)_DebugSlice)
                    : SAMPLE_TEXTURE2D(_DebugTexture, sampler_DebugTexture, debugUv);
                return EvaluateDebugColor(sampleColor);
            }

            bool IsInsideOverlay(float2 uv, float2 overlayMin, float2 overlayMax)
            {
                return all(uv >= overlayMin) && all(uv <= overlayMax);
            }

            bool IsOverlayBorder(float2 uv, float2 overlayMin, float2 overlayMax)
            {
                float2 borderThickness = _OverlayScreenSize.zw * 2.0;
                float2 distanceToMin = uv - overlayMin;
                float2 distanceToMax = overlayMax - uv;

                return any(distanceToMin <= borderThickness)
                    || any(distanceToMax <= borderThickness);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 sourceUv = ApplyScaleBias(input.uv, _SourceTextureScaleBias);
                float4 sourceColor = SAMPLE_TEXTURE2D(_SourceTexture, sampler_SourceTexture, sourceUv);

                if (_DebugTextureAvailable == 0)
                    return sourceColor;

                float2 overlayMin = _OverlayRect.xy;
                float2 overlayMax = overlayMin + _OverlayRect.zw;
                if (!IsInsideOverlay(input.uv, overlayMin, overlayMax))
                    return sourceColor;

                if (all(_OverlayRect.zw < 0.999) && IsOverlayBorder(input.uv, overlayMin, overlayMax))
                    return float4(1.0, 1.0, 1.0, 1.0);

                float2 overlayUv = saturate((input.uv - overlayMin) / max(_OverlayRect.zw, float2(1e-5, 1e-5)));
                return SampleDebugTexture(overlayUv);
            }
            ENDHLSL
        }
    }
}
