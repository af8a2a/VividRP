Shader "Hidden/VividRP/GPUDriven/VisibilityBufferShadowCasterPass"
{
    Properties
    {
        [HideInInspector] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "VividRenderPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ShadowCaster"
            Tags
            {
                "LightMode" = "ShadowCaster"
            }

            ZWrite On
            ZTest LEqual
            Cull [_Cull]
            ColorMask 0

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON

            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividGPUDrivenCommon.hlsl"
            #define VIVID_GPU_DRIVEN_TEXTURE_BACKEND_BINDLESS 1
            #include_with_pragmas "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividSurfaceSampling.hlsl"

            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawArgs
            #include "UnityIndirect.cginc"

            StructuredBuffer<VividMeshletRenderRequestPacked> _VisibleMeshletRenderRequests;
            StructuredBuffer<VividMeshletVertex> _SharedVertexBuffer;
            ByteAddressBuffer _SharedIndexBuffer;
            float4 _ShadowBias;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                #ifdef _ALPHATEST_ON
                nointerpolation uint instanceIndex : TEXCOORD0;
                float2 uv0 : TEXCOORD1;
                #endif
            };

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

            float2 GetUV0(const VividMeshletVertex vertex, const VividMaterialData materialData)
            {
                return vertex.UV.xy * materialData.TextureTilingOffset.xy + materialData.TextureTilingOffset.zw;
            }

            float4 SampleAlbedo(
                const float2 uv,
                const VividMaterialData materialData,
                const VividSurfaceBindingData surfaceBindingData)
            {
                return materialData.AlbedoColor * VividSampleBaseColor(surfaceBindingData, uv);
            }

            float4 ApplyVividShadowClamping(float4 positionCS)
            {
            #if UNITY_REVERSED_Z
                float clampedZ = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
            #else
                float clampedZ = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
            #endif

                positionCS.z = lerp(positionCS.z, clampedZ, round(_ShadowBias.z) == 1.0 ? 1.0 : 0.0);
                return positionCS;
            }

            Varyings Vert(Attributes input)
            {
                InitIndirectDrawArgs(0);

                Varyings output;
                output.positionCS = float4(-2.0, -2.0, 0.0, 1.0);
                #ifdef _ALPHATEST_ON
                output.instanceIndex = 0u;
                output.uv0 = 0.0;
                #endif

                const uint instanceID = GetIndirectInstanceID_Base(input.instanceID);
                const uint vertexID = GetIndirectVertexID_Base(input.vertexID);
                const VividMeshletRenderRequestPacked renderRequest = _VisibleMeshletRenderRequests[instanceID];
                const VividInstanceData instanceData = PullInstanceData(renderRequest.InstanceID_LOD);
                const VividMaterialData materialData = PullMaterialData(instanceData.MaterialIndex);
                const VividMeshlet meshlet = PullMeshletData(renderRequest.MeshletID);
                const uint indexCount = meshlet.TriangleCount * 3u;

                if (vertexID >= indexCount)
                {
                    return output;
                }

                const uint vertexIndex = PullIndex(meshlet, vertexID);
                const VividMeshletVertex vertex = PullVertex(meshlet, vertexIndex);

                output.positionCS = TransformWorldToHClip(TransformPosition(instanceData.ObjectToWorldMatrix, vertex.Position.xyz));
                output.positionCS = ApplyVividShadowClamping(output.positionCS);

                #ifdef _ALPHATEST_ON
                output.instanceIndex = renderRequest.InstanceID_LOD;
                output.uv0 = GetUV0(vertex, materialData);
                #endif

                return output;
            }

            void Frag(Varyings input)
            {
                #ifdef _ALPHATEST_ON
                const VividInstanceData instanceData = PullInstanceData(input.instanceIndex);
                const VividMaterialData materialData = PullMaterialData(instanceData.MaterialIndex);
                const VividSurfaceBindingData surfaceBindingData = PullSurfaceBindingData(materialData.SurfaceBindingIndex);
                const float4 albedo = SampleAlbedo(input.uv0, materialData, surfaceBindingData);
                clip(albedo.a - materialData.AlphaClipThreshold);
                #endif
            }
            ENDHLSL
        }
    }

    FallBack Off
}
