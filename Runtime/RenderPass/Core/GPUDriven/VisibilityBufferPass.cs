using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.VirtualTexture;
using VividRP.Runtime.MeshShader;
using VividRP.Runtime.PrimitiveScene;

namespace VividRP.Runtime.RenderPass.Core
{
    public enum VisibilityBufferRasterizationPath
    {
        DrawProceduralIndirect = 0,
        ExperimentalMeshShader = 1,
    }

    public class VisibilityBufferPass : UnsafePass
    {
        internal const string VisibilityBufferShaderName = "Hidden/VividRP/GPUDriven/VisibilityBufferPass";

        private const int IndirectDrawArgsByteStride = sizeof(uint) * 4;
        private const string MeshShaderProgramResourcePath =
            "VividMeshShader/VisibilityBufferMeshShader";
        private const int SpdTileSize = 64;
        private const int SpdMipTextureCount = VividGPUDrivenOcclusionHistorySystem.MaxMipCount;
        private const int SpdAtomicCounterCount = 6;

        private static readonly int s_CullId = Shader.PropertyToID("_Cull");
        private static readonly int s_UnityIndirectDrawArgsId = Shader.PropertyToID("unity_IndirectDrawArgs");
        private static readonly int s_UnityBaseCommandIdId = Shader.PropertyToID("unity_BaseCommandID");
        private static readonly int s_VisibleMeshletRenderRequestsId = Shader.PropertyToID("_VisibleMeshletRenderRequests");
        private static readonly int s_SpdMipsId = Shader.PropertyToID("mips");
        private static readonly int s_SpdNumWorkGroupsId = Shader.PropertyToID("numWorkGroups");
        private static readonly int s_SpdWorkGroupOffsetId = Shader.PropertyToID("workGroupOffset");
        private static readonly int s_SpdGlobalAtomicId = Shader.PropertyToID("spdGlobalAtomic");
        private static readonly int[] s_SpdMipTextureIds =
        {
            Shader.PropertyToID("rw_spd_mip0"),
            Shader.PropertyToID("rw_spd_mip1"),
            Shader.PropertyToID("rw_spd_mip2"),
            Shader.PropertyToID("rw_spd_mip3"),
            Shader.PropertyToID("rw_spd_mip4"),
            Shader.PropertyToID("rw_spd_mip5"),
            Shader.PropertyToID("rw_spd_mip6"),
            Shader.PropertyToID("rw_spd_mip7"),
            Shader.PropertyToID("rw_spd_mip8"),
            Shader.PropertyToID("rw_spd_mip9"),
            Shader.PropertyToID("rw_spd_mip10"),
            Shader.PropertyToID("rw_spd_mip11"),
            Shader.PropertyToID("rw_spd_mip12"),
        };
        private static readonly SpdGlobalAtomicBufferData[] s_ZeroSpdAtomicCounterData = { default };
        private static readonly string s_AlphaTestKeyword = "_ALPHATEST_ON";

        [StructLayout(LayoutKind.Sequential)]
        private struct SpdGlobalAtomicBufferData
        {
            public uint Counter0;
            public uint Counter1;
            public uint Counter2;
            public uint Counter3;
            public uint Counter4;
            public uint Counter5;
        }

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
            Name = "VisibilityBufferAttributes0",
            Access = AccessFlags.ReadWrite,
            AttachmentIndex = 1,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_Attributes0;

