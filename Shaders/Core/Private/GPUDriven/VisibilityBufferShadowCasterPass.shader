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
            #pragma multi_compile_local_fragment _ VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE

            #define VIVIDRP_SHADERPASS_SHADOW_CASTER 1
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividGPUDrivenCommon.hlsl"
            #if !defined(VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE)
                #define VIVID_GPU_DRIVEN_TEXTURE_BACKEND_BINDLESS 1
            #endif
            #include_with_pragmas "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividSurfaceSampling.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividMaterialCoverage.hlsl"

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
                nointerpolation uint instanceIndex : TEXCOORD0;
                float2 uv0 : TEXCOORD1;
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
                output.instanceIndex = 0u;
                output.uv0 = 0.0;

                const uint instanceID = GetIndirectInstanceID_Base(input.instanceID);
                const uint vertexID = GetIndirectVertexID_Base(input.vertexID);
                const VividMeshletRenderRequestPacked renderRequest = _VisibleMeshletRenderRequests[instanceID];
                const VividInstanceData instanceData = PullInstanceData(renderRequest.InstanceID_LOD);
                const VividDecodedMeshlet meshlet = PullMeshletData(renderRequest.MeshletID);
                const uint indexCount = meshlet.TriangleCount * 3u;

                if (vertexID >= indexCount)
                {
                    return output;
                }

                const uint vertexIndex = PullIndex(meshlet, vertexID);
                const VividDecodedMeshletVertex vertex = PullVertex(meshlet, vertexIndex);

                output.positionCS = TransformWorldToHClip(TransformPosition(instanceData.ObjectToWorldMatrix, vertex.Position.xyz));
                output.positionCS = ApplyVividShadowClamping(output.positionCS);

                output.instanceIndex = renderRequest.InstanceID_LOD;
                output.uv0 = vertex.UV.xy;

                return output;
            }

            void Frag(Varyings input)
            {
                #ifdef _ALPHATEST_ON
                const float2 uv0Ddx = ddx(input.uv0);
                const float2 uv0Ddy = ddy(input.uv0);
                const VividInstanceData instanceData = PullInstanceData(input.instanceIndex);
                VividMaterialCoverageEvaluation coverage;
                const uint coverageStatus = VividEvaluateCoverageProgram(
                    instanceData.MaterialIndex,
                    input.uv0,
                    uv0Ddx,
                    uv0Ddy,
                    coverage);
                if (coverageStatus == VIVID_MATERIAL_COVERAGE_LEGACY_FALLBACK)
                {
                    const VividMaterialData materialData =
                        PullMaterialData(instanceData.MaterialIndex);
                    const VividSurfaceBindingData surfaceBindingData =
                        PullSurfaceBindingData(materialData.SurfaceBindingIndex);
                    coverage = VividEvaluateBaseColorAlphaCoverage(
                        materialData,
                        surfaceBindingData,
                        input.uv0,
                        uv0Ddx,
                        uv0Ddy);
                }
                if (coverageStatus == VIVID_MATERIAL_COVERAGE_KNOWN_FAILURE)
                    clip(-1.0f);
                clip(coverage.Coverage - coverage.AlphaClipThreshold);
                #endif
            }
            ENDHLSL
        }
    }

    FallBack Off
}
