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

            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VirtualTexture/VirtualTexture.hlsl"

            #define VIVID_VT_VISUALIZATION_NONE 0
            #define VIVID_VT_VISUALIZATION_PHYSICAL_CACHE 2
            #define VIVID_VT_VISUALIZATION_PAGE_TABLE_RESIDENCY 3
            #define VIVID_VT_VISUALIZATION_PHYSICAL_CACHE_AND_PAGE_TABLE_RESIDENCY 4
            #define VIVID_VT_VISUALIZATION_PAGE_TABLE_RESOLVED_MIP 5
            #define VIVID_VT_VISUALIZATION_PAGE_TABLE_PHYSICAL_PAGE 6

            #define VIVID_VT_VISUALIZATION_LAYER_BASE_COLOR 0
            #define VIVID_VT_VISUALIZATION_LAYER_NORMAL 1
            #define VIVID_VT_VISUALIZATION_LAYER_MASK 2

            TEXTURE2D(_SourceTexture);
            SAMPLER(sampler_SourceTexture);

            float4 _SourceTextureScaleBias;
            float4 _VTOverlayRect;
            int _VTVisualizationMode;
            int _VTVisualizationLayer;
            int _VTVisualizationAvailable;
            int _VTVisualizationSpaceId;
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

            int ResolveVisualizationLayerIndex()
            {
                if (_VTVisualizationLayer == VIVID_VT_VISUALIZATION_LAYER_NORMAL)
                    return VT_NORMAL_LAYER;
                if (_VTVisualizationLayer == VIVID_VT_VISUALIZATION_LAYER_MASK)
                    return VT_MASK_LAYER;
                return VT_BASE_COLOR_LAYER;
            }

            float3 HashDebugColor(uint value)
            {
                float seed = (float)(value + 1u);
                return 0.25 + 0.75 * frac(sin(seed * float3(12.9898, 78.233, 37.719)) * 43758.5453);
            }

            float4 EvaluateUnavailableColor(float2 overlayUv)
            {
                float checker = fmod(floor(overlayUv.x * 12.0) + floor(overlayUv.y * 12.0), 2.0);
                float diagonal = step(0.5, frac((overlayUv.x + overlayUv.y) * 10.0));
                float3 dark = float3(0.12, 0.015, 0.03);
                float3 bright = float3(0.65, 0.04, 0.18);
                return float4(lerp(dark, bright, saturate(checker * 0.55 + diagonal * 0.35)), 1.0);
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
                int configuredLayerIndex = ResolveVisualizationLayerIndex();
                if (configuredLayerIndex < 0)
                {
                    float missingLayerChecker = fmod(floor(localUv.x * 8.0) + floor(localUv.y * 8.0), 2.0);
                    return float4(1.0, missingLayerChecker * 0.25, 1.0, 1.0);
                }

                uint layerIndex = VTResolveLayerIndex(configuredLayerIndex, 0u);
                uint physicalGroup = VTGetLayerPhysicalGroup(layerIndex);
                uint groupLayerCount = VTGetPhysicalGroupLayerCount(physicalGroup);
                uint physicalLayer = min(VTGetLayerPhysicalLayer(layerIndex), groupLayerCount - 1u);
                uint physicalSlice = pageId * groupLayerCount + physicalLayer;
                float4 pageColor = VTSamplePhysicalCacheGroup(physicalGroup, float3(localUv, (float)physicalSlice));
                if (_VTVisualizationLayer == VIVID_VT_VISUALIZATION_LAYER_NORMAL)
                {
                    float3 decodedNormal = normalize(pageColor.xyz * 2.0 - 1.0);
                    pageColor.rgb = decodedNormal * 0.5 + 0.5;
                }

                pageColor.rgb = lerp(pageColor.rgb, HashDebugColor(pageId), 0.08);
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

            float3 EvaluateResolvedMipColor(uint packedEntry)
            {
                bool resident = (packedEntry & (1u << 26u)) != 0u;
                bool fallback = (packedEntry & (1u << 27u)) != 0u;
                bool pendingUpload = (packedEntry & (1u << 28u)) != 0u;
                bool locked = (packedEntry & (1u << 29u)) != 0u;
                if (!resident && !fallback && !pendingUpload)
                    return float3(0.04, 0.04, 0.04);

                uint resolvedMip = (packedEntry >> 20u) & 0x3Fu;
                float mipT = saturate((float)resolvedMip / max((float)(VT_MIP_COUNT - 1), 1.0));
                float3 lowMipColor = float3(0.12, 0.55, 1.0);
                float3 highMipColor = float3(1.0, 0.18, 0.04);
                float3 color = lerp(lowMipColor, highMipColor, mipT);
                if (fallback)
                    color = lerp(color, float3(1.0, 0.78, 0.15), 0.45);
                if (pendingUpload)
                    color = float3(0.15, 0.70, 1.0);
                if (locked)
                    color = lerp(color, 1.0, 0.35);
                return color;
            }

            float3 EvaluatePhysicalPageColor(uint packedEntry)
            {
                bool resident = (packedEntry & (1u << 26u)) != 0u;
                bool fallback = (packedEntry & (1u << 27u)) != 0u;
                bool pendingUpload = (packedEntry & (1u << 28u)) != 0u;
                bool locked = (packedEntry & (1u << 29u)) != 0u;
                if (!resident && !fallback && !pendingUpload)
                    return float3(0.04, 0.04, 0.04);

                uint physicalPageId = packedEntry & 0xFFFFFu;
                float3 color = HashDebugColor(physicalPageId + (uint)max(_VTVisualizationSpaceId, 0) * 4099u);
                if (fallback)
                    color = lerp(color, float3(1.0, 0.78, 0.15), 0.35);
                if (pendingUpload)
                    color = float3(0.15, 0.70, 1.0);
                if (locked)
                    color = lerp(color, 1.0, 0.35);
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
                float3 color;
                if (_VTVisualizationMode == VIVID_VT_VISUALIZATION_PAGE_TABLE_RESOLVED_MIP)
                    color = EvaluateResolvedMipColor(packedEntry);
                else if (_VTVisualizationMode == VIVID_VT_VISUALIZATION_PAGE_TABLE_PHYSICAL_PAGE)
                    color = EvaluatePhysicalPageColor(packedEntry);
                else
                    color = EvaluatePageStateColor(packedEntry);
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

                if (_VTVisualizationMode == VIVID_VT_VISUALIZATION_PAGE_TABLE_RESOLVED_MIP
                    || _VTVisualizationMode == VIVID_VT_VISUALIZATION_PAGE_TABLE_PHYSICAL_PAGE)
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

                if (_VTVisualizationMode == VIVID_VT_VISUALIZATION_NONE)
                    return sourceColor;

                float2 overlayMin = _VTOverlayRect.xy;
                float2 overlayMax = overlayMin + _VTOverlayRect.zw;
                if (!IsInsideOverlay(input.uv, overlayMin, overlayMax))
                    return sourceColor;

                if (all(_VTOverlayRect.zw < 0.999) && IsOverlayBorder(input.uv, overlayMin, overlayMax))
                    return float4(1.0, 1.0, 1.0, 1.0);

                float2 overlayUv = saturate((input.uv - overlayMin) / max(_VTOverlayRect.zw, float2(1e-5, 1e-5)));
                if (_VTVisualizationAvailable == 0)
                    return lerp(sourceColor, EvaluateUnavailableColor(overlayUv), saturate(_VTOverlayOpacity));

                float4 overlayColor = EvaluateVisualizationColor(overlayUv);
                return lerp(sourceColor, overlayColor, saturate(_VTOverlayOpacity));
            }
            ENDHLSL
        }
    }
}
