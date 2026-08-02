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
            #pragma use_dxc 
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GBuffer.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividProbeVolume.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividGPUDrivenCommon.hlsl"
            #define VIVID_GPU_DRIVEN_TEXTURE_BACKEND_BINDLESS 1
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
                VividMeshlet meshlet;
                uint3 indices;
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

            uint PullIndex(const VividMeshlet meshlet, const uint indexID)
            {
                const uint absoluteIndexID = meshlet.TriangleOffset + indexID;
                const uint packedIndices = _SharedIndexBuffer.Load((absoluteIndexID / 4u) * 4u);
                const uint shiftAmount = (absoluteIndexID % 4u) * 8u;
                return (packedIndices >> shiftAmount) & 0xFFu;
            }

            VividMeshletVertex PullVertex(const VividMeshlet meshlet, const uint index)
            {
                return _SharedVertexBuffer[meshlet.VertexOffset + index];
            }

            float2 GetUV0(const VividMeshletVertex vertex)
            {
                return vertex.UV.xy;
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
                const InterpolatedUV uv,
                const VividSurfaceBindingData surfaceBindingData)
            {
                return VividSampleBaseColorGrad(surfaceBindingData, uv.uv, uv.ddx, uv.ddy);
            }

            float3 SampleNormalTSGrad(
                const InterpolatedUV uv,
                const VividMaterialData materialData,
                const VividSurfaceBindingData surfaceBindingData)
            {
                float4 packedNormal = VividSampleNormalGrad(surfaceBindingData, uv.uv, uv.ddx, uv.ddy);
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
                result.meshlet = PullMeshletData(visibilityBufferValue.MeshletID);

                result.indices = uint3(
                    PullIndex(result.meshlet, visibilityBufferValue.IndexID + 0u),
                    PullIndex(result.meshlet, visibilityBufferValue.IndexID + 1u),
                    PullIndex(result.meshlet, visibilityBufferValue.IndexID + 2u)
                );

                result.vertex0 = PullVertex(result.meshlet, result.indices.x);
                result.vertex1 = PullVertex(result.meshlet, result.indices.y);
                result.vertex2 = PullVertex(result.meshlet, result.indices.z);

                result.positionWS0 = TransformPosition(result.instanceData.ObjectToWorldMatrix, result.vertex0.Position.xyz);
                result.positionWS1 = TransformPosition(result.instanceData.ObjectToWorldMatrix, result.vertex1.Position.xyz);
                result.positionWS2 = TransformPosition(result.instanceData.ObjectToWorldMatrix, result.vertex2.Position.xyz);

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

            VividGBufferSurfaceData ResolveSurfaceData(
                const TriangleData triangleData,
                const VividBarycentricDerivatives barycentric)
            {
                InterpolatedUV uv = InterpolateUV(
                    barycentric,
                    triangleData.vertex0,
                    triangleData.vertex1,
                    triangleData.vertex2);
                ApplyTilingOffset(uv, triangleData.materialData.TextureTilingOffset);

                float4 albedoSample = SampleAlbedoTextureGrad(uv, triangleData.surfaceBindingData);
                float3 baseColor = albedoSample.rgb * triangleData.materialData.AlbedoColor.rgb;

                const float normalFlipSign = ComputeDoubleSidedNormalFlipSign(triangleData);
                const float3 vertexNormalWS0 = normalFlipSign * TransformInstanceObjectToWorldNormal(
                    SafeNormalize(triangleData.vertex0.Normal.xyz),
                    triangleData.instanceData.WorldToObjectMatrix);
                const float3 vertexNormalWS1 = normalFlipSign * TransformInstanceObjectToWorldNormal(
                    SafeNormalize(triangleData.vertex1.Normal.xyz),
                    triangleData.instanceData.WorldToObjectMatrix);
                const float3 vertexNormalWS2 = normalFlipSign * TransformInstanceObjectToWorldNormal(
                    SafeNormalize(triangleData.vertex2.Normal.xyz),
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
                if (VividSurfaceHasNormal(triangleData.surfaceBindingData))
                {
                    float4 tangentOS = InterpolateWithBarycentricNoDerivatives(
                        barycentric,
                        triangleData.vertex0.Tangent,
                        triangleData.vertex1.Tangent,
                        triangleData.vertex2.Tangent);
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
                        float3 normalTS = SampleNormalTSGrad(
                            uv,
                            triangleData.materialData,
                            triangleData.surfaceBindingData);
                        normalWS = TransformTangentToWorld(normalTS, tangentToWorld, true);
                    }
                }

                VividGBufferSurfaceData surfaceData;
                surfaceData.baseColor = baseColor;
                surfaceData.normalWS = normalWS;
                surfaceData.linearRoughness = triangleData.materialData.Roughness * triangleData.materialData.Roughness;
                surfaceData.metallic = triangleData.materialData.Metallic;
                surfaceData.ambientOcclusion = 1.0f;
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

                VividGBufferSurfaceData surfaceData = ResolveSurfaceData(triangleData, barycentric);
                return PackVividGBufferSurfaceData(surfaceData);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
