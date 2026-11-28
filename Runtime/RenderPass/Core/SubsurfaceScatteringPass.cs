using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    /// <summary>
    /// Standard RenderGraph screen-space Subsurface Scattering pass implementing
    /// Christensen-Burley 2015 Normalized Diffusion (UE5 Substrate SSS_TYPE_DIFFUSION model):
    ///   - Consumes DeferredLightingPass's RGBA16F SubsurfaceDiffuse (rgb = diff/sqrt(sssColor), a = linearViewZ)
    ///   - Sparse 8x8 tile classification + indirect dispatch (0 groups when no SSS pixels visible)
    ///   - Single-pass 3D bilateral Burley disk scattering + sqrt(sssColor) remodulation into Color
    ///   - Zero hooks: automatically coordinates Diffuse/Specular split with DeferredLightingPass.
    /// </summary>
    [FrameResourceSlot(FrameResourceSlots.SubstrateClosureBufferName, AccessFlags.Read, Optional = true)]
    public sealed class SubsurfaceScatteringPass : ComputePass, IBlueNoiseConsumerPass
    {
        private const string ClearArgsKernelName = "KClearSubsurfaceTileArgs";
        private const string ClassifyTilesKernelName = "KClassifySubsurfaceTiles";
        private const string ScatterKernelName = "KSubsurfaceBurleyScatter";
        private const int TileSize = 8;

        private static readonly int SubsurfaceDiffuseId = Shader.PropertyToID("_SubsurfaceDiffuse");
        private static readonly int SubstrateClosureBufferId = Shader.PropertyToID("_SubstrateClosureBuffer");
        private static readonly int SubsurfaceBlueNoiseId = Shader.PropertyToID("_SubsurfaceBlueNoise");
        private static readonly int SceneColorId = Shader.PropertyToID("_SceneColor");
        private static readonly int SubsurfaceTileListId = Shader.PropertyToID("_SubsurfaceTileList");
        private static readonly int SubsurfaceTileListSrvId = Shader.PropertyToID("_SubsurfaceTileListSRV");
        private static readonly int SubsurfaceIndirectArgsId = Shader.PropertyToID("_SubsurfaceIndirectArgs");

        private static readonly int ResolutionId = Shader.PropertyToID("_SubsurfaceResolution");
        private static readonly int InvResolutionId = Shader.PropertyToID("_SubsurfaceInvResolution");
        private static readonly int ProjScaleYId = Shader.PropertyToID("_SubsurfaceProjScaleY");
        private static readonly int MaxScreenRadiusId = Shader.PropertyToID("_SubsurfaceMaxScreenRadius");
        private static readonly int WorldScaleId = Shader.PropertyToID("_SubsurfaceWorldScale");
        private static readonly int QualityModeId = Shader.PropertyToID("_SubsurfaceQualityMode");
        private static readonly int FrameIndexId = Shader.PropertyToID("_SubsurfaceFrameIndex");
        private static readonly int HasBlueNoiseId = Shader.PropertyToID("_SubsurfaceHasBlueNoise");
        private static readonly int BlueNoiseSliceCountId = Shader.PropertyToID("_SubsurfaceBlueNoiseSliceCount");
        private static readonly int DebugModeId = Shader.PropertyToID("_SubsurfaceDebugMode");

        private static int s_LiveInstanceCount;

        public static bool EnableSubsurfaceScattering { get; set; } = true;
        public static int QualityMode { get; set; } = 0; // 0 = 16-tap Blue-Noise Burley, 1 = 32-tap Deterministic Golden-Angle
        public static float GlobalWorldScale { get; set; } = 1.0f;
        public static float MaxScreenRadiusPixels { get; set; } = 48.0f;
        public static int DebugMode { get; set; } = 0; // 0 = composite, 1 = scattered diffuse only, 2 = MFP visualization

        internal static bool ShouldSplitDeferredDiffuse => EnableSubsurfaceScattering && s_LiveInstanceCount > 0;

        [RenderGraphResource(Name = "Color", Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_ColorTexture;

        [RenderGraphResource(Name = "SubsurfaceDiffuse", Access = AccessFlags.Read)]
        private RenderGraphTexture m_SubsurfaceDiffuse;

        private readonly RenderGraphTexture m_LocalSubsurfaceDiffuse;

        private ComputeShader m_Compute;
        private int m_ClearArgsKernel = -1;
        private int m_ClassifyKernel = -1;
        private int m_ScatterKernel = -1;
        private bool m_RegisteredLiveInstance;

        private int m_Width = 1;
        private int m_Height = 1;
        private int m_TileCountX = 1;
        private int m_TileCountY = 1;
        private float m_ProjScaleY = 1.0f;
        private uint m_FrameIndex;
        private TextureHandle m_SubstrateClosureBufferHandle;
        private TextureHandle m_BlueNoiseVectorHandle;
        private int m_BlueNoiseSliceCount;
        private bool m_HasSubstrateClosureBuffer;

        private GraphicsBuffer m_TileListBuffer;
        private int m_TileListCapacity;
        private GraphicsBuffer m_IndirectArgsBuffer;
        private readonly int[] m_ResolutionPair = new int[2];

        public SubsurfaceScatteringPass()
        {
            profilingSampler = new ProfilingSampler(nameof(SubsurfaceScatteringPass));
            m_ColorTexture = RenderGraphTexture.CreateColorTarget("Color", GraphicsFormat.R16G16B16A16_SFloat);
            m_ColorTexture.desc.EnableRandomWrite = true;
            m_LocalSubsurfaceDiffuse = RenderGraphTexture.CreateInput("SubsurfaceDiffuse", GraphicsFormat.R16G16B16A16_SFloat);
            m_LocalSubsurfaceDiffuse.desc.Width = 1;
            m_LocalSubsurfaceDiffuse.desc.Height = 1;
            m_SubsurfaceDiffuse = m_LocalSubsurfaceDiffuse;
        }

        public override void Create()
        {
            if (!m_RegisteredLiveInstance)
            {
                s_LiveInstanceCount++;
                m_RegisteredLiveInstance = true;
            }

            EnsureShaderLoaded();
        }

        private bool EnsureShaderLoaded()
        {
            if (m_Compute != null && m_ClearArgsKernel >= 0 && m_ClassifyKernel >= 0 && m_ScatterKernel >= 0)
                return true;

            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_Compute = resources?.SubsurfaceScatteringCompute;
            if (m_Compute == null)
                return false;

            m_ClearArgsKernel = m_Compute.FindKernel(ClearArgsKernelName);
            m_ClassifyKernel = m_Compute.FindKernel(ClassifyTilesKernelName);
            m_ScatterKernel = m_Compute.FindKernel(ScatterKernelName);
            return m_ClearArgsKernel >= 0 && m_ClassifyKernel >= 0 && m_ScatterKernel >= 0;
        }

        public override bool IsActive(ContextContainer frameData)
        {
            if (!EnableSubsurfaceScattering
                || ReferenceEquals(m_SubsurfaceDiffuse, m_LocalSubsurfaceDiffuse)
                || !EnsureShaderLoaded())
            {
                return false;
            }

            return true;
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            int width = m_ColorTexture?.desc != null && m_ColorTexture.desc.Width > 0
                ? m_ColorTexture.desc.Width
                : (cameraData != null && cameraData.actualWidth > 0 ? cameraData.actualWidth : Screen.width);
            int height = m_ColorTexture?.desc != null && m_ColorTexture.desc.Height > 0
                ? m_ColorTexture.desc.Height
                : (cameraData != null && cameraData.actualHeight > 0 ? cameraData.actualHeight : Screen.height);

            m_Width = Mathf.Max(1, width);
            m_Height = Mathf.Max(1, height);
            m_TileCountX = Mathf.Max(1, (m_Width + TileSize - 1) / TileSize);
            m_TileCountY = Mathf.Max(1, (m_Height + TileSize - 1) / TileSize);

            if (m_ColorTexture?.desc != null)
                m_ColorTexture.desc.EnableRandomWrite = true;

            Matrix4x4 proj = cameraData != null ? cameraData.GetProjectionMatrix() : Matrix4x4.identity;
            m_ProjScaleY = Mathf.Max(Mathf.Abs(proj.m11), 1e-3f);
            m_FrameIndex = (uint)Time.frameCount;

            EnsureScratchBuffers(m_TileCountX * m_TileCountY);

            m_HasSubstrateClosureBuffer = VividSubstrateMaterialFrameData.ImportClosureBufferOrDummyForPass(
                this,
                frameData,
                m_Width,
                m_Height,
                out m_SubstrateClosureBufferHandle);

            m_BlueNoiseVectorHandle = default;
            m_BlueNoiseSliceCount = 0;
            if (BlueNoise.Instance != null && BlueNoise.Instance.VectorAtlas.IsValid())
            {
                m_BlueNoiseVectorHandle = BlueNoise.Instance.VectorAtlas;
                m_BlueNoiseSliceCount = 64;
            }

            if (PassRecorder.IsPassTextureImportActive)
            {
                if (m_TileListBuffer != null && m_TileListBuffer.IsValid())
                    PassRecorder.ImportBufferForPass(this, m_TileListBuffer, AccessFlags.ReadWrite);
                if (m_IndirectArgsBuffer != null && m_IndirectArgsBuffer.IsValid())
                    PassRecorder.ImportBufferForPass(this, m_IndirectArgsBuffer, AccessFlags.ReadWrite);
            }
        }

        private void EnsureScratchBuffers(int tileCount)
        {
            int required = Mathf.Max(1, tileCount);
            if (m_TileListBuffer == null || !m_TileListBuffer.IsValid() || m_TileListCapacity < required)
            {
                m_TileListBuffer?.Dispose();
                m_TileListBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, required, sizeof(uint))
                {
                    name = "VividSSS_TileList",
                };
                m_TileListCapacity = required;
            }

            if (m_IndirectArgsBuffer == null || !m_IndirectArgsBuffer.IsValid())
            {
                m_IndirectArgsBuffer?.Dispose();
                m_IndirectArgsBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments,
                    4,
                    sizeof(uint))
                {
                    name = "VividSSS_IndirectArgs",
                };
            }
        }

        public override void Record(ComputePassContext context)
        {
            if (!EnsureShaderLoaded()
                || m_ColorTexture == null
                || !m_ColorTexture.innerHandle.IsValid()
                || m_SubsurfaceDiffuse == null
                || !m_SubsurfaceDiffuse.innerHandle.IsValid()
                || m_TileListBuffer == null
                || !m_TileListBuffer.IsValid()
                || m_IndirectArgsBuffer == null
                || !m_IndirectArgsBuffer.IsValid())
            {
                return;
            }

            ComputeCommandBuffer cmd = context.cmd;
            using (new ProfilingScope(cmd, profilingSampler))
            {
                m_ResolutionPair[0] = m_Width;
                m_ResolutionPair[1] = m_Height;
                cmd.SetComputeIntParams(m_Compute, ResolutionId, m_ResolutionPair);
                cmd.SetComputeVectorParam(
                    m_Compute,
                    InvResolutionId,
                    new Vector4(1.0f / m_Width, 1.0f / m_Height, 0.0f, 0.0f));
                cmd.SetComputeFloatParam(m_Compute, ProjScaleYId, m_ProjScaleY);
                cmd.SetComputeFloatParam(m_Compute, MaxScreenRadiusId, Mathf.Clamp(MaxScreenRadiusPixels, 4.0f, 96.0f));
                cmd.SetComputeFloatParam(m_Compute, WorldScaleId, Mathf.Max(GlobalWorldScale, 0.0f));
                cmd.SetComputeIntParam(m_Compute, QualityModeId, QualityMode);
                cmd.SetComputeIntParam(m_Compute, FrameIndexId, (int)m_FrameIndex);
                cmd.SetComputeIntParam(m_Compute, DebugModeId, DebugMode);

                // 1. Reset indirect dispatch args
                cmd.SetComputeBufferParam(m_Compute, m_ClearArgsKernel, SubsurfaceIndirectArgsId, m_IndirectArgsBuffer);
                cmd.DispatchCompute(m_Compute, m_ClearArgsKernel, 1, 1, 1);

                // 2. Sparse 8x8 tile classification (only tiles with SubsurfaceDiffuse.a > 0)
                cmd.SetComputeTextureParam(m_Compute, m_ClassifyKernel, SubsurfaceDiffuseId, m_SubsurfaceDiffuse.innerHandle);
                cmd.SetComputeBufferParam(m_Compute, m_ClassifyKernel, SubsurfaceTileListId, m_TileListBuffer);
                cmd.SetComputeBufferParam(m_Compute, m_ClassifyKernel, SubsurfaceIndirectArgsId, m_IndirectArgsBuffer);
                cmd.DispatchCompute(m_Compute, m_ClassifyKernel, m_TileCountX, m_TileCountY, 1);

                // 3. Indirect Burley Normalized Diffusion + Recombine into Color (0 groups if no SSS pixels)
                bool hasBlueNoise = m_BlueNoiseVectorHandle.IsValid() && m_BlueNoiseSliceCount > 0;
                cmd.SetComputeIntParam(m_Compute, HasBlueNoiseId, hasBlueNoise ? 1 : 0);
                cmd.SetComputeIntParam(m_Compute, BlueNoiseSliceCountId, hasBlueNoise ? m_BlueNoiseSliceCount : 0);
                cmd.SetComputeTextureParam(
                    m_Compute,
                    m_ScatterKernel,
                    SubsurfaceBlueNoiseId,
                    hasBlueNoise ? m_BlueNoiseVectorHandle : context.renderGraphContext.defaultResources.blackTexture);

                cmd.SetComputeTextureParam(m_Compute, m_ScatterKernel, SubsurfaceDiffuseId, m_SubsurfaceDiffuse.innerHandle);
                if (m_SubstrateClosureBufferHandle.IsValid())
                    cmd.SetComputeTextureParam(m_Compute, m_ScatterKernel, SubstrateClosureBufferId, m_SubstrateClosureBufferHandle);
                cmd.SetComputeTextureParam(m_Compute, m_ScatterKernel, SceneColorId, m_ColorTexture.innerHandle);
                cmd.SetComputeBufferParam(m_Compute, m_ScatterKernel, SubsurfaceTileListSrvId, m_TileListBuffer);
                cmd.DispatchCompute(m_Compute, m_ScatterKernel, m_IndirectArgsBuffer, 0u);
            }
        }

        public override void Dispose()
        {
            if (m_RegisteredLiveInstance)
            {
                s_LiveInstanceCount = Mathf.Max(0, s_LiveInstanceCount - 1);
                m_RegisteredLiveInstance = false;
            }

            m_TileListBuffer?.Dispose();
            m_TileListBuffer = null;
            m_TileListCapacity = 0;
            m_IndirectArgsBuffer?.Dispose();
            m_IndirectArgsBuffer = null;
            m_Compute = null;
            m_ClearArgsKernel = -1;
            m_ClassifyKernel = -1;
            m_ScatterKernel = -1;
            m_SubstrateClosureBufferHandle = default;
            m_BlueNoiseVectorHandle = default;
            m_HasSubstrateClosureBuffer = false;
        }
    }
}
