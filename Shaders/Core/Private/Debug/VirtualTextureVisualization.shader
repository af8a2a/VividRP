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
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/TerrainRuntimeVirtualTextureSampling.hlsl"

            #define VIVID_VT_VISUALIZATION_NONE 0
            #define VIVID_VT_VISUALIZATION_PHYSICAL_ATLAS 2
            #define VIVID_VT_VISUALIZATION_PAGE_TABLE_RESIDENCY 3
            #define VIVID_VT_VISUALIZATION_PHYSICAL_ATLAS_AND_PAGE_TABLE_RESIDENCY 4
            #define VIVID_VT_VISUALIZATION_PAGE_TABLE_RESOLVED_MIP 5
            #define VIVID_VT_VISUALIZATION_PAGE_TABLE_PHYSICAL_PAGE 6
            #define VIVID_VT_VISUALIZATION_RESOLVED_WORLD_POSITION 7

            #define VIVID_VT_VISUALIZATION_LAYER_BASE_COLOR 0
            #define VIVID_VT_VISUALIZATION_LAYER_NORMAL 1
            #define VIVID_VT_VISUALIZATION_LAYER_MASK 2

            #define VIVID_TERRAIN_RVT_VISUALIZATION_NONE 0
            #define VIVID_TERRAIN_RVT_VISUALIZATION_CLIPMAP_LEVEL 1
            #define VIVID_TERRAIN_RVT_VISUALIZATION_PAGE_RESIDENCY 2
            #define VIVID_TERRAIN_RVT_VISUALIZATION_RESOLVED_SURFACE 3

            TEXTURE2D(_SourceTexture);
            SAMPLER(sampler_SourceTexture);
            TEXTURE2D(_DepthTexture);

            float4 _SourceTextureScaleBias;
            float4 _DepthTextureScaleBias;
            int _VTVisualizationMode;
            int _VTVisualizationLayer;
            int _VTVisualizationAvailable;
            int _VTVisualizationSpaceId;
            float _VTVisualizationWorldPageSize;
            int _TerrainRVTVisualizationMode;

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

            bool IsSkyDepth(float deviceDepth)
            {
                return abs(deviceDepth - UNITY_RAW_FAR_CLIP_VALUE) <= 1e-6;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float GridBorderMask(float2 localUv)
            {
                float2 edgeDistance = min(localUv, 1.0 - localUv);
                float border = min(edgeDistance.x, edgeDistance.y);
                return 1.0 - smoothstep(0.0, 0.025, border);
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

            bool TryResolveTerrainRVTRecord(
                float3 worldPosition,
                out uint recordIndex,
                out float2 terrainUv)
            {
                float4 homogeneousWorldPosition = float4(worldPosition, 1.0);
                [loop]
                for (uint candidateIndex = 0u;
                     candidateIndex < _VividTerrainRVTRecordCount;
                     candidateIndex++)
                {
                    TerrainRuntimeVirtualTextureRecordGPUData recordData =
                        _VividTerrainRVTRecords[candidateIndex];
                    if (recordData.LevelCount == 0u)
                        continue;

                    float2 candidateUv = float2(
                        dot(recordData.WorldToTerrainUvX, homogeneousWorldPosition),
                        dot(recordData.WorldToTerrainUvY, homogeneousWorldPosition));
                    float localY = dot(
                        recordData.WorldToTerrainLocalY,
                        homogeneousWorldPosition);
                    float heightPadding = max(
                        (recordData.LocalHeightRange.y - recordData.LocalHeightRange.x) * 0.002,
                        0.05);
                    bool insideUv = all(candidateUv >= 0.0) && all(candidateUv <= 1.0);
                    bool insideHeight = localY >= recordData.LocalHeightRange.x - heightPadding
                        && localY <= recordData.LocalHeightRange.y + heightPadding;
                    if (!insideUv || !insideHeight)
                        continue;

                    recordIndex = candidateIndex;
                    terrainUv = candidateUv;
                    return true;
                }

                recordIndex = 0u;
                terrainUv = 0.0.xx;
                return false;
            }

            void EvaluateTerrainRVTLevelState(
                TerrainRuntimeVirtualTextureLevelGPUData levelData,
                float2 terrainUv,
                float2 terrainUvDdx,
                float2 terrainUvDdy,
                out bool eligible,
                out bool resident,
                out float blendWeight,
                out float pageGridWeight)
            {
                eligible = false;
                resident = false;
                blendWeight = 0.0;
                pageGridWeight = 0.0;

                float2 totalPageCount = max((float2)levelData.TotalPageCount, 1.0.xx);
                float2 scaledPage = saturate(terrainUv) * totalPageCount;
                scaledPage = min(scaledPage, totalPageCount - 1e-5);
                int2 logicalPage = (int2)floor(scaledPage);
                int2 localPage = logicalPage - levelData.WindowPageOrigin;
                if (any(localPage < 0) || any(localPage >= 8))
                    return;

                float2 totalTexelCount = totalPageCount * VT_PAGE_SIZE;
                float texelFootprint = max(
                    length(terrainUvDdx * totalTexelCount),
                    length(terrainUvDdy * totalTexelCount));
                float detailWeight = saturate(2.0 - texelFootprint);
                float2 windowPosition = scaledPage - (float2)levelData.WindowPageOrigin;
                float edgeDistance = min(
                    min(windowPosition.x, windowPosition.y),
                    min(8.0 - windowPosition.x, 8.0 - windowPosition.y));
                blendWeight = min(detailWeight, saturate(edgeDistance));
                if (blendWeight <= 0.0)
                    return;

                eligible = true;
                if (_TerrainRVTVisualizationMode
                    == VIVID_TERRAIN_RVT_VISUALIZATION_CLIPMAP_LEVEL)
                {
                    float2 pageUv = frac(scaledPage);
                    float pageEdge = min(
                        min(pageUv.x, pageUv.y),
                        min(1.0 - pageUv.x, 1.0 - pageUv.y));
                    pageGridWeight = 1.0 - saturate(pageEdge * VT_PAGE_SIZE * 0.5);
                }

                uint2 ringPage = (uint2)logicalPage & 7u;
                uint2 atlasPage = levelData.AtlasPageOrigin + ringPage;
                float2 virtualUv =
                    (float2(atlasPage) + frac(scaledPage))
                    / float2(VT_VIRTUAL_PAGE_COUNT_X, VT_VIRTUAL_PAGE_COUNT_Y);
                VTResolvedAddress resolved = VTResolveAddress(virtualUv, 0u);
                resident = resolved.resident && resolved.valid && resolved.resolvedMip == 0u;
            }

            float3 TerrainRVTClipmapLevelColor(uint levelIndex, float pageGridWeight)
            {
                float3 levelColor = levelIndex == 0u
                    ? float3(1.0, 0.12, 0.04)
                    : levelIndex == 1u
                        ? float3(0.08, 1.0, 0.15)
                        : float3(0.05, 0.35, 1.0);
                return levelColor * lerp(1.0, 0.15, pageGridWeight);
            }

            float3 EvaluateTerrainRVTColor(uint recordIndex, float2 terrainUv)
            {
                TerrainRuntimeVirtualTextureRecordGPUData recordData =
                    _VividTerrainRVTRecords[recordIndex];
                float2 terrainUvDdx = ddx(terrainUv);
                float2 terrainUvDdy = ddy(terrainUv);
                int finestEligibleLevel = -1;
                int finestResidentLevel = -1;
                float3 clipmapLevelColor = 0.15.xxx;

                [unroll]
                for (int reverseLevelIndex = 2; reverseLevelIndex >= 0; reverseLevelIndex--)
                {
                    if ((uint)reverseLevelIndex >= recordData.LevelCount)
                        continue;

                    TerrainRuntimeVirtualTextureLevelGPUData levelData =
                        _VividTerrainRVTLevels[
                            recordData.LevelStartIndex + (uint)reverseLevelIndex];
                    bool eligible;
                    bool resident;
                    float blendWeight;
                    float pageGridWeight;
                    EvaluateTerrainRVTLevelState(
                        levelData,
                        terrainUv,
                        terrainUvDdx,
                        terrainUvDdy,
                        eligible,
                        resident,
                        blendWeight,
                        pageGridWeight);
                    if (eligible)
                        finestEligibleLevel = reverseLevelIndex;
                    if (!resident)
                        continue;

                    finestResidentLevel = reverseLevelIndex;
                    if (_TerrainRVTVisualizationMode
                        == VIVID_TERRAIN_RVT_VISUALIZATION_CLIPMAP_LEVEL)
                    {
                        clipmapLevelColor = lerp(
                            clipmapLevelColor,
                            TerrainRVTClipmapLevelColor(
                                (uint)reverseLevelIndex,
                                pageGridWeight),
                            blendWeight);
                    }
                }

                if (_TerrainRVTVisualizationMode
                    == VIVID_TERRAIN_RVT_VISUALIZATION_CLIPMAP_LEVEL)
                {
                    return clipmapLevelColor;
                }

                return finestEligibleLevel < 0
                    ? 0.15.xxx
                    : finestResidentLevel < 0
                        ? float3(1.0, 0.04, 0.02)
                        : finestResidentLevel == finestEligibleLevel
                            ? float3(0.05, 1.0, 0.12)
                            : float3(1.0, 0.72, 0.04);
            }

            float4 EvaluateTerrainRVTResolvedSurface(
                uint recordIndex,
                float2 terrainUv,
                float4 positionCS)
            {
                float3 baseColor = 0.0.xxx;
                float3 normalTS = float3(0.0, 0.0, 1.0);
                float4 mask = 1.0.xxxx;
                bool resolved = VividResolveTerrainRVT(
                    recordIndex,
                    terrainUv,
                    ddx(terrainUv),
                    ddy(terrainUv),
                    positionCS,
                    baseColor,
                    normalTS,
                    mask);
                if (!resolved)
                {
                    float missingChecker = fmod(
                        floor(terrainUv.x * 64.0) + floor(terrainUv.y * 64.0),
                        2.0);
                    return float4(
                        lerp(float3(0.025, 0.0, 0.025), float3(0.6, 0.0, 0.6), missingChecker),
                        1.0);
                }

                if (_VTVisualizationLayer == VIVID_VT_VISUALIZATION_LAYER_NORMAL)
                    return float4(normalTS * 0.5 + 0.5, 1.0);
                if (_VTVisualizationLayer == VIVID_VT_VISUALIZATION_LAYER_MASK)
                    return float4(mask.r, mask.g, mask.a, 1.0);
                return float4(baseColor, 1.0);
            }

            float4 EvaluateTerrainRVTVisualization(
                float2 pixelUv,
                float4 positionCS,
                float4 sourceColor)
            {
                float2 depthUv = ApplyScaleBias(pixelUv, _DepthTextureScaleBias);
                float deviceDepth = SAMPLE_TEXTURE2D_LOD(
                    _DepthTexture,
                    sampler_PointClamp,
                    depthUv,
                    0.0).r;
                if (IsSkyDepth(deviceDepth))
                    return sourceColor;

                float3 worldPosition = ComputeWorldSpacePosition(
                    pixelUv,
                    deviceDepth,
                    UNITY_MATRIX_I_VP);
                uint recordIndex;
                float2 terrainUv;
                if (!TryResolveTerrainRVTRecord(
                        worldPosition,
                        recordIndex,
                        terrainUv))
                {
                    return sourceColor;
                }

                if (_TerrainRVTVisualizationMode
                    == VIVID_TERRAIN_RVT_VISUALIZATION_RESOLVED_SURFACE)
                {
                    return EvaluateTerrainRVTResolvedSurface(
                        recordIndex,
                        terrainUv,
                        positionCS);
                }

                return float4(EvaluateTerrainRVTColor(recordIndex, terrainUv), 1.0);
            }

            float4 SamplePhysicalAtlas(uint physicalGroup, float2 atlasUv, out uint2 dimensions)
            {
                uint clampedGroup = min(physicalGroup, 3u);
                uint width;
                uint height;
                if (clampedGroup == 1u)
                {
                    _VTPhysicalCache1.GetDimensions(width, height);
                    dimensions = uint2(width, height);
                    return SAMPLE_TEXTURE2D_LOD(_VTPhysicalCache1, sampler_VTPhysicalCache, atlasUv, 0.0);
                }
                if (clampedGroup == 2u)
                {
                    _VTPhysicalCache2.GetDimensions(width, height);
                    dimensions = uint2(width, height);
                    return SAMPLE_TEXTURE2D_LOD(_VTPhysicalCache2, sampler_VTPhysicalCache, atlasUv, 0.0);
                }
                if (clampedGroup == 3u)
                {
                    _VTPhysicalCache3.GetDimensions(width, height);
                    dimensions = uint2(width, height);
                    return SAMPLE_TEXTURE2D_LOD(_VTPhysicalCache3, sampler_VTPhysicalCache, atlasUv, 0.0);
                }

                _VTPhysicalCache.GetDimensions(width, height);
                dimensions = uint2(width, height);
                return SAMPLE_TEXTURE2D_LOD(_VTPhysicalCache, sampler_VTPhysicalCache, atlasUv, 0.0);
            }

            float4 EvaluatePhysicalAtlasColor(float2 atlasUv)
            {
                int configuredLayerIndex = ResolveVisualizationLayerIndex();
                if (configuredLayerIndex < 0)
                {
                    float missingLayerChecker = fmod(floor(atlasUv.x * 16.0) + floor(atlasUv.y * 16.0), 2.0);
                    return float4(1.0, missingLayerChecker * 0.25, 1.0, 1.0);
                }

                uint layerIndex = VTResolveLayerIndex(configuredLayerIndex, 0u);
                uint physicalGroup = VTGetLayerPhysicalGroup(layerIndex);
                uint2 dimensions;
                float2 safeUv = min(saturate(atlasUv), 0.99999);
                float4 pageColor = SamplePhysicalAtlas(physicalGroup, safeUv, dimensions);
                pageColor = VTApplyLayerEncoding(pageColor, layerIndex);
                if (_VTVisualizationLayer == VIVID_VT_VISUALIZATION_LAYER_BASE_COLOR)
                    pageColor.rgb = VTApplyLayerColorSpace(pageColor.rgb, layerIndex);
                if (_VTVisualizationLayer == VIVID_VT_VISUALIZATION_LAYER_NORMAL)
                {
                    float3 decodedNormal = normalize(pageColor.xyz * 2.0 - 1.0);
                    pageColor.rgb = decodedNormal * 0.5 + 0.5;
                }
                float2 tileUv = frac(safeUv * dimensions / max((float)VT_PHYSICAL_PAGE_SIZE, 1.0));
                return lerp(pageColor, float4(1.0, 1.0, 1.0, 1.0), GridBorderMask(tileUv) * 0.75);
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

            float4 ApplyResolvedLayerDisplay(float4 value, uint layerIndex)
            {
                if (_VTVisualizationLayer == VIVID_VT_VISUALIZATION_LAYER_BASE_COLOR)
                    value.rgb = VTApplyLayerColorSpace(value.rgb, layerIndex);
                else if (_VTVisualizationLayer == VIVID_VT_VISUALIZATION_LAYER_NORMAL)
                {
                    float3 decodedNormal = normalize(value.xyz * 2.0 - 1.0);
                    value.rgb = decodedNormal * 0.5 + 0.5;
                }

                return value;
            }

            float4 EvaluateResolvedWorldPositionColor(float2 pixelUv, float4 sourceColor)
            {
                float2 depthUv = ApplyScaleBias(pixelUv, _DepthTextureScaleBias);
                float deviceDepth = SAMPLE_TEXTURE2D_LOD(_DepthTexture, sampler_PointClamp, depthUv, 0.0).r;
                if (IsSkyDepth(deviceDepth))
                    return sourceColor;

                float3 worldPosition = ComputeWorldSpacePosition(pixelUv, deviceDepth, UNITY_MATRIX_I_VP);
                float worldPageSize = max(_VTVisualizationWorldPageSize, 0.001);
                float2 virtualPageCount = max(
                    float2(VT_VIRTUAL_PAGE_COUNT_X, VT_VIRTUAL_PAGE_COUNT_Y),
                    float2(1.0, 1.0));
                float2 unwrappedVirtualUv = worldPosition.xz / (worldPageSize * virtualPageCount);
                float2 virtualUv = frac(unwrappedVirtualUv);
                VTMipRange mipRange = VTComputeRequestedMipRangeGrad(
                    virtualUv,
                    ddx(unwrappedVirtualUv),
                    ddy(unwrappedVirtualUv),
                    (uint)max(VT_MIP_COUNT - 1, 0));
                VTResolvedAddress lowerResolved = VTResolveAddress(virtualUv, mipRange.lowerMip);
                VTResolvedAddress upperResolved = VTResolveAddress(virtualUv, mipRange.upperMip);

                uint2 requestedPageCoord = VTGetPageCoord(virtualUv, mipRange.lowerMip);
                uint requestedPackedEntry = ReadPackedPageTableEntry(
                    requestedPageCoord,
                    mipRange.lowerMip);
                uint borderMip = lowerResolved.valid
                    ? lowerResolved.resolvedMip
                    : (upperResolved.valid ? upperResolved.resolvedMip : mipRange.lowerMip);
                float2 resolvedPageCount = float2(
                    VTGetPageCount((uint)VT_VIRTUAL_PAGE_COUNT_X, borderMip),
                    VTGetPageCount((uint)VT_VIRTUAL_PAGE_COUNT_Y, borderMip));
                float borderMask = GridBorderMask(frac(virtualUv * resolvedPageCount));

                if (!lowerResolved.valid && !upperResolved.valid)
                {
                    float3 stateColor = EvaluatePageStateColor(requestedPackedEntry);
                    return float4(lerp(stateColor, 1.0, borderMask * 0.65), 1.0);
                }

                int configuredLayerIndex = ResolveVisualizationLayerIndex();
                if (configuredLayerIndex < 0)
                {
                    float missingLayerChecker = fmod(
                        floor(virtualUv.x * 16.0) + floor(virtualUv.y * 16.0),
                        2.0);
                    return float4(1.0, missingLayerChecker * 0.25, 1.0, 1.0);
                }

                uint layerIndex = VTResolveLayerIndex(configuredLayerIndex, 0u);
                float4 pageColor = VTSamplePhysicalCacheTrilinearLayer(
                    virtualUv,
                    lowerResolved,
                    upperResolved,
                    mipRange.blend,
                    layerIndex);
                pageColor = ApplyResolvedLayerDisplay(pageColor, layerIndex);

                bool fallback = lowerResolved.fallback || upperResolved.fallback;
                bool pendingUpload = lowerResolved.pendingUpload || upperResolved.pendingUpload;
                if (fallback)
                    pageColor.rgb = lerp(pageColor.rgb, float3(1.0, 0.68, 0.08), 0.22);
                if (pendingUpload)
                    pageColor.rgb = lerp(pageColor.rgb, float3(0.10, 0.72, 1.0), 0.38);

                pageColor.rgb = lerp(pageColor.rgb, float3(1.0, 1.0, 1.0), borderMask * 0.8);
                pageColor.a = 1.0;
                return pageColor;
            }

            float4 EvaluateVisualizationColor(float2 overlayUv)
            {
                if (_VTVisualizationMode == VIVID_VT_VISUALIZATION_PHYSICAL_ATLAS)
                    return EvaluatePhysicalAtlasColor(overlayUv);

                if (_VTVisualizationMode == VIVID_VT_VISUALIZATION_PAGE_TABLE_RESIDENCY)
                    return EvaluatePageTableResidencyColor(overlayUv);

                if (_VTVisualizationMode == VIVID_VT_VISUALIZATION_PAGE_TABLE_RESOLVED_MIP
                    || _VTVisualizationMode == VIVID_VT_VISUALIZATION_PAGE_TABLE_PHYSICAL_PAGE)
                    return EvaluatePageTableResidencyColor(overlayUv);

                if (_VTVisualizationMode == VIVID_VT_VISUALIZATION_PHYSICAL_ATLAS_AND_PAGE_TABLE_RESIDENCY)
                {
                    if (overlayUv.y >= 0.5)
                        return EvaluatePhysicalAtlasColor(float2(overlayUv.x, overlayUv.y * 2.0 - 1.0));

                    return EvaluatePageTableResidencyColor(float2(overlayUv.x, overlayUv.y * 2.0));
                }

                return 0;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 sourceUv = ApplyScaleBias(input.uv, _SourceTextureScaleBias);
                float4 sourceColor = SAMPLE_TEXTURE2D(_SourceTexture, sampler_SourceTexture, sourceUv);

                if (_TerrainRVTVisualizationMode != VIVID_TERRAIN_RVT_VISUALIZATION_NONE)
                {
                    if (_VTVisualizationAvailable == 0
                        || _VividTerrainRVTEnabled == 0u
                        || _VividTerrainRVTRecordCount == 0u)
                    {
                        return EvaluateUnavailableColor(input.uv);
                    }

                    return EvaluateTerrainRVTVisualization(
                        input.uv,
                        input.positionCS,
                        sourceColor);
                }

                if (_VTVisualizationMode == VIVID_VT_VISUALIZATION_NONE)
                    return sourceColor;
                if (_VTVisualizationAvailable == 0)
                    return EvaluateUnavailableColor(input.uv);

                if (_VTVisualizationMode == VIVID_VT_VISUALIZATION_RESOLVED_WORLD_POSITION)
                    return EvaluateResolvedWorldPositionColor(input.uv, sourceColor);

                return EvaluateVisualizationColor(input.uv);
            }
            ENDHLSL
        }
    }
}