        [RenderGraphResource(
            Name = "VisibilityBufferAttributes1",
            Access = AccessFlags.ReadWrite,
            AttachmentIndex = 2,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_Attributes1;

        [RenderGraphResource(
            Name = "VisibilityBufferBarycentrics",
            Access = AccessFlags.ReadWrite,
            AttachmentIndex = 3,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_Barycentrics;

        [RenderGraphResource(
            Name = "Depth",
            Access = AccessFlags.ReadWrite,
            IsDepthAttachment = true)]
        private RenderGraphTexture m_Depth;

        private readonly RenderGraphTexture m_DefaultVisibilityBuffer;
        private readonly RenderGraphTexture m_DefaultAttributes0;
        private readonly RenderGraphTexture m_DefaultAttributes1;
        private readonly RenderGraphTexture m_DefaultBarycentrics;
        private readonly RenderGraphTexture m_DefaultDepth;
        private readonly RenderTargetIdentifier[] m_ColorTargets = new RenderTargetIdentifier[4];
        private readonly Material[] m_Materials = new Material[(int)VividRendererListID.Count];
        private readonly VividMeshShaderObject[] m_MeshShaderObjects = new VividMeshShaderObject[3];
        private readonly MaterialPropertyBlock m_DrawProperties = new MaterialPropertyBlock();
        private readonly float[] m_VirtualTextureSpaceParams = new float[VirtualTextureSpaceShaderParams.IntCount];
        private readonly float[] m_VirtualTextureMipOffsets = new float[VirtualTextureFeedbackProcessor.MaxMipCount];
        private readonly Vector4[] m_VirtualTextureLayerFallbacks = new Vector4[VTStackDesc.MaxLayerCount];
        private VividVirtualTextureFrameData m_VirtualTextureFrameData;
        private VividPrimitiveDrawSet m_PrimitiveDrawSet;
        private ComputeShader m_MeshletCullingCompute;
        private CameraHistoryTexture m_OccluderDepthPyramidHistory;
        private RTHandle m_CurrentOccluderDepthPyramid;
        private Camera m_Camera;
        private Matrix4x4 m_CurrentViewProjectionMatrix = Matrix4x4.identity;
        private Matrix4x4 m_VisibilityViewProjectionMatrix = Matrix4x4.identity;
        private GraphicsBuffer m_OccludedMeshletRenderRequestsBuffer;
        private GraphicsBuffer m_OccludedMeshletRenderRequestCounterBuffer;
        private GraphicsBuffer m_OccludedMeshletIndirectDispatchArgsBuffer;
        private GraphicsBuffer m_RecoveredMeshletRenderRequestsBuffer;
        private GraphicsBuffer m_RecoveredRendererListMeshletCountsBuffer;
        private GraphicsBuffer m_RecoveredMeshletIndirectDrawArgsBuffer;
        private GraphicsBuffer m_SpdGlobalAtomicBuffer;
        private int m_CopyOccluderDepthKernel = -1;
        private int m_DownsampleOccluderDepthKernel = -1;
        private int m_OccluderWidth;
        private int m_OccluderHeight;
        private int m_OccluderTextureWidth;
        private int m_OccluderTextureHeight;
        private int m_OccluderMipCount;
        private bool m_OcclusionCullingEnabled;
        private bool m_OcclusionHistoryValid;
        private bool m_OcclusionObservationMode;
        private bool m_MeshShaderInitializationAttempted;
        private bool m_MeshShaderFailureLogged;
        private int m_FrameIndex;

        [SerializeField, Tooltip("Experimental D3D12 mesh-shader path. Alpha-tested buckets continue to use DrawProceduralIndirect.")]
        private VisibilityBufferRasterizationPath m_RasterizationPath =
            VisibilityBufferRasterizationPath.DrawProceduralIndirect;

        public VisibilityBufferRasterizationPath RasterizationPath
        {
            get => m_RasterizationPath;
            set
            {
                if (m_RasterizationPath == value)
                    return;

                m_RasterizationPath = value;
                DisposeMeshShaderObjects();
            }
        }

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

            m_Attributes0 = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(
                    1,
                    1,
                    GraphicsFormat.R16G16B16A16_SFloat)
            };
            m_Attributes0.desc.Name = "VisibilityBufferAttributes0";
            m_Attributes0.desc.FilterMode = FilterMode.Point;
            m_Attributes0.desc.WrapMode = TextureWrapMode.Clamp;
            m_Attributes0.desc.ClearBuffer = true;
            m_Attributes0.desc.ClearColor = Color.clear;
            m_Attributes0.desc.UseMipMap = false;
            m_Attributes0.desc.AutoGenerateMips = false;
            m_Attributes0.desc.MipCount = 1;

            m_Attributes1 = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(
                    1,
                    1,
                    GraphicsFormat.R16G16B16A16_SFloat)
            };
            m_Attributes1.desc.Name = "VisibilityBufferAttributes1";
            m_Attributes1.desc.FilterMode = FilterMode.Point;
            m_Attributes1.desc.WrapMode = TextureWrapMode.Clamp;
            m_Attributes1.desc.ClearBuffer = true;
            m_Attributes1.desc.ClearColor = Color.clear;
            m_Attributes1.desc.UseMipMap = false;
            m_Attributes1.desc.AutoGenerateMips = false;
            m_Attributes1.desc.MipCount = 1;

            m_Barycentrics = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(
                    1,
                    1,
                    GraphicsFormat.R16G16_SFloat)
            };
            m_Barycentrics.desc.Name = "VisibilityBufferBarycentrics";
            m_Barycentrics.desc.FilterMode = FilterMode.Point;
            m_Barycentrics.desc.WrapMode = TextureWrapMode.Clamp;
            m_Barycentrics.desc.ClearBuffer = true;
            m_Barycentrics.desc.ClearColor = Color.clear;
            m_Barycentrics.desc.UseMipMap = false;
            m_Barycentrics.desc.AutoGenerateMips = false;
            m_Barycentrics.desc.MipCount = 1;

            m_Depth = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateDepthTarget(1, 1, DepthBits.Depth32)
            };
            m_Depth.desc.Name = "Depth";
            m_Depth.desc.ClearBuffer = false;
            m_DefaultVisibilityBuffer = m_VisibilityBuffer;
            m_DefaultAttributes0 = m_Attributes0;
            m_DefaultAttributes1 = m_Attributes1;
            m_DefaultBarycentrics = m_Barycentrics;
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
            m_VisibilityViewProjectionMatrix = m_CurrentViewProjectionMatrix;
            m_VirtualTextureFrameData = frameData.GetOrCreate<VividVirtualTextureFrameData>();
            m_FrameIndex = cameraData.frameIndex >= 0 ? cameraData.frameIndex : Time.frameCount;
            int width = cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth;
            int height = cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight;

