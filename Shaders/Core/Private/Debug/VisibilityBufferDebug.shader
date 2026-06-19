Shader "Hidden/VividRP/VisibilityBufferDebug"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "VisibilityBufferDebug"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividGPUDrivenCommon.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividVisibilityBuffer.hlsl"

            #define VIVID_VISIBILITY_BUFFER_DEBUG_INSTANCE 0
            #define VIVID_VISIBILITY_BUFFER_DEBUG_CLUSTER 1
            #define VIVID_VISIBILITY_BUFFER_DEBUG_CLUSTER_LOD 2
            #define VIVID_VISIBILITY_BUFFER_DEBUG_TRIANGLE 3

            TYPED_TEXTURE2D(float2, _VisibilityBuffer);

            float4 _VisibilityBufferScaleBias;
            int _VisualizationMode;
            float _DebugExposure;

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

            float3 HashColor(uint seed)
            {
                float seedValue = (float)(seed + 1u);
                float3 value = float3(seedValue, seedValue + 17.0, seedValue + 37.0);
                value = frac(sin(value * float3(12.9898, 78.233, 39.425)) * 43758.5453);
                return saturate(0.25 + value * 0.75);
            }

            bool TryLoadVisibilityBufferValue(float2 uv, out VividVisibilityBufferValue value)
            {
                float2 visibilityUv = ApplyScaleBias(uv, _VisibilityBufferScaleBias);
                uint2 packedValue = asuint(SAMPLE_TEXTURE2D_LOD(_VisibilityBuffer, sampler_PointClamp, visibilityUv, 0).xy);
                if (!IsPackedVisibilityBufferValueValid(packedValue))
                {
                    value = (VividVisibilityBufferValue)0;
                    return false;
                }

                value = UnpackVisibilityBufferValue(packedValue);
                return true;
            }

            uint ResolveClusterLODLevel(VividVisibilityBufferValue value)
            {
                if (value.InstanceID >= _InstanceDataCount || _MeshLODNodeCount == 0u)
                    return 0u;

                VividInstanceData instanceData = PullInstanceData(value.InstanceID);
                uint nodeStart = min(instanceData.TopMeshLODStartIndex, _MeshLODNodeCount);
                uint nodeCount = min(instanceData.TotalMeshLODCount, _MeshLODNodeCount - nodeStart);
                uint nodeEnd = nodeStart + nodeCount;

                UNITY_LOOP
                for (uint nodeIndex = nodeStart; nodeIndex < nodeEnd; ++nodeIndex)
                {
                    VividMeshLODNode node = PullMeshLODNode(nodeIndex);
                    uint meshletStart = node.MeshletStartIndex;
                    uint meshletEnd = meshletStart + node.MeshletCount;
                    if (value.MeshletID >= meshletStart && value.MeshletID < meshletEnd)
                        return node.LevelIndex;
                }

                return 0u;
            }

            uint ResolveDebugSeed(VividVisibilityBufferValue value)
            {
                if (_VisualizationMode == VIVID_VISIBILITY_BUFFER_DEBUG_INSTANCE)
                    return value.InstanceID;

                if (_VisualizationMode == VIVID_VISIBILITY_BUFFER_DEBUG_CLUSTER_LOD)
                    return ResolveClusterLODLevel(value);

                if (_VisualizationMode == VIVID_VISIBILITY_BUFFER_DEBUG_TRIANGLE)
                    return value.IndexID / 3u;

                return value.MeshletID;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                VividVisibilityBufferValue value;
                if (!TryLoadVisibilityBufferValue(input.uv, value))
                    return 0;

                float exposureMultiplier = exp2(_DebugExposure);
                return float4(HashColor(ResolveDebugSeed(value)) * exposureMultiplier, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
