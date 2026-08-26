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
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/SurfaceSummaryGBuffer.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividVisibilityBuffer.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividGPUDrivenCommon.hlsl"

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
            TEXTURE2D(_SourceTexture);
            SAMPLER(sampler_SourceTexture);
            TEXTURE2D(_CameraDepthTexture);
            TEXTURE2D(_GBuffer0);
            TEXTURE2D(_GBuffer1);
            TEXTURE2D(_GBuffer2);
            TEXTURE2D(_GBuffer3);
            TEXTURE2D(_DiffuseIrradiance);
            TEXTURE2D(_VisibilityBuffer);
            StructuredBuffer<uint> _MaterialTileFeatureFlags;

            float4 _SourceTextureScaleBias;
            float4 _CameraDepthTextureScaleBias;
            float4 _GBuffer0ScaleBias;
            float4 _GBuffer1ScaleBias;
            float4 _GBuffer2ScaleBias;
            float4 _GBuffer3ScaleBias;
            float4 _DiffuseIrradianceScaleBias;
            float4 _VisibilityBufferScaleBias;
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

            float3 EvaluateMaterialIdColor(uint materialProgramID)
            {
                return materialProgramID == VIVIDMATERIALPROGRAMID_INVALID
                    ? float3(1.0, 0.0, 1.0)
                    : HashColor(materialProgramID);
            }

            float3 EvaluateDeferredExportHeaderColor(uint deferredExportHeader)
            {
                uint exportClass = VividGetDeferredExportClass(
                    deferredExportHeader);
                if (exportClass == VIVID_DEFERRED_EXPORT_CLASS_ERROR)
                    return float3(1.0, 0.0, 1.0);
                if (exportClass == VIVID_DEFERRED_EXPORT_CLASS_UNLIT)
                    return float3(0.25, 0.25, 0.25);
                return HashColor(exportClass);
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

                uint exportClassMask = _MaterialTileFeatureFlags[tileIndex];
                uint featureCount = countbits(exportClassMask);
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

            uint LoadMaterialProgramID(float2 uv)
            {
                float2 visibilityUv = ApplyScaleBias(
                    uv,
                    _VisibilityBufferScaleBias);
                uint2 packedVisibility = asuint(SAMPLE_TEXTURE2D_LOD(
                    _VisibilityBuffer,
                    sampler_PointClamp,
                    visibilityUv,
                    0).xy);
                if (!IsPackedVisibilityBufferValueValid(packedVisibility))
                    return VIVIDMATERIALPROGRAMID_INVALID;

                VividVisibilityBufferValue visibility =
                    UnpackVisibilityBufferValue(packedVisibility);
                VividInstanceData instanceData = PullInstanceData(
                    visibility.InstanceID);
                VividMaterialRuntimeHeader runtimeHeader;
                VividMaterialProgramData programData;
                uint programStatus = VividGetMaterialProgramStatus(
                    instanceData.MaterialIndex,
                    runtimeHeader,
                    programData);
                return programStatus == VIVID_MATERIAL_PROGRAM_KNOWN
                    ? runtimeHeader.ProgramID
                    : VIVIDMATERIALPROGRAMID_INVALID;
            }

            float3 EvaluateMaterialDebugColor(
                VividSurfaceSummaryData surfaceData,
                uint materialProgramID,
                float deviceDepth,
                float4 sourceColor)
            {
                float exposureMultiplier = exp2(_MaterialDebugExposure);

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_DEPTH)
                    return Linear01Depth(deviceDepth, _ZBufferParams).xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_BAKE_DIFFUSE_LIGHTING_WITH_ALBEDO_PLUS_EMISSIVE)
                    return (surfaceData.diffuseIrradiance * surfaceData.diffuseAlbedo + surfaceData.emissive) * exposureMultiplier;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_BASE_COLOR)
                    return surfaceData.diffuseAlbedo;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_DIFFUSE_COLOR)
                    return surfaceData.diffuseAlbedo;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_NORMAL_WS)
                    return EncodeDirectionDebug(surfaceData.normalWS);

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_NORMAL_VIEW_SPACE)
                    return EncodeDirectionDebug(TransformWorldToViewDir(surfaceData.normalWS, true));

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_LINEAR_ROUGHNESS)
                    return (surfaceData.perceptualRoughness * surfaceData.perceptualRoughness).xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_PERCEPTUAL_ROUGHNESS)
                    return surfaceData.perceptualRoughness.xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_SMOOTHNESS)
                    return (1.0 - surfaceData.perceptualRoughness).xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_METALLIC)
                    return 0.0;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_AMBIENT_OCCLUSION)
                    return surfaceData.ambientOcclusion.xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_SPECULAR_OCCLUSION)
                    return surfaceData.ambientOcclusion.xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_FRESNEL0)
                    return surfaceData.specularF0;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_FRESNEL90)
                    return float3(1.0, 1.0, 1.0);

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_COAT_MASK)
                    return 0.0;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_COAT_ROUGHNESS)
                {
                    return 0.0;
                }

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_MATERIAL_FEATURES)
                    return EvaluateDeferredExportHeaderColor(
                        surfaceData.deferredExportHeader);

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_CUSTOM_DATA)
                    return ((surfaceData.deferredExportHeader >> 4u) * (1.0 / 15.0)).xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_CUSTOM_DATA_1)
                    return (VividGetDeferredExportClass(
                        surfaceData.deferredExportHeader) * (1.0 / 15.0)).xxx;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_MATERIAL_ID)
                    return EvaluateMaterialIdColor(materialProgramID);

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_EMISSIVE)
                    return surfaceData.emissive * exposureMultiplier;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_BAKED_GI)
                    return surfaceData.diffuseIrradiance * exposureMultiplier;

                if (_MaterialDebugMode == VIVID_MATERIAL_DEBUG_HAS_BAKED_GI)
                    return VividHasDeferredExportFlag(
                        surfaceData.deferredExportHeader,
                        VIVID_DEFERRED_EXPORT_FLAG_HAS_DIFFUSE_IRRADIANCE)
                            ? 1.0
                            : 0.0;

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
                float4 rt4 = SAMPLE_TEXTURE2D_LOD(
                    _DiffuseIrradiance,
                    sampler_PointClamp,
                    ApplyScaleBias(input.uv, _DiffuseIrradianceScaleBias),
                    0);
                VividSurfaceSummaryData surfaceData =
                    VividUnpackSurfaceSummaryGBuffer(rt0, rt1, rt2, rt3, rt4);
                uint materialProgramID = _MaterialDebugMode
                        == VIVID_MATERIAL_DEBUG_MATERIAL_ID
                    ? LoadMaterialProgramID(input.uv)
                    : VIVIDMATERIALPROGRAMID_INVALID;

                return float4(
                    EvaluateMaterialDebugColor(
                        surfaceData,
                        materialProgramID,
                        deviceDepth,
                        sourceColor),
                    sourceColor.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
