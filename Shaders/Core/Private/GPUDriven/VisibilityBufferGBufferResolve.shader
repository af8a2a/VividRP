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
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/SurfaceSummaryGBuffer.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividProbeVolume.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividGPUDrivenCommon.hlsl"
            #if defined(VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE)
                #define VIVID_VT_ENABLE_FEEDBACK_RW 1
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
                VividSurfaceBindingData surfaceBindingData;
                uint isUnlit;
                uint materialProgramFailed;
                uint materialProgramID;
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
                result.materialData = (VividMaterialData) 0;
                result.surfaceBindingData = (VividSurfaceBindingData) 0;
                result.materialProgramID = VIVIDMATERIALPROGRAMID_INVALID;
                result.materialProgramFailed = 1u;
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
                    if (programData.SurfaceProgramID
                        == VIVIDMATERIALSURFACEPROGRAMID_STANDARD_SINGLE_SLAB)
                    {
                        loadedMaterialProgram = VividTryLoadStandardSingleSlabSurfaceProgram(
                            result.instanceData.MaterialIndex,
                            0u,
                            result.materialData,
                            result.surfaceBindingData);
                    }
                    result.materialProgramFailed = loadedMaterialProgram ? 0u : 1u;
                }

                result.isUnlit = 0u;
                if (loadedMaterialProgram)
                {
                    result.isUnlit =
                        (programData.CapabilityFlags
                            & VIVIDMATERIALPROGRAMCAPABILITIES_UNLIT) != 0u
                        && (runtimeHeader.Flags
                            & VIVIDMATERIALRUNTIMEFLAGS_UNLIT) != 0u
                            ? 1u
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

            VividSurfaceSummaryData ResolveSurfaceData(
                const TriangleData triangleData,
                const VividBarycentricDerivatives barycentric,
                const InterpolatedUV visibilityUV,
                const float4 positionCS)
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
                VividAOTSurfaceProgramOutput aotSurfaceOutput =
                    (VividAOTSurfaceProgramOutput) 0;
                bool dispatchedAOTSurface = false;
                if (triangleData.materialProgramID != VIVIDMATERIALPROGRAMID_INVALID
                    && triangleData.materialProgramFailed == 0u)
                {
                    dispatchedAOTSurface = VividTryEvaluateAOTSurfaceProgram(
                        triangleData.materialProgramID,
                        triangleData.materialData,
                        (VividDualSlabMaterialData) 0,
                        triangleData.surfaceBindingData,
                        (VividSurfaceBindingData) 0,
                        aotContext,
                        aotSurfaceOutput);
                }
                const bool evaluatedAOTSingleSurface = dispatchedAOTSurface
                    && aotSurfaceOutput.ClosureCount == 1u
                    && aotSurfaceOutput.LayerOperator == 0u;
                const bool failedAOTSurface = triangleData.materialProgramFailed != 0u
                    || !evaluatedAOTSingleSurface;

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
                }

                const float3 normalWS = evaluatedAOTNormalWS;
                const bool isUnlit = !failedAOTSurface
                    && triangleData.isUnlit != 0u;
                const bool hasDiffuseIrradiance = !failedAOTSurface
                    && !isUnlit
                    && VividHasProbeVolumeGI();
                // Runtime material flags do not expose SSR/decal policy yet.
                const bool receiveSSR = true;
                const bool receiveDecals = true;
                const float3 diffuseIrradiance = hasDiffuseIrradiance
                    ? SampleVividProbeVolume(
                        positionWS,
                        normalWS,
                        GetWorldSpaceNormalizeViewDir(positionWS),
                        0xFFFFFFFFu)
                    : 0.0f;

                VividSurfaceSummaryData surfaceData =
                    (VividSurfaceSummaryData) 0;
                surfaceData.normalWS = normalWS;
                surfaceData.perceptualRoughness = perceptualRoughness;
                surfaceData.ambientOcclusion = ambientOcclusion;
                surfaceData.diffuseIrradiance = diffuseIrradiance;

                UNITY_BRANCH
                if (failedAOTSurface)
                {
                    surfaceData.diffuseAlbedo = float3(1.0f, 0.0f, 1.0f);
                    surfaceData.specularF0 = 0.0f;
                    surfaceData.emissive = float3(1.0f, 0.0f, 1.0f);
                    surfaceData.deferredExportHeader =
                        VividBuildDeferredExportHeader(
                            VIVID_DEFERRED_EXPORT_CLASS_ERROR,
                            false,
                            false,
                            false,
                            false);
                }
                else if (isUnlit)
                {
                    surfaceData.diffuseAlbedo = 0.0f;
                    surfaceData.specularF0 = 0.0f;
                    surfaceData.emissive = max(
                        baseColor + aotSurfaceOutput.Emission,
                        0.0f);
                    surfaceData.deferredExportHeader =
                        VividBuildDeferredExportHeader(
                            VIVID_DEFERRED_EXPORT_CLASS_UNLIT,
                            false,
                            false,
                            false,
                            receiveDecals);
                }
                else
                {
                    const float saturatedMetallic = saturate(metallic);
                    surfaceData.diffuseAlbedo = baseColor
                        * (1.0f - saturatedMetallic);
                    surfaceData.specularF0 = lerp(
                        0.04f.xxx,
                        baseColor,
                        saturatedMetallic);
                    surfaceData.emissive = max(
                        aotSurfaceOutput.Emission,
                        0.0f);
                    surfaceData.deferredExportHeader =
                        VividBuildDeferredExportHeader(
                            VIVID_DEFERRED_EXPORT_CLASS_FAST_SLAB,
                            false,
                            hasDiffuseIrradiance,
                            receiveSSR,
                            receiveDecals);
                }
                return surfaceData;
            }

            VividSurfaceSummaryGBufferOutput Frag(Varyings input)
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
                    return (VividSurfaceSummaryGBufferOutput) 0;
                }

                TriangleData triangleData = LoadTriangleData(visibilityBufferValue);
                VividSurfaceSummaryData surfaceData = ResolveSurfaceData(
                    triangleData,
                    barycentric,
                    interpolatedUV,
                    input.positionCS);
                return VividPackSurfaceSummaryGBuffer(surfaceData);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
