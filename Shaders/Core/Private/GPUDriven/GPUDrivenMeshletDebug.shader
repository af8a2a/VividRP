Shader "Hidden/VividRP/GPUDriven/MeshletDebug"
{
    Properties
    {
        [HideInInspector] _Cull("Cull", Float) = 2
        [HideInInspector] _OverlayAlpha("Overlay Alpha", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "VividRenderPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "Overlay"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/GPUDriven/VividGPUDrivenCommon.hlsl"

            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawArgs
            #include "UnityIndirect.cginc"

            StructuredBuffer<VividMeshletVertex> _SharedVertexBuffer;
            ByteAddressBuffer _SharedIndexBuffer;
            StructuredBuffer<VividMeshletRenderRequestPacked> _VisibleMeshletRenderRequests;

            float _OverlayAlpha;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR0;
            };

            // VividMeshlet PullMeshletData(const uint meshletID)
            // {
            //     return _Meshlets[meshletID];
            // }

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

            float3 HashColor(const uint seed)
            {
                const float seedValue = (float) (seed + 1u);
                float3 value = float3(seedValue, seedValue + 17.0, seedValue + 37.0);
                value = frac(sin(value * float3(12.9898, 78.233, 39.425)) * 43758.5453);
                return saturate(0.25 + value * 0.75);
            }

            Varyings Vert(Attributes input)
            {
                InitIndirectDrawArgs(0);

                Varyings output;
                output.positionCS = float4(-2.0, -2.0, 0.0, 1.0);
                output.color = 0.0;

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

                const float3 positionWS = mul(instanceData.ObjectToWorldMatrix, float4(vertex.Position.xyz, 1.0)).xyz;
                const float3 normalWS = normalize(mul((float3x3) instanceData.ObjectToWorldMatrix, vertex.Normal.xyz));
                const float3 normalColor = abs(normalWS) * 0.6 + 0.2;
                const float3 materialColor = HashColor(instanceData.MaterialIndex + renderRequest.MeshletID * 31u);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.color.rgb = lerp(normalColor, materialColor, 0.35);
                output.color.a = _OverlayAlpha * (1.0 - 0.15 * (float) ((uint) materialData.RendererListID & 1u));
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                return input.color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
