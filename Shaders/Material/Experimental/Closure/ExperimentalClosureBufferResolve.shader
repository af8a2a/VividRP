Shader "Hidden/VividRP/Experimental/ClosureBufferResolve"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "ExperimentalClosureBufferResolve"
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
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividProbeVolume.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividGPUDrivenCommon.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/Closure/ExperimentalClosureBuffer.hlsl"
            #if defined(VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE)
                #define VIVID_VT_ENABLE_FEEDBACK_RW 1
            #else
                #define VIVID_GPU_DRIVEN_TEXTURE_BACKEND_BINDLESS 1
            #endif
            #include_with_pragmas "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividSurfaceSampling.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividMaterialSurface.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividVisibilityBuffer.hlsl"

            TYPED_TEXTURE2D(float2, _ExperimentalVisibilityBuffer);
            TEXTURE2D(_ExperimentalVisibilityAttributes0);
            TEXTURE2D(_ExperimentalVisibilityAttributes1);
            TEXTURE2D_X_FLOAT(_ExperimentalDepthTexture);

            float4 _ExperimentalVisibilityScaleBias;
            float4 _ExperimentalAttributes0ScaleBias;
            float4 _ExperimentalAttributes1ScaleBias;
            float4 _ExperimentalDepthScaleBias;

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

            float3 DecodeNormalOct(float2 encoded)
            {
                float2 oct = encoded * 2.0 - 1.0;
                float3 normal = float3(oct, 1.0 - abs(oct.x) - abs(oct.y));
                if (normal.z < 0.0)
                {
                    float2 signs = float2(
                        normal.x >= 0.0 ? 1.0 : -1.0,
                        normal.y >= 0.0 ? 1.0 : -1.0);
                    normal.xy = (1.0 - abs(normal.yx)) * signs;
                }
                return SafeNormalize(normal);
            }

            float3x3 ReconstructTangentToWorld(
                float3 positionWS,
                float3 geometricNormalWS,
                float2 uvDdx,
                float2 uvDdy)
            {
                float3 positionDdx = ddx(positionWS);
                float3 positionDdy = ddy(positionWS);
                float determinant = uvDdx.x * uvDdy.y - uvDdx.y * uvDdy.x;
                float3 normalWS = SafeNormalize(geometricNormalWS);
                if (abs(determinant) <= 1e-8)
                {
                    float3 axis = abs(normalWS.z) < 0.999
                        ? float3(0.0, 0.0, 1.0)
                        : float3(0.0, 1.0, 0.0);
                    float3 tangentWS = SafeNormalize(cross(axis, normalWS));
                    return float3x3(tangentWS, cross(normalWS, tangentWS), normalWS);
                }

                float inverseDeterminant = rcp(determinant);
                float3 tangentCandidate =
                    (positionDdx * uvDdy.y - positionDdy * uvDdx.y)
                    * inverseDeterminant;
                float3 bitangentCandidate =
                    (positionDdy * uvDdx.x - positionDdx * uvDdy.x)
                    * inverseDeterminant;
                float3 tangentWS = SafeNormalize(
                    tangentCandidate - normalWS * dot(normalWS, tangentCandidate));
                float3 crossBitangent = cross(normalWS, tangentWS);
                float handedness = dot(crossBitangent, bitangentCandidate) < 0.0
                    ? -1.0
                    : 1.0;
                return float3x3(tangentWS, crossBitangent * handedness, normalWS);
            }

            VividBuiltinData BuildProbeVolumeBuiltinData(
                float3 positionWS,
                float3 normalWS)
            {
                float hasBakedGI = VividHasProbeVolumeGI() ? 1.0 : 0.0;
                float3 bakedGI = SampleVividProbeVolume(
                    positionWS,
                    normalWS,
                    GetWorldSpaceNormalizeViewDir(positionWS),
                    0xFFFFFFFFu);
                return CreateVividBuiltinData(
                    bakedGI,
                    hasBakedGI,
                    0.0,
                    float4(1.0, 1.0, 1.0, 1.0));
            }

            float3 ResolveSlabNormalWS(
                const VividEvaluatedSlabSurface surface,
                const VividSlabMaterialData slabData,
                const float3 positionWS,
                const float3 geometricNormalWS,
                const float2 rawUVDdx,
                const float2 rawUVDdy)
            {
                if (surface.HasNormal == 0u)
                    return geometricNormalWS;

                const float2 uvDdx = rawUVDdx * slabData.TextureTilingOffset.xy;
                const float2 uvDdy = rawUVDdy * slabData.TextureTilingOffset.xy;
                const float3x3 tangentToWorld = ReconstructTangentToWorld(
                    positionWS,
                    geometricNormalWS,
                    uvDdx,
                    uvDdy);
                return SafeNormalize(
                    surface.NormalTS.x * tangentToWorld[0]
                    + surface.NormalTS.y * tangentToWorld[1]
                    + surface.NormalTS.z * tangentToWorld[2]);
            }

            float3 ResolveAOTSlabNormalWS(
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
                const float3 bitangentWS = cross(normalWS, tangentWS)
                    * slab.TangentWS.w;
                return SafeNormalize(
                    slab.NormalTS.x * tangentWS
                    + slab.NormalTS.y * bitangentWS
                    + slab.NormalTS.z * normalWS);
            }

            VividEvaluatedSlabSurface ConvertAOTSlabSurface(
                const VividAOTSurfaceSlabValues surface)
            {
                VividEvaluatedSlabSurface result;
                result.BaseColor = surface.BaseColor.rgb;
                result.NormalTS = surface.NormalTS;
                result.PerceptualRoughness = surface.PerceptualRoughness;
                result.Metallic = surface.Metallic;
                result.AmbientOcclusion = surface.AmbientOcclusion;
                result.HasNormal = surface.HasNormal;
                return result;
            }

            VividExperimentalStandardSurface BuildStandardSurface(
                const VividEvaluatedSlabSurface surface,
                const float3 normalWS,
                const float3 emissive,
                const uint materialFeatures,
                const VividBuiltinData builtinData)
            {
                VividExperimentalStandardSurfaceParameters parameters;
                parameters.baseColor = surface.BaseColor;
                parameters.normalWS = normalWS;
                parameters.perceptualRoughness =
                    saturate(surface.PerceptualRoughness);
                parameters.metallic = surface.Metallic;
                parameters.ambientOcclusion = surface.AmbientOcclusion;
                parameters.coverage = 1.0;
                parameters.specularIor = 1.5;
                parameters.clearCoatWeight = 0.0;
                parameters.clearCoatPerceptualRoughness = 0.0;
                parameters.transmissionWeight = 0.0;
                parameters.subsurfaceWeight = 0.0;
                parameters.emissive = max(emissive, 0.0);
                parameters.materialFeatures = materialFeatures;
                parameters.builtinData = builtinData;
                return VividResolveExperimentalStandardSurface(parameters);
            }

            VividExperimentalClosureBufferOutput Frag(Varyings input)
            {
                float2 visibilityUV = ApplyScaleBias(
                    input.uv,
                    _ExperimentalVisibilityScaleBias);
                uint2 packedVisibility = asuint(SAMPLE_TEXTURE2D_LOD(
                    _ExperimentalVisibilityBuffer,
                    sampler_PointClamp,
                    visibilityUV,
                    0).xy);
                if (!IsPackedVisibilityBufferValueValid(packedVisibility))
                    discard;

                const VividVisibilityBufferValue visibility =
                    UnpackVisibilityBufferValue(packedVisibility);
                if (visibility.InstanceID >= _InstanceDataCount)
                    discard;

                const VividInstanceData instanceData =
                    PullInstanceData(visibility.InstanceID);

                VividMaterialData materialData = (VividMaterialData) 0;
                VividSurfaceBindingData surfaceBindingData =
                    (VividSurfaceBindingData) 0;
                VividSurfaceBindingData topSurfaceBindingData =
                    (VividSurfaceBindingData) 0;
                VividDualSlabMaterialData dualSlabMaterialData =
                    (VividDualSlabMaterialData) 0;
                VividMaterialRuntimeHeader runtimeHeader;
                VividMaterialProgramData programData;
                const uint programStatus = VividGetMaterialProgramStatus(
                    instanceData.MaterialIndex,
                    runtimeHeader,
                    programData);
                const bool usesLegacyMaterial = programStatus
                    == VIVID_MATERIAL_PROGRAM_LEGACY_FALLBACK;
                uint materialProgramID = VIVIDMATERIALPROGRAMID_INVALID;
                bool loadedMaterialProgram = false;
                bool materialProgramFailed = programStatus
                    == VIVID_MATERIAL_PROGRAM_KNOWN_FAILURE;
                bool isDualSlab = false;
                if (programStatus == VIVID_MATERIAL_PROGRAM_KNOWN)
                {
                    materialProgramID = runtimeHeader.ProgramID;
                    if (programData.SurfaceProgramID
                        == VIVIDMATERIALSURFACEPROGRAMID_DUAL_SLAB)
                    {
                        loadedMaterialProgram = VividTryLoadDualSlabSurfaceProgram(
                            instanceData.MaterialIndex,
                            0u,
                            dualSlabMaterialData,
                            surfaceBindingData,
                            topSurfaceBindingData);
                        isDualSlab = loadedMaterialProgram;
                    }
                    else if (programData.SurfaceProgramID
                        == VIVIDMATERIALSURFACEPROGRAMID_STANDARD_SINGLE_SLAB)
                    {
                        loadedMaterialProgram =
                            VividTryLoadStandardSingleSlabSurfaceProgram(
                                instanceData.MaterialIndex,
                                0u,
                                materialData,
                                surfaceBindingData);
                    }
                    materialProgramFailed = !loadedMaterialProgram;
                }
                else if (usesLegacyMaterial)
                {
                    if (instanceData.MaterialIndex >= _MaterialDataCount)
                        discard;

                    materialData = PullMaterialData(instanceData.MaterialIndex);
                    if (materialData.SurfaceBindingIndex >= _SurfaceBindingDataCount)
                        discard;

                    surfaceBindingData = PullSurfaceBindingData(
                        materialData.SurfaceBindingIndex);
                }

                if (usesLegacyMaterial
                    && (materialData.MaterialFlags & VIVIDMATERIALFLAGS_TERRAIN) != 0u)
                    discard;

                float4 attributes0 = SAMPLE_TEXTURE2D_LOD(
                    _ExperimentalVisibilityAttributes0,
                    sampler_PointClamp,
                    ApplyScaleBias(input.uv, _ExperimentalAttributes0ScaleBias),
                    0);
                float4 attributes1 = SAMPLE_TEXTURE2D_LOD(
                    _ExperimentalVisibilityAttributes1,
                    sampler_PointClamp,
                    ApplyScaleBias(input.uv, _ExperimentalAttributes1ScaleBias),
                    0);
                float deviceDepth = SAMPLE_TEXTURE2D_X_LOD(
                    _ExperimentalDepthTexture,
                    sampler_PointClamp,
                    ApplyScaleBias(input.uv, _ExperimentalDepthScaleBias),
                    0).r;
                float3 positionWS = ComputeWorldSpacePosition(
                    input.uv,
                    deviceDepth,
                    UNITY_MATRIX_I_VP);

                float2 rawUV = attributes0.xy;
                float2 rawUVDdx = attributes0.zw;
                float2 rawUVDdy = attributes1.xy;
                float3 geometricNormalWS = DecodeNormalOct(attributes1.zw);
                const float3x3 geometryTangentToWorld =
                    ReconstructTangentToWorld(
                        positionWS,
                        geometricNormalWS,
                        rawUVDdx,
                        rawUVDdy);
                const float geometryTangentSign = dot(
                    geometryTangentToWorld[1],
                    cross(geometricNormalWS, geometryTangentToWorld[0])) < 0.0f
                        ? -1.0f
                        : 1.0f;
                VividAOTSurfaceContext aotContext;
                aotContext.UV0 = rawUV;
                aotContext.UV0Ddx = rawUVDdx;
                aotContext.UV0Ddy = rawUVDdy;
                aotContext.GeometryNormalWS = geometricNormalWS;
                aotContext.GeometryTangentWS = float4(
                    geometryTangentToWorld[0],
                    geometryTangentSign);
                aotContext.PositionCS = input.positionCS;
                VividAOTSurfaceProgramOutput aotSurfaceOutput =
                    (VividAOTSurfaceProgramOutput) 0;
                bool dispatchedAOTSurface = false;
                if (materialProgramID != VIVIDMATERIALPROGRAMID_INVALID
                    && !materialProgramFailed)
                {
                    dispatchedAOTSurface = VividTryEvaluateAOTSurfaceProgram(
                        materialProgramID,
                        materialData,
                        dualSlabMaterialData,
                        surfaceBindingData,
                        topSurfaceBindingData,
                        aotContext,
                        aotSurfaceOutput);
                }
                const bool evaluatedAOTSingleSurface = dispatchedAOTSurface
                    && !isDualSlab
                    && aotSurfaceOutput.ClosureCount == 1u
                    && aotSurfaceOutput.LayerOperator == 0u;
                const bool evaluatedAOTDualSurface = dispatchedAOTSurface
                    && isDualSlab
                    && aotSurfaceOutput.ClosureCount == 2u
                    && (aotSurfaceOutput.LayerOperator == 1u
                        || aotSurfaceOutput.LayerOperator == 2u);
                const bool evaluatedAOTSurface = evaluatedAOTSingleSurface
                    || evaluatedAOTDualSurface;
                const bool failedAOTSurface = materialProgramFailed
                    || (materialProgramID != VIVIDMATERIALPROGRAMID_INVALID
                        && !evaluatedAOTSurface);

                bool isUnlit = false;
                if (loadedMaterialProgram)
                {
                    isUnlit =
                        (programData.CapabilityFlags
                            & VIVIDMATERIALPROGRAMCAPABILITIES_UNLIT) != 0u
                        && (runtimeHeader.Flags
                            & VIVIDMATERIALRUNTIMEFLAGS_UNLIT) != 0u;
                }
                else if (usesLegacyMaterial)
                {
                    isUnlit =
                        (materialData.MaterialFlags & VIVIDMATERIALFLAGS_UNLIT) != 0u;
                }
                uint materialFeatures = isUnlit
                    ? 0u
                    : VIVID_MATERIALFEATURE_DEFAULT;
                VividEvaluatedSlabSurface baseEvaluation =
                    (VividEvaluatedSlabSurface) 0;
                VividEvaluatedSlabSurface topEvaluation =
                    (VividEvaluatedSlabSurface) 0;
                float3 normalWS = geometricNormalWS;
                float3 emission = 0.0f;
                uint layerOperator = 0u;
                float layerWeight = 0.0f;
                bool resolveDualSurface = false;
                if (failedAOTSurface)
                {
                    baseEvaluation.BaseColor = float3(1.0f, 0.0f, 1.0f);
                    baseEvaluation.NormalTS = float3(0.0f, 0.0f, 1.0f);
                    baseEvaluation.PerceptualRoughness = 1.0f;
                    baseEvaluation.Metallic = 0.0f;
                    baseEvaluation.AmbientOcclusion = 1.0f;
                    baseEvaluation.HasNormal = 0u;
                    emission = float3(1.0f, 0.0f, 1.0f);
                    materialFeatures = 0u;
                }
                else if (evaluatedAOTSurface)
                {
                    baseEvaluation = ConvertAOTSlabSurface(
                        aotSurfaceOutput.BaseSlab);
                    normalWS = ResolveAOTSlabNormalWS(
                        aotSurfaceOutput.BaseSlab,
                        geometricNormalWS);
                    emission = aotSurfaceOutput.Emission;
                    if (evaluatedAOTDualSurface)
                    {
                        topEvaluation = ConvertAOTSlabSurface(
                            aotSurfaceOutput.TopSlab);
                        layerOperator = aotSurfaceOutput.LayerOperator;
                        layerWeight = aotSurfaceOutput.LayerWeight;
                        resolveDualSurface = true;
                    }
                }
                else
                {
                    const VividSlabMaterialData baseSlabData =
                        VividCreateSlabMaterialData(materialData);
                    baseEvaluation = VividEvaluateSlabSurfaceGrad(
                        baseSlabData,
                        surfaceBindingData,
                        rawUV,
                        rawUVDdx,
                        rawUVDdy,
                        input.positionCS);
                    normalWS = ResolveSlabNormalWS(
                        baseEvaluation,
                        baseSlabData,
                        positionWS,
                        geometricNormalWS,
                        rawUVDdx,
                        rawUVDdy);
                    emission = materialData.Emission.rgb;
                }

                VividBuiltinData builtinData = BuildProbeVolumeBuiltinData(
                    positionWS,
                    normalWS);
                VividExperimentalStandardSurface baseSurface =
                    BuildStandardSurface(
                        baseEvaluation,
                        normalWS,
                        emission,
                        materialFeatures,
                        builtinData);

                VividExperimentalClosureMaterial closureMaterial;
                if (resolveDualSurface)
                {
                    // Closure Buffer ABI v2 intentionally shares the base normal.
                    const VividExperimentalStandardSurface topSurface =
                        BuildStandardSurface(
                            topEvaluation,
                            normalWS,
                            0.0f,
                            materialFeatures,
                            builtinData);
                    closureMaterial = VividCompileExperimentalLayeredSurface(
                        baseSurface,
                        topSurface,
                        layerOperator,
                        layerWeight);
                }
                else
                {
                    closureMaterial =
                        VividCompileExperimentalStandardSurface(baseSurface);
                }

                return VividPackExperimentalClosureBuffer(closureMaterial);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
