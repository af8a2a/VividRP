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
            #pragma target 5.0
            #pragma require randomwrite
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_local_fragment _ VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE
            #pragma multi_compile_local_fragment _ VIVID_VSM_CASTER
            #pragma multi_compile_local _ VIVID_VSM_PAGE_CASTER

            #define VIVIDRP_SHADERPASS_SHADOW_CASTER 1
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividGPUDrivenCommon.hlsl"
            #if !defined(VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE)
                #define VIVID_GPU_DRIVEN_TEXTURE_BACKEND_BINDLESS 1
            #endif
            #include_with_pragmas "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividSurfaceSampling.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividMaterialCoverage.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Shadow/VividVirtualShadowMapCaster.hlsl"
            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Shadow/VividVirtualShadowMapProjection.hlsl"

            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawArgs
            #include "UnityIndirect.cginc"

            StructuredBuffer<VividMeshletRenderRequestPacked> _VisibleMeshletRenderRequests;
#if defined(VIVID_VSM_PAGE_CASTER)
            StructuredBuffer<uint4> _VSMPrototypeMeshletPageRequests;
            StructuredBuffer<uint> _VSMPrototypeMeshletRasterPages;
#endif
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
#if defined(VIVID_VSM_PAGE_CASTER)
                float4 pageClipDistances : SV_ClipDistance0;
                nointerpolation uint virtualPageIndex : TEXCOORD2;
                uint renderTargetArrayIndex : SV_RenderTargetArrayIndex;
#endif
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

#if defined(VIVID_VSM_PAGE_CASTER)
            float4 GetVSMPageClipDistances(
                float4 positionCS,
                uint virtualPageIndex,
                uint cascadeIndex)
            {
                const uint pagesPerAxis = (uint)max(
                    _VSMPrototypePagesPerAxis,
                    1);
                const uint pagesPerCascade = pagesPerAxis * pagesPerAxis;
                const uint cascadePageIndex = virtualPageIndex
                    - cascadeIndex * pagesPerCascade;
                const uint2 pageCoord = uint2(
                    cascadePageIndex % pagesPerAxis,
                    cascadePageIndex / pagesPerAxis);
                const float2 pageMinUV = (float2)pageCoord
                    / (float)pagesPerAxis;
                const float2 pageMaxUV = (float2)(pageCoord + 1u)
                    / (float)pagesPerAxis;
                const float minNdcX = pageMinUV.x * 2.0 - 1.0;
                const float maxNdcX = pageMaxUV.x * 2.0 - 1.0;
#if UNITY_UV_STARTS_AT_TOP
                const float minNdcY = 1.0 - pageMaxUV.y * 2.0;
                const float maxNdcY = 1.0 - pageMinUV.y * 2.0;
#else
                const float minNdcY = pageMinUV.y * 2.0 - 1.0;
                const float maxNdcY = pageMaxUV.y * 2.0 - 1.0;
#endif
                return float4(
                    positionCS.x - minNdcX * positionCS.w,
                    maxNdcX * positionCS.w - positionCS.x,
                    positionCS.y - minNdcY * positionCS.w,
                    maxNdcY * positionCS.w - positionCS.y);
            }
