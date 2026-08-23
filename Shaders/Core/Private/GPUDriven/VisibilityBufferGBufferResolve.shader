Shader "Hidden/VividRP/GPUDriven/VisibilityBufferGBufferResolve"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "VividRenderPipeline"
        }

        Pass
        {
            Name "VisibilityBufferGBufferResolve"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma editor_sync_compilation
            #pragma multi_compile_fragment _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
            #pragma multi_compile_local_fragment _ VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE
            #pragma target 5.0
            #pragma require randomwrite
            #pragma use_dxc
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GBuffer.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividProbeVolume.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividGPUDrivenCommon.hlsl"
            #if defined(VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE)
                #define VIVID_VT_ENABLE_FEEDBACK_RW 1
            #else
                #define VIVID_GPU_DRIVEN_TEXTURE_BACKEND_BINDLESS 1
            #endif
            #include_with_pragmas "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividSurfaceSampling.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividMaterialSurface.hlsl"
            #if defined(VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE)
                #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/TerrainRuntimeVirtualTextureSampling.hlsl"
            #endif
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividVisibilityBuffer.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividBarycentric.hlsl"

            TYPED_TEXTURE2D(float2, _VisibilityBuffer);
            TEXTURE2D(_VisibilityBufferAttributes0);
            TEXTURE2D(_VisibilityBufferAttributes1);
            TEXTURE2D(_VisibilityBufferBarycentrics);

            StructuredBuffer<VividMeshletVertex> _SharedVertexBuffer;
            ByteAddressBuffer _SharedIndexBuffer;

            float4 _VisibilityBufferScaleBias;
            float4 _VisibilityBufferAttributes0ScaleBias;
            float4 _VisibilityBufferAttributes1ScaleBias;
            float4 _VisibilityBufferBarycentricsScaleBias;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            struct InterpolatedUV
            {
                float2 uv;
                float2 ddx;
                float2 ddy;
            };

            struct TriangleData
            {
                VividInstanceData instanceData;
                VividMaterialData materialData;
                VividDualSlabMaterialData dualSlabMaterialData;
                VividSurfaceBindingData surfaceBindingData;
                VividSurfaceBindingData topSurfaceBindingData;
                uint isDualSlab;
                uint isUnlit;
                VividMeshletVertex vertex0;
                VividMeshletVertex vertex1;
                VividMeshletVertex vertex2;
                float3 positionWS0;
                float3 positionWS1;
                float3 positionWS2;
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

            uint PullIndex(const VividDecodedMeshlet meshlet, const uint indexID)
            {
                const uint absoluteIndexID = meshlet.TriangleOffset + indexID;
                const uint packedIndices = _SharedIndexBuffer.Load((absoluteIndexID / 4u) * 4u);
                const uint shiftAmount = (absoluteIndexID % 4u) * 8u;
                return (packedIndices >> shiftAmount) & 0xFFu;
            }

            VividMeshletVertex PullVertex(const VividDecodedMeshlet meshlet, const uint index)
            {
                return _SharedVertexBuffer[meshlet.VertexOffset + index];
            }

            float3 GetPositionOS(const VividMeshletVertex vertex)
            {
                return float3(vertex.PositionX, vertex.PositionY, vertex.PositionZ);
            }

            float3 DecodeVertexNormalOS(const uint packedNormal)
            {
                return (packedNormal & VIVID_MESHLET_NORMAL_VALID_BIT) != 0u
                    ? DecodeVividMeshletOctahedral15(packedNormal)
                    : 0.0f;
            }

            float4 DecodeVertexTangentOS(const uint packedTangent)
            {
                if ((packedTangent & VIVID_MESHLET_TANGENT_VALID_BIT) == 0u)
                    return 0.0f;

                return float4(
                    DecodeVividMeshletOctahedral15(packedTangent),
                    (packedTangent & VIVID_MESHLET_TANGENT_NEGATIVE_HANDEDNESS_BIT) != 0u
                        ? -1.0f
                        : 1.0f);
            }

            float3 TransformInstanceObjectToWorldDir(float3 dirOS, float4x4 objectToWorldMatrix, bool doNormalize = true)
            {
                float3 dirWS = mul((float3x3) objectToWorldMatrix, dirOS);
                return doNormalize ? SafeNormalize(dirWS) : dirWS;
            }

            float3 TransformInstanceObjectToWorldNormal(float3 normalOS, float4x4 worldToObjectMatrix, bool doNormalize = true)
            {
                float3 normalWS = mul(normalOS, (float3x3) worldToObjectMatrix);
                return doNormalize ? SafeNormalize(normalWS) : normalWS;
            }

            float GetInstanceOddNegativeScaleSign(const VividInstanceData instanceData)
            {
                return (instanceData.Flags & VIVIDINSTANCEFLAGS_FLIP_WINDING_ORDER) != 0u ? -1.0f : 1.0f;
            }

            float3x3 CreateInstanceTangentToWorld(float3 normalWS, float3 tangentWS, float tangentSign)
            {
                float3 bitangentWS = cross(normalWS, tangentWS) * tangentSign;
                return float3x3(tangentWS, bitangentWS, normalWS);
            }

            float3 UnpackVividNormalScale(float4 packedNormal, float scale)
            {
                float3 normalTS;
                normalTS.xy = packedNormal.wy * 2.0 - 1.0;
                normalTS.xy *= scale;
                normalTS.z = sqrt(saturate(1.0 - dot(normalTS.xy, normalTS.xy)));
                return normalTS;
            }

            void ApplyTilingOffset(inout InterpolatedUV uv, float4 tilingOffset)
            {
                uv.uv = uv.uv * tilingOffset.xy + tilingOffset.zw;
                uv.ddx *= tilingOffset.xy;
                uv.ddy *= tilingOffset.xy;
            }

            float4 SampleAlbedoTextureGrad(
                const VividSurfaceBindingData surfaceBindingData,
                const VividSurfaceSampleContext surfaceSampleContext)
            {
                return VividSampleBaseColorGrad(surfaceBindingData, surfaceSampleContext);
            }

            float3 SampleNormalTSGrad(
                const VividMaterialData materialData,
                const VividSurfaceBindingData surfaceBindingData,
                const VividSurfaceSampleContext surfaceSampleContext)
            {
                float4 packedNormal = VividSampleNormalGrad(surfaceBindingData, surfaceSampleContext);
                return UnpackVividNormalScale(packedNormal, materialData.NormalsStrength);
            }

            float ComputeDoubleSidedNormalFlipSign(const TriangleData triangleData)
            {
                const uint rendererListID = GetRendererListID(triangleData.instanceData, triangleData.materialData);
                if ((rendererListID & VIVIDRENDERERLISTID_CULL_OFF) == 0u)
                    return 1.0f;

                const float3 autoNormalWS = cross(
                    SafeNormalize(triangleData.positionWS0 - triangleData.positionWS1),
                    SafeNormalize(triangleData.positionWS2 - triangleData.positionWS0));
                const float3 viewForwardDirWS = GetViewForwardDir(UNITY_MATRIX_V);
                return dot(autoNormalWS, viewForwardDirWS) < 0.0f ? -1.0f : 1.0f;
            }

            bool TryLoadVisibilityData(
                Varyings input,
                out VividVisibilityBufferValue visibilityBufferValue,
                out VividBarycentricDerivatives barycentric,
                out InterpolatedUV interpolatedUV)
            {
                barycentric = (VividBarycentricDerivatives) 0;
                interpolatedUV = (InterpolatedUV) 0;
                float2 visibilityUv = ApplyScaleBias(input.uv, _VisibilityBufferScaleBias);
                uint2 packedValue = asuint(SAMPLE_TEXTURE2D_LOD(_VisibilityBuffer, sampler_PointClamp, visibilityUv, 0).xy);
                if (!IsPackedVisibilityBufferValueValid(packedValue))
                {
                    visibilityBufferValue = (VividVisibilityBufferValue) 0;
                    return false;
                }

                visibilityBufferValue = UnpackVisibilityBufferValue(packedValue);

                float2 attributes0Uv = ApplyScaleBias(
                    input.uv,
                    _VisibilityBufferAttributes0ScaleBias);
                float2 attributes1Uv = ApplyScaleBias(
                    input.uv,
                    _VisibilityBufferAttributes1ScaleBias);
                float2 barycentricsUv = ApplyScaleBias(
                    input.uv,
                    _VisibilityBufferBarycentricsScaleBias);
                float4 attributes0 = SAMPLE_TEXTURE2D_LOD(
                    _VisibilityBufferAttributes0,
                    sampler_PointClamp,
                    attributes0Uv,
                    0);
                float4 attributes1 = SAMPLE_TEXTURE2D_LOD(
                    _VisibilityBufferAttributes1,
                    sampler_PointClamp,
                    attributes1Uv,
                    0);
                float2 barycentrics = SAMPLE_TEXTURE2D_LOD(
                    _VisibilityBufferBarycentrics,
                    sampler_PointClamp,
                    barycentricsUv,
                    0).xy;

                barycentric.lambda = DecodeVividVisibilityBufferBarycentrics(barycentrics);
                interpolatedUV.uv = attributes0.xy;
                interpolatedUV.ddx = attributes0.zw;
                interpolatedUV.ddy = attributes1.xy;
                return true;
            }

            TriangleData LoadTriangleData(VividVisibilityBufferValue visibilityBufferValue)
            {
                TriangleData result;
                result.instanceData = PullInstanceData(visibilityBufferValue.InstanceID);
                result.isDualSlab = VividTryLoadDualSlabSurfaceProgram(
                    result.instanceData.MaterialIndex,
                    VIVIDMATERIALPROGRAMCAPABILITIES_LEGACY_GBUFFER_EXPORT,
                    result.dualSlabMaterialData,
                    result.surfaceBindingData,
                    result.topSurfaceBindingData)
                        ? 1u
                        : 0u;
                bool loadedMaterialProgram = result.isDualSlab != 0u;
                if (result.isDualSlab != 0u)
                {
                    result.materialData = PullMaterialData(
                        result.instanceData.MaterialIndex);
                }
                else
                {
                    loadedMaterialProgram = VividTryLoadStandardSingleSlabSurfaceProgram(
                        result.instanceData.MaterialIndex,
                        VIVIDMATERIALPROGRAMCAPABILITIES_LEGACY_GBUFFER_EXPORT,
                        result.materialData,
                        result.surfaceBindingData);
                    if (!loadedMaterialProgram)
                    {
                        result.materialData = PullMaterialData(
                            result.instanceData.MaterialIndex);
                        result.surfaceBindingData = PullSurfaceBindingData(
                            result.materialData.SurfaceBindingIndex);
                    }
                }

                result.isUnlit = 0u;
                if (loadedMaterialProgram)
                {
                    const VividMaterialRuntimeHeader runtimeHeader =
                        PullMaterialRuntimeHeader(result.instanceData.MaterialIndex);
                    const VividMaterialProgramData programData =
                        PullMaterialProgramData(runtimeHeader.ProgramID);
                    result.isUnlit =
                        (programData.CapabilityFlags
                            & VIVIDMATERIALPROGRAMCAPABILITIES_UNLIT) != 0u
                        && (runtimeHeader.Flags
                            & VIVIDMATERIALRUNTIMEFLAGS_UNLIT) != 0u
                            ? 1u
                            : 0u;
                }
                else if ((result.materialData.MaterialFlags
                        & VIVIDMATERIALFLAGS_UNLIT) != 0u)
                {
                    result.isUnlit = 1u;
                }
                const VividDecodedMeshlet meshlet = PullMeshletData(visibilityBufferValue.MeshletID);

                const uint3 indices = uint3(
                    PullIndex(meshlet, visibilityBufferValue.IndexID + 0u),
                    PullIndex(meshlet, visibilityBufferValue.IndexID + 1u),
                    PullIndex(meshlet, visibilityBufferValue.IndexID + 2u)
                );

                result.vertex0 = PullVertex(meshlet, indices.x);
                result.vertex1 = PullVertex(meshlet, indices.y);
                result.vertex2 = PullVertex(meshlet, indices.z);

                result.positionWS0 = TransformPosition(result.instanceData.ObjectToWorldMatrix, GetPositionOS(result.vertex0));
                result.positionWS1 = TransformPosition(result.instanceData.ObjectToWorldMatrix, GetPositionOS(result.vertex1));
                result.positionWS2 = TransformPosition(result.instanceData.ObjectToWorldMatrix, GetPositionOS(result.vertex2));
                return result;
            }

            #define VIVID_MAX_TERRAIN_LAYERS 8u

            float RemapPBRChannel(const float sampleValue, const float2 remap)
            {
                return saturate(lerp(remap.x, remap.y, saturate(sampleValue)));
            }

            void ApplyMaskSample(
                const uint maskMode,
                const float4 maskSample,
                const float4 metallicSmoothnessRemap,
                const float2 ambientOcclusionRemap,
                inout float perceptualRoughness,
                inout float metallic,
                inout float ambientOcclusion)
            {
                if (maskMode == 1u)
                {
                    metallic = RemapPBRChannel(maskSample.r, metallicSmoothnessRemap.xy);
                    perceptualRoughness = 1.0f
                        - RemapPBRChannel(maskSample.a, metallicSmoothnessRemap.zw);
                }
                else if (maskMode == 2u)
                {
                    perceptualRoughness = 1.0f
                        - RemapPBRChannel(1.0f - maskSample.r, metallicSmoothnessRemap.zw);
                }
                else if (maskMode == 3u)
                {
                    metallic = RemapPBRChannel(maskSample.r, metallicSmoothnessRemap.xy);
                    ambientOcclusion = RemapPBRChannel(maskSample.g, ambientOcclusionRemap);
                    perceptualRoughness = 1.0f
                        - RemapPBRChannel(maskSample.a, metallicSmoothnessRemap.zw);
                }
                else if (maskMode == 4u)
                {
                    perceptualRoughness = 1.0f
                        - RemapPBRChannel(1.0f - maskSample.r, metallicSmoothnessRemap.zw);
                    metallic = RemapPBRChannel(maskSample.g, metallicSmoothnessRemap.xy);
                    ambientOcclusion = RemapPBRChannel(maskSample.b, ambientOcclusionRemap);
                }
            }

            void LoadTerrainControlWeights(
                const VividTerrainMaterialData terrainMaterialData,
                const InterpolatedUV terrainUv,
                const float4 positionCS,
                out float weights[8])
            {
                [unroll]
                for (uint layerIndex = 0u; layerIndex < VIVID_MAX_TERRAIN_LAYERS; ++layerIndex)
                    weights[layerIndex] = 0.0f;

                [unroll]
                for (uint controlMapIndex = 0u; controlMapIndex < 2u; ++controlMapIndex)
                {
                    uint controlBindingIndex = controlMapIndex == 0u
                        ? terrainMaterialData.ControlBindingIndex0
                        : terrainMaterialData.ControlBindingIndex1;
                    if (controlBindingIndex == 0xFFFFFFFFu || controlBindingIndex >= _SurfaceBindingDataCount)
                        continue;

                    VividSurfaceBindingData controlBinding = PullSurfaceBindingData(controlBindingIndex);
                    if (!VividSurfaceHasMask(controlBinding))
                        continue;

                    VividSurfaceSampleContext controlSampleContext = VividCreateSurfaceSampleContextGrad(
                        controlBinding,
                        terrainUv.uv,
                        terrainUv.ddx,
                        terrainUv.ddy,
                        positionCS);
                    float4 controlWeights = VividSampleMaskGrad(controlBinding, controlSampleContext);
                    uint weightOffset = controlMapIndex * 4u;
                    weights[weightOffset + 0u] = controlWeights.r;
                    weights[weightOffset + 1u] = controlWeights.g;
                    weights[weightOffset + 2u] = controlWeights.b;
                    weights[weightOffset + 3u] = controlWeights.a;
                }

                uint layerCount = min(terrainMaterialData.LayerCount, VIVID_MAX_TERRAIN_LAYERS);
                float weightSum = 0.0f;
                [unroll]
                for (uint layerIndex = 0u; layerIndex < VIVID_MAX_TERRAIN_LAYERS; ++layerIndex)
                {
                    weights[layerIndex] = layerIndex < layerCount
                        ? max(weights[layerIndex], 0.0f)
                        : 0.0f;
                    weightSum += weights[layerIndex];
                }

                if (weightSum > 1e-5f)
                {
                    float inverseWeightSum = rcp(weightSum);
                    [unroll]
                    for (uint layerIndex = 0u; layerIndex < VIVID_MAX_TERRAIN_LAYERS; ++layerIndex)
                        weights[layerIndex] *= inverseWeightSum;
                }
                else if (layerCount > 0u)
                {
                    weights[0] = 1.0f;
                }
            }

            void ResolveTerrainSurfaceSamples(
                const VividMaterialData materialData,
                const InterpolatedUV terrainUv,
                const float4 positionCS,
                out float3 baseColor,
                out float3 normalTS,
                out bool hasNormal,
                out float perceptualRoughness,
                out float metallic,
                out float ambientOcclusion)
            {
                VividTerrainMaterialData terrainMaterialData = PullTerrainMaterialData(materialData.Padding1);
                float weights[8];
                LoadTerrainControlWeights(terrainMaterialData, terrainUv, positionCS, weights);

                baseColor = 0.0f;
                normalTS = 0.0f;
                hasNormal = false;
                perceptualRoughness = 0.0f;
                metallic = 0.0f;
                ambientOcclusion = 0.0f;

                uint layerCount = min(terrainMaterialData.LayerCount, VIVID_MAX_TERRAIN_LAYERS);
                [unroll]
                for (uint layerIndex = 0u; layerIndex < VIVID_MAX_TERRAIN_LAYERS; ++layerIndex)
                {
                    if (layerIndex >= layerCount || weights[layerIndex] <= 0.0f)
                        continue;

                    VividTerrainLayerGPUData layerData = PullTerrainLayerData(
                        terrainMaterialData.LayerStartIndex + layerIndex);
                    VividSurfaceBindingData layerBinding = PullSurfaceBindingData(layerData.SurfaceBindingIndex);
                    InterpolatedUV layerUv = terrainUv;
                    ApplyTilingOffset(layerUv, layerData.TextureTilingOffset);
                    VividSurfaceSampleContext layerSampleContext = VividCreateSurfaceSampleContextGrad(
                        layerBinding,
                        layerUv.uv,
                        layerUv.ddx,
                        layerUv.ddy,
                        positionCS);

                    float weight = weights[layerIndex];
                    baseColor += VividSampleBaseColorGrad(layerBinding, layerSampleContext).rgb * weight;

                    float3 layerNormalTS = float3(0.0f, 0.0f, 1.0f);
                    if (VividSurfaceHasNormal(layerBinding))
                    {
                        layerNormalTS = UnpackVividNormalScale(
                            VividSampleNormalGrad(layerBinding, layerSampleContext),
                            layerData.NormalsStrength);
                        hasNormal = true;
                    }
                    normalTS += layerNormalTS * weight;

                    float layerPerceptualRoughness = layerData.Roughness;
                    float layerMetallic = layerData.Metallic;
                    float layerAmbientOcclusion = 1.0f;
                    if (VividSurfaceHasMask(layerBinding))
                    {
                        ApplyMaskSample(
                            layerData.MaskMode,
                            VividSampleMaskGrad(layerBinding, layerSampleContext),
                            float4(0.0f, 1.0f, 0.0f, 1.0f),
                            float2(0.0f, 1.0f),
                            layerPerceptualRoughness,
                            layerMetallic,
                            layerAmbientOcclusion);
                    }
                    perceptualRoughness += layerPerceptualRoughness * weight;
                    metallic += layerMetallic * weight;
                    ambientOcclusion += layerAmbientOcclusion * weight;
                }

                normalTS = SafeNormalize(normalTS);
            }

            VividGBufferSurfaceData ResolveSurfaceData(
                const TriangleData triangleData,
                const VividBarycentricDerivatives barycentric,
                const InterpolatedUV visibilityUV,
                const float4 positionCS)
            {
                InterpolatedUV terrainUv = visibilityUV;

                float3 baseColor;
                float3 sampledNormalTS = float3(0.0f, 0.0f, 1.0f);
                bool hasSampledNormal = false;
                float perceptualRoughness;
                float metallic;
                float ambientOcclusion;
                bool isTerrain = (triangleData.materialData.MaterialFlags & VIVIDMATERIALFLAGS_TERRAIN) != 0u
                    && triangleData.materialData.Padding1 < _TerrainMaterialDataCount;
                bool isTerrainRVT =
                    (triangleData.materialData.MaterialFlags
                        & VIVIDMATERIALFLAGS_TERRAIN_RUNTIME_VIRTUAL_TEXTURE) != 0u;
#if defined(VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE)
                uint terrainRVTRecordFlags = 0u;
                if (isTerrainRVT
                    && triangleData.materialData.Padding1 < _VividTerrainRVTRecordCount)
                {
                    terrainRVTRecordFlags =
                        _VividTerrainRVTRecords[triangleData.materialData.Padding1].Padding0;
                }
#endif

                UNITY_BRANCH
                if (isTerrain)
                {
                    ResolveTerrainSurfaceSamples(
                        triangleData.materialData,
                        terrainUv,
                        positionCS,
                        baseColor,
                        sampledNormalTS,
                        hasSampledNormal,
                        perceptualRoughness,
                        metallic,
                        ambientOcclusion);
                }
                else
                {
                    if (triangleData.isDualSlab != 0u)
                    {
                        const VividEvaluatedSlabSurface baseSlab =
                            VividEvaluateSlabSurfaceGrad(
                                VividGetBaseSlabMaterialData(
                                    triangleData.dualSlabMaterialData),
                                triangleData.surfaceBindingData,
                                visibilityUV.uv,
                                visibilityUV.ddx,
                                visibilityUV.ddy,
                                positionCS);
                        const VividEvaluatedSlabSurface topSlab =
                            VividEvaluateSlabSurfaceGrad(
                                VividGetTopSlabMaterialData(
                                    triangleData.dualSlabMaterialData),
                                triangleData.topSurfaceBindingData,
                                visibilityUV.uv,
                                visibilityUV.ddx,
                                visibilityUV.ddy,
                                positionCS);
                        const float layerWeight = saturate(
                            triangleData.dualSlabMaterialData.LayerWeight);

                        // Legacy GBuffer cannot preserve the two-Closure topology. Both
                        // operators deliberately degrade to the same parameter blend.
                        baseColor = lerp(
                            baseSlab.BaseColor,
                            topSlab.BaseColor,
                            layerWeight);
                        sampledNormalTS = SafeNormalize(lerp(
                            baseSlab.NormalTS,
                            topSlab.NormalTS,
                            layerWeight));
                        hasSampledNormal =
                            baseSlab.HasNormal != 0u || topSlab.HasNormal != 0u;
                        perceptualRoughness = lerp(
                            baseSlab.PerceptualRoughness,
                            topSlab.PerceptualRoughness,
                            layerWeight);
                        metallic = lerp(
                            baseSlab.Metallic,
                            topSlab.Metallic,
                            layerWeight);
                        ambientOcclusion = lerp(
                            baseSlab.AmbientOcclusion,
                            topSlab.AmbientOcclusion,
                            layerWeight);
                    }
                    else
                    {
                        InterpolatedUV uv = terrainUv;
                        ApplyTilingOffset(uv, triangleData.materialData.TextureTilingOffset);
                        VividSurfaceSampleContext surfaceSampleContext = VividCreateSurfaceSampleContextGrad(
                            triangleData.surfaceBindingData,
                            uv.uv,
                            uv.ddx,
                            uv.ddy,
                            positionCS);
                        float4 albedoSample = SampleAlbedoTextureGrad(
                            triangleData.surfaceBindingData,
                            surfaceSampleContext);
                        baseColor = albedoSample.rgb * triangleData.materialData.AlbedoColor.rgb;
                        perceptualRoughness = triangleData.materialData.Roughness;
                        metallic = triangleData.materialData.Metallic;
                        ambientOcclusion = 1.0f;

                        if (VividSurfaceHasNormal(triangleData.surfaceBindingData))
                        {
                            sampledNormalTS = SampleNormalTSGrad(
                                triangleData.materialData,
                                triangleData.surfaceBindingData,
                                surfaceSampleContext);
                            hasSampledNormal = true;
                        }

                        float4 maskSample = VividSurfaceHasMask(triangleData.surfaceBindingData)
                            ? VividSampleMaskGrad(
                                triangleData.surfaceBindingData,
                                surfaceSampleContext)
                            : 1.0.xxxx;
#if defined(VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE)
                        if (isTerrainRVT)
                        {
                            bool sampledTerrainRVT = VividResolveTerrainRVT(
                                triangleData.materialData.Padding1,
                                terrainUv.uv,
                                terrainUv.ddx,
                                terrainUv.ddy,
                                positionCS,
                                baseColor,
                                sampledNormalTS,
                                maskSample);
                            hasSampledNormal = hasSampledNormal || sampledTerrainRVT;
                        }
#endif
                        if (VividSurfaceHasMask(triangleData.surfaceBindingData) || isTerrainRVT)
                        {
                            ApplyMaskSample(
                                triangleData.materialData.Padding0,
                                maskSample,
                                triangleData.materialData.MetallicSmoothnessRemap,
                                triangleData.materialData.AmbientOcclusionRemap.xy,
                                perceptualRoughness,
                                metallic,
                                ambientOcclusion);
                        }
                    }
                }

                const float normalFlipSign = ComputeDoubleSidedNormalFlipSign(triangleData);
                const float3 vertexNormalWS0 = normalFlipSign * TransformInstanceObjectToWorldNormal(
                    SafeNormalize(DecodeVertexNormalOS(triangleData.vertex0.PackedNormal)),
                    triangleData.instanceData.WorldToObjectMatrix);
                const float3 vertexNormalWS1 = normalFlipSign * TransformInstanceObjectToWorldNormal(
                    SafeNormalize(DecodeVertexNormalOS(triangleData.vertex1.PackedNormal)),
                    triangleData.instanceData.WorldToObjectMatrix);
                const float3 vertexNormalWS2 = normalFlipSign * TransformInstanceObjectToWorldNormal(
                    SafeNormalize(DecodeVertexNormalOS(triangleData.vertex2.PackedNormal)),
                    triangleData.instanceData.WorldToObjectMatrix);

                VividBarycentricDerivatives barycentricVertexNormalWS = InterpolateWithBarycentric(
                    barycentric,
                    vertexNormalWS0,
                    vertexNormalWS1,
                    vertexNormalWS2);
                float3 normalWS = SafeNormalize(barycentricVertexNormalWS.lambda);
                float3 positionWS = InterpolateWithBarycentricNoDerivatives(
                    barycentric,
                    triangleData.positionWS0,
                    triangleData.positionWS1,
                    triangleData.positionWS2);

                UNITY_BRANCH
                if (hasSampledNormal)
                {
                    float4 tangentOS = InterpolateWithBarycentricNoDerivatives(
                        barycentric,
                        DecodeVertexTangentOS(triangleData.vertex0.PackedTangent),
                        DecodeVertexTangentOS(triangleData.vertex1.PackedTangent),
                        DecodeVertexTangentOS(triangleData.vertex2.PackedTangent));
                    float3 tangentWS = TransformInstanceObjectToWorldDir(
                        tangentOS.xyz,
                        triangleData.instanceData.ObjectToWorldMatrix,
                        false);
                    float tangentLengthSq = dot(tangentWS, tangentWS);
                    if (tangentLengthSq > 1e-8f)
                    {
                        tangentWS *= rsqrt(tangentLengthSq);
                        float tangentSign = tangentOS.w
                            * GetInstanceOddNegativeScaleSign(triangleData.instanceData)
                            * normalFlipSign;
                        float3x3 tangentToWorld = CreateInstanceTangentToWorld(normalWS, tangentWS, tangentSign);
                        normalWS = TransformTangentToWorld(sampledNormalTS, tangentToWorld, true);
                    }
                }

                VividGBufferSurfaceData surfaceData;
                surfaceData.baseColor = baseColor;
                surfaceData.normalWS = normalWS;
                surfaceData.linearRoughness = perceptualRoughness * perceptualRoughness;
                surfaceData.metallic = metallic;
                surfaceData.ambientOcclusion = ambientOcclusion;
                surfaceData.customData = 0.0f;
                surfaceData.customData1 = 0.0f;
                surfaceData.materialFeatures = triangleData.isUnlit != 0u
                    ? 0u
                    : VIVID_MATERIALFEATURE_DEFAULT;
#if defined(VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE)
                if (isTerrainRVT
                    && (terrainRVTRecordFlags & VIVID_TERRAIN_RVT_RECEIVE_DECALS) == 0u)
                {
                    surfaceData.materialFeatures &= ~VIVID_MATERIALFEATURE_DECAL_RECEIVE;
                }
#endif
                surfaceData.emissive = max(triangleData.materialData.Emission.rgb, 0.0f);
                surfaceData.builtinData = CreateVividBuiltinData(
                    SampleVividProbeVolume(
                        positionWS,
                        normalWS,
                        GetWorldSpaceNormalizeViewDir(positionWS),
                        0xFFFFFFFFu),
                    VividHasProbeVolumeGI() ? 1.0f : 0.0f,
                    0.0f,
                    float4(1.0f, 1.0f, 1.0f, 1.0f));
                return surfaceData;
            }

            VividGBufferFragmentOutput Frag(Varyings input)
            {
                VividVisibilityBufferValue visibilityBufferValue;
                VividBarycentricDerivatives barycentric;
                InterpolatedUV interpolatedUV;
                if (!TryLoadVisibilityData(
                        input,
                        visibilityBufferValue,
                        barycentric,
                        interpolatedUV))
                {
                    discard;
                    return (VividGBufferFragmentOutput) 0;
                }

                TriangleData triangleData = LoadTriangleData(visibilityBufferValue);
                VividGBufferSurfaceData surfaceData = ResolveSurfaceData(
                    triangleData,
                    barycentric,
                    interpolatedUV,
                    input.positionCS);
                return PackVividGBufferSurfaceData(surfaceData);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
