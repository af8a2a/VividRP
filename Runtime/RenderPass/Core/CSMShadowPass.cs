using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.VirtualTexture;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class CSMShadowPass : ShadowCasterPass
    {
        [RenderGraphResource(Name = "CSMShadowAtlas", Access = AccessFlags.Write)]
        private RenderGraphTexture m_ShadowAtlas;

        private static readonly int ShadowMatricesConstantBufferId =
            Shader.PropertyToID("ShaderVariablesShadowMatrices");

        private static readonly int ShadowCascadeIndexId = Shader.PropertyToID("_VividShadowCascadeIndex");

        private static readonly int s_VisibleMeshletRenderRequestsId = Shader.PropertyToID("_VisibleMeshletRenderRequests");

        private readonly ShadowDrawingSettings[] m_ShadowDrawSettings =
            new ShadowDrawingSettings[VividShadowData.MaxCascadeCount];

        private readonly VividGPUCullingContext[] m_ShadowCullingContexts =
            new VividGPUCullingContext[VividShadowData.MaxCascadeCount];

        private ShadowMatricesConstantBuffer m_ShadowMatrices;

        private int m_CascadeCount;

        private int m_CascadeResolution;

        private float m_SlopeScaleDepthBias;

        // Reading the VSM page table orders the fallback after virtual shadow production.
        [RenderGraphResource(Name = "VSMPageTable", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_VSMPageTable = RenderGraphBuffer.CreateStructured("VSMPageTable", sizeof(uint));

        public CSMShadowPass() : base(nameof(CSMShadowPass))
        {
            m_ShadowAtlas = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateDepthTarget(1, 1, DepthBits.Depth16)
            };
            m_ShadowAtlas.desc.Name = "CSMShadowAtlas";
            m_ShadowAtlas.desc.ClearBuffer = true;
            m_ShadowAtlas.desc.ClearColor = Color.black;
            m_ShadowAtlas.desc.IsShadowMap = true;
            m_ShadowAtlas.desc.FilterMode = FilterMode.Bilinear;
            m_ShadowAtlas.desc.WrapMode = TextureWrapMode.Clamp;
            m_ShadowAtlas.desc.Dimension = TextureDimension.Tex2DArray;
            m_ShadowAtlas.desc.Slices = VividShadowData.MaxCascadeCount;
        }

        public override void Prepare(ContextContainer frameData)
        {
            base.Prepare(frameData);
            if (!m_IsActive) return;
            var shadowData = m_ShadowData;
            m_CascadeCount = Mathf.Min(shadowData.cascadeCount, VividShadowData.MaxCascadeCount);
            m_CascadeResolution = shadowData.cascadeResolution;
            m_SlopeScaleDepthBias = shadowData.slopeScaleDepthBias;
            m_ShadowAtlas.desc.Width = m_CascadeResolution;
            m_ShadowAtlas.desc.Height = m_CascadeResolution;

            m_ShadowMatrices = new ShadowMatricesConstantBuffer
            {
                Cascade0 = BuildShadowViewProjectionMatrix(shadowData, m_CascadeCount, 0),
                Cascade1 = BuildShadowViewProjectionMatrix(shadowData, m_CascadeCount, 1),
                Cascade2 = BuildShadowViewProjectionMatrix(shadowData, m_CascadeCount, 2),
                Cascade3 = BuildShadowViewProjectionMatrix(shadowData, m_CascadeCount, 3),
            };

            // Configure ShadowDrawingSettings per cascade
            for (int i = 0; i < m_CascadeCount && m_HasUnityShadowCasters; i++)
            {
                var settings = new ShadowDrawingSettings(
                    m_CullingResults,
                    m_MainLightVisibleIndex);
#pragma warning disable CS0618 // Intentionally use the non-batched path without CullShadowCasters.
                settings.splitData = shadowData.splitData[i];
#pragma warning restore CS0618
                settings.splitIndex = -1;
                settings.useRenderingLayerMaskTest = false;
                settings.objectsFilter = ShadowObjectsFilter.AllObjects;
                m_ShadowDrawSettings[i] = settings;
            }

            if (m_MeshletRenderingActive)
                for (int i = 0; i < m_CascadeCount; i++)
                    BuildShadowCullingContext(i, out m_ShadowCullingContexts[i]);
        }

        public override void Record(UnsafePassContext context)
        {
            if (!m_IsActive || m_ShadowData.virtualShadowMapRendered || !m_ShadowAtlas.innerHandle.IsValid())
                return;
            var nativeCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            using (new ProfilingScope(nativeCmd, profilingSampler))
            {
                bool meshletPreparationSucceeded = TryPrepareMeshletShadowDraws(nativeCmd, out var meshletContext);
                ConstantBuffer.PushGlobal(nativeCmd, m_ShadowMatrices, ShadowMatricesConstantBufferId);
                nativeCmd.SetGlobalVector(ShadowBiasId, m_ShadowData.shadowCasterState);
                nativeCmd.SetGlobalDepthBias(1.0f, m_SlopeScaleDepthBias);
                DrawConventionalShadowMap(nativeCmd, meshletPreparationSucceeded, in meshletContext);
                nativeCmd.SetGlobalDepthBias(0.0f, 0.0f);
            }
        }

        private void DrawMeshletShadowCascade(
            CommandBuffer nativeCmd,
            VividGPUDrivenSystem system,
            GraphicsBuffer requestsBuffer,
            GraphicsBuffer argsBuffer,
            bool virtualTextureReady,
            in VirtualTextureSpaceBinding virtualTextureBinding,
            int cascadeIndex,
            Material[] materials)
        {
            if (materials == null)
                return;

            for (int rendererListIndex = 0; rendererListIndex < materials.Length; rendererListIndex++)
            {
                VividRendererListID batchKey = (VividRendererListID)rendererListIndex;
                if (!system.IsShadowRendererBatchActive(batchKey))
                    continue;

                Material material = materials[rendererListIndex];
                if (material == null)
                    continue;
                if (!virtualTextureReady
                    && (batchKey & VividRendererListID.AlphaTest) != 0)
                {
                    continue;
                }

                m_DrawProperties.Clear();
                m_DrawProperties.SetBuffer(s_VisibleMeshletRenderRequestsId, requestsBuffer);
                m_DrawProperties.SetBuffer(s_UnityIndirectDrawArgsId, argsBuffer);
                if (system.UsesVirtualTexture && virtualTextureReady)
                {
                    GPUDrivenVirtualTextureBindingUtility.BindSpaceProperties(
                        m_DrawProperties,
                        virtualTextureBinding,
                        m_VirtualTextureSpaceParams,
                        m_VirtualTextureMipOffsets,
                        m_VirtualTextureLayerFallbacks,
                        m_FrameIndex,
                        m_VirtualTextureFrameData.AdaptiveMipBias);
                }
                m_DrawProperties.SetInteger(ShadowCascadeIndexId, cascadeIndex);
                int commandIndex = VividGPUDrivenCullingBuffers.GetIndirectDrawArgsCommandIndex(
                    cascadeIndex,
                    rendererListIndex);
                m_DrawProperties.SetInteger(s_UnityBaseCommandIdId, commandIndex);
                nativeCmd.DrawProceduralIndirect(
                    Matrix4x4.identity,
                    material,
                    0,
                    MeshTopology.Triangles,
                    argsBuffer,
                    VividGPUDrivenCullingBuffers.GetIndirectDrawArgsByteOffset(
                        cascadeIndex,
                        rendererListIndex),
                    m_DrawProperties);
            }
        }

        private void DrawConventionalShadowMap(
            CommandBuffer nativeCmd,
            bool meshletPreparationSucceeded,
            in MeshletShadowRecordContext meshletContext)
        {
            bool canDrawMeshlets = false;
            GraphicsBuffer requestsBuffer = null;
            GraphicsBuffer argsBuffer = null;
            if (m_HasMeshletShadowCasters && meshletPreparationSucceeded)
            {
                bool cullSucceeded = TryPrepareMeshletShadowPoolDraws(
                    nativeCmd,
                    meshletContext.System,
                    meshletContext.AggregateDrawSet,
                    out requestsBuffer,
                    out argsBuffer,
                    out bool hasDraws,
                    m_ShadowCullingContexts, m_CascadeCount, in m_ShadowLODSelectionContext);
                canDrawMeshlets = cullSucceeded
                    && hasDraws
                    && HasRenderableMeshletShadowBatch(
                        meshletContext.System,
                        meshletContext.VirtualTextureReady);
            }

            for (int cascadeIndex = 0; cascadeIndex < m_CascadeCount; cascadeIndex++)
            {
                CoreUtils.SetRenderTarget(
                    nativeCmd,
                    m_ShadowAtlas.innerHandle,
                    ClearFlag.Depth,
                    Color.black,
                    depthSlice: cascadeIndex);
                nativeCmd.SetGlobalInt(ShadowCascadeIndexId, cascadeIndex);

                if (m_HasUnityShadowCasters)
                {
                    var settings = m_ShadowDrawSettings[cascadeIndex];
                    var rendererList = m_RenderContext.CreateShadowRendererList(ref settings);
                    nativeCmd.DrawRendererList(rendererList);
                }

                if (canDrawMeshlets)
                {
                    DrawMeshletShadowCascade(
                        nativeCmd,
                        meshletContext.System,
                        requestsBuffer,
                        argsBuffer,
                        meshletContext.VirtualTextureReady,
                        meshletContext.VirtualTextureBinding,
                        cascadeIndex,
                        m_Materials);
                }
            }
        }

        private void BuildShadowCullingContext(
            int cascadeIndex,
            out VividGPUCullingContext cullingContext)
        {
            var viewMatrix = m_ShadowData.viewMatrices[cascadeIndex];
            var projMatrix = m_ShadowData.projMatrices[cascadeIndex];
            var invViewMatrix = viewMatrix.inverse;
            Vector4 col0 = invViewMatrix.GetColumn(0);
            Vector4 col1 = invViewMatrix.GetColumn(1);
            Vector4 col3 = invViewMatrix.GetColumn(3);
            var cullingSphereWS = m_ShadowData.cascadeSpheres[cascadeIndex];
            cullingSphereWS.w = Mathf.Sqrt(Mathf.Max(0.0f, cullingSphereWS.w));

            VividGPUDrivenCullingContextUtility.Build(
                viewMatrix,
                projMatrix,
                cameraPositionWS: new Vector3(col3.x, col3.y, col3.z),
                cameraRightWS: new Vector3(col0.x, col0.y, col0.z),
                cameraUpWS: new Vector3(col1.x, col1.y, col1.z),
                pixelSize: new Vector2(m_CascadeResolution, m_CascadeResolution),
                isPerspective: false,
                passMask: VividInstancePassMask.Shadows,
                cullingSphereWS: cullingSphereWS,
                // Directional shadow vertices are pancaked onto the raster near plane, so
                // rejecting their bounds against that plane would remove valid casters.
                cullAgainstNearPlane: false,
                cullingContext: out cullingContext,
                lodSelectionContext: out _);
        }

        private static Matrix4x4 BuildShadowViewProjectionMatrix(
            VividShadowData shadowData,
            int cascadeCount,
            int cascadeIndex)
        {
            return cascadeIndex < cascadeCount
                ? GL.GetGPUProjectionMatrix(shadowData.projMatrices[cascadeIndex], true)
                    * shadowData.viewMatrices[cascadeIndex]
                : Matrix4x4.identity;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ShadowMatricesConstantBuffer
        {
            public Matrix4x4 Cascade0;
            public Matrix4x4 Cascade1;
            public Matrix4x4 Cascade2;
            public Matrix4x4 Cascade3;
        }
    }
}
