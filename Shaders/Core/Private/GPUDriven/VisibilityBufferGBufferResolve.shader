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
            #pragma multi_compile_local_fragment _ VIVID_DUAL_SLAB_SIDECAR_OUTPUT
            #pragma multi_compile_local_vertex _ VIVID_DUAL_SLAB_SIDECAR_TILED
            #pragma target 5.0
            #pragma require randomwrite
            #pragma use_dxc
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/SurfaceSummaryGBuffer.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividPostSurfaceSummary.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividProbeVolume.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividGPUDrivenCommon.hlsl"
            #if defined(VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE)
                #if !defined(VIVID_DUAL_SLAB_SIDECAR_OUTPUT)
                    #define VIVID_VT_ENABLE_FEEDBACK_RW 1
                #endif
            #else
                #define VIVID_GPU_DRIVEN_TEXTURE_BACKEND_BINDLESS 1
            #endif
            #include_with_pragmas "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividSurfaceSampling.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividMaterialSurface.hlsl"
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

            #if defined(VIVID_DUAL_SLAB_SIDECAR_TILED)
            #define VIVID_DUAL_SLAB_SIDECAR_TILE_SIZE 8u
            StructuredBuffer<uint> _DualSlabSidecarTileList;
            float4 _DualSlabSidecarScreenSize;
            #endif

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
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
                uint materialRuntimeFlags;
                uint isDualSlab;
                uint materialProgramFailed;
                uint materialProgramID;
                VividMaterialRuntimeHeader materialRuntimeHeader;
                VividMaterialProgramData materialProgramData;
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
                #if defined(VIVID_DUAL_SLAB_SIDECAR_TILED)
                const uint packedTileCoord =
                    _DualSlabSidecarTileList[input.instanceID];
                const uint2 tileCoord = uint2(
                    packedTileCoord & 0xFFFFu,
                    packedTileCoord >> 16u);
                const float2 tileMinPixel = float2(
                    tileCoord * VIVID_DUAL_SLAB_SIDECAR_TILE_SIZE);
                const float2 localUV = float2(
                    (input.vertexID << 1u) & 2u,
                    input.vertexID & 2u);
                output.uv = (tileMinPixel
                    + localUV * VIVID_DUAL_SLAB_SIDECAR_TILE_SIZE)
                    * _DualSlabSidecarScreenSize.zw;

                output.positionCS = float4(
                    output.uv.x * 2.0f - 1.0f,
                    1.0f - output.uv.y * 2.0f,
                    UNITY_NEAR_CLIP_VALUE,
                    1.0f);
                #ifdef UNITY_PRETRANSFORM_TO_DISPLAY_ORIENTATION
                output.positionCS = ApplyPretransformRotation(
                    output.positionCS);
                #endif
                #else
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                #endif
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

            InterpolatedUV InterpolateUV(
                const VividBarycentricDerivatives barycentric,
                const VividMeshletVertex vertex0,
                const VividMeshletVertex vertex1,
                const VividMeshletVertex vertex2)
            {
                const float3 u = InterpolateWithBarycentric(
                    barycentric,
                    vertex0.UV.x,
                    vertex1.UV.x,
                    vertex2.UV.x);
                const float3 v = InterpolateWithBarycentric(
                    barycentric,
                    vertex0.UV.y,
                    vertex1.UV.y,
                    vertex2.UV.y);

                InterpolatedUV result;
                result.uv = float2(u.x, v.x);
                result.ddx = float2(u.y, v.y);
                result.ddy = float2(u.z, v.z);
                return result;
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

            float3 EvaluateAOTSlabNormalWS(
                const VividAOTSurfaceSlabValues slab,
                const float3 fallbackNormalWS)
            {
                const float normalLengthSq = dot(slab.NormalWS, slab.NormalWS);
                const float3 normalWS = normalLengthSq > 1e-8f
                    ? slab.NormalWS * rsqrt(normalLengthSq)
                    : fallbackNormalWS;
                if (slab.HasNormal == 0u)
                    return normalWS;

                const float tangentLengthSq = dot(
                    slab.TangentWS.xyz,
                    slab.TangentWS.xyz);
                if (tangentLengthSq <= 1e-8f)
                    return normalWS;

                const float3 tangentWS = slab.TangentWS.xyz
                    * rsqrt(tangentLengthSq);
                const float3x3 tangentToWorld = CreateInstanceTangentToWorld(
                    normalWS,
                    tangentWS,
                    slab.TangentWS.w);
                return TransformTangentToWorld(
                    slab.NormalTS,
                    tangentToWorld,
                    true);
            }

            float ComputeDoubleSidedNormalFlipSign(const TriangleData triangleData)
            {
                const uint rendererListID = GetRendererListID(triangleData.instanceData, triangleData.materialData);
                if ((rendererListID & VIVIDRENDERERLISTID_CULL_OFF) == 0u)
                    return 1.0f;

                const float3 autoNormalWS = cross(
                    SafeNormalize(triangleData.positionWS0 - triangleData.positionWS1),
                    SafeNormalize(triangleData.positionWS2 - triangleData.positionWS0));
                float3 viewRayDirectionWS = GetViewForwardDir(UNITY_MATRIX_V);
                if (unity_OrthoParams.w == 0.0f)
                {
                    const float3 triangleCenterWS = (
                        triangleData.positionWS0
                        + triangleData.positionWS1
                        + triangleData.positionWS2) / 3.0f;
                    const float3 cameraToTriangleWS = triangleCenterWS - _WorldSpaceCameraPos;
                    if (dot(cameraToTriangleWS, cameraToTriangleWS) > 1e-8f)
                        viewRayDirectionWS = cameraToTriangleWS;
                }

                return dot(autoNormalWS, viewRayDirectionWS) < 0.0f ? -1.0f : 1.0f;
            }

            bool TryLoadVisibilityValue(
                Varyings input,
                out VividVisibilityBufferValue visibilityBufferValue)
            {
                float2 visibilityUv = ApplyScaleBias(input.uv, _VisibilityBufferScaleBias);
                uint2 packedValue = asuint(SAMPLE_TEXTURE2D_LOD(_VisibilityBuffer, sampler_PointClamp, visibilityUv, 0).xy);
                if (!IsPackedVisibilityBufferValueValid(packedValue))
                {
                    visibilityBufferValue = (VividVisibilityBufferValue) 0;
                    return false;
                }

                visibilityBufferValue = UnpackVisibilityBufferValue(packedValue);
                return true;
            }

            TriangleData LoadTriangleData(VividVisibilityBufferValue visibilityBufferValue)
            {
                TriangleData result;
                result.instanceData = PullInstanceData(visibilityBufferValue.InstanceID);
                result.materialData = (VividMaterialData) 0;
                result.isDualSlab = 0u;
                result.materialRuntimeFlags = 0u;
                result.materialProgramID = VIVIDMATERIALPROGRAMID_INVALID;
                result.materialProgramFailed = 1u;
                result.materialRuntimeHeader = (VividMaterialRuntimeHeader) 0;
                result.materialProgramData = (VividMaterialProgramData) 0;
                VividMaterialRuntimeHeader runtimeHeader;
                VividMaterialProgramData programData;
                const uint programStatus = VividGetMaterialProgramStatus(
                    result.instanceData.MaterialIndex,
                    runtimeHeader,
                    programData);
                bool loadedMaterialProgram = false;
                if (programStatus == VIVID_MATERIAL_PROGRAM_KNOWN)
                {
                    result.materialProgramID = runtimeHeader.ProgramID;
                    result.materialRuntimeHeader = runtimeHeader;
                    result.materialProgramData = programData;
                    loadedMaterialProgram = programData.ParameterLayoutID
                            == VIVIDMATERIALPARAMETERLAYOUTID_GENERIC_PARAMETER_LANES
                        && programData.ResourceLayoutID
                            == VIVIDMATERIALRESOURCELAYOUTID_GENERIC_RESOURCE_RECORDS
                        && (programData.SurfaceProgramID
                                == VIVIDMATERIALSURFACEPROGRAMID_STANDARD_SINGLE_SLAB
                            || programData.SurfaceProgramID
                                == VIVIDMATERIALSURFACEPROGRAMID_DUAL_SLAB);
                    result.isDualSlab = loadedMaterialProgram
                        && programData.SurfaceProgramID
                            == VIVIDMATERIALSURFACEPROGRAMID_DUAL_SLAB
                        ? 1u
                        : 0u;
                    if (result.instanceData.MaterialIndex < _MaterialDataCount)
                    {
                        result.materialData = PullMaterialData(
                            result.instanceData.MaterialIndex);
                    }
                    result.materialProgramFailed = loadedMaterialProgram ? 0u : 1u;
                    result.materialRuntimeFlags = loadedMaterialProgram
                        ? runtimeHeader.Flags
                        : 0u;
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

            void ResolveSurfaceData(
                const TriangleData triangleData,
                const VividBarycentricDerivatives barycentric,
                const InterpolatedUV visibilityUV,
                const float4 positionCS,
                out VividSurfaceSummaryData surfaceData,
                out VividDualSlabLayerData dualSlabLayerData)
            {
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
                const VividBarycentricDerivatives barycentricVertexNormalWS =
                    InterpolateWithBarycentric(
                        barycentric,
                        vertexNormalWS0,
                        vertexNormalWS1,
                        vertexNormalWS2);
                const float3 geometryNormalWS = SafeNormalize(
                    barycentricVertexNormalWS.lambda);
                const float3 positionWS = InterpolateWithBarycentricNoDerivatives(
                    barycentric,
                    triangleData.positionWS0,
                    triangleData.positionWS1,
                    triangleData.positionWS2);

                const float4 tangentOS = InterpolateWithBarycentricNoDerivatives(
                    barycentric,
                    DecodeVertexTangentOS(triangleData.vertex0.PackedTangent),
                    DecodeVertexTangentOS(triangleData.vertex1.PackedTangent),
                    DecodeVertexTangentOS(triangleData.vertex2.PackedTangent));
                float3 tangentWS = TransformInstanceObjectToWorldDir(
                    tangentOS.xyz,
                    triangleData.instanceData.ObjectToWorldMatrix,
                    false);
                const float tangentLengthSq = dot(tangentWS, tangentWS);
                float4 geometryTangentWS = 0.0f;
                if (tangentLengthSq > 1e-8f)
                {
                    tangentWS *= rsqrt(tangentLengthSq);
                    geometryTangentWS = float4(
                        tangentWS,
                        tangentOS.w
                            * GetInstanceOddNegativeScaleSign(triangleData.instanceData)
                            * normalFlipSign);
                }

                VividAOTSurfaceContext aotContext;
                aotContext.UV0 = visibilityUV.uv;
                aotContext.UV0Ddx = visibilityUV.ddx;
                aotContext.UV0Ddy = visibilityUV.ddy;
                aotContext.GeometryNormalWS = geometryNormalWS;
                aotContext.GeometryTangentWS = geometryTangentWS;
                aotContext.PositionCS = positionCS;
                VividAOTDeferredExportContract deferredExportContract =
                    (VividAOTDeferredExportContract) 0;
                VividAOTSurfaceProgramOutput aotSurfaceOutput =
                    (VividAOTSurfaceProgramOutput) 0;
                bool dispatchedAOTSurface = false;
                if (triangleData.materialProgramID != VIVIDMATERIALPROGRAMID_INVALID
                    && triangleData.materialProgramFailed == 0u)
                {
                    dispatchedAOTSurface = VividTryEvaluateAOTSurfaceProgram(
                        triangleData.materialRuntimeHeader,
                        triangleData.materialProgramData,
                        aotContext,
                        deferredExportContract,
                        aotSurfaceOutput);
                }
                const bool supportedDeferredExport = dispatchedAOTSurface
                    && VividIsAOTDeferredExportContractSupported(
                        deferredExportContract)
                    && deferredExportContract.SurfaceSummaryAbi
                        == VIVID_SURFACE_SUMMARY_GBUFFER_ABI_VERSION
                    && (deferredExportContract.DualSlabSidecarAbi
                            == VIVID_AOT_DEFERRED_EXPORT_SIDECAR_ABI_NONE
                        || deferredExportContract.DualSlabSidecarAbi
                            == VIVID_DUAL_SLAB_LAYER_SIDECAR_ABI_VERSION);
                const bool expectsDualTopology = deferredExportContract.Topology
                    != VIVID_AOT_DEFERRED_EXPORT_TOPOLOGY_NONE;
                const uint expectedLayerOperator = deferredExportContract.Topology;
                const bool evaluatedAOTSurface = supportedDeferredExport
                    && (triangleData.isDualSlab != 0u) == expectsDualTopology
                    && aotSurfaceOutput.ClosureCount
                        == deferredExportContract.ExpectedClosureCount
                    && aotSurfaceOutput.LayerOperator == expectedLayerOperator;
                const bool supportsLit = VividAOTDeferredExportHasShadingModel(
                    deferredExportContract,
                    VIVID_AOT_DEFERRED_EXPORT_SHADING_MODEL_STANDARD_LIT);
                const bool supportsUnlit = VividAOTDeferredExportHasShadingModel(
                    deferredExportContract,
                    VIVID_AOT_DEFERRED_EXPORT_SHADING_MODEL_UNLIT);
                const bool runtimeRequestsUnlit =
                    (triangleData.materialRuntimeFlags
                        & VIVIDMATERIALRUNTIMEFLAGS_UNLIT) != 0u;
                const bool invalidRuntimeUnlit = runtimeRequestsUnlit
                    && !supportsUnlit;
                const bool failedAOTSurface = triangleData.materialProgramFailed != 0u
                    || !evaluatedAOTSurface
                    || invalidRuntimeUnlit;
                const bool isUnlit = !failedAOTSurface
                    && supportsUnlit
                    && (!supportsLit || runtimeRequestsUnlit);
                const bool hasVisibleTopLayer = !failedAOTSurface
                    && expectsDualTopology
                    && saturate(aotSurfaceOutput.LayerWeight)
                        > VIVID_DUAL_SLAB_LAYER_SIDECAR_MIN_WEIGHT;
                const bool exportDualSlab = !isUnlit
                    && hasVisibleTopLayer
                    && deferredExportContract.LitClass
                        == VIVID_AOT_DEFERRED_EXPORT_LIT_CLASS_DUAL_SLAB
                    && deferredExportContract.DualSlabSidecarAbi
                        == VIVID_DUAL_SLAB_LAYER_SIDECAR_ABI_VERSION
                    && VividAOTDeferredExportHasPayload(
                        deferredExportContract,
                        VIVID_AOT_DEFERRED_EXPORT_PAYLOAD_DUAL_SLAB_SIDECAR)
                    && VividAOTDeferredExportHasPolicy(
                        deferredExportContract,
                        VIVID_AOT_DEFERRED_EXPORT_POLICY_FAST_SLAB_WHEN_SIDECAR_EMPTY);

                float3 baseColor;
                float3 evaluatedAOTNormalWS = geometryNormalWS;
                float perceptualRoughness;
                float metallic;
                float ambientOcclusion;

                UNITY_BRANCH
                if (failedAOTSurface)
                {
                    baseColor = float3(1.0f, 0.0f, 1.0f);
                    evaluatedAOTNormalWS = geometryNormalWS;
                    perceptualRoughness = 1.0f;
                    metallic = 0.0f;
                    ambientOcclusion = 1.0f;
                }
                else
                {
                    baseColor = aotSurfaceOutput.BaseSlab.BaseColor.rgb;
                    evaluatedAOTNormalWS = EvaluateAOTSlabNormalWS(
                        aotSurfaceOutput.BaseSlab,
                        geometryNormalWS);
                    perceptualRoughness =
                        aotSurfaceOutput.BaseSlab.PerceptualRoughness;
                    metallic = aotSurfaceOutput.BaseSlab.Metallic;
                    ambientOcclusion =
                        aotSurfaceOutput.BaseSlab.AmbientOcclusion;
                    if (hasVisibleTopLayer
                        && VividAOTDeferredExportHasPayload(
                            deferredExportContract,
                            VIVID_AOT_DEFERRED_EXPORT_PAYLOAD_SHARED_NORMAL_AO))
                    {
                        // Deferred Export has one shared core normal/AO. Blend
                        // the two evaluated Slabs into that representative so
                        // both layer-weight endpoints remain exact.
                        const float layerWeight = saturate(
                            aotSurfaceOutput.LayerWeight);
                        const float3 topNormalWS = EvaluateAOTSlabNormalWS(
                            aotSurfaceOutput.TopSlab,
                            geometryNormalWS);
                        evaluatedAOTNormalWS = SafeNormalize(lerp(
                            evaluatedAOTNormalWS,
                            topNormalWS,
                            layerWeight));
                        ambientOcclusion = lerp(
                            ambientOcclusion,
                            aotSurfaceOutput.TopSlab.AmbientOcclusion,
                            layerWeight);
                    }
                }

                const float3 normalWS = evaluatedAOTNormalWS;
                const bool hasDiffuseIrradiance = !failedAOTSurface
                    && !isUnlit
                    && VividAOTDeferredExportHasPolicy(
                        deferredExportContract,
                        VIVID_AOT_DEFERRED_EXPORT_POLICY_DYNAMIC_DIFFUSE_IRRADIANCE)
                    && VividHasProbeVolumeGI();
                const bool receiveSSR = VividAOTDeferredExportHasPolicy(
                    deferredExportContract,
                    VIVID_AOT_DEFERRED_EXPORT_POLICY_RECEIVE_SSR_ON_FAST_SLAB);
                const bool receiveDecals = VividAOTDeferredExportHasPolicy(
                    deferredExportContract,
                    VIVID_AOT_DEFERRED_EXPORT_POLICY_RECEIVE_DECALS);
                const float3 diffuseIrradiance = hasDiffuseIrradiance
                    ? SampleVividProbeVolume(
                        positionWS,
                        normalWS,
                        GetWorldSpaceNormalizeViewDir(positionWS),
                        0xFFFFFFFFu)
                    : 0.0f;

                VividPostSurfaceSummaryInput postSurfaceInput =
                    (VividPostSurfaceSummaryInput) 0;
                postSurfaceInput.baseColor = baseColor;
                postSurfaceInput.topBaseColor =
                    aotSurfaceOutput.TopSlab.BaseColor.rgb;
                postSurfaceInput.normalWS = normalWS;
                postSurfaceInput.perceptualRoughness = perceptualRoughness;
                postSurfaceInput.metallic = metallic;
                postSurfaceInput.ambientOcclusion = ambientOcclusion;
                postSurfaceInput.emissive = aotSurfaceOutput.Emission;
                postSurfaceInput.diffuseIrradiance = diffuseIrradiance;
                postSurfaceInput.topPerceptualRoughness =
                    aotSurfaceOutput.TopSlab.PerceptualRoughness;
                postSurfaceInput.topMetallic =
                    aotSurfaceOutput.TopSlab.Metallic;
                postSurfaceInput.layerWeight = aotSurfaceOutput.LayerWeight;
                postSurfaceInput.failedSurface = failedAOTSurface ? 1u : 0u;
                postSurfaceInput.unlitSurface = isUnlit ? 1u : 0u;
                postSurfaceInput.hasVisibleTopLayer =
                    hasVisibleTopLayer ? 1u : 0u;
                postSurfaceInput.exportDualSlab = exportDualSlab ? 1u : 0u;
                postSurfaceInput.horizontalMix =
                    deferredExportContract.Topology
                        == VIVID_AOT_DEFERRED_EXPORT_TOPOLOGY_HORIZONTAL_MIX
                    ? 1u
                    : 0u;
                postSurfaceInput.verticalLayer =
                    deferredExportContract.Topology
                        == VIVID_AOT_DEFERRED_EXPORT_TOPOLOGY_VERTICAL_LAYER
                    ? 1u
                    : 0u;
                postSurfaceInput.hasDiffuseIrradiance =
                    hasDiffuseIrradiance ? 1u : 0u;
                postSurfaceInput.receiveSSR = receiveSSR ? 1u : 0u;
                postSurfaceInput.receiveDecals = receiveDecals ? 1u : 0u;

                VividPostSurfaceSummaryOutput postSurfaceOutput =
                    VividPostSurfaceSummary(postSurfaceInput);
                surfaceData = postSurfaceOutput.surfaceData;
                dualSlabLayerData = postSurfaceOutput.dualSlabLayerData;
            }

            #if defined(VIVID_DUAL_SLAB_SIDECAR_OUTPUT)
            VividDualSlabLayerSidecarOutput Frag(Varyings input)
            #else
            VividSurfaceSummaryGBufferOutput Frag(Varyings input)
            #endif
            {
                VividVisibilityBufferValue visibilityBufferValue;
                if (!TryLoadVisibilityValue(input, visibilityBufferValue))
                {
                    discard;
                    #if defined(VIVID_DUAL_SLAB_SIDECAR_OUTPUT)
                    return (VividDualSlabLayerSidecarOutput) 0;
                    #else
                    return (VividSurfaceSummaryGBufferOutput) 0;
                    #endif
                }

                TriangleData triangleData = LoadTriangleData(visibilityBufferValue);
                #if defined(VIVID_DUAL_SLAB_SIDECAR_OUTPUT)
                if (triangleData.isDualSlab == 0u)
                    return (VividDualSlabLayerSidecarOutput) 0;
                #endif
                const float4 clipPosition0 = TransformWorldToHClip(
                    triangleData.positionWS0);
                const float4 clipPosition1 = TransformWorldToHClip(
                    triangleData.positionWS1);
                const float4 clipPosition2 = TransformWorldToHClip(
                    triangleData.positionWS2);
                const float2 pixelNdc = ScreenCoordsToNDC(input.positionCS);
                const VividBarycentricDerivatives barycentric =
                    CalculateFullBarycentric(
                        clipPosition0,
                        clipPosition1,
                        clipPosition2,
                        pixelNdc,
                        _ScreenSize.zw);
                const InterpolatedUV interpolatedUV = InterpolateUV(
                    barycentric,
                    triangleData.vertex0,
                    triangleData.vertex1,
                    triangleData.vertex2);
                VividSurfaceSummaryData surfaceData;
                VividDualSlabLayerData dualSlabLayerData;
                ResolveSurfaceData(
                    triangleData,
                    barycentric,
                    interpolatedUV,
                    input.positionCS,
                    surfaceData,
                    dualSlabLayerData);
                #if defined(VIVID_DUAL_SLAB_SIDECAR_OUTPUT)
                if (VividGetDeferredExportClass(
                        surfaceData.deferredExportHeader)
                    != VIVID_DEFERRED_EXPORT_CLASS_DUAL_SLAB)
                {
                    return (VividDualSlabLayerSidecarOutput) 0;
                }
                return VividPackDualSlabLayerSidecar(dualSlabLayerData);
                #else
                return VividPackSurfaceSummaryGBuffer(surfaceData);
                #endif
            }
            ENDHLSL
        }
    }

    FallBack Off
}
