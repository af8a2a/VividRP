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
            #pragma target 5.0
            #pragma require randomwrite
            #pragma use_dxc

            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividProbeVolume.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividGPUDrivenCommon.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/Closure/ExperimentalClosureBuffer.hlsl"
            #define VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE 1
            #define VIVID_VT_ENABLE_FEEDBACK_RW 1
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VirtualTextureSurfaceSampling.hlsl"

            #define VIVID_EXPERIMENTAL_VBUFFER_VERSION 2u
            #define VIVID_EXPERIMENTAL_VBUFFER_MATERIAL_OFFSET 1u
            #define VIVID_EXPERIMENTAL_VBUFFER_MATERIAL_STRIDE 192u

            #define VIVID_EXPERIMENTAL_FEATURE_NORMAL_MAP (1u << 0)
            #define VIVID_EXPERIMENTAL_FEATURE_METALLIC_MAP (1u << 1)
            #define VIVID_EXPERIMENTAL_FEATURE_ROUGHNESS_MAP (1u << 2)
            #define VIVID_EXPERIMENTAL_FEATURE_SMOOTHNESS_ALBEDO_ALPHA (1u << 3)
            #define VIVID_EXPERIMENTAL_FEATURE_OCCLUSION_MAP (1u << 4)
            #define VIVID_EXPERIMENTAL_FEATURE_EMISSION_MAP (1u << 5)
            #define VIVID_EXPERIMENTAL_FEATURE_CLEAR_COAT (1u << 6)
            #define VIVID_EXPERIMENTAL_FEATURE_RECEIVE_SSR (1u << 7)
            #define VIVID_EXPERIMENTAL_FEATURE_RECEIVE_DECALS (1u << 8)
            #define VIVID_EXPERIMENTAL_FEATURE_RMO_MAP (1u << 9)

            TYPED_TEXTURE2D(float2, _ExperimentalVisibilityBuffer);
            TEXTURE2D(_ExperimentalVisibilityAttributes0);
            TEXTURE2D(_ExperimentalVisibilityAttributes1);
            TEXTURE2D_X_FLOAT(_ExperimentalDepthTexture);

            float4 _ExperimentalVisibilityScaleBias;
            float4 _ExperimentalAttributes0ScaleBias;
            float4 _ExperimentalAttributes1ScaleBias;
            float4 _ExperimentalDepthScaleBias;
            uint _VividExperimentalVBufferMaterialCount;
            uint _VividExperimentalVBufferVTAvailable;

            struct VividExperimentalVBufferMaterialData
            {
                VividSurfaceBindingData BaseBinding;
                VividSurfaceBindingData AuxiliaryBinding;
                float4 BaseColor;
                float4 BaseMapST;
                float4 EmissionColor;
                float4 BaseSurface;
                float4 BaseRemap0;
                float4 BaseRemap1;
                float4 BaseClosure;
                uint4 FeatureFlags;
            };

            StructuredBuffer<VividExperimentalVBufferMaterialData>
                _VividExperimentalVBufferMaterials;

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

            uint BuildMaterialFeatures(uint featureFlags, float clearCoatWeight)
            {
                uint materialFeatures = VIVID_MATERIALFEATURE_LIT;
                if ((featureFlags & VIVID_EXPERIMENTAL_FEATURE_RECEIVE_SSR) != 0u)
                    materialFeatures |= VIVID_MATERIALFEATURE_SSR_RECEIVE;
                if ((featureFlags & VIVID_EXPERIMENTAL_FEATURE_RECEIVE_DECALS) != 0u)
                    materialFeatures |= VIVID_MATERIALFEATURE_DECAL_RECEIVE;
                if (clearCoatWeight > 0.0)
                    materialFeatures |= VIVID_MATERIALFEATURE_CLEAR_COAT;
                return materialFeatures;
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
                uint2 visibility = asuint(SAMPLE_TEXTURE2D_LOD(
                    _ExperimentalVisibilityBuffer,
                    sampler_PointClamp,
                    visibilityUV,
                    0).xy);
                if (visibility.x == 0u)
                    discard;

                uint materialSlot = visibility.x - VIVID_EXPERIMENTAL_VBUFFER_MATERIAL_OFFSET;
                if (_VividExperimentalVBufferVTAvailable == 0u
                    || materialSlot >= _VividExperimentalVBufferMaterialCount)
                {
                    materialSlot = 0u;
                }

                VividExperimentalVBufferMaterialData materialData =
                    _VividExperimentalVBufferMaterials[materialSlot];
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
                float2 baseUV = rawUV * materialData.BaseMapST.xy
                    + materialData.BaseMapST.zw;
                float2 baseUVDdx = rawUVDdx * materialData.BaseMapST.xy;
                float2 baseUVDdy = rawUVDdy * materialData.BaseMapST.xy;
                VividSurfaceSampleContext baseContext =
                    VividCreateSurfaceSampleContextGrad(
                        materialData.BaseBinding,
                        baseUV,
                        baseUVDdx,
                        baseUVDdy,
                        input.positionCS);
                float4 baseSample = VividSampleBaseColorGrad(
                    materialData.BaseBinding,
                    baseContext) * materialData.BaseColor;
                float4 baseMask = VividSampleMaskGrad(
                    materialData.BaseBinding,
                    baseContext);

                uint featureFlags = materialData.FeatureFlags.x;
                float metallic = saturate(materialData.BaseSurface.x);
                float smoothness = saturate(materialData.BaseSurface.y);
                float ambientOcclusion = 1.0;
                if ((featureFlags & VIVID_EXPERIMENTAL_FEATURE_RMO_MAP) != 0u)
                {
                    smoothness = lerp(
                        materialData.BaseRemap0.z,
                        materialData.BaseRemap0.w,
                        saturate(1.0 - baseMask.r));
                    metallic = lerp(
                        materialData.BaseRemap0.x,
                        materialData.BaseRemap0.y,
                        saturate(baseMask.g));
                    ambientOcclusion = saturate(lerp(
                        materialData.BaseRemap1.x,
                        materialData.BaseRemap1.y,
                        baseMask.b));
                }
                else if ((featureFlags & VIVID_EXPERIMENTAL_FEATURE_METALLIC_MAP) != 0u)
                {
                    metallic = lerp(
                        materialData.BaseRemap0.x,
                        materialData.BaseRemap0.y,
                        saturate(baseMask.r));
                    float smoothnessSource =
                        (featureFlags & VIVID_EXPERIMENTAL_FEATURE_SMOOTHNESS_ALBEDO_ALPHA) != 0u
                            ? baseSample.a
                            : baseMask.a;
                    smoothness = lerp(
                        materialData.BaseRemap0.z,
                        materialData.BaseRemap0.w,
                        saturate(smoothnessSource));
                }
                else if ((featureFlags & VIVID_EXPERIMENTAL_FEATURE_ROUGHNESS_MAP) != 0u)
                {
                    smoothness = lerp(
                        materialData.BaseRemap0.z,
                        materialData.BaseRemap0.w,
                        saturate(1.0 - baseMask.r));
                }
                else if ((featureFlags & VIVID_EXPERIMENTAL_FEATURE_SMOOTHNESS_ALBEDO_ALPHA) != 0u)
                {
                    smoothness = lerp(
                        materialData.BaseRemap0.z,
                        materialData.BaseRemap0.w,
                        saturate(baseSample.a));
                }

                float3 emissive = 0.0;
                if (materialData.AuxiliaryBinding.Flags != 0u)
                {
                    VividSurfaceSampleContext auxiliaryContext =
                        VividCreateSurfaceSampleContextGrad(
                            materialData.AuxiliaryBinding,
                            baseUV,
                            baseUVDdx,
                            baseUVDdy,
                            input.positionCS);
                    if ((featureFlags & VIVID_EXPERIMENTAL_FEATURE_OCCLUSION_MAP) != 0u)
                    {
                        float occlusion = VividSampleMaskGrad(
                            materialData.AuxiliaryBinding,
                            auxiliaryContext).g;
                        ambientOcclusion = saturate(lerp(
                            materialData.BaseRemap1.x,
                            materialData.BaseRemap1.y,
                            occlusion));
                    }
                    if ((featureFlags & VIVID_EXPERIMENTAL_FEATURE_EMISSION_MAP) != 0u)
                    {
                        emissive = max(
                            VividSampleBaseColorGrad(
                                materialData.AuxiliaryBinding,
                                auxiliaryContext).rgb
                            * materialData.EmissionColor.rgb,
                            0.0);
                    }
                }

                float3 normalWS = geometricNormalWS;
                if ((featureFlags & VIVID_EXPERIMENTAL_FEATURE_NORMAL_MAP) != 0u)
                {
                    float3 normalTS = VividExperimentalUnpackNormalScale(
                        VividSampleNormalGrad(materialData.BaseBinding, baseContext),
                        materialData.BaseSurface.z);
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

                float clearCoatWeight =
                    (featureFlags & VIVID_EXPERIMENTAL_FEATURE_CLEAR_COAT) != 0u
                        ? saturate(materialData.BaseRemap1.w)
                        : 0.0;
                VividBuiltinData builtinData = BuildProbeVolumeBuiltinData(
                    positionWS,
                    normalWS);
                VividExperimentalStandardSurfaceParameters baseParameters;
                baseParameters.baseColor = baseSample.rgb;
                baseParameters.normalWS = normalWS;
                baseParameters.perceptualRoughness = 1.0 - saturate(smoothness);
                baseParameters.metallic = metallic;
                baseParameters.ambientOcclusion = ambientOcclusion;
                baseParameters.coverage = 1.0;
                baseParameters.specularIor = materialData.BaseRemap1.z;
                baseParameters.clearCoatWeight = clearCoatWeight;
                baseParameters.clearCoatPerceptualRoughness =
                    1.0 - saturate(materialData.BaseClosure.x);
                baseParameters.transmissionWeight = materialData.BaseClosure.y;
                baseParameters.subsurfaceWeight = materialData.BaseClosure.z;
                baseParameters.emissive = emissive;
                baseParameters.materialFeatures = BuildMaterialFeatures(
                    featureFlags,
                    clearCoatWeight);
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
