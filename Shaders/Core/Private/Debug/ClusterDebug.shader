Shader "Hidden/VividRP/ClusterDebug"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "ClusterDebug"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Debug.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/LightingLoop.hlsl"

            #define VIVID_TILE_CLUSTER_DEBUG_NONE 0
            #define VIVID_TILE_CLUSTER_DEBUG_TILE 1
            #define VIVID_TILE_CLUSTER_DEBUG_CLUSTER 2
            #define VIVID_TILE_CLUSTER_DEBUG_MATERIAL_FEATURE_VARIANTS 3
            #define VIVID_CLUSTER_DEBUGMODE_VISUALIZE_OPAQUE 0
            #define VIVID_CLUSTER_DEBUGMODE_VISUALIZE_SLICE 1
            #define VIVID_TILE_CLUSTER_CATEGORY_PUNCTUAL (1u << 0)
            #define VIVID_TILE_CLUSTER_CATEGORY_AREA (1u << 1)
            #define VIVID_TILE_CLUSTER_CATEGORY_ENVIRONMENT (1u << 2)
            #define VIVID_TILE_CLUSTER_CATEGORY_DECAL (1u << 3)

            TEXTURE2D(_SourceTexture);
            SAMPLER(sampler_SourceTexture);
            TEXTURE2D(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);
            StructuredBuffer<uint> g_vBigTileLightList;

            float4 _SourceTextureScaleBias;
            float4 _CameraDepthTextureScaleBias;
            float4 _ClusterDebugLightViewportSize;
            float _ClusterDebugDistance;
            float _ClusterDebugMaxLightCount;
            uint _BigTileLightListEnabled;
            uint _PunctualLightCount;
            uint _AreaLightCount;
            uint _DecalCount;
            uint _NumTileBigTileX;
            uint _NumTileBigTileY;
            uint _ViewTilesFlags;
            int _TileClusterDebug;
            int _ClusterDebugMode;
            int _BigTileSize;

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

            bool IsClusterCategorySelected(uint categoryMask)
            {
                return (_ViewTilesFlags & categoryMask) != 0u;
            }

            bool IsClusterDebugEnabled()
            {
                return _TileClusterDebug == VIVID_TILE_CLUSTER_DEBUG_CLUSTER
                    && ((IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_PUNCTUAL) && _ClusteredPunctualLightGridEnabled != 0u)
                        || (IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_AREA) && _ClusteredAreaLightGridEnabled != 0u)
                        || (IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_DECAL) && _ClusteredDecalGridEnabled != 0u))
                    && _ClusterTileCountX > 0
                    && _ClusterTileCountY > 0
                    && _ClusterSliceCount > 0;
            }

            bool IsBigTileDebugEnabled()
            {
                return _TileClusterDebug == VIVID_TILE_CLUSTER_DEBUG_TILE
                    && _BigTileLightListEnabled != 0u
                    && ((IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_PUNCTUAL) && _PunctualLightCount > 0u)
                        || (IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_AREA) && _AreaLightCount > 0u)
                        || (IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_DECAL) && _DecalCount > 0u))
                    && _NumTileBigTileX > 0u
                    && _NumTileBigTileY > 0u;
            }

            bool IsSkyDepth(float deviceDepth)
            {
                return abs(deviceDepth - UNITY_RAW_FAR_CLIP_VALUE) <= 1e-6;
            }

            float4 AlphaBlend(float4 c0, float4 c1)
            {
                return float4(lerp(c0.rgb, c1.rgb, c1.a), c0.a + c1.a - c0.a * c1.a);
            }

            float3 EvaluateSliceTint(uint sliceIndex)
            {
                float sliceCount = max((float)_ClusterSliceCount, 1.0);
                float sliceRatio = (sliceIndex + 0.5) / sliceCount;
                return lerp(float3(0.2, 0.35, 1.0), float3(1.0, 0.3, 0.25), sliceRatio);
            }

            float ResolveClusterViewDepth(float2 pixelUv, float deviceDepth, out bool isValid)
            {
                if (_ClusterDebugMode == VIVID_CLUSTER_DEBUGMODE_VISUALIZE_SLICE)
                {
                    isValid = true;
                    return clamp(_ClusterDebugDistance, _ClusterNearClip, _ClusterFarClip);
                }

                if (IsSkyDepth(deviceDepth))
                {
                    isValid = false;
                    return 0.0;
                }

                isValid = true;
                return VividClusteredLighting::GetViewDepth(pixelUv, deviceDepth);
            }

            uint GetSelectedClusterLightCount(VividLightingLoopContext lightLoop)
            {
                uint lightCount = 0u;

                if (IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_PUNCTUAL))
                    lightCount += VividLightingLoop::GetPunctualLightCount(lightLoop);

                if (IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_AREA))
                    lightCount += VividLightingLoop::GetAreaLightCount(lightLoop);

                if (IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_DECAL))
                    lightCount += VividLightingLoop::GetDecalCount(lightLoop);

                return lightCount;
            }

            uint GetBigTileIndex(uint2 pixelCoord)
            {
                uint tileSize = max((uint)_BigTileSize, 1u);
                uint tileX = min(pixelCoord.x / tileSize, _NumTileBigTileX - 1u);
                uint tileY = min(pixelCoord.y / tileSize, _NumTileBigTileY - 1u);
                return tileY * _NumTileBigTileX + tileX;
            }

            uint GetBigTileLightCount(uint bigTileIndex)
            {
                uint baseOffset = (MAX_NR_BIG_TILE_LIGHTS_PLUS_ONE * bigTileIndex) >> 1;
                return min(g_vBigTileLightList[baseOffset] & 0xffffu, MAX_NR_BIG_TILE_LIGHTS_PLUS_ONE - 1u);
            }

            uint FetchBigTileLightIndex(uint bigTileIndex, uint lightOffset)
            {
                uint baseOffset = (MAX_NR_BIG_TILE_LIGHTS_PLUS_ONE * bigTileIndex) >> 1;
                uint lightOffsetPlusOne = lightOffset + 1u;
                uint packedLightIndices = g_vBigTileLightList[baseOffset + (lightOffsetPlusOne >> 1)];
                return (packedLightIndices >> ((lightOffsetPlusOne & 1u) * 16u)) & 0xffffu;
            }

            bool IsFiniteLightIndexSelected(uint lightIndex)
            {
                uint areaLightStart = _PunctualLightCount;
                uint decalStart = areaLightStart + _AreaLightCount;
                uint finiteLightEnd = decalStart + _DecalCount;

                if (lightIndex < areaLightStart)
                    return IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_PUNCTUAL);

                if (lightIndex < decalStart)
                    return IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_AREA);

                if (lightIndex < finiteLightEnd)
                    return IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_DECAL);

                return false;
            }

            uint GetSelectedBigTileLightCount(uint2 pixelCoord)
            {
                uint bigTileIndex = GetBigTileIndex(pixelCoord);
                uint bigTileLightCount = GetBigTileLightCount(bigTileIndex);
                uint selectedLightCount = 0u;

                [loop]
                for (uint lightOffset = 0u; lightOffset < bigTileLightCount; lightOffset++)
                {
                    uint lightIndex = FetchBigTileLightIndex(bigTileIndex, lightOffset);
                    selectedLightCount += IsFiniteLightIndexSelected(lightIndex) ? 1u : 0u;
                }

                return selectedLightCount;
            }

            uint GetSelectedClusterCategoryCount()
            {
                uint categoryCount = 0u;

                if (IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_PUNCTUAL) && _ClusteredPunctualLightGridEnabled != 0u)
                    categoryCount++;

                if (IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_AREA) && _ClusteredAreaLightGridEnabled != 0u)
                    categoryCount++;

                if (IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_DECAL) && _ClusteredDecalGridEnabled != 0u)
                    categoryCount++;

                return max(categoryCount, 1u);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                 uint2 viewportSize = uint2(
                    max((uint)_ClusterDebugLightViewportSize.x, 1u),
                    max((uint)_ClusterDebugLightViewportSize.y, 1u));
                uint2 pixelCoord = min(
                    (uint2)(saturate(input.uv) * viewportSize),
                    viewportSize - 1u);
                float2 pixelUv = (float2(pixelCoord) + 0.5) * _ClusterDebugLightViewportSize.zw;
                float2 sourceUv = ApplyScaleBias(pixelUv, _SourceTextureScaleBias);
                float2 depthUv = ApplyScaleBias(pixelUv, _CameraDepthTextureScaleBias);
                float4 sourceColor = SAMPLE_TEXTURE2D(_SourceTexture, sampler_SourceTexture, sourceUv);
                bool bigTileDebugEnabled = IsBigTileDebugEnabled();
                bool clusterDebugEnabled = IsClusterDebugEnabled();

                if (!bigTileDebugEnabled && !clusterDebugEnabled)
                    return sourceColor;

                if (bigTileDebugEnabled)
                {
                    uint lightCount = GetSelectedBigTileLightCount(pixelCoord);
                    uint tileSize = max((uint)_BigTileSize, 1u);
                    uint2 tileSize2 = uint2(tileSize, tileSize);
                    uint maxLightCount = max((uint)_ClusterDebugMaxLightCount, 1u);
                    float4 result = sourceColor;

                    if (lightCount > 0u)
                        result = AlphaBlend(result, OverlayHeatMap(pixelCoord, tileSize2, lightCount, maxLightCount, 0.35));

                    uint2 pixelInTile = pixelCoord % tileSize;
                    bool border = pixelInTile.x == 0u
                        || pixelInTile.y == 0u
                        || pixelInTile.x == tileSize - 1u
                        || pixelInTile.y == tileSize - 1u;

                    if (border)
                        result = AlphaBlend(result, float4(1.0, 1.0, 1.0, lightCount > 0u ? 0.22 : 0.12));

                    return result;
                }

                float deviceDepth = SAMPLE_TEXTURE2D_LOD(_CameraDepthTexture, sampler_PointClamp, depthUv, 0).r;
                bool isValid;
                float viewDepth = ResolveClusterViewDepth(pixelUv, deviceDepth, isValid);

                if (!isValid)
                    return sourceColor;

                VividLightingLoopContext lightLoop = VividLightingLoop::Create(pixelCoord, viewDepth);
                uint lightCount = GetSelectedClusterLightCount(lightLoop);
                uint sliceIndex = VividClusteredLighting::GetSliceIndex(pixelCoord, viewDepth);
                float3 sliceTint = EvaluateSliceTint(sliceIndex);
                uint tileSize = max((uint)_ClusterTileSize, 1u);
                uint2 tileSize2 = uint2(tileSize, tileSize);
                uint maxLightCount = max((uint)_ClusterDebugMaxLightCount * GetSelectedClusterCategoryCount(), 1u);
                float4 result = sourceColor;

                if (lightCount > 0u)
                {
                    float4 heatmapOverlay = OverlayHeatMap(pixelCoord, tileSize2, lightCount, maxLightCount, 0.35);
                    if (_ClusterDebugMode == VIVID_CLUSTER_DEBUGMODE_VISUALIZE_SLICE && heatmapOverlay.a < 0.99)
                        heatmapOverlay.rgb = lerp(heatmapOverlay.rgb, sliceTint, 0.2);

                    result = AlphaBlend(result, heatmapOverlay);
                }
                else if (_ClusterDebugMode == VIVID_CLUSTER_DEBUGMODE_VISUALIZE_SLICE)
                {
                    result = AlphaBlend(result, float4(sliceTint, 0.05));
                }

                uint2 pixelInTile = pixelCoord % tileSize;
                bool border = pixelInTile.x == 0u
                    || pixelInTile.y == 0u
                    || pixelInTile.x == tileSize - 1u
                    || pixelInTile.y == tileSize - 1u;
                float3 borderColor = _ClusterDebugMode == VIVID_CLUSTER_DEBUGMODE_VISUALIZE_SLICE ? sliceTint : float3(1.0, 1.0, 1.0);

                if (border)
                    result = AlphaBlend(result, float4(borderColor, lightCount > 0u ? 0.22 : 0.12));

                return result;
            }
            ENDHLSL
        }
    }
}