#endif

            Varyings Vert(Attributes input)
            {
                InitIndirectDrawArgs(0);

                Varyings output;
                output.positionCS = float4(-2.0, -2.0, 0.0, 1.0);
                output.instanceIndex = 0u;
                output.uv0 = 0.0;
#if defined(VIVID_VSM_PAGE_CASTER)
                output.pageClipDistances = -1.0;
                output.virtualPageIndex = 0u;
                output.renderTargetArrayIndex = 0u;
#endif

                const uint instanceID = GetIndirectInstanceID_Base(input.instanceID);
                const uint vertexID = GetIndirectVertexID_Base(input.vertexID);
#if defined(VIVID_VSM_PAGE_CASTER)
                uint4 pageRequest;
                uint virtualPageIndex;
                uint cascadeIndex;
                if (GetCommandID(0) >= VIVIDRENDERERLISTID_COUNT)
                {
                    const uint pageCount = _VSMPrototypeMeshletRasterPages[0];
                    if (pageCount == 0u)
                        return output;
                    const uint localInstance = GetIndirectInstanceID(input.instanceID);
                    const uint requestEnd = instanceID - localInstance;
                    pageRequest = _VSMPrototypeMeshletPageRequests[
                        requestEnd - 1u - localInstance / pageCount];
                    virtualPageIndex = _VSMPrototypeMeshletRasterPages[
                        1u + localInstance % pageCount];
                    const uint pagesPerAxis = (uint)max(_VSMPrototypePagesPerAxis, 1);
                    const uint pagesPerCascade = pagesPerAxis * pagesPerAxis;
                    cascadeIndex = pageRequest.w / pagesPerCascade;
                    const uint pageInCascade = virtualPageIndex % pagesPerCascade;
                    const uint minPage = pageRequest.z % pagesPerCascade;
                    const uint maxPage = pageRequest.w % pagesPerCascade;
                    if (virtualPageIndex / pagesPerCascade != cascadeIndex
                        || pageInCascade % pagesPerAxis < minPage % pagesPerAxis
                        || pageInCascade % pagesPerAxis > maxPage % pagesPerAxis
                        || pageInCascade / pagesPerAxis < minPage / pagesPerAxis
                        || pageInCascade / pagesPerAxis > maxPage / pagesPerAxis)
                    {
                        return output;
                    }
                }
                else
                {
                    pageRequest = _VSMPrototypeMeshletPageRequests[instanceID];
                    virtualPageIndex = pageRequest.z;
                    cascadeIndex = pageRequest.w;
                }
                VividMeshletRenderRequestPacked renderRequest;
                renderRequest.InstanceID_LOD = pageRequest.x;
                renderRequest.MeshletID = pageRequest.y;
#else
                const VividMeshletRenderRequestPacked renderRequest = _VisibleMeshletRenderRequests[instanceID];
#endif
                const VividInstanceData instanceData = PullInstanceData(renderRequest.InstanceID_LOD);
                const VividDecodedMeshlet meshlet = PullMeshletData(renderRequest.MeshletID);
                const uint indexCount = meshlet.TriangleCount * 3u;

                if (vertexID >= indexCount)
                {
                    return output;
                }

                const uint vertexIndex = PullIndex(meshlet, vertexID);
                const VividDecodedMeshletVertex vertex = PullVertex(meshlet, vertexIndex);

                const float3 positionWS = TransformPosition(
                    instanceData.ObjectToWorldMatrix,
                    vertex.Position.xyz);
#if defined(VIVID_VSM_PAGE_CASTER)
                output.positionCS = mul(
                    _VSMProjections[cascadeIndex].worldToClip,
                    float4(positionWS, 1.0));
#else
                output.positionCS = TransformWorldToHClip(positionWS);
#endif
                output.positionCS = ApplyVividShadowClamping(output.positionCS);

                output.instanceIndex = renderRequest.InstanceID_LOD;
                output.uv0 = vertex.UV.xy;
#if defined(VIVID_VSM_PAGE_CASTER)
                output.pageClipDistances = GetVSMPageClipDistances(
                    output.positionCS,
                    virtualPageIndex,
                    cascadeIndex);
                const uint pagesPerAxis = (uint)_VSMPrototypePagesPerAxis;
                const uint pageInProjection = virtualPageIndex % (pagesPerAxis * pagesPerAxis);
                const uint2 origin = uint2(pageInProjection % pagesPerAxis,
                    pageInProjection / pagesPerAxis) * (uint)_VSMPrototypePageSize;
                output.positionCS = VividVSMToRasterClip(output.positionCS, origin,
                    (uint)_VSMPrototypeVirtualResolution, (uint)_VSMPrototypePageSize);
                output.virtualPageIndex = virtualPageIndex;
                output.renderTargetArrayIndex = _VSMPrototypePageTable[virtualPageIndex] - 1u;
#endif

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

#if defined(VIVID_VSM_PAGE_CASTER)
                VividWriteVSMPageDepth(input.positionCS, input.virtualPageIndex);
#else
                VividWriteVSMDepth(input.positionCS);
#endif
            }
            ENDHLSL
        }
    }

    FallBack Off
}
