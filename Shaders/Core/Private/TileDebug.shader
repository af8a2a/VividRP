Shader "Hidden/VividRP/TileDebug"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "CopySource"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex CopyVert
            #pragma fragment CopyFrag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"

            TEXTURE2D(_SourceTexture);
            SAMPLER(sampler_SourceTexture);

            float4 _SourceTextureScaleBias;

            struct CopyAttributes
            {
                uint vertexID : SV_VertexID;
            };

            struct CopyVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float2 ApplyScaleBias(float2 uv, float4 scaleBias)
            {
                return uv * scaleBias.xy + scaleBias.zw;
            }

            CopyVaryings CopyVert(CopyAttributes input)
            {
                CopyVaryings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float4 CopyFrag(CopyVaryings input) : SV_Target
            {
                float2 sourceUv = ApplyScaleBias(input.uv, _SourceTextureScaleBias);
                return SAMPLE_TEXTURE2D(_SourceTexture, sampler_SourceTexture, sourceUv);
            }
            ENDHLSL
        }

        Pass
        {
            Name "TileOverlay"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex OverlayVert
            #pragma geometry OverlayGeom
            #pragma fragment OverlayFrag

            #define CLASSIFY_TILE_SIZE 8
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/TileClassification.hlsl"

            StructuredBuffer<uint> _TileIndices;

            float4 _TileDebugScreenSize;
            float4 _TileDebugColor;

            struct OverlayAttributes
            {
                uint instanceID : SV_InstanceID;
            };

            struct OverlayPoint
            {
                uint packedTileCoord : TEXCOORD0;
            };

            struct OverlayVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 localUV : TEXCOORD0;
                float2 tilePixelSize : TEXCOORD1;
            };

            OverlayPoint OverlayVert(OverlayAttributes input)
            {
                OverlayPoint output;
                output.packedTileCoord = _TileIndices[input.instanceID];
                return output;
            }

            void AppendOverlayVertex(
                inout TriangleStream<OverlayVaryings> stream,
                float2 positionNdc,
                float2 localUv,
                float2 tilePixelSize)
            {
                OverlayVaryings output;
                output.positionCS = float4(positionNdc, 0.0, 1.0);
                output.localUV = localUv;
                output.tilePixelSize = tilePixelSize;
                stream.Append(output);
            }

            [maxvertexcount(4)]
            void OverlayGeom(point OverlayPoint input[1], inout TriangleStream<OverlayVaryings> stream)
            {
                uint2 tileCoord = UnpackTileCoord(input[0].packedTileCoord);
                float2 screenSize = max(_TileDebugScreenSize.xy, float2(1.0, 1.0));
                float2 tileMinPixel = float2(tileCoord) * CLASSIFY_TILE_SIZE;
                float2 tileMaxPixel = min(tileMinPixel + CLASSIFY_TILE_SIZE, screenSize);
                float2 tilePixelSize = max(tileMaxPixel - tileMinPixel, float2(1.0, 1.0));

                float left = tileMinPixel.x * _TileDebugScreenSize.z * 2.0 - 1.0;
                float right = tileMaxPixel.x * _TileDebugScreenSize.z * 2.0 - 1.0;
                float top = 1.0 - tileMinPixel.y * _TileDebugScreenSize.w * 2.0;
                float bottom = 1.0 - tileMaxPixel.y * _TileDebugScreenSize.w * 2.0;

                AppendOverlayVertex(stream, float2(left, bottom), float2(0.0, 0.0), tilePixelSize);
                AppendOverlayVertex(stream, float2(right, bottom), float2(1.0, 0.0), tilePixelSize);
                AppendOverlayVertex(stream, float2(left, top), float2(0.0, 1.0), tilePixelSize);
                AppendOverlayVertex(stream, float2(right, top), float2(1.0, 1.0), tilePixelSize);
            }

            float4 OverlayFrag(OverlayVaryings input) : SV_Target
            {
                float borderWidth = 1.5 / max(min(input.tilePixelSize.x, input.tilePixelSize.y), 1.0);
                float edgeDistance = min(
                    min(input.localUV.x, input.localUV.y),
                    min(1.0 - input.localUV.x, 1.0 - input.localUV.y));
                float border = 1.0 - smoothstep(borderWidth, borderWidth * 2.0, edgeDistance);
                float alpha = saturate(_TileDebugColor.a * (0.35 + border * 0.65));
                return float4(_TileDebugColor.rgb, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
