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
            #pragma shader_feature_local_fragment _ALPHATEST_ON

            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
            #include_with_pragmas  "Packages/com.af8a2a.vividrp/Shaders/Core/Public/GPUDriven/Bindless.hlsl"
            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/GPUDriven/VividGPUDrivenCommon.hlsl"
            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/GPUDriven/VividVisibilityBuffer.hlsl"

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
                #ifdef _ALPHATEST_ON
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

            float4 SampleAlbedo(const float2 uv, const VividMaterialData materialData)
            {
                float4 textureAlbedo = float4(1.0, 1.0, 1.0, 1.0);

                UNITY_BRANCH
                if (materialData.AlbedoIndex != 0xffffffffu)
                {
                    Texture2D texture = GetBindlessTexture2D(NonUniformResourceIndex(materialData.AlbedoIndex));
                    textureAlbedo = SAMPLE_TEXTURE2D(texture, sampler_LinearRepeat, uv);
                }

                return materialData.AlbedoColor * textureAlbedo;
            }

            Varyings Vert(Attributes input)
            {
                InitIndirectDrawArgs(0);

                Varyings output;
                output.positionCS = float4(-2.0, -2.0, 0.0, 1.0);
                output.visibilityValue = 0u;
                #ifdef _ALPHATEST_ON
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

                VividVisibilityBufferValue visibilityBufferValue;
                visibilityBufferValue.InstanceID = renderRequest.InstanceID_LOD;
                visibilityBufferValue.MeshletID = renderRequest.MeshletID;
                visibilityBufferValue.IndexID = vertexID;
                output.visibilityValue = PackVisibilityBufferValue(visibilityBufferValue);

                #ifdef _ALPHATEST_ON
                output.uv0 = GetUV0(vertex, materialData);
                #endif

                return output;
            }

            uint2 Frag(Varyings input) : SV_Target
            {
                #ifdef _ALPHATEST_ON
                const VividVisibilityBufferValue visibilityBufferValue = UnpackVisibilityBufferValue(input.visibilityValue);
                const VividInstanceData instanceData = PullInstanceData(visibilityBufferValue.InstanceID);
                const VividMaterialData materialData = PullMaterialData(instanceData.MaterialIndex);
                const float4 albedo = SampleAlbedo(input.uv0, materialData);
                clip(albedo.a - materialData.AlphaClipThreshold);
                #endif

                return input.visibilityValue;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
