using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.VirtualTexture;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class VisibilityBufferPass : UnsafePass
    {
        internal const string VisibilityBufferShaderName = "Hidden/VividRP/GPUDriven/VisibilityBufferPass";

        private const int IndirectDrawArgsByteStride = sizeof(uint) * 4;

        private static readonly int s_CullId = Shader.PropertyToID("_Cull");
        private static readonly int s_UnityIndirectDrawArgsId = Shader.PropertyToID("unity_IndirectDrawArgs");
        private static readonly int s_UnityBaseCommandIdId = Shader.PropertyToID("unity_BaseCommandID");
        private static readonly int s_VisibleMeshletRenderRequestsId = Shader.PropertyToID("_VisibleMeshletRenderRequests");
        private static readonly string s_AlphaTestKeyword = "_ALPHATEST_ON";

        [RenderGraphResource(Name = "VisibleMeshletRenderRequests", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_VisibleMeshletRenderRequests;

        [RenderGraphResource(Name = "VisibleMeshletIndirectArgs", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_VisibleMeshletIndirectArgs;

        [RenderGraphResource(
            Name = "VisibilityBuffer",
            Access = AccessFlags.ReadWrite,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_VisibilityBuffer;

        [RenderGraphResource(
            Name = "Depth",
            Access = AccessFlags.ReadWrite,
            IsDepthAttachment = true)]
        private RenderGraphTexture m_Depth;

        private readonly RenderGraphTexture m_DefaultVisibilityBuffer;
        private readonly RenderGraphTexture m_DefaultDepth;
        private readonly Material[] m_Materials = new Material[(int)VividRendererListID.Count];
        private readonly MaterialPropertyBlock m_DrawProperties = new MaterialPropertyBlock();
        private readonly float[] m_VirtualTextureSpaceParams = new float[VirtualTextureSpaceShaderParams.IntCount];
        private readonly float[] m_VirtualTextureMipOffsets = new float[VirtualTextureFeedbackProcessor.MaxMipCount];
        private readonly Vector4[] m_VirtualTextureLayerFallbacks = new Vector4[VTStackDesc.MaxLayerCount];
        private VividVirtualTextureFrameData m_VirtualTextureFrameData;
        private ComputeShader m_MeshletCullingCompute;
        private CameraHistoryTexture m_OccluderDepthPyramidHistory;
        private RTHandle m_CurrentOccluderDepthPyramid;
        private Camera m_Camera;
        private Matrix4x4 m_CurrentViewProjectionMatrix = Matrix4x4.identity;
        private GraphicsBuffer m_OccludedMeshletRenderRequestsBuffer;
        private GraphicsBuffer m_OccludedMeshletRenderRequestCounterBuffer;
        private GraphicsBuffer m_OccludedMeshletIndirectDispatchArgsBuffer;
        private GraphicsBuffer m_RecoveredMeshletRenderRequestsBuffer;
        private GraphicsBuffer m_RecoveredRendererListMeshletCountsBuffer;
        private GraphicsBuffer m_RecoveredMeshletIndirectDrawArgsBuffer;
        private int m_CopyOccluderDepthKernel = -1;
        private int m_DownsampleOccluderDepthKernel = -1;
        private int m_OccluderWidth;
        private int m_OccluderHeight;
        private int m_OccluderTextureWidth;
        private int m_OccluderTextureHeight;
        private int m_OccluderMipCount;
        private bool m_OcclusionCullingEnabled;
        private bool m_OcclusionHistoryValid;
        private int m_FrameIndex;

        public VisibilityBufferPass()
        {
            profilingSampler = new ProfilingSampler(nameof(VisibilityBufferPass));

            m_VisibleMeshletRenderRequests = RenderGraphBuffer.CreateStructured(
                "VisibleMeshletRenderRequests",
                1,
                sizeof(uint) * 2,
                GraphicsBuffer.Target.Structured
            );
            m_VisibleMeshletIndirectArgs = RenderGraphBuffer.CreateStructured(
                "VisibleMeshletIndirectArgs",
                4,
                sizeof(uint),
                GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments
            );

            m_VisibilityBuffer = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.R32G32_UInt)
            };
            m_VisibilityBuffer.desc.Name = "VisibilityBuffer";
            m_VisibilityBuffer.desc.FilterMode = FilterMode.Point;
            m_VisibilityBuffer.desc.WrapMode = TextureWrapMode.Clamp;
            m_VisibilityBuffer.desc.ClearBuffer = true;
            m_VisibilityBuffer.desc.ClearColor = Color.clear;
            m_VisibilityBuffer.desc.UseMipMap = false;
            m_VisibilityBuffer.desc.AutoGenerateMips = false;
            m_VisibilityBuffer.desc.MipCount = 1;

            m_Depth = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateDepthTarget(1, 1, DepthBits.Depth32)
            };
            m_Depth.desc.Name = "Depth";

            m_DefaultVisibilityBuffer = m_VisibilityBuffer;
            m_DefaultDepth = m_Depth;
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_MeshletCullingCompute = resources?.GPUMeshletCullingCompute;
            if (m_MeshletCullingCompute != null)
            {
                try
                {
                    m_CopyOccluderDepthKernel = m_MeshletCullingCompute.FindKernel("CSCopyOccluderDepth");
                    m_DownsampleOccluderDepthKernel = m_MeshletCullingCompute.FindKernel("CSDownsampleOccluderDepth");
                }
                catch (ArgumentException)
                {
                    m_MeshletCullingCompute = null;
                    m_CopyOccluderDepthKernel = -1;
                    m_DownsampleOccluderDepthKernel = -1;
                    Debug.LogWarning(
                        $"[VividRP] {nameof(VisibilityBufferPass)} could not find GPUDriven occluder depth kernels. Occlusion culling will be disabled.");
                }
            }

            Shader shader = Shader.Find(VisibilityBufferShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{VisibilityBufferShaderName}' for {nameof(VisibilityBufferPass)}.");
                return;
            }

            for (int rendererListIndex = 0; rendererListIndex < m_Materials.Length; rendererListIndex++)
            {
                Material material = CoreUtils.CreateEngineMaterial(shader);
                material.name = $"{nameof(VisibilityBufferPass)}_{(VividRendererListID)rendererListIndex}";
                ConfigureMaterial(material, (VividRendererListID)rendererListIndex);
                m_Materials[rendererListIndex] = material;
            }
        }

        public override void Prepare(ContextContainer frameData)
        {
            ResetOcclusionFrameState();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            m_Camera = cameraData.camera;
            m_CurrentViewProjectionMatrix = cameraData.mainViewConstants.viewProjMatrix;
            m_VirtualTextureFrameData = frameData.GetOrCreate<VividVirtualTextureFrameData>();
            m_FrameIndex = cameraData.frameIndex >= 0 ? cameraData.frameIndex : Time.frameCount;
            int width = cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth;
            int height = cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight;

            if (width <= 0)
                width = Mathf.Max(1, Screen.width);

            if (height <= 0)
                height = Mathf.Max(1, Screen.height);

            ResizePassOwnedTexture(m_VisibilityBuffer, m_DefaultVisibilityBuffer, width, height);
            ResizePassOwnedTexture(m_Depth, m_DefaultDepth, width, height);

            var gpuDrivenFrameData = frameData.GetOrCreate<VividGPUDrivenFrameData>();
            GraphicsBuffer visibleMeshletRenderRequestsBuffer = gpuDrivenFrameData.visibleMeshletRenderRequestsBuffer;
            GraphicsBuffer visibleMeshletIndirectDrawArgsBuffer = gpuDrivenFrameData.visibleMeshletIndirectDrawArgsBuffer;

            if ((visibleMeshletRenderRequestsBuffer == null || visibleMeshletIndirectDrawArgsBuffer == null) &&
                VividGPUDrivenSystem.TryGetCurrentVisibleMeshletBuffers(
                    out GraphicsBuffer fallbackVisibleMeshletRenderRequestsBuffer,
                    out GraphicsBuffer fallbackVisibleMeshletIndirectDrawArgsBuffer))
            {
                visibleMeshletRenderRequestsBuffer ??= fallbackVisibleMeshletRenderRequestsBuffer;
                visibleMeshletIndirectDrawArgsBuffer ??= fallbackVisibleMeshletIndirectDrawArgsBuffer;
            }

            UpdateImportedBuffer(
                m_VisibleMeshletRenderRequests,
                visibleMeshletRenderRequestsBuffer,
                GraphicsBuffer.Target.Structured,
                "VisibleMeshletRenderRequests"
            );
            UpdateImportedBuffer(
                m_VisibleMeshletIndirectArgs,
                visibleMeshletIndirectDrawArgsBuffer,
                GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments,
                "VisibleMeshletIndirectArgs"
            );

            PrepareOcclusionResources(gpuDrivenFrameData, width, height);
        }

        public override void Record(UnsafePassContext context)
        {
            if (m_VisibilityBuffer?.IsValid() != true || m_Depth?.IsValid() != true)
                return;

            var nativeCmd = context.GetNativeCommandBuffer();
            BindVisibilityTargets(nativeCmd, clearTargets: true);

            GraphicsBuffer visibleMeshletRenderRequestsBuffer = m_VisibleMeshletRenderRequests?.ImportedGraphicsBuffer;
            GraphicsBuffer visibleMeshletIndirectArgsBuffer = m_VisibleMeshletIndirectArgs?.ImportedGraphicsBuffer;
            if (visibleMeshletRenderRequestsBuffer == null || visibleMeshletIndirectArgsBuffer == null)
                return;

            VividGPUDrivenSystem system = VividGPUDrivenSystem.HasInstance
                ? VividGPUDrivenSystem.instance
                : null;
            bool virtualTextureReady = true;
            VirtualTextureSpaceBinding virtualTextureBinding = default;
            if (system != null)
            {
                for (int materialIndex = 0; materialIndex < m_Materials.Length; materialIndex++)
                    system.ConfigureTextureBackendKeyword(m_Materials[materialIndex]);

                if (system.UsesVirtualTexture)
                {
                    virtualTextureReady = GPUDrivenVirtualTextureBindingUtility.TryGetBinding(
                        m_VirtualTextureFrameData,
                        out virtualTextureBinding);
                }
            }

            using (new ProfilingScope(nativeCmd, profilingSampler))
            {
                DrawRendererLists(
                    nativeCmd,
                    visibleMeshletRenderRequestsBuffer,
                    visibleMeshletIndirectArgsBuffer,
                    system,
                    virtualTextureReady,
                    virtualTextureBinding);

                if (!CanExecuteOcclusion(system))
                    return;

                GenerateCurrentOccluderDepthPyramid(nativeCmd);
                VividGPUDrivenOcclusionHistorySystem.CommitCurrent(
                    m_Camera,
                    m_OccluderDepthPyramidHistory,
                    m_CurrentViewProjectionMatrix,
                    m_OccluderWidth,
                    m_OccluderHeight,
                    m_OccluderTextureWidth,
                    m_OccluderTextureHeight,
                    m_OccluderMipCount);

                if (!m_OcclusionHistoryValid)
                    return;

                system.BindGlobals(nativeCmd);
                if (!system.DispatchOcclusionRetest(
                        nativeCmd,
                        m_MeshletCullingCompute,
                        m_CurrentOccluderDepthPyramid,
                        m_CurrentViewProjectionMatrix,
                        m_OccluderWidth,
                        m_OccluderHeight,
                        m_OccluderTextureWidth,
                        m_OccluderTextureHeight,
                        m_OccluderMipCount))
                {
                    return;
                }

                BindVisibilityTargets(nativeCmd, clearTargets: false);
                DrawRendererLists(
                    nativeCmd,
                    m_RecoveredMeshletRenderRequestsBuffer,
                    m_RecoveredMeshletIndirectDrawArgsBuffer,
                    system,
                    virtualTextureReady,
                    virtualTextureBinding);
            }
        }

        public override void Dispose()
        {
            m_VirtualTextureFrameData = null;
            m_MeshletCullingCompute = null;
            m_CopyOccluderDepthKernel = -1;
            m_DownsampleOccluderDepthKernel = -1;
            ResetOcclusionFrameState();
            m_FrameIndex = 0;
            for (int materialIndex = 0; materialIndex < m_Materials.Length; materialIndex++)
            {
                if (m_Materials[materialIndex] == null)
                    continue;

                CoreUtils.Destroy(m_Materials[materialIndex]);
                m_Materials[materialIndex] = null;
            }
        }

        private void PrepareOcclusionResources(
            VividGPUDrivenFrameData gpuDrivenFrameData,
            int fallbackWidth,
            int fallbackHeight)
        {
            if (gpuDrivenFrameData == null
                || !gpuDrivenFrameData.occlusionCullingEnabled
                || m_Camera == null
                || m_MeshletCullingCompute == null
                || m_CopyOccluderDepthKernel < 0
                || m_DownsampleOccluderDepthKernel < 0
                || m_Depth?.desc == null
                || m_Depth.desc.MsaaSamples != MSAASamples.None
                || m_Depth.desc.Dimension != TextureDimension.Tex2D
                || m_Depth.desc.Slices > 1)
            {
                return;
            }

            m_OccludedMeshletRenderRequestsBuffer = gpuDrivenFrameData.occludedMeshletRenderRequestsBuffer;
            m_OccludedMeshletRenderRequestCounterBuffer = gpuDrivenFrameData.occludedMeshletRenderRequestCounterBuffer;
            m_OccludedMeshletIndirectDispatchArgsBuffer = gpuDrivenFrameData.occludedMeshletIndirectDispatchArgsBuffer;
            m_RecoveredMeshletRenderRequestsBuffer = gpuDrivenFrameData.recoveredMeshletRenderRequestsBuffer;
            m_RecoveredRendererListMeshletCountsBuffer = gpuDrivenFrameData.recoveredRendererListMeshletCountsBuffer;
            m_RecoveredMeshletIndirectDrawArgsBuffer = gpuDrivenFrameData.recoveredMeshletIndirectDrawArgsBuffer;
            if (m_OccludedMeshletRenderRequestsBuffer == null
                || m_OccludedMeshletRenderRequestCounterBuffer == null
                || m_OccludedMeshletIndirectDispatchArgsBuffer == null
                || m_RecoveredMeshletRenderRequestsBuffer == null
                || m_RecoveredRendererListMeshletCountsBuffer == null
                || m_RecoveredMeshletIndirectDrawArgsBuffer == null)
            {
                ResetOcclusionFrameState();
                return;
            }

            m_OccluderWidth = Mathf.Max(1, m_Depth.desc.Width > 0 ? m_Depth.desc.Width : fallbackWidth);
            m_OccluderHeight = Mathf.Max(1, m_Depth.desc.Height > 0 ? m_Depth.desc.Height : fallbackHeight);
            m_OccluderTextureWidth = VividGPUDrivenOcclusionHistorySystem.CalculateTextureDimension(m_OccluderWidth);
            m_OccluderTextureHeight = VividGPUDrivenOcclusionHistorySystem.CalculateTextureDimension(m_OccluderHeight);
            m_OccluderMipCount = VividGPUDrivenOcclusionHistorySystem.CalculateMipCount(
                m_OccluderTextureWidth,
                m_OccluderTextureHeight);
            m_OccluderDepthPyramidHistory = VividGPUDrivenOcclusionHistorySystem.PrepareCurrent(
                m_Camera,
                m_OccluderWidth,
                m_OccluderHeight);
            m_CurrentOccluderDepthPyramid = m_OccluderDepthPyramidHistory?.GetCurrent();
            if (m_CurrentOccluderDepthPyramid == null)
            {
                ResetOcclusionFrameState();
                return;
            }

            PassRecorder.ImportTextureForPass(this, m_CurrentOccluderDepthPyramid, AccessFlags.ReadWrite);
            PassRecorder.ImportBufferForPass(this, m_OccludedMeshletRenderRequestsBuffer, AccessFlags.Read);
            PassRecorder.ImportBufferForPass(this, m_OccludedMeshletRenderRequestCounterBuffer, AccessFlags.Read);
            PassRecorder.ImportBufferForPass(this, m_OccludedMeshletIndirectDispatchArgsBuffer, AccessFlags.Read);
            PassRecorder.ImportBufferForPass(this, m_RecoveredMeshletRenderRequestsBuffer, AccessFlags.ReadWrite);
            PassRecorder.ImportBufferForPass(this, m_RecoveredRendererListMeshletCountsBuffer, AccessFlags.ReadWrite);
            PassRecorder.ImportBufferForPass(this, m_RecoveredMeshletIndirectDrawArgsBuffer, AccessFlags.ReadWrite);
            m_OcclusionCullingEnabled = true;
            m_OcclusionHistoryValid = gpuDrivenFrameData.occlusionHistoryValid;
        }

        private bool CanExecuteOcclusion(VividGPUDrivenSystem system)
        {
            return m_OcclusionCullingEnabled
                && system != null
                && m_MeshletCullingCompute != null
                && m_CopyOccluderDepthKernel >= 0
                && m_DownsampleOccluderDepthKernel >= 0
                && m_CurrentOccluderDepthPyramid != null
                && m_Depth?.innerHandle.IsValid() == true;
        }

        private void GenerateCurrentOccluderDepthPyramid(CommandBuffer cmd)
        {
            cmd.SetComputeTextureParam(
                m_MeshletCullingCompute,
                m_CopyOccluderDepthKernel,
                VividGPUDrivenShaderIDs._InputDepth,
                m_Depth.innerHandle);
            cmd.SetComputeTextureParam(
                m_MeshletCullingCompute,
                m_CopyOccluderDepthKernel,
                VividGPUDrivenShaderIDs._OccluderDepthPyramidDestination,
                m_CurrentOccluderDepthPyramid,
                0);
            cmd.SetComputeVectorParam(
                m_MeshletCullingCompute,
                VividGPUDrivenShaderIDs._OccluderSourceSize,
                new Vector4(m_OccluderWidth, m_OccluderHeight, 0.0f, 0.0f));
            cmd.SetComputeVectorParam(
                m_MeshletCullingCompute,
                VividGPUDrivenShaderIDs._OccluderDestinationSize,
                new Vector4(m_OccluderTextureWidth, m_OccluderTextureHeight, 0.0f, 0.0f));
            cmd.DispatchCompute(
                m_MeshletCullingCompute,
                m_CopyOccluderDepthKernel,
                CoreUtils.DivRoundUp(m_OccluderTextureWidth, 8),
                CoreUtils.DivRoundUp(m_OccluderTextureHeight, 8),
                1);

            cmd.SetComputeTextureParam(
                m_MeshletCullingCompute,
                m_DownsampleOccluderDepthKernel,
                VividGPUDrivenShaderIDs._OccluderDepthPyramid,
                m_CurrentOccluderDepthPyramid);
            for (int mipLevel = 1; mipLevel < m_OccluderMipCount; mipLevel++)
            {
                int sourceWidth = Mathf.Max(1, m_OccluderTextureWidth >> (mipLevel - 1));
                int sourceHeight = Mathf.Max(1, m_OccluderTextureHeight >> (mipLevel - 1));
                int destinationWidth = Mathf.Max(1, m_OccluderTextureWidth >> mipLevel);
                int destinationHeight = Mathf.Max(1, m_OccluderTextureHeight >> mipLevel);
                cmd.SetComputeIntParam(
                    m_MeshletCullingCompute,
                    VividGPUDrivenShaderIDs._OccluderSourceMip,
                    mipLevel - 1);
                cmd.SetComputeVectorParam(
                    m_MeshletCullingCompute,
                    VividGPUDrivenShaderIDs._OccluderSourceSize,
                    new Vector4(sourceWidth, sourceHeight, 0.0f, 0.0f));
                cmd.SetComputeVectorParam(
                    m_MeshletCullingCompute,
                    VividGPUDrivenShaderIDs._OccluderDestinationSize,
                    new Vector4(destinationWidth, destinationHeight, 0.0f, 0.0f));
                cmd.SetComputeTextureParam(
                    m_MeshletCullingCompute,
                    m_DownsampleOccluderDepthKernel,
                    VividGPUDrivenShaderIDs._OccluderDepthPyramidDestination,
                    m_CurrentOccluderDepthPyramid,
                    mipLevel);
                cmd.DispatchCompute(
                    m_MeshletCullingCompute,
                    m_DownsampleOccluderDepthKernel,
                    CoreUtils.DivRoundUp(destinationWidth, 8),
                    CoreUtils.DivRoundUp(destinationHeight, 8),
                    1);
            }
        }

        private void DrawRendererLists(
            CommandBuffer cmd,
            GraphicsBuffer visibleMeshletRenderRequestsBuffer,
            GraphicsBuffer indirectArgsBuffer,
            VividGPUDrivenSystem system,
            bool virtualTextureReady,
            in VirtualTextureSpaceBinding virtualTextureBinding)
        {
            if (visibleMeshletRenderRequestsBuffer == null || indirectArgsBuffer == null)
                return;

            for (int rendererListIndex = 0; rendererListIndex < m_Materials.Length; rendererListIndex++)
            {
                Material material = m_Materials[rendererListIndex];
                if (material == null)
                    continue;
                if (!virtualTextureReady
                    && (((VividRendererListID) rendererListIndex & VividRendererListID.AlphaTest) != 0))
                {
                    continue;
                }

                m_DrawProperties.Clear();
                m_DrawProperties.SetBuffer(s_VisibleMeshletRenderRequestsId, visibleMeshletRenderRequestsBuffer);
                m_DrawProperties.SetBuffer(s_UnityIndirectDrawArgsId, indirectArgsBuffer);
                m_DrawProperties.SetInteger(s_UnityBaseCommandIdId, rendererListIndex);
                if (system?.UsesVirtualTexture == true && virtualTextureReady)
                {
                    GPUDrivenVirtualTextureBindingUtility.BindSpaceProperties(
                        m_DrawProperties,
                        virtualTextureBinding,
                        m_VirtualTextureSpaceParams,
                        m_VirtualTextureMipOffsets,
                        m_VirtualTextureLayerFallbacks,
                        m_FrameIndex);
                }

                cmd.DrawProceduralIndirect(
                    Matrix4x4.identity,
                    material,
                    0,
                    MeshTopology.Triangles,
                    indirectArgsBuffer,
                    rendererListIndex * IndirectDrawArgsByteStride,
                    m_DrawProperties);
            }
        }

        private void BindVisibilityTargets(CommandBuffer cmd, bool clearTargets)
        {
            cmd.SetRenderTarget(m_VisibilityBuffer, m_Depth);
            if (!clearTargets)
                return;

            ClearFlag clearFlag = ClearFlag.None;
            if (m_VisibilityBuffer?.desc?.ClearBuffer == true)
                clearFlag |= ClearFlag.Color;
            if (m_Depth?.desc?.ClearBuffer == true)
                clearFlag |= ClearFlag.DepthStencil;
            if (clearFlag != ClearFlag.None)
                CoreUtils.ClearRenderTarget(cmd, clearFlag, m_VisibilityBuffer?.desc?.ClearColor ?? Color.clear);
        }

        private void ResetOcclusionFrameState()
        {
            m_OccluderDepthPyramidHistory = null;
            m_CurrentOccluderDepthPyramid = null;
            m_Camera = null;
            m_CurrentViewProjectionMatrix = Matrix4x4.identity;
            m_OccludedMeshletRenderRequestsBuffer = null;
            m_OccludedMeshletRenderRequestCounterBuffer = null;
            m_OccludedMeshletIndirectDispatchArgsBuffer = null;
            m_RecoveredMeshletRenderRequestsBuffer = null;
            m_RecoveredRendererListMeshletCountsBuffer = null;
            m_RecoveredMeshletIndirectDrawArgsBuffer = null;
            m_OccluderWidth = 0;
            m_OccluderHeight = 0;
            m_OccluderTextureWidth = 0;
            m_OccluderTextureHeight = 0;
            m_OccluderMipCount = 0;
            m_OcclusionCullingEnabled = false;
            m_OcclusionHistoryValid = false;
        }

        private static void ConfigureMaterial(Material material, VividRendererListID rendererListID)
        {
            if (material == null)
                return;

            material.SetFloat(s_CullId, (float)GetCullMode(rendererListID));
            CoreUtils.SetKeyword(material, s_AlphaTestKeyword, (rendererListID & VividRendererListID.AlphaTest) != 0);
        }

        private static CullMode GetCullMode(VividRendererListID rendererListID)
        {
            if ((rendererListID & VividRendererListID.CullFront) != 0)
                return CullMode.Front;

            if ((rendererListID & VividRendererListID.CullOff) != 0)
                return CullMode.Off;

            return CullMode.Back;
        }

        private static void ResizePassOwnedTexture(
            RenderGraphTexture texture,
            RenderGraphTexture defaultTexture,
            int width,
            int height)
        {
            if (!ReferenceEquals(texture, defaultTexture) || texture?.desc == null)
                return;

            texture.desc.Width = width;
            texture.desc.Height = height;
        }

        private static void UpdateImportedBuffer(
            RenderGraphBuffer renderGraphBuffer,
            GraphicsBuffer graphicsBuffer,
            GraphicsBuffer.Target fallbackTarget,
            string name)
        {
            if (renderGraphBuffer == null)
                return;

            renderGraphBuffer.desc.Name = name;

            if (graphicsBuffer == null)
            {
                renderGraphBuffer.desc.Target = fallbackTarget;
                renderGraphBuffer.ClearImportedBuffer();
                return;
            }

            renderGraphBuffer.desc.Count = Mathf.Max(1, graphicsBuffer.count);
            renderGraphBuffer.desc.Stride = Mathf.Max(1, graphicsBuffer.stride);
            renderGraphBuffer.desc.Target = graphicsBuffer.target;
            renderGraphBuffer.SetImportedBuffer(graphicsBuffer);
        }
    }
}
