Shader "Hidden/VividRP/MaterialDebug"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "MaterialDebug"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GBuffer.hlsl"

            #define CLASSIFY_TILE_SIZE 8
            #define VIVID_MATERIAL_DEBUG_NONE 0
            #define VIVID_MATERIAL_DEBUG_BASE_COLOR 1
            #define VIVID_MATERIAL_DEBUG_NORMAL_WS 2
            #define VIVID_MATERIAL_DEBUG_LINEAR_ROUGHNESS 3
            #define VIVID_MATERIAL_DEBUG_PERCEPTUAL_ROUGHNESS 4
            #define VIVID_MATERIAL_DEBUG_SMOOTHNESS 5
            #define VIVID_MATERIAL_DEBUG_METALLIC 6
            #define VIVID_MATERIAL_DEBUG_AMBIENT_OCCLUSION 7
            #define VIVID_MATERIAL_DEBUG_CUSTOM_DATA 8
            #define VIVID_MATERIAL_DEBUG_CUSTOM_DATA_1 9
            #define VIVID_MATERIAL_DEBUG_MATERIAL_ID 10
            #define VIVID_MATERIAL_DEBUG_EMISSIVE 11
            #define VIVID_MATERIAL_DEBUG_BAKED_GI 12
            #define VIVID_MATERIAL_DEBUG_HAS_BAKED_GI 13
            #define VIVID_MATERIAL_DEBUG_DEPTH 14
            #define VIVID_MATERIAL_DEBUG_BAKE_DIFFUSE_LIGHTING_WITH_ALBEDO_PLUS_EMISSIVE 15
            #define VIVID_MATERIAL_DEBUG_DIFFUSE_COLOR 16
            #define VIVID_MATERIAL_DEBUG_NORMAL_VIEW_SPACE 17
            #define VIVID_MATERIAL_DEBUG_SPECULAR_OCCLUSION 18
            #define VIVID_MATERIAL_DEBUG_FRESNEL0 19
            #define VIVID_MATERIAL_DEBUG_FRESNEL90 20
            #define VIVID_MATERIAL_DEBUG_COAT_MASK 21
            #define VIVID_MATERIAL_DEBUG_COAT_ROUGHNESS 22
            #define VIVID_MATERIAL_DEBUG_MATERIAL_FEATURES 23
            #define VIVID_MATERIAL_FEATURE_DEBUG_MASK \
                (VIVID_MATERIALFEATURE_LIT | VIVID_MATERIALFEATURE_FABRIC | VIVID_MATERIALFEATURE_CLEAR_COAT | VIVID_MATERIALFEATURE_SSR_RECEIVE | VIVID_MATERIALFEATURE_DECAL_RECEIVE)

            static const float3 kVividMaterialDebugDielectricF0 = float3(0.04, 0.04, 0.04);
            static const float kVividMaterialDebugClearCoatRoughness = 0.01;

            TEXTURE2D(_SourceTexture);
            SAMPLER(sampler_SourceTexture);
            TEXTURE2D(_CameraDepthTexture);
            TEXTURE2D(_GBuffer0);
            TEXTURE2D(_GBuffer1);
            TEXTURE2D(_GBuffer2);
            TEXTURE2D(_GBuffer3);
            TEXTURE2D(_GBuffer4);
            StructuredBuffer<uint> _MaterialTileFeatureFlags;

            float4 _SourceTextureScaleBias;
            float4 _CameraDepthTextureScaleBias;
            float4 _GBuffer0ScaleBias;
            float4 _GBuffer1ScaleBias;
            float4 _GBuffer2ScaleBias;
            float4 _GBuffer3ScaleBias;
            float4 _GBuffer4ScaleBias;
            int _MaterialDebugMode;
            float _MaterialDebugExposure;
            uint _MaterialTileCount;
            uint _MaterialTileCountX;
            int _MaterialFeatureDebugAvailable;
            float4 _MaterialFeatureDebugScreenSize;

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

            bool IsSkyDepth(float deviceDepth)
            {
                return abs(deviceDepth - UNITY_RAW_FAR_CLIP_VALUE) <= 1e-6;
            }

            float3 HashColor(uint seed)
            {
                float seedValue = (float)(seed + 1u);
                float3 value = float3(seedValue, seedValue + 29.0, seedValue + 71.0);
                value = frac(sin(value * float3(12.9898, 78.233, 39.425)) * 43758.5453);
                return saturate(0.25 + value * 0.75);
            }

            float3 EvaluateMaterialIdColor(uint materialFeatures)
            {
                return HashColor(EncodeVividMaterialFeatureIdRaw(materialFeatures));
            }

            float3 EvaluateMaterialFeatureColor(uint materialFeatures)
            {
                float3 color = 0.0;

                if (HasVividMaterialFeature(materialFeatures, VIVID_MATERIALFEATURE_LIT))
                    color += float3(0.0, 0.45, 1.0);

                if (HasVividMaterialFeature(materialFeatures, VIVID_MATERIALFEATURE_FABRIC))
                    color += float3(0.8, 0.2, 0.95);

                if (HasVividMaterialFeature(materialFeatures, VIVID_MATERIALFEATURE_CLEAR_COAT))
                    color += float3(1.0, 0.65, 0.0);

                if (HasVividMaterialFeature(materialFeatures, VIVID_MATERIALFEATURE_SSR_RECEIVE))
                    color += float3(0.0, 0.85, 0.6);

                if (HasVividMaterialFeature(materialFeatures, VIVID_MATERIALFEATURE_DECAL_RECEIVE))
                    color += float3(0.9, 0.9, 0.15);

                return any(color > 0.0) ? saturate(color) : HashColor(materialFeatures);
            }

            float3 EvaluateMaterialFeatureHeatmapColor(uint featureCount)
            {
                if (featureCount <= 1u)
                    return float3(0.22, 0.02, 0.62);

                if (featureCount == 2u)
                    return float3(0.06, 0.20, 0.86);

                if (featureCount == 3u)
                    return float3(0.00, 0.62, 0.92);

                if (featureCount == 4u)
                    return float3(1.00, 0.72, 0.08);

                return float3(1.00, 0.16, 0.04);
            }

            float4 EvaluateMaterialFeatureTileHeatmap(float2 uv, float4 sourceColor)
            {
                if (_MaterialFeatureDebugAvailable == 0 || _MaterialTileCount == 0u || _MaterialTileCountX == 0u)
                    return sourceColor;

                uint2 screenSize = (uint2)max(_MaterialFeatureDebugScreenSize.xy, float2(1.0, 1.0));
                uint2 pixelCoord = min((uint2)(saturate(uv) * (float2)screenSize), screenSize - uint2(1u, 1u));
                uint2 tileCoord = pixelCoord / CLASSIFY_TILE_SIZE;
                uint tileIndex = tileCoord.y * _MaterialTileCountX + tileCoord.x;

                if (tileIndex >= _MaterialTileCount)
                    return sourceColor;

                uint materialFeatures = _MaterialTileFeatureFlags[tileIndex] & VIVID_MATERIAL_FEATURE_DEBUG_MASK;
                uint featureCount = countbits(materialFeatures);
                if (featureCount == 0u)
                    return sourceColor;

                float2 tileMinPixel = float2(tileCoord * CLASSIFY_TILE_SIZE);
                float2 tileMaxPixel = min(tileMinPixel + CLASSIFY_TILE_SIZE, _MaterialFeatureDebugScreenSize.xy);
                float2 tilePixelSize = max(tileMaxPixel - tileMinPixel, float2(1.0, 1.0));
                float2 localUv = (float2(pixelCoord) + 0.5 - tileMinPixel) / tilePixelSize;
                float edgeDistance = min(
                    min(localUv.x, localUv.y),
                    min(1.0 - localUv.x, 1.0 - localUv.y));
                float borderWidth = 1.25 / max(min(tilePixelSize.x, tilePixelSize.y), 1.0);
                float border = 1.0 - smoothstep(borderWidth, borderWidth * 2.0, edgeDistance);
                float overlayOpacity = saturate(0.42 + border * 0.38);
                float3 heatmapColor = EvaluateMaterialFeatureHeatmapColor(featureCount);
                return float4(lerp(sourceColor.rgb, heatmapColor, overlayOpacity), sourceColor.a);
            }

            float3 EncodeDirectionDebug(float3 direction)
            {
                return IsNormalized(direction) ? direction * 0.5 + 0.5 : float3(1.0, 0.0, 0.0);
            }

            float3 EvaluateDiffuseColor(VividGBufferSurfaceData surfaceData)
            {
                return surfaceData.baseColor * (1.0 - surfaceData.metallic);
            }

            real Luminance(real3 linearRgb)
            {
                return dot(linearRgb, real3(0.2126729, 0.7151522, 0.0721750));
            }

            float3 EvaluateFresnel0(VividGBufferSurfaceData surfaceData)
            {
                float3 baseSpecular = lerp(kVividMaterialDebugDielectricF0, surfaceData.baseColor, surfaceData.metallic);
                if (!HasVividMaterialFeature(surfaceData.materialFeatures, VIVID_MATERIALFEATURE_FABRIC))
                    return baseSpecular;

                float luminance = Luminance(surfaceData.baseColor);
                float3 sheenTint = lerp(luminance.xxx, surfaceData.baseColor, 0.35);
                return lerp(baseSpecular, sheenTint, saturate(surfaceData.customData));
            }

            float EvaluateCoatMask(VividGBufferSurfaceData surfaceData)
            {
                return HasVividMaterialFeature(surfaceData.materialFeatures, VIVID_MATERIALFEATURE_CLEAR_COAT)
                    ? saturate(surfaceData.customData)
                    : 0.0;
            }

            float3 EvaluateMaterialDebugColor(
                VividGBufferSurfaceData surfaceData,
                float deviceDepth,
                float4 sourceColor)
            {
                float exposureMultiplier = exp2(_MaterialDebugExposure);

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_DEPTH)
                    return Linear01Depth(deviceDepth, _ZBufferParams).xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_BAKE_DIFFUSE_LIGHTING_WITH_ALBEDO_PLUS_EMISSIVE)
                    return (surfaceData.builtinData.bakeDiffuseLighting * EvaluateDiffuseColor(surfaceData) + surfaceData.emissive) * exposureMultiplier;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_BASE_COLOR)
                    return surfaceData.baseColor;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_DIFFUSE_COLOR)
                    return EvaluateDiffuseColor(surfaceData);

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_NORMAL_WS)
                    return EncodeDirectionDebug(surfaceData.normalWS);

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_NORMAL_VIEW_SPACE)
                    return EncodeDirectionDebug(TransformWorldToViewDir(surfaceData.normalWS, true));

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_LINEAR_ROUGHNESS)
                    return surfaceData.linearRoughness.xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_PERCEPTUAL_ROUGHNESS)
                    return GetPerceptualRoughnessFromLinearRoughness(surfaceData.linearRoughness).xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_SMOOTHNESS)
                    return (1.0 - GetPerceptualRoughnessFromLinearRoughness(surfaceData.linearRoughness)).xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_METALLIC)
                    return surfaceData.metallic.xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_AMBIENT_OCCLUSION)
                    return surfaceData.ambientOcclusion.xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_SPECULAR_OCCLUSION)
                    return surfaceData.ambientOcclusion.xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_FRESNEL0)
                    return EvaluateFresnel0(surfaceData);

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_FRESNEL90)
                    return float3(1.0, 1.0, 1.0);

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_COAT_MASK)
                    return EvaluateCoatMask(surfaceData).xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_COAT_ROUGHNESS)
                {
                    float coatRoughness = EvaluateCoatMask(surfaceData) > 0.0
                        ? kVividMaterialDebugClearCoatRoughness
                        : 0.0;
                    return coatRoughness.xxx;
                }

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_MATERIAL_FEATURES)
                    return EvaluateMaterialFeatureColor(surfaceData.materialFeatures);

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_CUSTOM_DATA)
                    return surfaceData.customData.xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_CUSTOM_DATA_1)
                    return surfaceData.customData1.xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_MATERIAL_ID)
                    return EvaluateMaterialIdColor(surfaceData.materialFeatures);

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_EMISSIVE)
                    return surfaceData.emissive * exposureMultiplier;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_BAKED_GI)
                    return surfaceData.builtinData.bakeDiffuseLighting * exposureMultiplier;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_HAS_BAKED_GI)
                    return surfaceData.builtinData.hasBakedGI.xxx;

                return sourceColor.rgb;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 sourceUv = ApplyScaleBias(input.uv, _SourceTextureScaleBias);
                float4 sourceColor = SAMPLE_TEXTURE2D(_SourceTexture, sampler_SourceTexture, sourceUv);


                float2 depthUv = ApplyScaleBias(input.uv, _CameraDepthTextureScaleBias);
                float deviceDepth = SAMPLE_TEXTURE2D_LOD(_CameraDepthTexture, sampler_PointClamp, depthUv, 0).r;
                if (IsSkyDepth(deviceDepth) || _MaterialDebugMode == VIVID_MATERIAL_DEBUG_NONE)
                    return sourceColor;


                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_MATERIAL_FEATURES)
                    return EvaluateMaterialFeatureTileHeatmap(input.uv, sourceColor);

                float4 rt0 = SAMPLE_TEXTURE2D_LOD(_GBuffer0, sampler_PointClamp, ApplyScaleBias(input.uv, _GBuffer0ScaleBias), 0);
                float4 rt1 = SAMPLE_TEXTURE2D_LOD(_GBuffer1, sampler_PointClamp, ApplyScaleBias(input.uv, _GBuffer1ScaleBias), 0);
                float4 rt2 = SAMPLE_TEXTURE2D_LOD(_GBuffer2, sampler_PointClamp, ApplyScaleBias(input.uv, _GBuffer2ScaleBias), 0);
                float4 rt3 = SAMPLE_TEXTURE2D_LOD(_GBuffer3, sampler_PointClamp, ApplyScaleBias(input.uv, _GBuffer3ScaleBias), 0);
                float4 rt4 = SAMPLE_TEXTURE2D_LOD(_GBuffer4, sampler_PointClamp, ApplyScaleBias(input.uv, _GBuffer4ScaleBias), 0);
                VividGBufferSurfaceData surfaceData = UnpackVividGBufferSurfaceData(rt0, rt1, rt2, rt3, rt4);

                return float4(EvaluateMaterialDebugColor(surfaceData, deviceDepth, sourceColor), sourceColor.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
