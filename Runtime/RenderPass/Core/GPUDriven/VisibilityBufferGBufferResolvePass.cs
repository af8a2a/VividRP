using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.VirtualTexture;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class VisibilityBufferGBufferResolvePass : UnsafePass, IAllowGlobalStateModificationPass
    {
        internal const int DualSlabSidecarTileSize = 8;
        internal const int ComputeResolveGroupSize = 8;
        internal const string VisibilityBufferGBufferResolveShaderName = "Hidden/VividRP/GPUDriven/VisibilityBufferGBufferResolve";
        internal const string ResolveVisibilityBufferKernelName = "ResolveVisibilityBuffer";
        internal const string ClearMaterialBinningArgsKernelName = "ClearMaterialBinningArgs";
        internal const string BinVisibilityTilesByProgramKernelName = "BinVisibilityTilesByProgram";
        internal const string ResolveVisibilityBufferBinnedKernelName = "ResolveVisibilityBufferBinned";
        internal const string DualSlabSidecarKeyword = "VIVID_DUAL_SLAB_SIDECAR_OUTPUT";
        internal const string DualSlabSidecarTiledKeyword =
            "VIVID_DUAL_SLAB_SIDECAR_TILED";
        private const string ClearDualSlabSidecarDrawArgsKernelName =
            "ClearDualSlabSidecarDrawArgs";
        private const string ClassifyDualSlabSidecarTilesKernelName =
            "ClassifyDualSlabSidecarTiles";
        private const int IndirectDrawArgsElementCount = 4;

        private static readonly int VisibilityBufferId = Shader.PropertyToID("_VisibilityBuffer");
        private static readonly int VisibilityBufferScaleBiasId = Shader.PropertyToID("_VisibilityBufferScaleBias");
        private static readonly int VisibilityBufferAttributes0Id = Shader.PropertyToID("_VisibilityBufferAttributes0");
        private static readonly int VisibilityBufferAttributes0ScaleBiasId = Shader.PropertyToID("_VisibilityBufferAttributes0ScaleBias");
        private static readonly int VisibilityBufferAttributes1Id = Shader.PropertyToID("_VisibilityBufferAttributes1");
        private static readonly int VisibilityBufferAttributes1ScaleBiasId = Shader.PropertyToID("_VisibilityBufferAttributes1ScaleBias");
        private static readonly int VisibilityBufferBarycentricsId = Shader.PropertyToID("_VisibilityBufferBarycentrics");
        private static readonly int VisibilityBufferBarycentricsScaleBiasId = Shader.PropertyToID("_VisibilityBufferBarycentricsScaleBias");
        private static readonly int ResolveOutputSizeId = Shader.PropertyToID("_ResolveOutputSize");
        private static readonly int WriteDualSlabSidecarId = Shader.PropertyToID("_WriteDualSlabSidecar");
        private static readonly int WriteSubstrateClosureBufferId = Shader.PropertyToID("_WriteSubstrateClosureBuffer");
        private static readonly int OutGBuffer0Id = Shader.PropertyToID("_OutGBuffer0");
        private static readonly int OutGBuffer1Id = Shader.PropertyToID("_OutGBuffer1");
        private static readonly int OutGBuffer2Id = Shader.PropertyToID("_OutGBuffer2");
        private static readonly int OutGBuffer3Id = Shader.PropertyToID("_OutGBuffer3");
        private static readonly int OutGBuffer4Id = Shader.PropertyToID("_OutGBuffer4");
        private static readonly int OutLayerAux0Id = Shader.PropertyToID("_OutLayerAux0");
        private static readonly int OutLayerAux1Id = Shader.PropertyToID("_OutLayerAux1");
        private static readonly int OutSubstrateClosureBufferId = Shader.PropertyToID("_OutSubstrateClosureBuffer");
        private static readonly int MaterialBinTileStrideId = Shader.PropertyToID("_MaterialBinTileStride");
        private static readonly int TargetMaterialBinId = Shader.PropertyToID("_TargetMaterialBin");
        private static readonly int MaterialBinTileListId = Shader.PropertyToID("_MaterialBinTileList");
        private static readonly int MaterialBinTileListSRVId = Shader.PropertyToID("_MaterialBinTileListSRV");
        private static readonly int MaterialBinIndirectArgsId = Shader.PropertyToID("_MaterialBinIndirectArgs");
        private static readonly int GBuffer0Id = Shader.PropertyToID("_GBuffer0");
        private static readonly int GBuffer1Id = Shader.PropertyToID("_GBuffer1");
        private static readonly int ClassificationWidthId = Shader.PropertyToID("_ClassificationWidth");
        private static readonly int ClassificationHeightId = Shader.PropertyToID("_ClassificationHeight");
        private static readonly int MaterialFeatureTileListId = Shader.PropertyToID("_MaterialFeatureTileList");
        private static readonly int MaterialFeatureIndirectArgsId = Shader.PropertyToID("_MaterialFeatureIndirectArgs");
        private static readonly int DualSlabSidecarTileListId = Shader.PropertyToID("_DualSlabSidecarTileList");
        private static readonly int DualSlabSidecarScreenSizeId = Shader.PropertyToID("_DualSlabSidecarScreenSize");

        [RenderGraphResource(Name = "VisibilityBuffer", Access = AccessFlags.Read)]
        private RenderGraphTexture m_VisibilityBuffer;

        [RenderGraphResource(Name = "VisibilityBufferAttributes0", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Attributes0;

        [RenderGraphResource(Name = "VisibilityBufferAttributes1", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Attributes1;

        [RenderGraphResource(Name = "VisibilityBufferBarycentrics", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Barycentrics;
        
        [RenderGraphResource(
            Name = "GBuffer0",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable,
            AttachmentIndex = 0)]
        private RenderGraphTexture m_GBuffer0;

        [RenderGraphResource(
            Name = "GBuffer1",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable,
            AttachmentIndex = 1)]
        private RenderGraphTexture m_GBuffer1;

        [RenderGraphResource(
            Name = "GBuffer2",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable,
            AttachmentIndex = 2)]
        private RenderGraphTexture m_GBuffer2;

        [RenderGraphResource(
            Name = "GBuffer3",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable,
            AttachmentIndex = 3)]
        private RenderGraphTexture m_GBuffer3;

        [RenderGraphResource(
            Name = "DiffuseIrradiance",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable,
            AttachmentIndex = 4)]
        // Keep the legacy field name as the serialized RenderGraph port key.
        private RenderGraphTexture m_GBuffer4;

        [RenderGraphResource(
            Name = "LayerAux0",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable,
            AttachmentIndex = 5)]
        private RenderGraphTexture m_LayerAux0;

        [RenderGraphResource(
            Name = "LayerAux1",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable,
            AttachmentIndex = 6)]
        private RenderGraphTexture m_LayerAux1;

        [RenderGraphResource(
            Name = "DualSlabSidecarTileList",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphBuffer m_DualSlabSidecarTileList;

        [RenderGraphResource(
            Name = "DualSlabSidecarIndirectDrawArgs",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphBuffer m_DualSlabSidecarIndirectDrawArgs;

        private readonly RenderGraphTexture m_DefaultGBuffer0;
        private readonly RenderGraphTexture m_DefaultGBuffer1;
        private readonly RenderGraphTexture m_DefaultGBuffer2;
        private readonly RenderGraphTexture m_DefaultGBuffer3;
        private readonly RenderGraphTexture m_DefaultDiffuseIrradiance;
        private readonly RenderGraphTexture m_DefaultLayerAux0;
        private readonly RenderGraphTexture m_DefaultLayerAux1;
        private readonly RenderTargetIdentifier[] m_GBufferColorTargets = new RenderTargetIdentifier[5];
        private readonly RenderTargetIdentifier[] m_DualSlabSidecarTargets = new RenderTargetIdentifier[2];
        private readonly MaterialPropertyBlock m_DrawProperties = new MaterialPropertyBlock();
        private readonly float[] m_VirtualTextureSpaceParams = new float[VirtualTextureSpaceShaderParams.IntCount];
        private readonly float[] m_VirtualTextureMipOffsets = new float[VirtualTextureFeedbackProcessor.MaxMipCount];
        private readonly Vector4[] m_VirtualTextureLayerFallbacks = new Vector4[VTStackDesc.MaxLayerCount];

        [SerializeField, Min(1f)]
        private float m_VirtualTextureFeedbackSampleRate = 4.0f;

        [SerializeField, Range(VividSubstrateContract.OpenPBROpaqueSingleTotalSlices, VividSubstrateContract.MaxSliceCount)]
        private int m_SubstrateSliceBudget = VividSubstrateContract.DefaultSliceCount;

        [SerializeField]
        private bool m_EnableMaterialBinning = true;

        private ComputeShader m_ResolveCompute;
        private int m_ResolveVisibilityBufferKernel = -1;
        private int m_ClearMaterialBinningArgsKernel = -1;
        private int m_BinVisibilityTilesByProgramKernel = -1;
        private int m_ResolveVisibilityBufferBinnedKernel = -1;
        private GraphicsBuffer m_MaterialBinTileListBuffer;
        private GraphicsBuffer m_MaterialBinIndirectArgsBuffer;
        private int m_MaterialBinTileStride = 1;
        private VividSubstrateMaterialFrameData m_SubstrateFrameData;
        private TextureHandle m_SubstrateClosureBufferHandle;
        private bool m_RequiresSubstrateBuffer;

        private Material m_Material;
        private Material m_DualSlabSidecarMaterial;
        private Material m_DualSlabSidecarTiledMaterial;
        private ComputeShader m_MaterialClassificationCompute;
        private int m_ClearDualSlabSidecarDrawArgsKernel = -1;
        private int m_ClassifyDualSlabSidecarTilesKernel = -1;
        private VividVirtualTextureFrameData m_VirtualTextureFrameData;
        private int m_FrameIndex;
        private int m_ResolveWidth = 1;
        private int m_ResolveHeight = 1;
        private int m_DualSlabSidecarTileCountX = 1;
        private int m_DualSlabSidecarTileCountY = 1;
        private bool m_RequiresDualSlabSidecar;

        public VisibilityBufferGBufferResolvePass()
        {
            profilingSampler = new ProfilingSampler(nameof(VisibilityBufferGBufferResolvePass));

            m_VisibilityBuffer = RenderGraphTexture.CreateInput("VisibilityBuffer", GraphicsFormat.R32G32_UInt);
            m_VisibilityBuffer.desc.FilterMode = FilterMode.Point;
            m_Attributes0 = RenderGraphTexture.CreateInput(
                "VisibilityBufferAttributes0",
                GraphicsFormat.R32G32B32A32_SFloat);
            m_Attributes0.desc.FilterMode = FilterMode.Point;
            m_Attributes1 = RenderGraphTexture.CreateInput(
                "VisibilityBufferAttributes1",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_Attributes1.desc.FilterMode = FilterMode.Point;
            m_Barycentrics = RenderGraphTexture.CreateInput(
                "VisibilityBufferBarycentrics",
                GraphicsFormat.R16G16_SFloat);
            m_Barycentrics.desc.FilterMode = FilterMode.Point;

            m_GBuffer0 = RenderGraphTexture.CreateColorTarget("GBuffer0", GraphicsFormat.R8G8B8A8_UNorm);
            m_GBuffer0.desc.EnableRandomWrite = true;
            m_GBuffer1 = RenderGraphTexture.CreateColorTarget("GBuffer1", GraphicsFormat.A2B10G10R10_UNormPack32);
            m_GBuffer1.desc.EnableRandomWrite = true;
            m_GBuffer2 = RenderGraphTexture.CreateColorTarget("GBuffer2", GraphicsFormat.R8G8B8A8_UNorm);
            m_GBuffer2.desc.EnableRandomWrite = true;
            m_GBuffer3 = RenderGraphTexture.CreateColorTarget("GBuffer3", GraphicsFormat.B10G11R11_UFloatPack32);
            m_GBuffer3.desc.EnableRandomWrite = true;
            m_GBuffer4 = RenderGraphTexture.CreateColorTarget(
                "DiffuseIrradiance",
                GraphicsFormat.B10G11R11_UFloatPack32);
            m_GBuffer4.desc.EnableRandomWrite = true;
            m_LayerAux0 = RenderGraphTexture.CreateColorTarget(
                "LayerAux0",
                GraphicsFormat.R8G8B8A8_UNorm);
            m_LayerAux0.desc.EnableRandomWrite = true;
            m_LayerAux1 = RenderGraphTexture.CreateColorTarget(
                "LayerAux1",
                GraphicsFormat.R8G8B8A8_UNorm);
            m_LayerAux1.desc.EnableRandomWrite = true;
            m_DualSlabSidecarTileList = RenderGraphBuffer.CreateStructured(
                "DualSlabSidecarTileList",
                1,
                sizeof(uint));
            m_DualSlabSidecarIndirectDrawArgs = new RenderGraphBuffer
            {
                desc = new RenderGraphBufferDesc
                {
                    Count = IndirectDrawArgsElementCount,
                    Stride = sizeof(uint),
                    Target = GraphicsBuffer.Target.Structured
                        | GraphicsBuffer.Target.IndirectArguments,
                    Name = "DualSlabSidecarIndirectDrawArgs",
                }
            };

            m_DefaultGBuffer0 = m_GBuffer0;
            m_DefaultGBuffer1 = m_GBuffer1;
            m_DefaultGBuffer2 = m_GBuffer2;
            m_DefaultGBuffer3 = m_GBuffer3;
            m_DefaultDiffuseIrradiance = m_GBuffer4;
            m_DefaultLayerAux0 = m_LayerAux0;
            m_DefaultLayerAux1 = m_LayerAux1;
        }

        public override void Create()
        {
            var coreResources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_ResolveCompute = coreResources?.VisibilityBufferGBufferResolveCompute;
            m_ResolveVisibilityBufferKernel = TryFindKernel(
                m_ResolveCompute,
                ResolveVisibilityBufferKernelName);
            m_ClearMaterialBinningArgsKernel = TryFindKernel(
                m_ResolveCompute,
                ClearMaterialBinningArgsKernelName);
            m_BinVisibilityTilesByProgramKernel = TryFindKernel(
                m_ResolveCompute,
                BinVisibilityTilesByProgramKernelName);
            m_ResolveVisibilityBufferBinnedKernel = TryFindKernel(
                m_ResolveCompute,
                ResolveVisibilityBufferBinnedKernelName);

            var shader = Shader.Find(VisibilityBufferGBufferResolveShaderName);
            if (shader != null)
            {
                m_Material = CoreUtils.CreateEngineMaterial(shader);
                m_DualSlabSidecarMaterial = CoreUtils.CreateEngineMaterial(shader);
                m_DualSlabSidecarTiledMaterial = CoreUtils.CreateEngineMaterial(shader);
                CoreUtils.SetKeyword(
                    m_DualSlabSidecarMaterial,
                    DualSlabSidecarKeyword,
                    true);
                CoreUtils.SetKeyword(
                    m_DualSlabSidecarTiledMaterial,
                    DualSlabSidecarKeyword,
                    true);
                CoreUtils.SetKeyword(
                    m_DualSlabSidecarTiledMaterial,
                    DualSlabSidecarTiledKeyword,
                    true);
            }
            else if (m_ResolveCompute == null)
            {
                Debug.LogWarning(
                    $"[VividRP] Could not find compute shader or fallback shader '{VisibilityBufferGBufferResolveShaderName}' for {nameof(VisibilityBufferGBufferResolvePass)}.");
            }

            m_MaterialClassificationCompute = coreResources?.MaterialClassificationCompute;
            m_ClearDualSlabSidecarDrawArgsKernel = TryFindKernel(
                m_MaterialClassificationCompute,
                ClearDualSlabSidecarDrawArgsKernelName);
            m_ClassifyDualSlabSidecarTilesKernel = TryFindKernel(
                m_MaterialClassificationCompute,
                ClassifyDualSlabSidecarTilesKernelName);
        }

        public override void Prepare(ContextContainer frameData)
        {
            bool hasResolver = m_ResolveCompute != null && m_ResolveVisibilityBufferKernel >= 0
                || m_Material != null;
            if (hasResolver && frameData.Contains<VividSdfReceiverFrameData>())
            {
                var receiver = frameData.Get<VividSdfReceiverFrameData>();
                if (ReferenceEquals(receiver.Visibility, m_VisibilityBuffer))
                {
                    receiver.GBuffer0 = m_GBuffer0;
                    receiver.GBuffer1 = m_GBuffer1;
                }
            }
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var gpuDrivenFrameData = frameData.GetOrCreate<VividGPUDrivenFrameData>();
            m_RequiresDualSlabSidecar = gpuDrivenFrameData.requiresDualSlabSidecar;
            m_RequiresSubstrateBuffer = gpuDrivenFrameData.requiresSubstrateBuffer;
            m_VirtualTextureFrameData = frameData.GetOrCreate<VividVirtualTextureFrameData>();
            VirtualTextureSystem.RegisterPageTableReadDependencies(this, m_VirtualTextureFrameData);
            m_FrameIndex = cameraData.frameIndex >= 0 ? cameraData.frameIndex : Time.frameCount;
            var visibilityBufferDescriptor = m_VisibilityBuffer?.desc;
            var width = ResolveOutputWidth(
                cameraData.actualWidth,
                cameraData.pixelWidth,
                Screen.width,
                visibilityBufferDescriptor);
            var height = ResolveOutputHeight(
                cameraData.actualHeight,
                cameraData.pixelHeight,
                Screen.height,
                visibilityBufferDescriptor);
            m_ResolveWidth = width;
            m_ResolveHeight = height;
            m_DualSlabSidecarTileCountX = Mathf.Max(
                1,
                (width + DualSlabSidecarTileSize - 1)
                    / DualSlabSidecarTileSize);
            m_DualSlabSidecarTileCountY = Mathf.Max(
                1,
                (height + DualSlabSidecarTileSize - 1)
                    / DualSlabSidecarTileSize);
            ResizeStructuredBuffer(
                m_DualSlabSidecarTileList,
                m_DualSlabSidecarTileCountX
                    * m_DualSlabSidecarTileCountY,
                sizeof(uint));
            ResizeIndirectDrawArgsBuffer(m_DualSlabSidecarIndirectDrawArgs);

            ConfigurePassOwnedTarget(m_GBuffer0, m_DefaultGBuffer0, width, height, GraphicsFormat.R8G8B8A8_UNorm, true, "GBuffer0");
            ConfigurePassOwnedTarget(m_GBuffer1, m_DefaultGBuffer1, width, height, GraphicsFormat.A2B10G10R10_UNormPack32, true, "GBuffer1");
            ConfigurePassOwnedTarget(m_GBuffer2, m_DefaultGBuffer2, width, height, GraphicsFormat.R8G8B8A8_UNorm, true, "GBuffer2");
            ConfigurePassOwnedTarget(m_GBuffer3, m_DefaultGBuffer3, width, height, GraphicsFormat.B10G11R11_UFloatPack32, true, "GBuffer3");
            ConfigurePassOwnedTarget(
                m_GBuffer4,
                m_DefaultDiffuseIrradiance,
                width,
                height,
                GraphicsFormat.B10G11R11_UFloatPack32,
                true,
                "DiffuseIrradiance");
            // Preserve the static RenderGraph ports while avoiding two
            // full-resolution allocations on frames without Dual Slab or native
            // OpenPBR pixels. Both profiles share the sidecar resource ports.
            ConfigurePassOwnedTarget(
                m_LayerAux0,
                m_DefaultLayerAux0,
                m_RequiresDualSlabSidecar ? width : 1,
                m_RequiresDualSlabSidecar ? height : 1,
                GraphicsFormat.R8G8B8A8_UNorm,
                true,
                "LayerAux0");
            ConfigurePassOwnedTarget(
                m_LayerAux1,
                m_DefaultLayerAux1,
                m_RequiresDualSlabSidecar ? width : 1,
                m_RequiresDualSlabSidecar ? height : 1,
                GraphicsFormat.R8G8B8A8_UNorm,
                true,
                "LayerAux1");

            PrepareSubstrateClosureBuffer(frameData, cameraData, width, height);
        }

        public override void Record(UnsafePassContext context)
        {
            if (!m_VisibilityBuffer.innerHandle.IsValid()
                || !m_Attributes0.innerHandle.IsValid()
                || !m_Attributes1.innerHandle.IsValid()
                || !m_Barycentrics.innerHandle.IsValid()
                || !m_GBuffer0.innerHandle.IsValid()
                || !m_GBuffer1.innerHandle.IsValid()
                || !m_GBuffer2.innerHandle.IsValid()
                || !m_GBuffer3.innerHandle.IsValid()
                || !m_GBuffer4.innerHandle.IsValid()
                || !m_LayerAux0.innerHandle.IsValid()
                || !m_LayerAux1.innerHandle.IsValid())
            {
                return;
            }

            var visibilityTexture = m_VisibilityBuffer.innerHandle.ResolveTexture();
            var attributes0Texture = m_Attributes0.innerHandle.ResolveTexture();
            var attributes1Texture = m_Attributes1.innerHandle.ResolveTexture();
            var barycentricsTexture = m_Barycentrics.innerHandle.ResolveTexture();
            if (visibilityTexture == null
                || attributes0Texture == null
                || attributes1Texture == null
                || barycentricsTexture == null)
                return;

            VividGPUDrivenSystem system = VividGPUDrivenSystem.HasInstance
                ? VividGPUDrivenSystem.instance
                : null;
            var nativeCmd = context.GetNativeCommandBuffer();

            if (CanUseSinglePassComputeResolve())
            {
                RecordSinglePassComputeResolve(
                    nativeCmd,
                    system,
                    visibilityTexture);
                return;
            }

            if (m_Material == null || m_DualSlabSidecarMaterial == null)
                return;

            system?.ConfigureTextureBackendKeyword(m_Material);
            system?.ConfigureTextureBackendKeyword(m_DualSlabSidecarMaterial);
            system?.ConfigureTextureBackendKeyword(
                m_DualSlabSidecarTiledMaterial);

            bool hasFeedback = false;
            if (system?.UsesVirtualTexture == true)
            {
                if (!GPUDrivenVirtualTextureBindingUtility.BindSpaceGlobals(
                        nativeCmd,
                        m_VirtualTextureFrameData,
                        m_VirtualTextureSpaceParams,
                        m_VirtualTextureMipOffsets,
                        m_VirtualTextureLayerFallbacks,
                        m_FrameIndex,
                        Mathf.RoundToInt(m_VirtualTextureFeedbackSampleRate),
                        out VirtualTextureSpaceBinding binding))
                {
                    return;
                }

                hasFeedback = VirtualTextureFeedbackBindingUtility.BindFeedbackTargets(nativeCmd, binding);
            }

            m_DrawProperties.Clear();
            m_DrawProperties.SetTexture(VisibilityBufferId, visibilityTexture);
            m_DrawProperties.SetVector(VisibilityBufferScaleBiasId, m_VisibilityBuffer.innerHandle.GetScaleBias());
            m_DrawProperties.SetTexture(VisibilityBufferAttributes0Id, attributes0Texture);
            m_DrawProperties.SetVector(
                VisibilityBufferAttributes0ScaleBiasId,
                m_Attributes0.innerHandle.GetScaleBias());
            m_DrawProperties.SetTexture(VisibilityBufferAttributes1Id, attributes1Texture);
            m_DrawProperties.SetVector(
                VisibilityBufferAttributes1ScaleBiasId,
                m_Attributes1.innerHandle.GetScaleBias());
            m_DrawProperties.SetTexture(VisibilityBufferBarycentricsId, barycentricsTexture);
            m_DrawProperties.SetVector(
                VisibilityBufferBarycentricsScaleBiasId,
                m_Barycentrics.innerHandle.GetScaleBias());

            BindGBufferTargets(nativeCmd);
            CoreUtils.DrawFullScreen(nativeCmd, m_Material, m_DrawProperties, 0);
            if (hasFeedback)
                nativeCmd.ClearRandomWriteTargets();

            if (!m_RequiresDualSlabSidecar)
                return;

            // Legacy raster fallback when compute shader asset has not been collected yet.
            BindDualSlabSidecarTargets(nativeCmd);
            CoreUtils.ClearRenderTarget(
                nativeCmd,
                ClearFlag.Color,
                Color.clear);
            if (!CanUseTileAdaptiveSidecarResolve())
            {
                CoreUtils.DrawFullScreen(
                    nativeCmd,
                    m_DualSlabSidecarMaterial,
                    m_DrawProperties,
                    0);
                return;
            }

            ClassifyDualSlabSidecarTiles(nativeCmd);
            m_DrawProperties.SetBuffer(
                DualSlabSidecarTileListId,
                m_DualSlabSidecarTileList.innerHandle);
            m_DrawProperties.SetVector(
                DualSlabSidecarScreenSizeId,
                new Vector4(
                    m_ResolveWidth,
                    m_ResolveHeight,
                    1.0f / m_ResolveWidth,
                    1.0f / m_ResolveHeight));
            nativeCmd.DrawProceduralIndirect(
                Matrix4x4.identity,
                m_DualSlabSidecarTiledMaterial,
                0,
                MeshTopology.Triangles,
                m_DualSlabSidecarIndirectDrawArgs.innerHandle,
                0,
                m_DrawProperties);
        }

        public override void Dispose()
        {
            m_ResolveCompute = null;
            m_ResolveVisibilityBufferKernel = -1;
            m_ClearMaterialBinningArgsKernel = -1;
            m_BinVisibilityTilesByProgramKernel = -1;
            m_ResolveVisibilityBufferBinnedKernel = -1;
            if (m_MaterialBinTileListBuffer != null)
            {
                m_MaterialBinTileListBuffer.Dispose();
                m_MaterialBinTileListBuffer = null;
            }
            if (m_MaterialBinIndirectArgsBuffer != null)
            {
                m_MaterialBinIndirectArgsBuffer.Dispose();
                m_MaterialBinIndirectArgsBuffer = null;
            }
            m_MaterialBinTileStride = 1;
            m_SubstrateFrameData = null;
            m_SubstrateClosureBufferHandle = default;
            m_RequiresSubstrateBuffer = false;
            m_VirtualTextureFrameData = null;
            m_FrameIndex = 0;
            m_ResolveWidth = 1;
            m_ResolveHeight = 1;
            m_DualSlabSidecarTileCountX = 1;
            m_DualSlabSidecarTileCountY = 1;
            m_RequiresDualSlabSidecar = false;
            m_MaterialClassificationCompute = null;
            m_ClearDualSlabSidecarDrawArgsKernel = -1;
            m_ClassifyDualSlabSidecarTilesKernel = -1;
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }
            if (m_DualSlabSidecarMaterial != null)
            {
                CoreUtils.Destroy(m_DualSlabSidecarMaterial);
                m_DualSlabSidecarMaterial = null;
            }
            if (m_DualSlabSidecarTiledMaterial != null)
            {
                CoreUtils.Destroy(m_DualSlabSidecarTiledMaterial);
                m_DualSlabSidecarTiledMaterial = null;
            }
        }

        private void PrepareSubstrateClosureBuffer(
            ContextContainer frameData,
            VividCameraData cameraData,
            int width,
            int height)
        {
            m_SubstrateClosureBufferHandle = default;
            m_SubstrateFrameData = frameData.GetOrCreate<VividSubstrateMaterialFrameData>();
            m_SubstrateFrameData.Reset();

            bool allocateSubstrateBuffer = m_ResolveCompute != null
                && m_ResolveVisibilityBufferKernel >= 0
                && m_RequiresSubstrateBuffer
                && cameraData?.camera != null;
            if (!allocateSubstrateBuffer)
                return;

            var cameraHistory = cameraData.camera.GetVividCameraHistory();
            if (!cameraHistory.IsFrameActive)
                return;

            int sliceCount = VividSubstrateContract.ClampSliceCount(m_SubstrateSliceBudget);
            var descriptor = VividSubstrateMaterialFrameData.CreateClosureBufferDescriptor(
                width,
                height,
                sliceCount);
            CameraHistoryTexture history = cameraHistory.GetOrCreateTexture(
                VividSubstrateMaterialFrameData.ClosureBufferHistoryId,
                1,
                descriptor);
            TextureHandle handle = CameraHistoryRenderGraphBridge.ImportForPass(
                this,
                history,
                0,
                AccessFlags.Write);
            if (!handle.IsValid())
                return;

            m_SubstrateClosureBufferHandle = handle;
            m_SubstrateFrameData.ClosureBufferHistory = history;
            m_SubstrateFrameData.ClosureBufferHandle = handle;
            m_SubstrateFrameData.Width = width;
            m_SubstrateFrameData.Height = height;
            m_SubstrateFrameData.SliceCount = sliceCount;
            m_SubstrateFrameData.HasSubstrateContent = true;
        }

        private bool CanUseSinglePassComputeResolve()
        {
            if (m_ResolveCompute == null || m_ResolveVisibilityBufferKernel < 0)
                return false;

            return SupportsComputeWrite(m_GBuffer0.innerHandle.ResolveTexture())
                && SupportsComputeWrite(m_GBuffer1.innerHandle.ResolveTexture())
                && SupportsComputeWrite(m_GBuffer2.innerHandle.ResolveTexture())
                && SupportsComputeWrite(m_GBuffer3.innerHandle.ResolveTexture())
                && SupportsComputeWrite(m_GBuffer4.innerHandle.ResolveTexture())
                && SupportsComputeWrite(m_LayerAux0.innerHandle.ResolveTexture())
                && SupportsComputeWrite(m_LayerAux1.innerHandle.ResolveTexture());
        }

        private static bool SupportsComputeWrite(Texture texture)
        {
            return texture is RenderTexture rt && rt.enableRandomWrite;
        }

        private void RecordSinglePassComputeResolve(
            CommandBuffer cmd,
            VividGPUDrivenSystem system,
            Texture visibilityTexture)
        {
            system?.ConfigureTextureBackendKeyword(m_ResolveCompute);

            if (system?.UsesVirtualTexture == true)
            {
                if (!GPUDrivenVirtualTextureBindingUtility.BindSpaceGlobals(
                        cmd,
                        m_VirtualTextureFrameData,
                        m_VirtualTextureSpaceParams,
                        m_VirtualTextureMipOffsets,
                        m_VirtualTextureLayerFallbacks,
                        m_FrameIndex,
                        Mathf.RoundToInt(m_VirtualTextureFeedbackSampleRate),
                        out VirtualTextureSpaceBinding binding))
                {
                    return;
                }

                VirtualTextureFeedbackBindingUtility.BindFeedbackComputeTargets(
                    cmd,
                    m_ResolveCompute,
                    m_ResolveVisibilityBufferKernel,
                    binding);
            }

            bool writeSubstrate = m_SubstrateClosureBufferHandle.IsValid()
                && m_SubstrateFrameData != null
                && m_SubstrateFrameData.HasSubstrateContent;
            cmd.SetComputeTextureParam(
                m_ResolveCompute,
                m_ResolveVisibilityBufferKernel,
                VisibilityBufferId,
                visibilityTexture);
            cmd.SetComputeVectorParam(
                m_ResolveCompute,
                VisibilityBufferScaleBiasId,
                m_VisibilityBuffer.innerHandle.GetScaleBias());
            cmd.SetComputeVectorParam(
                m_ResolveCompute,
                ResolveOutputSizeId,
                new Vector4(
                    m_ResolveWidth,
                    m_ResolveHeight,
                    1.0f / Mathf.Max(1, m_ResolveWidth),
                    1.0f / Mathf.Max(1, m_ResolveHeight)));
            cmd.SetComputeIntParam(
                m_ResolveCompute,
                WriteDualSlabSidecarId,
                m_RequiresDualSlabSidecar ? 1 : 0);
            cmd.SetComputeIntParam(
                m_ResolveCompute,
                WriteSubstrateClosureBufferId,
                writeSubstrate ? 1 : 0);

            cmd.SetComputeTextureParam(m_ResolveCompute, m_ResolveVisibilityBufferKernel, OutGBuffer0Id, m_GBuffer0.innerHandle);
            cmd.SetComputeTextureParam(m_ResolveCompute, m_ResolveVisibilityBufferKernel, OutGBuffer1Id, m_GBuffer1.innerHandle);
            cmd.SetComputeTextureParam(m_ResolveCompute, m_ResolveVisibilityBufferKernel, OutGBuffer2Id, m_GBuffer2.innerHandle);
            cmd.SetComputeTextureParam(m_ResolveCompute, m_ResolveVisibilityBufferKernel, OutGBuffer3Id, m_GBuffer3.innerHandle);
            cmd.SetComputeTextureParam(m_ResolveCompute, m_ResolveVisibilityBufferKernel, OutGBuffer4Id, m_GBuffer4.innerHandle);
            cmd.SetComputeTextureParam(m_ResolveCompute, m_ResolveVisibilityBufferKernel, OutLayerAux0Id, m_LayerAux0.innerHandle);
            cmd.SetComputeTextureParam(m_ResolveCompute, m_ResolveVisibilityBufferKernel, OutLayerAux1Id, m_LayerAux1.innerHandle);
            if (writeSubstrate)
            {
                cmd.SetComputeTextureParam(
                    m_ResolveCompute,
                    m_ResolveVisibilityBufferKernel,
                    OutSubstrateClosureBufferId,
                    m_SubstrateClosureBufferHandle);
            }
            else
            {
                cmd.SetComputeTextureParam(
                    m_ResolveCompute,
                    m_ResolveVisibilityBufferKernel,
                    OutSubstrateClosureBufferId,
                    VividSubstrateMaterialFrameData.GetOrCreateDummyClosureBuffer());
            }

            int dispatchX = Mathf.Max(1, (m_ResolveWidth + ComputeResolveGroupSize - 1) / ComputeResolveGroupSize);
            int dispatchY = Mathf.Max(1, (m_ResolveHeight + ComputeResolveGroupSize - 1) / ComputeResolveGroupSize);

            if (CanUseMaterialBinning())
            {
                RecordBinnedComputeResolve(
                    cmd,
                    system,
                    visibilityTexture,
                    writeSubstrate,
                    dispatchX,
                    dispatchY);
            }
            else
            {
                cmd.DispatchCompute(
                    m_ResolveCompute,
                    m_ResolveVisibilityBufferKernel,
                    dispatchX,
                    dispatchY,
                    1);
            }

            if (writeSubstrate)
                m_SubstrateFrameData.ClosureBufferHistory?.MarkWritten();
        }

        private bool CanUseMaterialBinning()
        {
            return m_EnableMaterialBinning
                && m_ResolveCompute != null
                && m_ClearMaterialBinningArgsKernel >= 0
                && m_BinVisibilityTilesByProgramKernel >= 0
                && m_ResolveVisibilityBufferBinnedKernel >= 0;
        }

        private void EnsureMaterialBinningBuffers(int width, int height)
        {
            int requiredStride = VividMaterialBinningContract.ComputeTileStride(width, height);
            int requiredTileListCount = VividMaterialBinningContract.ComputeTileListElementCount(width, height);
            if (m_MaterialBinTileListBuffer == null || m_MaterialBinTileListBuffer.count < requiredTileListCount)
            {
                m_MaterialBinTileListBuffer?.Dispose();
                m_MaterialBinTileListBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    requiredTileListCount,
                    sizeof(uint));
            }
            if (m_MaterialBinIndirectArgsBuffer == null
                || m_MaterialBinIndirectArgsBuffer.count < VividMaterialBinningContract.TotalIndirectArgsUints)
            {
                m_MaterialBinIndirectArgsBuffer?.Dispose();
                m_MaterialBinIndirectArgsBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments,
                    VividMaterialBinningContract.TotalIndirectArgsUints,
                    sizeof(uint));
            }
            m_MaterialBinTileStride = requiredStride;
        }

        private void RecordBinnedComputeResolve(
            CommandBuffer cmd,
            VividGPUDrivenSystem system,
            Texture visibilityTexture,
            bool writeSubstrate,
            int tileCountX,
            int tileCountY)
        {
            EnsureMaterialBinningBuffers(m_ResolveWidth, m_ResolveHeight);

            // 1. Reset indirect dispatch args for all 66 bins (Empty + 64 direct ProgramIDs + Overflow)
            cmd.SetComputeBufferParam(
                m_ResolveCompute,
                m_ClearMaterialBinningArgsKernel,
                MaterialBinIndirectArgsId,
                m_MaterialBinIndirectArgsBuffer);
            int clearGroups = (VividMaterialBinningContract.TotalBinCount + 63) / 64;
            cmd.DispatchCompute(m_ResolveCompute, m_ClearMaterialBinningArgsKernel, clearGroups, 1, 1);

            // 2. Classify 8x8 tiles by ProgramID into compact per-program tile lists
            cmd.SetComputeIntParam(m_ResolveCompute, MaterialBinTileStrideId, m_MaterialBinTileStride);
            cmd.SetComputeTextureParam(m_ResolveCompute, m_BinVisibilityTilesByProgramKernel, VisibilityBufferId, visibilityTexture);
            cmd.SetComputeBufferParam(m_ResolveCompute, m_BinVisibilityTilesByProgramKernel, MaterialBinTileListId, m_MaterialBinTileListBuffer);
            cmd.SetComputeBufferParam(m_ResolveCompute, m_BinVisibilityTilesByProgramKernel, MaterialBinIndirectArgsId, m_MaterialBinIndirectArgsBuffer);
            cmd.DispatchCompute(m_ResolveCompute, m_BinVisibilityTilesByProgramKernel, tileCountX, tileCountY, 1);

            // 3. Bind outputs once for ResolveVisibilityBufferBinned, then DispatchIndirect per active bin
            if (system?.UsesVirtualTexture == true)
            {
                if (GPUDrivenVirtualTextureBindingUtility.BindSpaceGlobals(
                        cmd,
                        m_VirtualTextureFrameData,
                        m_VirtualTextureSpaceParams,
                        m_VirtualTextureMipOffsets,
                        m_VirtualTextureLayerFallbacks,
                        m_FrameIndex,
                        Mathf.RoundToInt(m_VirtualTextureFeedbackSampleRate),
                        out VirtualTextureSpaceBinding vtBinding))
                {
                    VirtualTextureFeedbackBindingUtility.BindFeedbackComputeTargets(
                        cmd,
                        m_ResolveCompute,
                        m_ResolveVisibilityBufferBinnedKernel,
                        vtBinding);
                }
            }

            cmd.SetComputeTextureParam(m_ResolveCompute, m_ResolveVisibilityBufferBinnedKernel, VisibilityBufferId, visibilityTexture);
            cmd.SetComputeBufferParam(m_ResolveCompute, m_ResolveVisibilityBufferBinnedKernel, MaterialBinTileListSRVId, m_MaterialBinTileListBuffer);
            cmd.SetComputeTextureParam(m_ResolveCompute, m_ResolveVisibilityBufferBinnedKernel, OutGBuffer0Id, m_GBuffer0.innerHandle);
            cmd.SetComputeTextureParam(m_ResolveCompute, m_ResolveVisibilityBufferBinnedKernel, OutGBuffer1Id, m_GBuffer1.innerHandle);
            cmd.SetComputeTextureParam(m_ResolveCompute, m_ResolveVisibilityBufferBinnedKernel, OutGBuffer2Id, m_GBuffer2.innerHandle);
            cmd.SetComputeTextureParam(m_ResolveCompute, m_ResolveVisibilityBufferBinnedKernel, OutGBuffer3Id, m_GBuffer3.innerHandle);
            cmd.SetComputeTextureParam(m_ResolveCompute, m_ResolveVisibilityBufferBinnedKernel, OutGBuffer4Id, m_GBuffer4.innerHandle);
            cmd.SetComputeTextureParam(m_ResolveCompute, m_ResolveVisibilityBufferBinnedKernel, OutLayerAux0Id, m_LayerAux0.innerHandle);
            cmd.SetComputeTextureParam(m_ResolveCompute, m_ResolveVisibilityBufferBinnedKernel, OutLayerAux1Id, m_LayerAux1.innerHandle);
            cmd.SetComputeTextureParam(
                m_ResolveCompute,
                m_ResolveVisibilityBufferBinnedKernel,
                OutSubstrateClosureBufferId,
                writeSubstrate
                    ? (RenderTargetIdentifier)m_SubstrateClosureBufferHandle
                    : (RenderTargetIdentifier)VividSubstrateMaterialFrameData.GetOrCreateDummyClosureBuffer());

            // Dispatch Empty bin (0), direct ProgramID bins (1..MaxDirectProgramBins), and Overflow bin (65).
            // GPU skips bins with tileCount == 0 via DispatchIndirect at near-zero cost.
            for (int binIndex = 0; binIndex < VividMaterialBinningContract.TotalBinCount; binIndex++)
            {
                cmd.SetComputeIntParam(m_ResolveCompute, TargetMaterialBinId, binIndex);
                cmd.DispatchCompute(
                    m_ResolveCompute,
                    m_ResolveVisibilityBufferBinnedKernel,
                    m_MaterialBinIndirectArgsBuffer,
                    (uint)VividMaterialBinningContract.GetIndirectArgsByteOffset(binIndex));
            }
        }

        private static void ConfigurePassOwnedTarget(
            RenderGraphTexture texture,
            RenderGraphTexture defaultTexture,
            int width,
            int height,
            GraphicsFormat format,
            bool enableRandomWrite,
            string name)
        {
            if (!ReferenceEquals(texture, defaultTexture) || texture?.desc == null)
                return;

            texture.desc.Width = width;
            texture.desc.Height = height;
            texture.desc.ColorFormat = format;
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.MsaaSamples = MSAASamples.None;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.ClearBuffer = true;
            texture.desc.ClearColor = Color.clear;
            texture.desc.UseMipMap = false;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = 1;
            texture.desc.EnableRandomWrite = enableRandomWrite;
            texture.desc.BindTextureMS = false;
            texture.desc.Name = name;
        }

        private void BindGBufferTargets(CommandBuffer cmd)
        {
            m_GBufferColorTargets[0] = m_GBuffer0;
            m_GBufferColorTargets[1] = m_GBuffer1;
            m_GBufferColorTargets[2] = m_GBuffer2;
            m_GBufferColorTargets[3] = m_GBuffer3;
            m_GBufferColorTargets[4] = m_GBuffer4;
            cmd.SetRenderTarget(m_GBufferColorTargets, BuiltinRenderTextureType.None);
        }

        private void BindDualSlabSidecarTargets(CommandBuffer cmd)
        {
            m_DualSlabSidecarTargets[0] = m_LayerAux0;
            m_DualSlabSidecarTargets[1] = m_LayerAux1;
            cmd.SetRenderTarget(
                m_DualSlabSidecarTargets,
                BuiltinRenderTextureType.None);
        }

        private bool CanUseTileAdaptiveSidecarResolve()
        {
            return m_MaterialClassificationCompute != null
                && m_DualSlabSidecarTiledMaterial != null
                && m_ClearDualSlabSidecarDrawArgsKernel >= 0
                && m_ClassifyDualSlabSidecarTilesKernel >= 0
                && m_DualSlabSidecarTileList?.innerHandle.IsValid() == true
                && m_DualSlabSidecarIndirectDrawArgs?.innerHandle.IsValid()
                    == true;
        }

        private void ClassifyDualSlabSidecarTiles(CommandBuffer cmd)
        {
            cmd.SetComputeBufferParam(
                m_MaterialClassificationCompute,
                m_ClearDualSlabSidecarDrawArgsKernel,
                MaterialFeatureIndirectArgsId,
                m_DualSlabSidecarIndirectDrawArgs.innerHandle);
            cmd.DispatchCompute(
                m_MaterialClassificationCompute,
                m_ClearDualSlabSidecarDrawArgsKernel,
                1,
                1,
                1);

            cmd.SetComputeIntParam(
                m_MaterialClassificationCompute,
                ClassificationWidthId,
                m_ResolveWidth);
            cmd.SetComputeIntParam(
                m_MaterialClassificationCompute,
                ClassificationHeightId,
                m_ResolveHeight);
            cmd.SetComputeTextureParam(
                m_MaterialClassificationCompute,
                m_ClassifyDualSlabSidecarTilesKernel,
                GBuffer0Id,
                m_GBuffer0.innerHandle);
            cmd.SetComputeTextureParam(
                m_MaterialClassificationCompute,
                m_ClassifyDualSlabSidecarTilesKernel,
                GBuffer1Id,
                m_GBuffer1.innerHandle);
            cmd.SetComputeBufferParam(
                m_MaterialClassificationCompute,
                m_ClassifyDualSlabSidecarTilesKernel,
                MaterialFeatureTileListId,
                m_DualSlabSidecarTileList.innerHandle);
            cmd.SetComputeBufferParam(
                m_MaterialClassificationCompute,
                m_ClassifyDualSlabSidecarTilesKernel,
                MaterialFeatureIndirectArgsId,
                m_DualSlabSidecarIndirectDrawArgs.innerHandle);
            cmd.DispatchCompute(
                m_MaterialClassificationCompute,
                m_ClassifyDualSlabSidecarTilesKernel,
                m_DualSlabSidecarTileCountX,
                m_DualSlabSidecarTileCountY,
                1);
        }

        private static int TryFindKernel(
            ComputeShader computeShader,
            string kernelName)
        {
            return computeShader != null && computeShader.HasKernel(kernelName)
                ? computeShader.FindKernel(kernelName)
                : -1;
        }

        private static void ResizeStructuredBuffer(
            RenderGraphBuffer buffer,
            int count,
            int stride)
        {
            if (buffer?.desc == null)
                return;

            buffer.desc.Count = Mathf.Max(1, count);
            buffer.desc.Stride = stride;
            buffer.desc.Target = GraphicsBuffer.Target.Structured;
        }

        private static void ResizeIndirectDrawArgsBuffer(
            RenderGraphBuffer buffer)
        {
            if (buffer?.desc == null)
                return;

            buffer.desc.Count = IndirectDrawArgsElementCount;
            buffer.desc.Stride = sizeof(uint);
            buffer.desc.Target = GraphicsBuffer.Target.Structured
                | GraphicsBuffer.Target.IndirectArguments;
        }

        private static int ResolveOutputWidth(
            int actualCameraDimension,
            int cameraDimension,
            int screenDimension,
            RenderGraphTextureDesc descriptor)
        {
            if (descriptor.HasExplicitSize())
                return Mathf.Max(1, descriptor.Width);

            return CameraDimensionUtility.ResolveCameraDimension(
                actualCameraDimension,
                cameraDimension,
                screenDimension);
        }

        private static int ResolveOutputHeight(
            int actualCameraDimension,
            int cameraDimension,
            int screenDimension,
            RenderGraphTextureDesc descriptor)
        {
            if (descriptor.HasExplicitSize())
                return Mathf.Max(1, descriptor.Height);

            return CameraDimensionUtility.ResolveCameraDimension(
                actualCameraDimension,
                cameraDimension,
                screenDimension);
        }

    }
}
