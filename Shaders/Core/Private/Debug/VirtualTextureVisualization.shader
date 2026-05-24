Shader "Hidden/VividRP/VirtualTextureVisualization"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "VirtualTextureVisualization"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/VirtualTexture/VirtualTexture.hlsl"

            #define VIVID_VT_VISUALIZATION_USE_PASS_SETTINGS 0
            #define VIVID_VT_VISUALIZATION_NONE 1
            #define VIVID_VT_VISUALIZATION_PHYSICAL_CACHE 2
            #define VIVID_VT_VISUALIZATION_PAGE_TABLE_RESIDENCY 3
            #define VIVID_VT_VISUALIZATION_PHYSICAL_CACHE_AND_PAGE_TABLE_RESIDENCY 4

            TEXTURE2D(_SourceTexture);
            SAMPLER(sampler_SourceTexture);

            float4 _SourceTextureScaleBias;
            float4 _VTOverlayRect;
            int _VTVisualizationMode;
            int _VTVisualizationAvailable;
            float _VTOverlayOpacity;

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

            bool IsInsideOverlay(float2 uv, float2 overlayMin, float2 overlayMax)
            {
                return all(uv >= overlayMin) && all(uv <= overlayMax);
            }

            bool IsOverlayBorder(float2 uv, float2 overlayMin, float2 overlayMax)
            {
                float2 borderThickness = max(_VTOverlayRect.zw * 0.01, float2(0.002, 0.002));
                float2 distanceToMin = uv - overlayMin;
                float2 distanceToMax = overlayMax - uv;
                return any(distanceToMin <= borderThickness) || any(distanceToMax <= borderThickness);
            }

            float GridBorderMask(float2 localUv)
            {
                float2 edgeDistance = min(localUv, 1.0 - localUv);
                float border = min(edgeDistance.x, edgeDistance.y);
                return 1.0 - smoothstep(0.0, 0.025, border);
            }

            float2 ResolvePhysicalCacheGridCount()
            {
                float cachePageCount = max((float)VT_CACHE_PAGE_COUNT, 1.0);
                float gridX = ceil(sqrt(cachePageCount));
                float gridY = ceil(cachePageCount / gridX);
                return float2(gridX, gridY);
            }

            float4 EvaluatePhysicalCacheColor(float2 overlayUv)
            {
                float2 gridCount = ResolvePhysicalCacheGridCount();
                float2 safeUv = min(saturate(overlayUv), 0.99999);
                float2 scaledUv = safeUv * gridCount;
                uint2 tileCoord = (uint2)floor(scaledUv);
                uint pageId = tileCoord.y * (uint)gridCount.x + tileCoord.x;

                if (pageId >= (uint)VT_CACHE_PAGE_COUNT)
                    return float4(0.05, 0.05, 0.05, 1.0);

                float2 localUv = frac(scaledUv);
                float4 pageColor = SAMPLE_TEXTURE2D_ARRAY(_VTPhysicalCache, sampler_VTPhysicalCache, localUv, (float)pageId);
                return lerp(pageColor, float4(1.0, 1.0, 1.0, 1.0), GridBorderMask(localUv));
            }

            uint ReadPackedPageTableEntry(uint2 pageCoord, uint mip)
            {
                uint flatIndex = VTGetFlatPageIndex(pageCoord, mip);
                return _VTPageTable[flatIndex];
            }

            float3 EvaluatePageStateColor(uint packedEntry)
            {
                bool resident = (packedEntry & (1u << 26u)) != 0u;
                bool fallback = (packedEntry & (1u << 27u)) != 0u;
                bool pendingUpload = (packedEntry & (1u << 28u)) != 0u;
                bool locked = (packedEntry & (1u << 29u)) != 0u;

                float3 color = float3(0.08, 0.08, 0.08);
                if (pendingUpload)
                    color = float3(0.15, 0.70, 1.0);
                else if (fallback)
                    color = float3(1.0, 0.78, 0.15);
                else if (resident)
                    color = float3(0.15, 1.0, 0.25);

                if (locked)
                    color = lerp(color, float3(1.0, 1.0, 1.0), 0.35);

                return color;
            }

            float4 EvaluatePageTableResidencyColor(float2 overlayUv)
            {
                float safeY = min(saturate(overlayUv.y), 0.99999);
                float mipBandCount = max((float)VT_MIP_COUNT, 1.0);
                uint mip = min((uint)floor((1.0 - safeY) * mipBandCount), (uint)max(VT_MIP_COUNT - 1, 0));
                float rowUv = frac((1.0 - safeY) * mipBandCount);
                float2 localUv = float2(saturate(overlayUv.x), rowUv);

                uint pageCountX = VTGetPageCount((uint)VT_VIRTUAL_PAGE_COUNT_X, mip);
                uint pageCountY = VTGetPageCount((uint)VT_VIRTUAL_PAGE_COUNT_Y, mip);
                float2 safeLocalUv = min(localUv, 0.99999);
                uint2 pageCoord = uint2(
                    min((uint)floor(safeLocalUv.x * pageCountX), max(pageCountX - 1u, 0u)),
                    min((uint)floor((1.0 - safeLocalUv.y) * pageCountY), max(pageCountY - 1u, 0u)));
                uint packedEntry = ReadPackedPageTableEntry(pageCoord, mip);
                float3 color = EvaluatePageStateColor(packedEntry);
                float borderMask = GridBorderMask(frac(float2(
                    safeLocalUv.x * max((float)pageCountX, 1.0),
                    safeLocalUv.y * max((float)pageCountY, 1.0))));

                float rowMask = 1.0 - smoothstep(0.0, 0.0125, min(rowUv, 1.0 - rowUv));
                float bandSeparator = 1.0 - smoothstep(0.0, 0.02, min(frac((1.0 - safeY) * mipBandCount), 1.0 - frac((1.0 - safeY) * mipBandCount)));
                float separator = saturate(max(borderMask, max(rowMask, bandSeparator * 0.6)));
                return float4(lerp(color, float3(1.0, 1.0, 1.0), separator), 1.0);
            }

            float4 EvaluateVisualizationColor(float2 overlayUv)
            {
                if (_VTVisualizationMode == VIVID_VT_VISUALIZATION_PHYSICAL_CACHE)
                    return EvaluatePhysicalCacheColor(overlayUv);

                if (_VTVisualizationMode == VIVID_VT_VISUALIZATION_PAGE_TABLE_RESIDENCY)
                    return EvaluatePageTableResidencyColor(overlayUv);

                if (_VTVisualizationMode == VIVID_VT_VISUALIZATION_PHYSICAL_CACHE_AND_PAGE_TABLE_RESIDENCY)
                {
                    if (overlayUv.y >= 0.5)
                        return EvaluatePhysicalCacheColor(float2(overlayUv.x, overlayUv.y * 2.0 - 1.0));

                    return EvaluatePageTableResidencyColor(float2(overlayUv.x, overlayUv.y * 2.0));
                }

                return 0;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 sourceUv = ApplyScaleBias(input.uv, _SourceTextureScaleBias);
                float4 sourceColor = SAMPLE_TEXTURE2D(_SourceTexture, sampler_SourceTexture, sourceUv);

                if (_VTVisualizationAvailable == 0 || _VTVisualizationMode == VIVID_VT_VISUALIZATION_NONE)
                    return sourceColor;

                float2 overlayMin = _VTOverlayRect.xy;
                float2 overlayMax = overlayMin + _VTOverlayRect.zw;
                if (!IsInsideOverlay(input.uv, overlayMin, overlayMax))
                    return sourceColor;

                if (all(_VTOverlayRect.zw < 0.999) && IsOverlayBorder(input.uv, overlayMin, overlayMax))
                    return float4(1.0, 1.0, 1.0, 1.0);

                float2 overlayUv = saturate((input.uv - overlayMin) / max(_VTOverlayRect.zw, float2(1e-5, 1e-5)));
                float4 overlayColor = EvaluateVisualizationColor(overlayUv);
                return lerp(sourceColor, overlayColor, saturate(_VTOverlayOpacity));
            }
            ENDHLSL
        }
    }
}
