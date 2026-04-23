Shader "Hidden/VividRP/GPUDriven/VisibilityBufferResolve"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "VividRenderPipeline"
        }

        Pass
        {
            Name "VisibilityBufferResolve"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/GPUDriven/VividGPUDrivenCommon.hlsl"
            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/GPUDriven/VividVisibilityBuffer.hlsl"
            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/GPUDriven/VividBarycentric.hlsl"

            #define VIVID_VISIBILITY_RESOLVE_DEBUG_INSTANCE_ID 0
            #define VIVID_VISIBILITY_RESOLVE_DEBUG_MESHLET_ID 1
            #define VIVID_VISIBILITY_RESOLVE_DEBUG_TRIANGLE_ID 2
            #define VIVID_VISIBILITY_RESOLVE_DEBUG_WIREFRAME 3
            #define VIVID_VISIBILITY_RESOLVE_DEBUG_BARYCENTRIC 4

            TYPED_TEXTURE2D(float2, _VisibilityBuffer);
            TEXTURE2D(_DepthTexture);
            SAMPLER(sampler_DepthTexture);

            StructuredBuffer<VividMeshletVertex> _SharedVertexBuffer;
            ByteAddressBuffer _SharedIndexBuffer;

            float4 _VisibilityBufferScaleBias;
            float4 _DepthTextureScaleBias;
            int _ResolveDebugMode;
            float _ResolveExposure;
            float _WireframeThickness;

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

            bool IsSceneDepthValid(float sceneDepth)
            {
                #if UNITY_REVERSED_Z
                return sceneDepth > 1e-6f;
                #else
                return sceneDepth < 0.999999f;
                #endif
            }

            float ResolveVisibilityDepth(
                const float4 clipPosition0,
                const float4 clipPosition1,
                const float4 clipPosition2,
                const VividBarycentricDerivatives barycentric)
            {
                float4 clipPosition = InterpolateWithBarycentricNoDerivatives(
                    barycentric,
                    clipPosition0,
                    clipPosition1,
                    clipPosition2);

                return saturate(clipPosition.z / max(abs(clipPosition.w), 1e-6f));
            }

            bool IsVisibilitySampleVisible(float visibilityDepth, float sceneDepth)
            {
                float depthTolerance = max(1e-4f, fwidth(visibilityDepth) * 2.0f);
                return abs(visibilityDepth - sceneDepth) <= depthTolerance;
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

            float3 HashColor(uint seed)
            {
                float seedValue = (float) (seed + 1u);
                float3 value = float3(seedValue, seedValue + 17.0, seedValue + 37.0);
                value = frac(sin(value * float3(12.9898, 78.233, 39.425)) * 43758.5453);
                return saturate(0.25 + value * 0.75);
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

            struct TriangleData
            {
                VividInstanceData instanceData;
                VividMeshlet meshlet;
                uint3 indices;
                VividMeshletVertex vertex0;
                VividMeshletVertex vertex1;
                VividMeshletVertex vertex2;
                float4 clipPosition0;
                float4 clipPosition1;
                float4 clipPosition2;
            };

            TriangleData LoadTriangleData(VividVisibilityBufferValue visibilityBufferValue)
            {
                TriangleData result;
                result.instanceData = PullInstanceData(visibilityBufferValue.InstanceID);
                result.meshlet = PullMeshletData(visibilityBufferValue.MeshletID);

                result.indices = uint3(
                    PullIndex(result.meshlet, visibilityBufferValue.IndexID + 0u),
                    PullIndex(result.meshlet, visibilityBufferValue.IndexID + 1u),
                    PullIndex(result.meshlet, visibilityBufferValue.IndexID + 2u)
                );

                result.vertex0 = PullVertex(result.meshlet, result.indices.x);
                result.vertex1 = PullVertex(result.meshlet, result.indices.y);
                result.vertex2 = PullVertex(result.meshlet, result.indices.z);

                result.clipPosition0 = TransformWorldToHClip(
                    TransformPosition(result.instanceData.ObjectToWorldMatrix, result.vertex0.Position.xyz));
                result.clipPosition1 = TransformWorldToHClip(
                    TransformPosition(result.instanceData.ObjectToWorldMatrix, result.vertex1.Position.xyz));
                result.clipPosition2 = TransformWorldToHClip(
                    TransformPosition(result.instanceData.ObjectToWorldMatrix, result.vertex2.Position.xyz));
                return result;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float sceneDepth;
                VividVisibilityBufferValue visibilityBufferValue;
                if (!TryLoadVisibilityValue(input, visibilityBufferValue, sceneDepth))
                    return 0.0f;

                TriangleData triangleData = LoadTriangleData(visibilityBufferValue);

                float2 pixelNdc = ScreenCoordsToNDC(input.positionCS);
                VividBarycentricDerivatives barycentric = CalculateFullBarycentric(
                    triangleData.clipPosition0,
                    triangleData.clipPosition1,
                    triangleData.clipPosition2,
                    pixelNdc,
                    _ScreenSize.zw
                );

                float visibilityDepth = ResolveVisibilityDepth(
                    triangleData.clipPosition0,
                    triangleData.clipPosition1,
                    triangleData.clipPosition2,
                    barycentric);
                if (!IsVisibilitySampleVisible(visibilityDepth, sceneDepth))
                    return 0.0f;

                float exposureMultiplier = exp2(_ResolveExposure);

                if (_ResolveDebugMode == VIVID_VISIBILITY_RESOLVE_DEBUG_INSTANCE_ID)
                    return float4(HashColor(visibilityBufferValue.InstanceID) * exposureMultiplier, 1.0f);

                if (_ResolveDebugMode == VIVID_VISIBILITY_RESOLVE_DEBUG_MESHLET_ID)
                    return float4(HashColor(visibilityBufferValue.MeshletID) * exposureMultiplier, 1.0f);

                uint triangleID = visibilityBufferValue.IndexID / 3u;
                if (_ResolveDebugMode == VIVID_VISIBILITY_RESOLVE_DEBUG_TRIANGLE_ID)
                    return float4(HashColor(triangleID) * exposureMultiplier, 1.0f);

                if (_ResolveDebugMode == VIVID_VISIBILITY_RESOLVE_DEBUG_BARYCENTRIC)
                    return float4(saturate(barycentric.lambda) * exposureMultiplier, 1.0f);

                float baryMinValue = min(barycentric.lambda.x, min(barycentric.lambda.y, barycentric.lambda.z));
                float threshold = _ScreenSize.z * _WireframeThickness;
                float wireBlend = smoothstep(threshold, threshold + 0.01f, baryMinValue);
                float3 faceColor = HashColor(visibilityBufferValue.MeshletID) * exposureMultiplier;
                return float4(lerp(1.0f.xxx, faceColor, wireBlend), 1.0f);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
