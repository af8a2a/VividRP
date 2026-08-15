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
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividVisibilityBuffer.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividBarycentric.hlsl"

            TYPED_TEXTURE2D(float2, _VisibilityBuffer);
            TEXTURE2D(_DepthTexture);
            SAMPLER(sampler_DepthTexture);

            StructuredBuffer<VividMeshletVertex> _SharedVertexBuffer;
            ByteAddressBuffer _SharedIndexBuffer;

            float4 _VisibilityBufferScaleBias;
            float4 _DepthTextureScaleBias;

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
                VividMeshletVertex vertex0;
                VividMeshletVertex vertex1;
                VividMeshletVertex vertex2;
                float3 positionWS0;
                float3 positionWS1;
                float3 positionWS2;
                float4 clipPosition0;
                float4 clipPosition1;
                float4 clipPosition2;
            };

            float2 ApplyScaleBias(float2 uv, float4 scaleBias)
            {
                return uv * scaleBias.xy + scaleBias.zw;
            }

            bool IsSceneDepthValid(float sceneDepth)
            {
                #if UNITY_REVERSED_Z
                return sceneDepth > 1e-6f;
                #else
                return sceneDepth < 0.999999f;
                #endif
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

            float2 GetUV0(const VividMeshletVertex vertex)
            {
                return vertex.UV;
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

            InterpolatedUV InterpolateUV(
                const VividBarycentricDerivatives barycentric,
                const VividMeshletVertex vertex0,
                const VividMeshletVertex vertex1,
                const VividMeshletVertex vertex2)
            {
                const float3 u = InterpolateWithBarycentric(
                    barycentric,
                    GetUV0(vertex0).x,
                    GetUV0(vertex1).x,
                    GetUV0(vertex2).x);
                const float3 v = InterpolateWithBarycentric(
                    barycentric,
                    GetUV0(vertex0).y,
                    GetUV0(vertex1).y,
                    GetUV0(vertex2).y);

                InterpolatedUV result;
                result.uv = float2(u.x, v.x);
                result.ddx = float2(u.y, v.y);
                result.ddy = float2(u.z, v.z);
                return result;
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

            bool TryLoadVisibilityValue(
                Varyings input,
                out VividVisibilityBufferValue visibilityBufferValue,
                out float sceneDepth)
            {
                sceneDepth = 1.0f;
                float2 visibilityUv = ApplyScaleBias(input.uv, _VisibilityBufferScaleBias);
                uint2 packedValue = asuint(SAMPLE_TEXTURE2D_LOD(_VisibilityBuffer, sampler_PointClamp, visibilityUv, 0).xy);
                if (!IsPackedVisibilityBufferValueValid(packedValue))
                {
                    visibilityBufferValue = (VividVisibilityBufferValue) 0;
                    return false;
                }

                float2 depthUv = ApplyScaleBias(input.uv, _DepthTextureScaleBias);
                sceneDepth = SAMPLE_TEXTURE2D_LOD(_DepthTexture, sampler_PointClamp, depthUv, 0).r;
                if (!IsSceneDepthValid(sceneDepth))
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
                result.materialData = PullMaterialData(result.instanceData.MaterialIndex);
                result.surfaceBindingData = PullSurfaceBindingData(result.materialData.SurfaceBindingIndex);
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

                result.clipPosition0 = TransformWorldToHClip(result.positionWS0);
                result.clipPosition1 = TransformWorldToHClip(result.positionWS1);
                result.clipPosition2 = TransformWorldToHClip(result.positionWS2);
                return result;
            }

            float ResolveVisibilityDepth(
                const TriangleData triangleData,
                const VividBarycentricDerivatives barycentric)
            {
                float4 clipPosition = InterpolateWithBarycentricNoDerivatives(
                    barycentric,
                    triangleData.clipPosition0,
                    triangleData.clipPosition1,
                    triangleData.clipPosition2);

                return saturate(clipPosition.z / max(abs(clipPosition.w), 1e-6f));
            }

            bool IsVisibilitySampleVisible(float visibilityDepth, float sceneDepth)
            {
                float depthTolerance = max(1e-4f, fwidth(visibilityDepth) * 2.0f);
                return abs(visibilityDepth - sceneDepth) <= depthTolerance;
            }

            #define VIVID_MAX_TERRAIN_LAYERS 8u

            void ApplyMaskSample(
                const uint maskMode,
                const float4 maskSample,
                inout float perceptualRoughness,
                inout float metallic,
                inout float ambientOcclusion)
            {
                if (maskMode == 1u)
                {
                    metallic = maskSample.r;
                    perceptualRoughness = 1.0f - maskSample.a;
                }
                else if (maskMode == 2u)
                {
                    perceptualRoughness = maskSample.r;
                }
                else if (maskMode == 3u)
                {
                    metallic = maskSample.r;
                    ambientOcclusion = maskSample.g;
                    perceptualRoughness = 1.0f - maskSample.a;
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
                const float4 positionCS)
            {
                InterpolatedUV terrainUv = InterpolateUV(
                    barycentric,
                    triangleData.vertex0,
                    triangleData.vertex1,
                    triangleData.vertex2);

                float3 baseColor;
                float3 sampledNormalTS = float3(0.0f, 0.0f, 1.0f);
                bool hasSampledNormal = false;
                float perceptualRoughness;
                float metallic;
                float ambientOcclusion;
                bool isTerrain = (triangleData.materialData.MaterialFlags & VIVIDMATERIALFLAGS_TERRAIN) != 0u
                    && triangleData.materialData.Padding1 < _TerrainMaterialDataCount;

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

                    if (VividSurfaceHasMask(triangleData.surfaceBindingData))
                    {
                        ApplyMaskSample(
                            triangleData.materialData.Padding0,
                            VividSampleMaskGrad(
                                triangleData.surfaceBindingData,
                                surfaceSampleContext),
                            perceptualRoughness,
                            metallic,
                            ambientOcclusion);
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
                surfaceData.materialFeatures = VIVID_MATERIALFEATURE_DEFAULT;
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
                float sceneDepth;
                VividVisibilityBufferValue visibilityBufferValue;
                if (!TryLoadVisibilityValue(input, visibilityBufferValue, sceneDepth))
                {
                    discard;
                    return (VividGBufferFragmentOutput) 0;
                }

                TriangleData triangleData = LoadTriangleData(visibilityBufferValue);

                float2 pixelNdc = ScreenCoordsToNDC(input.positionCS);
                VividBarycentricDerivatives barycentric = CalculateFullBarycentric(
                    triangleData.clipPosition0,
                    triangleData.clipPosition1,
                    triangleData.clipPosition2,
                    pixelNdc,
                    _ScreenSize.zw
                );

                float visibilityDepth = ResolveVisibilityDepth(triangleData, barycentric);
                if (!IsVisibilitySampleVisible(visibilityDepth, sceneDepth))
                {
                    discard;
                    return (VividGBufferFragmentOutput) 0;
                }

                VividGBufferSurfaceData surfaceData = ResolveSurfaceData(triangleData, barycentric, input.positionCS);
                return PackVividGBufferSurfaceData(surfaceData);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
