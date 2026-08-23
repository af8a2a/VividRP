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

            float3 VividExperimentalUnpackNormalScale(float4 packedNormal, float scale)
            {
                float3 normalTS;
                normalTS.xy = packedNormal.wy * 2.0 - 1.0;
                normalTS.xy *= scale;
                normalTS.z = sqrt(saturate(1.0 - dot(normalTS.xy, normalTS.xy)));
                return normalTS;
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
                    perceptualRoughness = 1.0
                        - RemapPBRChannel(maskSample.a, metallicSmoothnessRemap.zw);
                }
                else if (maskMode == 2u)
                {
                    perceptualRoughness = 1.0
                        - RemapPBRChannel(1.0 - maskSample.r, metallicSmoothnessRemap.zw);
                }
                else if (maskMode == 3u)
                {
                    metallic = RemapPBRChannel(maskSample.r, metallicSmoothnessRemap.xy);
                    ambientOcclusion = RemapPBRChannel(
                        maskSample.g,
                        ambientOcclusionRemap);
                    perceptualRoughness = 1.0
                        - RemapPBRChannel(maskSample.a, metallicSmoothnessRemap.zw);
                }
                else if (maskMode == 4u)
                {
                    perceptualRoughness = 1.0
                        - RemapPBRChannel(1.0 - maskSample.r, metallicSmoothnessRemap.zw);
                    metallic = RemapPBRChannel(maskSample.g, metallicSmoothnessRemap.xy);
                    ambientOcclusion = RemapPBRChannel(
                        maskSample.b,
                        ambientOcclusionRemap);
                }
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
                VividMaterialData materialData;
                VividSurfaceBindingData surfaceBindingData;
                if (!VividTryLoadStandardSingleSlabSurfaceProgram(
                        instanceData.MaterialIndex,
                        0u,
                        materialData,
                        surfaceBindingData))
                {
                    if (instanceData.MaterialIndex >= _MaterialDataCount)
                        discard;

                    materialData = PullMaterialData(instanceData.MaterialIndex);
                    if (materialData.SurfaceBindingIndex >= _SurfaceBindingDataCount)
                        discard;

                    surfaceBindingData = PullSurfaceBindingData(
                        materialData.SurfaceBindingIndex);
                }

                if ((materialData.MaterialFlags & VIVIDMATERIALFLAGS_TERRAIN) != 0u)
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
                float2 baseUV = rawUV * materialData.TextureTilingOffset.xy
                    + materialData.TextureTilingOffset.zw;
                float2 baseUVDdx = rawUVDdx * materialData.TextureTilingOffset.xy;
                float2 baseUVDdy = rawUVDdy * materialData.TextureTilingOffset.xy;
                VividSurfaceSampleContext baseContext =
                    VividCreateSurfaceSampleContextGrad(
                        surfaceBindingData,
                        baseUV,
                        baseUVDdx,
                        baseUVDdy,
                        input.positionCS);
                float4 baseSample = VividSampleBaseColorGrad(
                    surfaceBindingData,
                    baseContext) * materialData.AlbedoColor;
                float perceptualRoughness = materialData.Roughness;
                float metallic = materialData.Metallic;
                float ambientOcclusion = 1.0;
                if (VividSurfaceHasMask(surfaceBindingData))
                {
                    ApplyMaskSample(
                        materialData.Padding0,
                        VividSampleMaskGrad(surfaceBindingData, baseContext),
                        materialData.MetallicSmoothnessRemap,
                        materialData.AmbientOcclusionRemap.xy,
                        perceptualRoughness,
                        metallic,
                        ambientOcclusion);
                }

                float3 normalWS = geometricNormalWS;
                if (VividSurfaceHasNormal(surfaceBindingData))
                {
                    float3 normalTS = VividExperimentalUnpackNormalScale(
                        VividSampleNormalGrad(surfaceBindingData, baseContext),
                        materialData.NormalsStrength);
                    float3x3 tangentToWorld = ReconstructTangentToWorld(
                        positionWS,
                        geometricNormalWS,
                        baseUVDdx,
                        baseUVDdy);
                    normalWS = SafeNormalize(
                        normalTS.x * tangentToWorld[0]
                        + normalTS.y * tangentToWorld[1]
                        + normalTS.z * tangentToWorld[2]);
                }

                VividBuiltinData builtinData = BuildProbeVolumeBuiltinData(
                    positionWS,
                    normalWS);
                VividExperimentalStandardSurfaceParameters baseParameters;
                baseParameters.baseColor = baseSample.rgb;
                baseParameters.normalWS = normalWS;
                baseParameters.perceptualRoughness = saturate(perceptualRoughness);
                baseParameters.metallic = metallic;
                baseParameters.ambientOcclusion = ambientOcclusion;
                baseParameters.coverage = 1.0;
                baseParameters.specularIor = 1.5;
                baseParameters.clearCoatWeight = 0.0;
                baseParameters.clearCoatPerceptualRoughness = 0.0;
                baseParameters.transmissionWeight = 0.0;
                baseParameters.subsurfaceWeight = 0.0;
                baseParameters.emissive = max(materialData.Emission.rgb, 0.0);
                baseParameters.materialFeatures =
                    (materialData.MaterialFlags & VIVIDMATERIALFLAGS_UNLIT) != 0u
                        ? 0u
                        : VIVID_MATERIALFEATURE_DEFAULT;
                baseParameters.builtinData = builtinData;
                VividExperimentalStandardSurface baseSurface =
                    VividResolveExperimentalStandardSurface(baseParameters);

                VividExperimentalClosureMaterial closureMaterial =
                    VividCompileExperimentalStandardSurface(baseSurface);

                return VividPackExperimentalClosureBuffer(closureMaterial);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
