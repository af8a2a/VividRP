Shader "Hidden/VividRP/GPUDriven/VisibilityBufferPass"
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
            Name "VisibilityBuffer"
            ZWrite On
            ZTest LEqual
            Cull [_Cull]

            HLSLPROGRAM
            
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma use_dxc
            #pragma require barycentrics
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_local_fragment _ VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE

            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividGPUDrivenCommon.hlsl"
            #if !defined(VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE)
                #define VIVID_GPU_DRIVEN_TEXTURE_BACKEND_BINDLESS 1
            #endif
            #include_with_pragmas "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividSurfaceSampling.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividVisibilityBuffer.hlsl"

            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawArgs
            #include "UnityIndirect.cginc"

            StructuredBuffer<VividMeshletRenderRequestPacked> _VisibleMeshletRenderRequests;
            StructuredBuffer<VividMeshletVertex> _SharedVertexBuffer;
            ByteAddressBuffer _SharedIndexBuffer;


            struct Attributes
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                nointerpolation uint2 visibilityValue : TEXCOORD0;
                float2 uv0 : TEXCOORD1;
                float3 geometricNormalWS : TEXCOORD2;
            };

            uint PullIndex(const VividDecodedMeshlet meshlet, const uint indexID)
            {
                const uint absoluteIndexID = meshlet.TriangleOffset + indexID;
                const uint packedIndices = _SharedIndexBuffer.Load((absoluteIndexID / 4u) * 4u);
                const uint shiftAmount = (absoluteIndexID % 4u) * 8u;
                return (packedIndices >> shiftAmount) & 0xFFu;
            }

            VividDecodedMeshletVertex PullVertex(const VividDecodedMeshlet meshlet, const uint index)
            {
                return DecodeVividMeshletVertex(_SharedVertexBuffer[meshlet.VertexOffset + index]);
            }

            float2 GetUV0(const float2 uv0, const VividMaterialData materialData)
            {
                return uv0 * materialData.TextureTilingOffset.xy + materialData.TextureTilingOffset.zw;
            }

            float4 SampleAlbedo(
                const float2 uv,
                const VividMaterialData materialData,
                const VividSurfaceBindingData surfaceBindingData)
            {
                return materialData.AlbedoColor * VividSampleBaseColor(surfaceBindingData, uv);
            }

            Varyings Vert(Attributes input)
            {
                InitIndirectDrawArgs(0);

                Varyings output;
                output.positionCS = float4(-2.0, -2.0, 0.0, 1.0);
                output.visibilityValue = 0u;
                output.uv0 = 0.0;
                output.geometricNormalWS = float3(0.0, 0.0, 1.0);

                const uint instanceID = GetIndirectInstanceID_Base(input.instanceID);
                const uint vertexID = GetIndirectVertexID_Base(input.vertexID);
                const VividMeshletRenderRequestPacked renderRequest = _VisibleMeshletRenderRequests[instanceID];
                const VividInstanceData instanceData = PullInstanceData(renderRequest.InstanceID_LOD);
                const VividMaterialData materialData = PullMaterialData(instanceData.MaterialIndex);
                const VividDecodedMeshlet meshlet = PullMeshletData(renderRequest.MeshletID);
                const uint indexCount = meshlet.TriangleCount * 3u;

                if (vertexID >= indexCount)
                {
                    return output;
                }

                const uint vertexIndex = PullIndex(meshlet, vertexID);
                const VividDecodedMeshletVertex vertex = PullVertex(meshlet, vertexIndex);

                output.positionCS = TransformWorldToHClip(TransformPosition(instanceData.ObjectToWorldMatrix, vertex.Position.xyz));

                VividVisibilityBufferValue visibilityBufferValue;
                visibilityBufferValue.InstanceID = renderRequest.InstanceID_LOD;
                visibilityBufferValue.MeshletID = renderRequest.MeshletID;
                visibilityBufferValue.IndexID = vertexID;
                output.visibilityValue = PackVisibilityBufferValue(visibilityBufferValue);
                output.uv0 = vertex.UV.xy;
                output.geometricNormalWS = SafeNormalize(
                    mul(vertex.Normal.xyz, (float3x3)instanceData.WorldToObjectMatrix));

                return output;
            }
            // [earlydepthstencil]
            VividVisibilityBufferFragmentOutput Frag(
                Varyings input,
                linear float3 barycentrics : SV_Barycentrics)
            {
                #ifdef _ALPHATEST_ON
                const VividVisibilityBufferValue visibilityBufferValue = UnpackVisibilityBufferValue(input.visibilityValue);
                const VividInstanceData instanceData = PullInstanceData(visibilityBufferValue.InstanceID);
                const VividMaterialData materialData = PullMaterialData(instanceData.MaterialIndex);
                const VividSurfaceBindingData surfaceBindingData = PullSurfaceBindingData(materialData.SurfaceBindingIndex);
                const float4 albedo = SampleAlbedo(
                    GetUV0(input.uv0, materialData),
                    materialData,
                    surfaceBindingData);
                clip(albedo.a - materialData.AlphaClipThreshold);
                #endif

                return PackVividVisibilityBufferFragmentOutput(
                    input.visibilityValue,
                    input.uv0,
                    input.geometricNormalWS,
                    barycentrics);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