            if (width <= 0)
                width = Mathf.Max(1, Screen.width);

            if (height <= 0)
                height = Mathf.Max(1, Screen.height);

            ResizePassOwnedTexture(m_VisibilityBuffer, m_DefaultVisibilityBuffer, width, height);
            ResizePassOwnedTexture(m_Attributes0, m_DefaultAttributes0, width, height);
            ResizePassOwnedTexture(m_Attributes1, m_DefaultAttributes1, width, height);
            ResizePassOwnedTexture(m_Barycentrics, m_DefaultBarycentrics, width, height);
            ResizePassOwnedTexture(m_Depth, m_DefaultDepth, width, height);

            var gpuDrivenFrameData = frameData.GetOrCreate<VividGPUDrivenFrameData>();
            m_PrimitiveDrawSet = gpuDrivenFrameData.primitiveDrawSet;
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

            if (m_RasterizationPath == VisibilityBufferRasterizationPath.ExperimentalMeshShader
                && VividGPUDrivenSystem.HasInstance)
            {
                ImportMeshShaderSceneBuffers(VividGPUDrivenSystem.instance);
            }

            PrepareOcclusionResources(gpuDrivenFrameData, width, height);
        }

        public override void Record(UnsafePassContext context)
        {
            if (m_VisibilityBuffer?.IsValid() != true
                || m_Attributes0?.IsValid() != true
                || m_Attributes1?.IsValid() != true
                || m_Barycentrics?.IsValid() != true
                || m_Depth?.IsValid() != true)
                return;

            var nativeCmd = context.GetNativeCommandBuffer();
            BindVisibilityTargets(nativeCmd, false);

            GraphicsBuffer visibleMeshletRenderRequestsBuffer = m_VisibleMeshletRenderRequests?.ImportedGraphicsBuffer;
            GraphicsBuffer visibleMeshletIndirectArgsBuffer = m_VisibleMeshletIndirectArgs?.ImportedGraphicsBuffer;
            bool hasGPUDrivenDraws = visibleMeshletRenderRequestsBuffer != null
                                     && visibleMeshletIndirectArgsBuffer != null;

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
                if (hasGPUDrivenDraws)
                {
                    DrawRendererLists(
                        nativeCmd,
                        visibleMeshletRenderRequestsBuffer,
                        visibleMeshletIndirectArgsBuffer,
                        system,
                        virtualTextureReady,
                        virtualTextureBinding);
                }

                if (!hasGPUDrivenDraws)
                    return;

                if (m_OcclusionObservationMode)
                {
                    ExecuteOcclusionRecovery(
                        nativeCmd,
                        system,
                        virtualTextureReady,
                        virtualTextureBinding);
                    return;
                }

                if (!CanGenerateCurrentOccluderDepthPyramid(system))
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

                ExecuteOcclusionRecovery(
                    nativeCmd,
                    system,
                    virtualTextureReady,
                    virtualTextureBinding);
            }
        }

        public override void Dispose()
        {
            m_VirtualTextureFrameData = null;
            m_PrimitiveDrawSet = null;
            m_MeshletCullingCompute = null;
            m_CopyOccluderDepthKernel = -1;
            m_DownsampleOccluderDepthKernel = -1;
            ReleaseSpdGlobalAtomicBuffer();
            DisposeMeshShaderObjects();
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
                || m_DownsampleOccluderDepthKernel < 0)
            {
                return;
            }

            bool observationMode = gpuDrivenFrameData.occlusionObservationMode;
            if (!observationMode
                && (m_Depth?.desc == null
                    || m_Depth.desc.MsaaSamples != MSAASamples.None
                    || m_Depth.desc.Dimension != TextureDimension.Tex2D
                    || m_Depth.desc.Slices > 1))
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

            if (observationMode)
            {
                var retestParameters = gpuDrivenFrameData.observationRetestParameters;
                if (!retestParameters.IsEnabled)
                {
                    ResetOcclusionFrameState();
                    return;
                }

                m_CurrentOccluderDepthPyramid = retestParameters.DepthPyramid;
                m_CurrentViewProjectionMatrix = retestParameters.ViewProjectionMatrix;
                m_OccluderWidth = retestParameters.Width;
                m_OccluderHeight = retestParameters.Height;
                m_OccluderTextureWidth = retestParameters.TextureWidth;
                m_OccluderTextureHeight = retestParameters.TextureHeight;
                m_OccluderMipCount = retestParameters.MipCount;
                PassRecorder.ImportTextureForPass(this, m_CurrentOccluderDepthPyramid, AccessFlags.Read);
                ImportOcclusionBuffers();
                m_OcclusionCullingEnabled = true;
                m_OcclusionHistoryValid = true;
                m_OcclusionObservationMode = true;
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
            EnsureSpdGlobalAtomicBuffer();
            PassRecorder.ImportBufferForPass(this, m_SpdGlobalAtomicBuffer, AccessFlags.ReadWrite);
            ImportOcclusionBuffers();
            m_OcclusionCullingEnabled = true;
            m_OcclusionHistoryValid = gpuDrivenFrameData.occlusionHistoryValid;
        }

        private void ImportOcclusionBuffers()
        {
            PassRecorder.ImportBufferForPass(this, m_OccludedMeshletRenderRequestsBuffer, AccessFlags.Read);
            PassRecorder.ImportBufferForPass(this, m_OccludedMeshletRenderRequestCounterBuffer, AccessFlags.Read);
            PassRecorder.ImportBufferForPass(this, m_OccludedMeshletIndirectDispatchArgsBuffer, AccessFlags.Read);
            PassRecorder.ImportBufferForPass(this, m_RecoveredMeshletRenderRequestsBuffer, AccessFlags.ReadWrite);
            PassRecorder.ImportBufferForPass(this, m_RecoveredRendererListMeshletCountsBuffer, AccessFlags.ReadWrite);
            PassRecorder.ImportBufferForPass(this, m_RecoveredMeshletIndirectDrawArgsBuffer, AccessFlags.ReadWrite);
        }

        private void ImportMeshShaderSceneBuffers(VividGPUDrivenSystem system)
        {
            VividGPUDrivenBufferSet buffers = system?.BufferSet;
            if (buffers == null)
                return;

            ImportMeshShaderSceneBuffer(buffers.InstanceDataBuffer);
            ImportMeshShaderSceneBuffer(buffers.MeshletsBuffer);
            ImportMeshShaderSceneBuffer(buffers.SharedVertexBuffer);
            ImportMeshShaderSceneBuffer(buffers.SharedIndexBuffer);
        }

        private void ImportMeshShaderSceneBuffer(GraphicsBuffer buffer)
        {
            if (buffer != null)
                PassRecorder.ImportBufferForPass(this, buffer, AccessFlags.Read);
        }

        private bool CanGenerateCurrentOccluderDepthPyramid(VividGPUDrivenSystem system)
        {
            return m_OcclusionCullingEnabled
                && !m_OcclusionObservationMode
                && system != null
                && m_MeshletCullingCompute != null
                && m_CopyOccluderDepthKernel >= 0
                && m_DownsampleOccluderDepthKernel >= 0
                && m_SpdGlobalAtomicBuffer != null
                && m_CurrentOccluderDepthPyramid != null
                && m_Depth?.innerHandle.IsValid() == true;
        }

        private bool ExecuteOcclusionRecovery(
            CommandBuffer cmd,
            VividGPUDrivenSystem system,
            bool virtualTextureReady,
            in VirtualTextureSpaceBinding virtualTextureBinding)
        {
            if (!m_OcclusionCullingEnabled
                || !m_OcclusionHistoryValid
                || system == null
                || m_MeshletCullingCompute == null
                || m_CurrentOccluderDepthPyramid == null
                || m_RecoveredMeshletRenderRequestsBuffer == null
                || m_RecoveredMeshletIndirectDrawArgsBuffer == null)
            {
                return false;
            }

            system.BindGlobals(cmd);
            if (!system.DispatchOcclusionRetest(
                    cmd,
                    m_MeshletCullingCompute,
                    m_CurrentOccluderDepthPyramid,
                    m_CurrentViewProjectionMatrix,
                    m_OccluderWidth,
                    m_OccluderHeight,
                    m_OccluderTextureWidth,
                    m_OccluderTextureHeight,
                    m_OccluderMipCount))
            {
                return false;
            }

            BindVisibilityTargets(cmd, clearTargets: false);
            DrawRendererLists(
                cmd,
                m_RecoveredMeshletRenderRequestsBuffer,
                m_RecoveredMeshletIndirectDrawArgsBuffer,
                system,
                virtualTextureReady,
                virtualTextureBinding);
            return true;
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

            if (m_OccluderMipCount <= 1)
                return;

            int dispatchGroupCountX = CoreUtils.DivRoundUp(m_OccluderTextureWidth, SpdTileSize);
            int dispatchGroupCountY = CoreUtils.DivRoundUp(m_OccluderTextureHeight, SpdTileSize);
            cmd.SetComputeIntParam(m_MeshletCullingCompute, s_SpdMipsId, m_OccluderMipCount - 1);
            cmd.SetComputeIntParam(
                m_MeshletCullingCompute,
                s_SpdNumWorkGroupsId,
                dispatchGroupCountX * dispatchGroupCountY);
            cmd.SetComputeVectorParam(m_MeshletCullingCompute, s_SpdWorkGroupOffsetId, Vector4.zero);
            cmd.SetComputeBufferParam(
                m_MeshletCullingCompute,
                m_DownsampleOccluderDepthKernel,
                s_SpdGlobalAtomicId,
                m_SpdGlobalAtomicBuffer);
            BindSpdMipTextureViews(
                cmd,
                m_MeshletCullingCompute,
                m_DownsampleOccluderDepthKernel,
                m_CurrentOccluderDepthPyramid,
                m_OccluderMipCount);
            cmd.DispatchCompute(
                m_MeshletCullingCompute,
                m_DownsampleOccluderDepthKernel,
                dispatchGroupCountX,
                dispatchGroupCountY,
                1);
        }

        private void EnsureSpdGlobalAtomicBuffer()
        {
            const int stride = sizeof(uint) * SpdAtomicCounterCount;
            if (m_SpdGlobalAtomicBuffer != null
                && m_SpdGlobalAtomicBuffer.count == 1
                && m_SpdGlobalAtomicBuffer.stride == stride)
            {
                return;
            }

            m_SpdGlobalAtomicBuffer?.Dispose();
            m_SpdGlobalAtomicBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, stride);
            m_SpdGlobalAtomicBuffer.SetData(s_ZeroSpdAtomicCounterData);
        }

        private void ReleaseSpdGlobalAtomicBuffer()
        {
            m_SpdGlobalAtomicBuffer?.Dispose();
            m_SpdGlobalAtomicBuffer = null;
        }

        private static void BindSpdMipTextureViews(
            CommandBuffer cmd,
            ComputeShader computeShader,
            int kernelIndex,
            RTHandle depthPyramid,
            int mipCount)
        {
            if (cmd == null || computeShader == null || depthPyramid == null)
                return;

            int boundMipCount = Mathf.Clamp(mipCount, 1, SpdMipTextureCount);
            for (int shaderMipIndex = 0; shaderMipIndex < s_SpdMipTextureIds.Length; shaderMipIndex++)
            {
                int boundMipIndex = Mathf.Clamp(shaderMipIndex, 0, boundMipCount - 1);
                cmd.SetComputeTextureParam(
                    computeShader,
                    kernelIndex,
                    s_SpdMipTextureIds[shaderMipIndex],
                    depthPyramid,
                    boundMipIndex);
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

            bool meshShaderRequested = m_RasterizationPath
                                       == VisibilityBufferRasterizationPath.ExperimentalMeshShader;
            bool compatibleTargets = !meshShaderRequested || HasMeshShaderCompatibleTargets();
            if (meshShaderRequested && !compatibleTargets)
            {
                LogMeshShaderFallback(
                    "The bound targets must use the default four MRT formats, D32_SFloat depth, Tex2D, and no MSAA.");
            }

            bool useMeshShader = meshShaderRequested
                                 && system != null
                                 && compatibleTargets
                                 && TryEnsureMeshShaderObjects();

            bool meshShaderStateDirty = false;
            for (int rendererListIndex = 0; rendererListIndex < m_Materials.Length; rendererListIndex++)
            {
                VividRendererListID batchKey = (VividRendererListID) rendererListIndex;
                bool alphaTest = (batchKey & VividRendererListID.AlphaTest) != 0;
                if (m_PrimitiveDrawSet?.IsBuilt == true)
                {
                    if (!m_PrimitiveDrawSet.TryGetBucket(batchKey, out VividPrimitiveDrawBucket bucket)
                        || bucket.DrawCount == 0u)
                    {
                        continue;
                    }
                }
                else if (system != null && !system.IsMainViewRendererBatchActive(batchKey))
                {
                    continue;
                }

                if (!virtualTextureReady && alphaTest)
                    continue;

                if (useMeshShader
                    && !alphaTest
                    && TryDrawMeshShaderRendererList(
                        cmd,
                        visibleMeshletRenderRequestsBuffer,
                        indirectArgsBuffer,
                        system,
                        batchKey,
                        rendererListIndex))
                {
                    meshShaderStateDirty = true;
                    continue;
                }

                Material material = m_Materials[rendererListIndex];
                if (material == null)
                    continue;

                if (meshShaderStateDirty)
                {
                    QueueMeshShaderStateBoundary(cmd);
                    meshShaderStateDirty = false;
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
                        m_FrameIndex,
                        m_VirtualTextureFrameData.AdaptiveMipBias);
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

            if (meshShaderStateDirty)
                QueueMeshShaderStateBoundary(cmd);
        }

        private static void QueueMeshShaderStateBoundary(CommandBuffer cmd)
        {
            VividMeshShaderPlugin.QueueStateBoundary(cmd);
        }

        private bool TryDrawMeshShaderRendererList(
            CommandBuffer cmd,
            GraphicsBuffer visibleMeshletRenderRequestsBuffer,
            GraphicsBuffer indirectArgsBuffer,
            VividGPUDrivenSystem system,
            VividRendererListID rendererListID,
            int rendererListIndex)
        {
            VividGPUDrivenBufferSet buffers = system?.BufferSet;
            VividMeshShaderObject shaderObject = ResolveMeshShaderObject(GetCullMode(rendererListID));
            if (buffers == null || shaderObject?.IsValid != true)
                return false;

            bool queued = VividMeshShaderPlugin.TryQueueDispatch(
                cmd,
                shaderObject,
                visibleMeshletRenderRequestsBuffer,
                indirectArgsBuffer,
                buffers.InstanceDataBuffer,
                buffers.MeshletsBuffer,
                buffers.SharedVertexBuffer,
                buffers.SharedIndexBuffer,
                (uint)rendererListIndex,
                (uint)Mathf.Max(0, visibleMeshletRenderRequestsBuffer.count),
                m_VisibilityViewProjectionMatrix,
                out string error);
            if (!queued)
                LogMeshShaderFallback(error);

            return queued;
        }

        private bool TryEnsureMeshShaderObjects()
        {
            if (m_MeshShaderInitializationAttempted)
                return AreMeshShaderObjectsValid();

            m_MeshShaderInitializationAttempted = true;
            if (!VividMeshShaderPlugin.TryGetSupport(
                    out VividMeshShaderSupportStatus supportStatus,
                    out string supportError))
            {
                if (supportStatus is VividMeshShaderSupportStatus.Unknown
                    or VividMeshShaderSupportStatus.NoDevice)
                {
                    m_MeshShaderInitializationAttempted = false;
                }
                LogMeshShaderFallback(supportError);
                return false;
            }

            VividMeshShaderProgramAsset programAsset =
                Resources.Load<VividMeshShaderProgramAsset>(MeshShaderProgramResourcePath);
            if (programAsset == null)
            {
                LogMeshShaderFallback(
                    $"Could not load precompiled mesh-shader program "
                    + $"'{MeshShaderProgramResourcePath}'.");
                return false;
            }

            VividMeshShaderCompareFunction depthCompare = SystemInfo.usesReversedZBuffer
                ? VividMeshShaderCompareFunction.GreaterEqual
                : VividMeshShaderCompareFunction.LessEqual;
            VividMeshShaderCullMode[] cullModes =
            {
                VividMeshShaderCullMode.None,
                VividMeshShaderCullMode.Front,
                VividMeshShaderCullMode.Back,
            };

            for (int shaderIndex = 0; shaderIndex < cullModes.Length; shaderIndex++)
            {
                var renderState = new VividMeshShaderRenderState(cullModes[shaderIndex], depthCompare);
                if (VividMeshShaderObject.TryCreate(
                        programAsset,
                        renderState,
                        out VividMeshShaderObject shaderObject,
                        out string creationError))
                {
                    m_MeshShaderObjects[shaderIndex] = shaderObject;
                    continue;
                }

                DisposeMeshShaderObjects();
                m_MeshShaderInitializationAttempted = true;
                LogMeshShaderFallback(creationError);
                return false;
            }

            return true;
        }

        private bool AreMeshShaderObjectsValid()
        {
            for (int shaderIndex = 0; shaderIndex < m_MeshShaderObjects.Length; shaderIndex++)
            {
                if (m_MeshShaderObjects[shaderIndex]?.IsValid != true)
                    return false;
            }

            return true;
        }

        private VividMeshShaderObject ResolveMeshShaderObject(CullMode cullMode)
        {
            int shaderIndex = cullMode switch
            {
                CullMode.Off => 0,
                CullMode.Front => 1,
                _ => 2,
            };
            return m_MeshShaderObjects[shaderIndex];
        }

        private void DisposeMeshShaderObjects()
        {
            for (int shaderIndex = 0; shaderIndex < m_MeshShaderObjects.Length; shaderIndex++)
            {
                m_MeshShaderObjects[shaderIndex]?.Dispose();
                m_MeshShaderObjects[shaderIndex] = null;
            }

            m_MeshShaderInitializationAttempted = false;
            m_MeshShaderFailureLogged = false;
        }

        private void LogMeshShaderFallback(string reason)
        {
            if (m_MeshShaderFailureLogged)
                return;

            m_MeshShaderFailureLogged = true;
            Debug.LogWarning(
                $"[VividRP] Experimental VisibilityBuffer mesh-shader path is unavailable; "
                + $"falling back to DrawProceduralIndirect. {reason}");
        }

        private bool HasMeshShaderCompatibleTargets()
        {
            return HasMeshShaderCompatibleColorTarget(
                       m_VisibilityBuffer,
                       GraphicsFormat.R32G32_UInt)
                   && HasMeshShaderCompatibleColorTarget(
                       m_Attributes0,
                       GraphicsFormat.R16G16B16A16_SFloat)
                   && HasMeshShaderCompatibleColorTarget(
                       m_Attributes1,
                       GraphicsFormat.R16G16B16A16_SFloat)
                   && HasMeshShaderCompatibleColorTarget(
                       m_Barycentrics,
                       GraphicsFormat.R16G16_SFloat)
                   && m_Depth?.desc != null
                   && m_Depth.desc.DepthBufferBits == DepthBits.Depth32
                   && GraphicsFormatUtility.GetDepthStencilFormat(32, 0)
                   == GraphicsFormat.D32_SFloat
                   && HasMeshShaderCompatibleTextureLayout(m_Depth.desc);
        }

        private static bool HasMeshShaderCompatibleColorTarget(
            RenderGraphTexture texture,
            GraphicsFormat format)
        {
            return texture?.desc != null
                   && texture.desc.ColorFormat == format
                   && HasMeshShaderCompatibleTextureLayout(texture.desc);
        }

        private static bool HasMeshShaderCompatibleTextureLayout(RenderGraphTextureDesc desc)
        {
            return desc.Dimension == TextureDimension.Tex2D
                   && desc.Slices == 1
                   && desc.MsaaSamples == MSAASamples.None;
        }

        private void BindVisibilityTargets(CommandBuffer cmd, bool clearTargets)
        {
            m_ColorTargets[0] = m_VisibilityBuffer;
            m_ColorTargets[1] = m_Attributes0;
            m_ColorTargets[2] = m_Attributes1;
            m_ColorTargets[3] = m_Barycentrics;
            cmd.SetRenderTarget(m_ColorTargets, m_Depth);
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
            m_VisibilityViewProjectionMatrix = Matrix4x4.identity;
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
            m_OcclusionObservationMode = false;
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
