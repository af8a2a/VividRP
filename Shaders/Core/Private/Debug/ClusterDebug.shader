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

            float4 _SourceTextureScaleBias;
            float4 _CameraDepthTextureScaleBias;
            float4 _ClusterDebugLightViewportSize;
            float _ClusterDebugDistance;
            float _ClusterDebugMaxLightCount;
            uint _BigTileLightListEnabled;
            uint _ViewTilesFlags;
            int _TileClusterDebug;
            int _ClusterDebugMode;

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
                        || (IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_ENVIRONMENT) && _ClusteredReflectionProbeGridEnabled != 0u)
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
                        || (IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_ENVIRONMENT) && _ReflectionProbeCount > 0u)
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

                if (IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_ENVIRONMENT))
                    lightCount += VividLightingLoop::GetReflectionProbeCount(lightLoop);

                if (IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_DECAL))
                    lightCount += VividLightingLoop::GetDecalCount(lightLoop);

                return lightCount;
            }

            uint GetSelectedBigTileLightCount(VividBigTileLightingLoopContext lightLoop)
            {
                uint lightCount = 0u;

                if (IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_PUNCTUAL))
                    lightCount += VividLightingLoop::GetBigTilePunctualLightCount(lightLoop);

                if (IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_AREA))
                    lightCount += VividLightingLoop::GetBigTileAreaLightCount(lightLoop);

                if (IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_ENVIRONMENT))
                    lightCount += VividLightingLoop::GetBigTileReflectionProbeCount(lightLoop);

                if (IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_DECAL))
                    lightCount += VividLightingLoop::GetBigTileDecalCount(lightLoop);

                return lightCount;
            }

            uint GetSelectedClusterCategoryCount()
            {
                uint categoryCount = 0u;

                if (IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_PUNCTUAL) && _ClusteredPunctualLightGridEnabled != 0u)
                    categoryCount++;

                if (IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_AREA) && _ClusteredAreaLightGridEnabled != 0u)
                    categoryCount++;

                if (IsClusterCategorySelected(VIVID_TILE_CLUSTER_CATEGORY_ENVIRONMENT) && _ClusteredReflectionProbeGridEnabled != 0u)
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

                float deviceDepth = SAMPLE_TEXTURE2D_LOD(_CameraDepthTexture, sampler_PointClamp, depthUv, 0).r;

                if (bigTileDebugEnabled)
                {
                    VividBigTileLightingLoopContext lightLoop = VividLightingLoop::CreateBigTile(pixelCoord);
                    uint lightCount = GetSelectedBigTileLightCount(lightLoop);
                    uint tileSize = VividClusteredLighting::GetBigTileSize();
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

        Pass
        {
            Name "MaterialFeatureVariantsOverlay"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex MaterialFeatureVariantOverlayVert
            #pragma fragment MaterialFeatureVariantOverlayFrag

            #define CLASSIFY_TILE_SIZE 8
            #define VIVID_MATERIAL_FEATURE_TILE_SIZE 8u
            #define VIVID_MATERIAL_FEATURE_VARIANT_COUNT 7u
            #define VIVID_MATERIAL_FEATURE_VARIANT_CATCH_ALL 6u
            #define VIVID_INDIRECT_ARGS_ELEMENT_COUNT 4u
            #define VIVID_MATERIAL_FEATURE_DEBUG_ALL 0u
            #define VIVID_MATERIAL_FEATURE_DEBUG_MASK \
                (VIVID_MATERIALFEATURE_LIT | VIVID_MATERIALFEATURE_FABRIC | VIVID_MATERIALFEATURE_CLEAR_COAT | VIVID_MATERIALFEATURE_SSR_RECEIVE | VIVID_MATERIALFEATURE_DECAL_RECEIVE)

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Debug.hlsl"
            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/GBuffer.hlsl"
            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/TileClassification.hlsl"

            StructuredBuffer<uint> _MaterialTileFeatureFlags;
            StructuredBuffer<uint> _MaterialFeatureTileList;
            StructuredBuffer<uint> _MaterialFeatureIndirectArgs;

            float4 _ClusterDebugLightViewportSize;
            uint _MaterialFeatureDebug;
            uint _MaterialFeatureDebugAvailable;
            uint _MaterialTileCount;
            uint _MaterialTileCountX;
            uint _MaterialTileCountY;

            struct OverlayAttributes
            {
                uint vertexID : SV_VertexID;
            };

            struct OverlayVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 localUV : TEXCOORD0;
                float2 tilePixelSize : TEXCOORD1;
                nointerpolation uint variant : TEXCOORD2;
                nointerpolation uint tileIndex : TEXCOORD3;
                nointerpolation uint isValid : TEXCOORD4;
            };

            uint GetMaterialFeatureVariantFlags(uint variant)
            {
                if (variant == 0u)
                    return VIVID_MATERIALFEATURE_LIT;

                if (variant == 1u)
                    return VIVID_MATERIALFEATURE_LIT | VIVID_MATERIALFEATURE_SSR_RECEIVE;

                if (variant == 2u)
                    return VIVID_MATERIALFEATURE_LIT | VIVID_MATERIALFEATURE_CLEAR_COAT;

                if (variant == 3u)
                    return VIVID_MATERIALFEATURE_LIT | VIVID_MATERIALFEATURE_CLEAR_COAT | VIVID_MATERIALFEATURE_SSR_RECEIVE;

                if (variant == 4u)
                    return VIVID_MATERIALFEATURE_LIT | VIVID_MATERIALFEATURE_FABRIC;

                if (variant == 5u)
                    return VIVID_MATERIALFEATURE_LIT | VIVID_MATERIALFEATURE_FABRIC | VIVID_MATERIALFEATURE_SSR_RECEIVE;

                return VIVID_MATERIALFEATURE_DEFERRED_MASK;
            }

            bool IsValidMaterialFeatureMask(uint materialFeatures)
            {
                if (!HasVividMaterialFeature(materialFeatures, VIVID_MATERIALFEATURE_LIT))
                    return false;

                bool isFabric = HasVividMaterialFeature(materialFeatures, VIVID_MATERIALFEATURE_FABRIC);
                bool isClearCoat = HasVividMaterialFeature(materialFeatures, VIVID_MATERIALFEATURE_CLEAR_COAT);
                return !(isFabric && isClearCoat);
            }

            float3 EvaluateMaterialFeatureColor(uint featureMask)
            {
                if ((featureMask & VIVID_MATERIALFEATURE_LIT) != 0u)
                    return float3(0.0, 0.45, 1.0);

                if ((featureMask & VIVID_MATERIALFEATURE_FABRIC) != 0u)
                    return float3(0.8, 0.2, 0.95);

                if ((featureMask & VIVID_MATERIALFEATURE_CLEAR_COAT) != 0u)
                    return float3(1.0, 0.65, 0.0);

                if ((featureMask & VIVID_MATERIALFEATURE_SSR_RECEIVE) != 0u)
                    return float3(0.0, 0.85, 0.6);

                if ((featureMask & VIVID_MATERIALFEATURE_DECAL_RECEIVE) != 0u)
                    return float3(0.9, 0.9, 0.15);

                return float3(0.22, 0.02, 0.62);
            }

            bool TryResolveMaterialFeatureVariant(uint quadIndex, out uint variant, out uint variantTileIndex)
            {
                variantTileIndex = quadIndex;

                [unroll]
                for (variant = 0u; variant < VIVID_MATERIAL_FEATURE_VARIANT_COUNT; variant++)
                {
                    uint argsOffset = variant * VIVID_INDIRECT_ARGS_ELEMENT_COUNT;
                    uint tileCount = _MaterialFeatureIndirectArgs[argsOffset];

                    if (variantTileIndex < tileCount)
                        return true;

                    variantTileIndex -= tileCount;
                }

                variant = VIVID_MATERIAL_FEATURE_VARIANT_COUNT;
                variantTileIndex = 0u;
                return false;
            }

            float2 ResolveQuadLocalUv(uint vertexInQuad)
            {
                if (vertexInQuad == 0u)
                    return float2(0.0, 0.0);

                if (vertexInQuad == 1u)
                    return float2(1.0, 0.0);

                if (vertexInQuad == 2u || vertexInQuad == 3u)
                    return float2(0.0, 1.0);

                if (vertexInQuad == 4u)
                    return float2(1.0, 0.0);

                return float2(1.0, 1.0);
            }

            OverlayVaryings CreateInvalidOverlayVaryings()
            {
                OverlayVaryings output;
                output.positionCS = float4(-2.0, -2.0, 0.0, 1.0);
                output.localUV = float2(0.0, 0.0);
                output.tilePixelSize = float2(1.0, 1.0);
                output.variant = 0u;
                output.tileIndex = 0u;
                output.isValid = 0u;
                return output;
            }

            OverlayVaryings MaterialFeatureVariantOverlayVert(OverlayAttributes input)
            {
                if (_MaterialFeatureDebugAvailable == 0u
                    || _MaterialTileCount == 0u
                    || _MaterialTileCountX == 0u
                    || _MaterialTileCountY == 0u)
                {
                    return CreateInvalidOverlayVaryings();
                }

                uint quadIndex = input.vertexID / 6u;
                if (quadIndex >= _MaterialTileCount)
                    return CreateInvalidOverlayVaryings();

                uint variant = 0u;
                uint variantTileIndex = 0u;
                if (!TryResolveMaterialFeatureVariant(quadIndex, variant, variantTileIndex))
                    return CreateInvalidOverlayVaryings();

                uint packedTileCoord = _MaterialFeatureTileList[variant * _MaterialTileCount + variantTileIndex];
                uint2 tileCoord = TileClassifaction::UnpackTileCoord(packedTileCoord);
                if (tileCoord.x >= _MaterialTileCountX || tileCoord.y >= _MaterialTileCountY)
                    return CreateInvalidOverlayVaryings();

                float2 screenSize = max(_ClusterDebugLightViewportSize.xy, float2(1.0, 1.0));
                float2 invScreenSize = rcp(screenSize);
                float2 tileMinPixel = float2(tileCoord) * VIVID_MATERIAL_FEATURE_TILE_SIZE;
                float2 tileMaxPixel = min(tileMinPixel + VIVID_MATERIAL_FEATURE_TILE_SIZE, screenSize);
                float2 tilePixelSize = max(tileMaxPixel - tileMinPixel, float2(1.0, 1.0));

                float left = tileMinPixel.x * invScreenSize.x * 2.0 - 1.0;
                float right = tileMaxPixel.x * invScreenSize.x * 2.0 - 1.0;
                float top = 1.0 - tileMinPixel.y * invScreenSize.y * 2.0;
                float bottom = 1.0 - tileMaxPixel.y * invScreenSize.y * 2.0;
                float2 localUv = ResolveQuadLocalUv(input.vertexID - quadIndex * 6u);
                float2 positionNdc = float2(
                    lerp(left, right, localUv.x),
                    lerp(bottom, top, localUv.y));

                OverlayVaryings output;
                output.positionCS = float4(positionNdc, 0.0, 1.0);
                output.localUV = localUv;
                output.tilePixelSize = tilePixelSize;
                output.variant = variant;
                output.tileIndex = tileCoord.y * _MaterialTileCountX + tileCoord.x;
                output.isValid = 1u;
                return output;
            }

            float4 MaterialFeatureVariantOverlayFrag(OverlayVaryings input) : SV_Target
            {
                if (input.isValid == 0u || input.tileIndex >= _MaterialTileCount)
                    return float4(0.0, 0.0, 0.0, 0.0);

                uint materialFeatures = _MaterialTileFeatureFlags[input.tileIndex] & VIVID_MATERIAL_FEATURE_DEBUG_MASK;
                if (!IsValidMaterialFeatureMask(materialFeatures))
                    return float4(0.0, 0.0, 0.0, 0.0);

                uint selectedFeatureMask = _MaterialFeatureDebug & VIVID_MATERIAL_FEATURE_DEBUG_MASK;
                if (selectedFeatureMask != VIVID_MATERIAL_FEATURE_DEBUG_ALL
                    && (materialFeatures & selectedFeatureMask) == 0u)
                {
                    return float4(0.0, 0.0, 0.0, 0.0);
                }

                float4 overlay;
                if (selectedFeatureMask == VIVID_MATERIAL_FEATURE_DEBUG_ALL)
                {
                    uint2 pixelCoord = (uint2)input.positionCS.xy;
                    overlay = OverlayHeatMap(
                        pixelCoord,
                        uint2(VIVID_MATERIAL_FEATURE_TILE_SIZE, VIVID_MATERIAL_FEATURE_TILE_SIZE),
                        input.variant + 1u,
                        VIVID_MATERIAL_FEATURE_VARIANT_COUNT,
                        0.42);
                }
                else
                {
                    overlay = float4(EvaluateMaterialFeatureColor(selectedFeatureMask), 0.46);
                }

                float borderWidth = 1.25 / max(min(input.tilePixelSize.x, input.tilePixelSize.y), 1.0);
                float edgeDistance = min(
                    min(input.localUV.x, input.localUV.y),
                    min(1.0 - input.localUV.x, 1.0 - input.localUV.y));
                float border = 1.0 - smoothstep(borderWidth, borderWidth * 2.0, edgeDistance);
                overlay.rgb = lerp(overlay.rgb, float3(1.0, 1.0, 1.0), border * 0.12);
                overlay.a = saturate(overlay.a + border * 0.16);
                return overlay;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
