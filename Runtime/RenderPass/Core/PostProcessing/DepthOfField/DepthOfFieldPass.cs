using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public sealed class DepthOfFieldPass : UnsafePass, IPostProcessSourceOverridePass
    {
        private const int ThreadGroupSize = 8;
        private const int TileSize = 8;
        private const int ApertureShapeSampleCount = 256;
        private const string CoCHistoryKey = "DepthOfFieldCoC";

        private static readonly int InputColorId = Shader.PropertyToID("_InputColorTexture");
        private static readonly int InputLinearDepthId = Shader.PropertyToID("_InputLinearDepthTexture");
        private static readonly int InputCoCId = Shader.PropertyToID("_InputCoCTexture");
        private static readonly int InputHistoryCoCId = Shader.PropertyToID("_InputHistoryCoCTexture");
        private static readonly int MotionVectorsId = Shader.PropertyToID("_MotionVectorsTexture");
        private static readonly int TileTextureId = Shader.PropertyToID("_TileTexture");
        private static readonly int OutputColorId = Shader.PropertyToID("_OutputColorTexture");
        private static readonly int OutputCoCId = Shader.PropertyToID("_OutputCoCTexture");
        private static readonly int OutputTileId = Shader.PropertyToID("_OutputTileTexture");
        private static readonly int SourceSizeId = Shader.PropertyToID("_VividDoFSourceSize");
        private static readonly int FullResSizeId = Shader.PropertyToID("_VividDoFFullResSize");
        private static readonly int CurrentSizeId = Shader.PropertyToID("_VividDoFCurrentSize");
        private static readonly int TileSizeId = Shader.PropertyToID("_VividDoFTileSize");
        private static readonly int Params0Id = Shader.PropertyToID("_VividDoFParams0");
        private static readonly int Params1Id = Shader.PropertyToID("_VividDoFParams1");
        private static readonly int ApertureShapeTableId = Shader.PropertyToID("_VividDoFApertureShapeTable");
        private static readonly int ApertureShapeTableCountId = Shader.PropertyToID("_VividDoFApertureShapeTableCount");

        [RenderGraphResource(Access = AccessFlags.Read)]
        private RenderGraphTexture source = new();

        [RenderGraphResource(Name = "LinearDepth", Access = AccessFlags.Read)]
        private RenderGraphTexture linearDepth = new();

        [RenderGraphResource(Name = "MotionVectors", Access = AccessFlags.Read)]
        private RenderGraphTexture motionVectors = new();

        [RenderGraphResource(
            Name = "DepthOfFieldCoC",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedHidden)]
        private RenderGraphTexture m_FullResCoC;

        [RenderGraphResource(
            Name = "DepthOfFieldCoCHistory",
            Access = AccessFlags.Read,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedHidden)]
        private RenderGraphTexture m_CoCHistoryPrevious;

        [RenderGraphResource(
            Name = "DepthOfFieldCoCHistoryCurrent",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedHidden)]
        private RenderGraphTexture m_CoCHistoryCurrent;

        [RenderGraphResource(
            Name = "DepthOfFieldTileMinMaxPing",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedHidden)]
        private RenderGraphTexture m_TileMinMaxPing;

        [RenderGraphResource(
            Name = "DepthOfFieldTileMinMaxPong",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedHidden)]
        private RenderGraphTexture m_TileMinMaxPong;

        [RenderGraphResource(
            Name = "DepthOfFieldScaledSource",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedHidden)]
        private RenderGraphTexture m_ScaledSource;

        [RenderGraphResource(
            Name = "DepthOfFieldScaledBlur",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedHidden)]
        private RenderGraphTexture m_ScaledBlur;

        [RenderGraphResource(
            Name = "DepthOfFieldOutput",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture output = new();

        private ComputeShader m_ComputeShader;
        private int m_ResampleColorKernel = -1;
        private int m_CopyCoCKernel = -1;
        private int m_PhysicalCoCKernel = -1;
        private int m_ManualCoCKernel = -1;
        private int m_ReprojectCoCKernel = -1;
        private int m_MinMaxKernel = -1;
        private int m_DilateKernel = -1;
        private int m_ComputeSlowTilesKernel = -1;
        private int m_GatherFastTilesKernel = -1;
        private int m_CombineFastTilesKernel = -1;

        private DepthOfFieldSettingsData m_Settings;
        private TAASettings m_TAASettings;
        private Camera m_Camera;
        private GraphicsBuffer m_ApertureShapeBuffer;
        private readonly Vector2[] m_ApertureShapeSamples = new Vector2[ApertureShapeSampleCount];
        private Vector2 m_LastApertureCurvature;
        private float m_LastAperture;
        private float m_LastAnamorphism;
        private float m_LastBarrelClipping;
        private int m_LastBladeCount;
        private bool m_ApertureShapeDirty = true;
        private int m_Width;
        private int m_Height;
        private int m_ScaledWidth;
        private int m_ScaledHeight;
        private int m_ResolutionDivisor;
        private int m_TileCountX;
        private int m_TileCountY;
        private int m_NearSampleCount;
        private int m_FarSampleCount;
        private bool m_ShouldApply;
        private bool m_IsFirstFrame;
        private bool m_HasValidCoCHistory;
        private bool m_IsPassResourceLayoutDirty;
        private RenderGraphTexture m_OriginalSource;
        private bool m_HasSourceTextureOverride;

        public bool IsPassResourceLayoutDirty => m_IsPassResourceLayoutDirty;

        public DepthOfFieldPass()
        {
            profilingSampler = new ProfilingSampler(nameof(DepthOfFieldPass));

            source = RenderGraphTexture.CreateInput("source", GraphicsFormat.R16G16B16A16_SFloat);
            linearDepth = RenderGraphTexture.CreateInput("LinearDepth", GraphicsFormat.R32_SFloat);
            motionVectors = RenderGraphTexture.CreateInput("MotionVectors", GraphicsFormat.R16G16_SFloat);
            m_FullResCoC = CreateTexture("DepthOfFieldCoC", 1, 1, GraphicsFormat.R16_SFloat, FilterMode.Point, true);
            m_CoCHistoryPrevious = RenderGraphTexture.CreateInput("DepthOfFieldCoCHistory", GraphicsFormat.R16_SFloat);
            m_CoCHistoryCurrent = CreateTexture("DepthOfFieldCoCHistoryCurrent", 1, 1, GraphicsFormat.R16_SFloat, FilterMode.Point, true);
            m_TileMinMaxPing = CreateTexture("DepthOfFieldTileMinMaxPing", 1, 1, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point, true);
            m_TileMinMaxPong = CreateTexture("DepthOfFieldTileMinMaxPong", 1, 1, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point, true);
            m_ScaledSource = CreateTexture("DepthOfFieldScaledSource", 1, 1, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, true);
            m_ScaledBlur = CreateTexture("DepthOfFieldScaledBlur", 1, 1, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, true);
            output = CreateTexture("DepthOfFieldOutput", 1, 1, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, true);
        }

        public void ClearPassResourceLayoutDirty()
        {
            m_IsPassResourceLayoutDirty = false;
        }

        internal RenderGraphTexture GetSourceTexture()
        {
            return source;
        }

        internal void SetSourceTexture(RenderGraphTexture sourceTexture)
        {
            if (sourceTexture == null)
                throw new ArgumentNullException(nameof(sourceTexture));

            UpdateOutputDescriptor(sourceTexture);

            if (ReferenceEquals(source, sourceTexture))
                return;

            if (!m_HasSourceTextureOverride)
                m_OriginalSource = source;

            source = sourceTexture;
            m_HasSourceTextureOverride = true;
            m_IsPassResourceLayoutDirty = true;
        }

        internal void RestoreSourceTexture()
        {
            if (!m_HasSourceTextureOverride)
                return;

            if (!ReferenceEquals(source, m_OriginalSource) && m_OriginalSource != null)
            {
                source = m_OriginalSource;
                UpdateOutputDescriptor(source);
                m_IsPassResourceLayoutDirty = true;
            }

            m_OriginalSource = null;
            m_HasSourceTextureOverride = false;
        }

        RenderGraphTexture IPostProcessSourceOverridePass.GetSourceTexture() => GetSourceTexture();

        void IPostProcessSourceOverridePass.SetSourceTexture(RenderGraphTexture sourceTexture) => SetSourceTexture(sourceTexture);

        void IPostProcessSourceOverridePass.RestoreSourceTexture() => RestoreSourceTexture();

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_ComputeShader = resources?.DepthOfFieldCompute;
            if (m_ComputeShader == null)
                return;

            m_ResampleColorKernel = FindKernel("KResampleColor");
            m_CopyCoCKernel = FindKernel("KCopyCoC");
            m_PhysicalCoCKernel = FindKernel("KCoCPhysical");
            m_ManualCoCKernel = FindKernel("KCoCManual");
            m_ReprojectCoCKernel = FindKernel("KReprojectCoC");
            m_MinMaxKernel = FindKernel("KCoCMinMax");
            m_DilateKernel = FindKernel("KMinMaxDilate");
            m_ComputeSlowTilesKernel = FindKernel("KComputeSlowTiles");
            m_GatherFastTilesKernel = FindKernel("KGatherFastTiles");
            m_CombineFastTilesKernel = FindKernel("KCombineFastTiles");

            EnsureApertureShapeBuffer();
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var temporalData = frameData.Get<VividTemporalData>();
            m_Camera = cameraData?.camera;
            var postProcessingAllowed = m_Camera != null && CoreUtils.ArePostProcessesEnabled(m_Camera);

            m_Settings = postProcessingAllowed
                ? DepthOfFieldSettingsResolver.Resolve()
                : DepthOfFieldSettingsData.CreateDefault();
            m_TAASettings = cameraData != null
                ? TAASettings.FromCamera(cameraData.additionalData)
                : TAASettings.Disabled;
            m_IsFirstFrame = temporalData?.isFirstFrame ?? true;

            m_Width = ResolveWidth(cameraData);
            m_Height = ResolveHeight(cameraData);

            var computeColorFormat = ResolveComputeColorFormat(source?.desc);
            m_ResolutionDivisor = Mathf.Max(1, (int)m_Settings.resolution);
            m_ScaledWidth = Mathf.Max(1, Mathf.CeilToInt(m_Width / (float)m_ResolutionDivisor));
            m_ScaledHeight = Mathf.Max(1, Mathf.CeilToInt(m_Height / (float)m_ResolutionDivisor));
            m_TileCountX = Mathf.Max(1, Mathf.CeilToInt(m_Width / (float)TileSize));
            m_TileCountY = Mathf.Max(1, Mathf.CeilToInt(m_Height / (float)TileSize));
            m_NearSampleCount = ResolveSampleCount(m_Settings.nearSampleCount);
            m_FarSampleCount = ResolveSampleCount(m_Settings.farSampleCount);

            UpdateOutputDescriptor(source);
            ConfigureColorTexture(output, source?.desc, "DepthOfFieldOutput", m_Width, m_Height, computeColorFormat, true, FilterMode.Bilinear, 1f);
            ConfigureColorTexture(m_ScaledSource, source?.desc, "DepthOfFieldScaledSource", m_ScaledWidth, m_ScaledHeight, computeColorFormat, true, FilterMode.Bilinear, 1f / m_ResolutionDivisor);
            ConfigureColorTexture(m_ScaledBlur, source?.desc, "DepthOfFieldScaledBlur", m_ScaledWidth, m_ScaledHeight, computeColorFormat, true, FilterMode.Bilinear, 1f / m_ResolutionDivisor);
            ConfigureColorTexture(m_FullResCoC, null, "DepthOfFieldCoC", m_Width, m_Height, GraphicsFormat.R16_SFloat, true, FilterMode.Point, 1f);
            ConfigureColorTexture(m_TileMinMaxPing, null, "DepthOfFieldTileMinMaxPing", m_TileCountX, m_TileCountY, GraphicsFormat.R16G16B16A16_SFloat, true, FilterMode.Point, 1f);
            ConfigureColorTexture(m_TileMinMaxPong, null, "DepthOfFieldTileMinMaxPong", m_TileCountX, m_TileCountY, GraphicsFormat.R16G16B16A16_SFloat, true, FilterMode.Point, 1f);

            m_ShouldApply = postProcessingAllowed
                && m_Settings.enabled
                && m_Settings.physicallyBased
                && m_Settings.focusMode != DepthOfFieldMode.Off
                && (IsNearLayerActive() || IsFarLayerActive());

            if (m_ShouldApply && m_Settings.coCStabilization && m_TAASettings.Enabled)
            {
                var historyDesc = CreateHistoryDescriptor();
                m_HasValidCoCHistory = AllocHistoryTexture(CoCHistoryKey, m_CoCHistoryPrevious, m_CoCHistoryCurrent, historyDesc);
            }
            else
            {
                m_HasValidCoCHistory = false;
            }

            EnsureApertureShapeBuffer();
            UpdateApertureShapeBuffer();
        }

        public override void Record(UnsafePassContext context)
        {
            if (source?.innerHandle.IsValid() != true || output?.innerHandle.IsValid() != true)
                return;

            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            using (new ProfilingScope(cmd, profilingSampler))
            {
                if (!CanExecutePhysicalPath())
                {
                    DispatchResampleColor(cmd, source.innerHandle, output.innerHandle, m_Width, m_Height, m_Width, m_Height);
                    return;
                }

                DispatchCircleOfConfusion(cmd);

                RTHandle effectiveCoC = m_FullResCoC.innerHandle;
                if (m_CoCHistoryCurrent?.innerHandle.IsValid() == true)
                {
                    if (ShouldReprojectCoC())
                        DispatchCoCReprojection(cmd, m_FullResCoC.innerHandle, m_CoCHistoryPrevious.innerHandle, m_CoCHistoryCurrent.innerHandle);
                    else
                        DispatchCopyCoC(cmd, m_FullResCoC.innerHandle, m_CoCHistoryCurrent.innerHandle, m_Width, m_Height);

                    effectiveCoC = m_CoCHistoryCurrent.innerHandle;
                }

                DispatchCoCMinMax(cmd, effectiveCoC, m_TileMinMaxPing.innerHandle);
                var tileTexture = DispatchDilatedTileMinMax(cmd, m_TileMinMaxPing.innerHandle, m_TileMinMaxPong.innerHandle);

                DispatchSlowTiles(cmd, source.innerHandle, effectiveCoC, tileTexture, output.innerHandle);
                DispatchResampleColor(cmd, source.innerHandle, m_ScaledSource.innerHandle, m_Width, m_Height, m_ScaledWidth, m_ScaledHeight);
                DispatchFastTiles(cmd, m_ScaledSource.innerHandle, effectiveCoC, tileTexture, m_ScaledBlur.innerHandle);
                DispatchCombine(cmd, m_ScaledBlur.innerHandle, tileTexture, output.innerHandle);
            }
        }

        public override void Dispose()
        {
            m_ComputeShader = null;
            m_ResampleColorKernel = -1;
            m_CopyCoCKernel = -1;
            m_PhysicalCoCKernel = -1;
            m_ManualCoCKernel = -1;
            m_ReprojectCoCKernel = -1;
            m_MinMaxKernel = -1;
            m_DilateKernel = -1;
            m_ComputeSlowTilesKernel = -1;
            m_GatherFastTilesKernel = -1;
            m_CombineFastTilesKernel = -1;
            m_HasValidCoCHistory = false;
            m_ShouldApply = false;
            m_IsPassResourceLayoutDirty = false;

            if (m_ApertureShapeBuffer != null)
            {
                m_ApertureShapeBuffer.Release();
                m_ApertureShapeBuffer = null;
            }
        }

        private bool CanExecutePhysicalPath()
        {
            return m_ComputeShader != null
                && m_ShouldApply
                && m_ApertureShapeBuffer != null
                && linearDepth?.innerHandle.IsValid() == true
                && m_FullResCoC?.innerHandle.IsValid() == true
                && m_TileMinMaxPing?.innerHandle.IsValid() == true
                && m_TileMinMaxPong?.innerHandle.IsValid() == true
                && m_ScaledSource?.innerHandle.IsValid() == true
                && m_ScaledBlur?.innerHandle.IsValid() == true
                && m_ResampleColorKernel >= 0
                && m_CopyCoCKernel >= 0
                && m_PhysicalCoCKernel >= 0
                && m_ManualCoCKernel >= 0
                && m_ReprojectCoCKernel >= 0
                && m_MinMaxKernel >= 0
                && m_DilateKernel >= 0
                && m_ComputeSlowTilesKernel >= 0
                && m_GatherFastTilesKernel >= 0
                && m_CombineFastTilesKernel >= 0;
        }

        private bool ShouldReprojectCoC()
        {
            return m_Settings.coCStabilization
                && m_TAASettings.Enabled
                && !m_IsFirstFrame
                && m_HasValidCoCHistory
                && m_CoCHistoryPrevious?.innerHandle.IsValid() == true
                && motionVectors?.innerHandle.IsValid() == true;
        }

        private void DispatchResampleColor(
            CommandBuffer cmd,
            RTHandle input,
            RTHandle outputHandle,
            int sourceWidth,
            int sourceHeight,
            int targetWidth,
            int targetHeight)
        {
            if (input == null || outputHandle == null || m_ResampleColorKernel < 0)
                return;

            SetSizeParams(
                cmd,
                new Vector4(sourceWidth, sourceHeight, 1f / Mathf.Max(1, sourceWidth), 1f / Mathf.Max(1, sourceHeight)),
                new Vector4(m_Width, m_Height, 1f / Mathf.Max(1, m_Width), 1f / Mathf.Max(1, m_Height)),
                new Vector4(targetWidth, targetHeight, 1f / Mathf.Max(1, targetWidth), 1f / Mathf.Max(1, targetHeight)),
                new Vector4(m_TileCountX, m_TileCountY, 1f / Mathf.Max(1, m_TileCountX), 1f / Mathf.Max(1, m_TileCountY)));

            cmd.SetComputeTextureParam(m_ComputeShader, m_ResampleColorKernel, InputColorId, input);
            cmd.SetComputeTextureParam(m_ComputeShader, m_ResampleColorKernel, OutputColorId, outputHandle);
            Dispatch(cmd, m_ResampleColorKernel, targetWidth, targetHeight);
        }

        private void DispatchCopyCoC(CommandBuffer cmd, RTHandle input, RTHandle outputHandle, int width, int height)
        {
            if (input == null || outputHandle == null || m_CopyCoCKernel < 0)
                return;

            SetSizeParams(
                cmd,
                new Vector4(width, height, 1f / Mathf.Max(1, width), 1f / Mathf.Max(1, height)),
                new Vector4(width, height, 1f / Mathf.Max(1, width), 1f / Mathf.Max(1, height)),
                new Vector4(width, height, 1f / Mathf.Max(1, width), 1f / Mathf.Max(1, height)),
                new Vector4(m_TileCountX, m_TileCountY, 1f / Mathf.Max(1, m_TileCountX), 1f / Mathf.Max(1, m_TileCountY)));

            cmd.SetComputeTextureParam(m_ComputeShader, m_CopyCoCKernel, InputCoCId, input);
            cmd.SetComputeTextureParam(m_ComputeShader, m_CopyCoCKernel, OutputCoCId, outputHandle);
            Dispatch(cmd, m_CopyCoCKernel, width, height);
        }

        private void DispatchCircleOfConfusion(CommandBuffer cmd)
        {
            var focusDistance = ResolveFocusDistance();
            var blurLimits = ResolveBlurLimits();

            SetSizeParams(
                cmd,
                new Vector4(m_Width, m_Height, 1f / Mathf.Max(1, m_Width), 1f / Mathf.Max(1, m_Height)),
                new Vector4(m_Width, m_Height, 1f / Mathf.Max(1, m_Width), 1f / Mathf.Max(1, m_Height)),
                new Vector4(m_Width, m_Height, 1f / Mathf.Max(1, m_Width), 1f / Mathf.Max(1, m_Height)),
                new Vector4(m_TileCountX, m_TileCountY, 1f / Mathf.Max(1, m_TileCountX), 1f / Mathf.Max(1, m_TileCountY)));
            cmd.SetComputeTextureParam(m_ComputeShader, m_PhysicalCoCKernel, InputLinearDepthId, linearDepth.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_ManualCoCKernel, InputLinearDepthId, linearDepth.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_PhysicalCoCKernel, OutputCoCId, m_FullResCoC.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_ManualCoCKernel, OutputCoCId, m_FullResCoC.innerHandle);

            if (m_Settings.focusMode == DepthOfFieldMode.UsePhysicalCamera)
            {
                var physicalMaxCoC = ResolvePhysicalMaxCoC(focusDistance);
                cmd.SetComputeVectorParam(
                    m_ComputeShader,
                    Params0Id,
                    new Vector4(focusDistance, physicalMaxCoC, blurLimits.x, blurLimits.y));
                Dispatch(cmd, m_PhysicalCoCKernel, m_Width, m_Height);
                return;
            }

            ResolveManualFocusRanges(out var nearStart, out var nearEnd, out var farStart, out var farEnd);
            cmd.SetComputeVectorParam(
                m_ComputeShader,
                Params0Id,
                new Vector4(nearStart, nearEnd, farStart, farEnd));
            cmd.SetComputeVectorParam(
                m_ComputeShader,
                Params1Id,
                new Vector4(
                    blurLimits.x,
                    blurLimits.y,
                    m_Settings.limitManualRangeNearBlur ? 1f : 0f,
                    0f));
            Dispatch(cmd, m_ManualCoCKernel, m_Width, m_Height);
        }

        private void DispatchCoCReprojection(CommandBuffer cmd, RTHandle currentCoC, RTHandle historyCoC, RTHandle outputHandle)
        {
            if (currentCoC == null || historyCoC == null || outputHandle == null || m_ReprojectCoCKernel < 0)
                return;

            SetSizeParams(
                cmd,
                new Vector4(m_Width, m_Height, 1f / Mathf.Max(1, m_Width), 1f / Mathf.Max(1, m_Height)),
                new Vector4(m_Width, m_Height, 1f / Mathf.Max(1, m_Width), 1f / Mathf.Max(1, m_Height)),
                new Vector4(m_Width, m_Height, 1f / Mathf.Max(1, m_Width), 1f / Mathf.Max(1, m_Height)),
                new Vector4(m_TileCountX, m_TileCountY, 1f / Mathf.Max(1, m_TileCountX), 1f / Mathf.Max(1, m_TileCountY)));
            cmd.SetComputeVectorParam(m_ComputeShader, Params0Id, new Vector4(0.86f, 0f, 0f, 0f));
            cmd.SetComputeTextureParam(m_ComputeShader, m_ReprojectCoCKernel, InputCoCId, currentCoC);
            cmd.SetComputeTextureParam(m_ComputeShader, m_ReprojectCoCKernel, InputHistoryCoCId, historyCoC);
            cmd.SetComputeTextureParam(m_ComputeShader, m_ReprojectCoCKernel, MotionVectorsId, motionVectors.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_ReprojectCoCKernel, OutputCoCId, outputHandle);
            Dispatch(cmd, m_ReprojectCoCKernel, m_Width, m_Height);
        }

        private void DispatchCoCMinMax(CommandBuffer cmd, RTHandle cocTexture, RTHandle tileTexture)
        {
            if (cocTexture == null || tileTexture == null || m_MinMaxKernel < 0)
                return;

            SetSizeParams(
                cmd,
                new Vector4(m_Width, m_Height, 1f / Mathf.Max(1, m_Width), 1f / Mathf.Max(1, m_Height)),
                new Vector4(m_Width, m_Height, 1f / Mathf.Max(1, m_Width), 1f / Mathf.Max(1, m_Height)),
                new Vector4(m_TileCountX, m_TileCountY, 1f / Mathf.Max(1, m_TileCountX), 1f / Mathf.Max(1, m_TileCountY)),
                new Vector4(m_TileCountX, m_TileCountY, 1f / Mathf.Max(1, m_TileCountX), 1f / Mathf.Max(1, m_TileCountY)));
            cmd.SetComputeTextureParam(m_ComputeShader, m_MinMaxKernel, InputCoCId, cocTexture);
            cmd.SetComputeTextureParam(m_ComputeShader, m_MinMaxKernel, OutputTileId, tileTexture);
            Dispatch(cmd, m_MinMaxKernel, m_TileCountX, m_TileCountY);
        }

        private RTHandle DispatchDilatedTileMinMax(CommandBuffer cmd, RTHandle ping, RTHandle pong)
        {
            if (ping == null || pong == null || m_DilateKernel < 0)
                return ping;

            var result = ping;
            var scratch = pong;
            var blurLimits = ResolveBlurLimits();
            var dilationIterations = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(blurLimits.x, blurLimits.y) / TileSize));

            for (var iteration = 0; iteration < dilationIterations; iteration++)
            {
                SetSizeParams(
                    cmd,
                    new Vector4(m_TileCountX, m_TileCountY, 1f / Mathf.Max(1, m_TileCountX), 1f / Mathf.Max(1, m_TileCountY)),
                    new Vector4(m_Width, m_Height, 1f / Mathf.Max(1, m_Width), 1f / Mathf.Max(1, m_Height)),
                    new Vector4(m_TileCountX, m_TileCountY, 1f / Mathf.Max(1, m_TileCountX), 1f / Mathf.Max(1, m_TileCountY)),
                    new Vector4(m_TileCountX, m_TileCountY, 1f / Mathf.Max(1, m_TileCountX), 1f / Mathf.Max(1, m_TileCountY)));
                cmd.SetComputeTextureParam(m_ComputeShader, m_DilateKernel, TileTextureId, result);
                cmd.SetComputeTextureParam(m_ComputeShader, m_DilateKernel, OutputTileId, scratch);
                Dispatch(cmd, m_DilateKernel, m_TileCountX, m_TileCountY);
                CoreUtils.Swap(ref result, ref scratch);
            }

            return result;
        }

        private void DispatchSlowTiles(CommandBuffer cmd, RTHandle inputColor, RTHandle cocTexture, RTHandle tileTexture, RTHandle outputHandle)
        {
            if (inputColor == null || cocTexture == null || tileTexture == null || outputHandle == null || m_ComputeSlowTilesKernel < 0)
                return;

            SetGatherParams(cmd, m_Width, m_Height, m_Width, m_Height, 1f);
            cmd.SetComputeTextureParam(m_ComputeShader, m_ComputeSlowTilesKernel, InputColorId, inputColor);
            cmd.SetComputeTextureParam(m_ComputeShader, m_ComputeSlowTilesKernel, InputLinearDepthId, linearDepth.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_ComputeSlowTilesKernel, InputCoCId, cocTexture);
            cmd.SetComputeTextureParam(m_ComputeShader, m_ComputeSlowTilesKernel, TileTextureId, tileTexture);
            cmd.SetComputeTextureParam(m_ComputeShader, m_ComputeSlowTilesKernel, OutputColorId, outputHandle);
            cmd.SetComputeBufferParam(m_ComputeShader, m_ComputeSlowTilesKernel, ApertureShapeTableId, m_ApertureShapeBuffer);
            cmd.SetComputeIntParam(m_ComputeShader, ApertureShapeTableCountId, ApertureShapeSampleCount);
            Dispatch(cmd, m_ComputeSlowTilesKernel, m_Width, m_Height);
        }

        private void DispatchFastTiles(CommandBuffer cmd, RTHandle inputColor, RTHandle cocTexture, RTHandle tileTexture, RTHandle outputHandle)
        {
            if (inputColor == null || cocTexture == null || tileTexture == null || outputHandle == null || m_GatherFastTilesKernel < 0)
                return;

            SetGatherParams(cmd, m_ScaledWidth, m_ScaledHeight, m_Width, m_Height, m_ResolutionDivisor);
            cmd.SetComputeTextureParam(m_ComputeShader, m_GatherFastTilesKernel, InputColorId, inputColor);
            cmd.SetComputeTextureParam(m_ComputeShader, m_GatherFastTilesKernel, InputLinearDepthId, linearDepth.innerHandle);
            cmd.SetComputeTextureParam(m_ComputeShader, m_GatherFastTilesKernel, InputCoCId, cocTexture);
            cmd.SetComputeTextureParam(m_ComputeShader, m_GatherFastTilesKernel, TileTextureId, tileTexture);
            cmd.SetComputeTextureParam(m_ComputeShader, m_GatherFastTilesKernel, OutputColorId, outputHandle);
            cmd.SetComputeBufferParam(m_ComputeShader, m_GatherFastTilesKernel, ApertureShapeTableId, m_ApertureShapeBuffer);
            cmd.SetComputeIntParam(m_ComputeShader, ApertureShapeTableCountId, ApertureShapeSampleCount);
            Dispatch(cmd, m_GatherFastTilesKernel, m_ScaledWidth, m_ScaledHeight);
        }

        private void DispatchCombine(CommandBuffer cmd, RTHandle scaledBlur, RTHandle tileTexture, RTHandle outputHandle)
        {
            if (scaledBlur == null || tileTexture == null || outputHandle == null || m_CombineFastTilesKernel < 0)
                return;

            SetSizeParams(
                cmd,
                new Vector4(m_ScaledWidth, m_ScaledHeight, 1f / Mathf.Max(1, m_ScaledWidth), 1f / Mathf.Max(1, m_ScaledHeight)),
                new Vector4(m_Width, m_Height, 1f / Mathf.Max(1, m_Width), 1f / Mathf.Max(1, m_Height)),
                new Vector4(m_Width, m_Height, 1f / Mathf.Max(1, m_Width), 1f / Mathf.Max(1, m_Height)),
                new Vector4(m_TileCountX, m_TileCountY, 1f / Mathf.Max(1, m_TileCountX), 1f / Mathf.Max(1, m_TileCountY)));
            cmd.SetComputeVectorParam(m_ComputeShader, Params0Id, new Vector4(m_ResolutionDivisor, 0f, 0f, 0f));
            cmd.SetComputeTextureParam(m_ComputeShader, m_CombineFastTilesKernel, InputColorId, scaledBlur);
            cmd.SetComputeTextureParam(m_ComputeShader, m_CombineFastTilesKernel, TileTextureId, tileTexture);
            cmd.SetComputeTextureParam(m_ComputeShader, m_CombineFastTilesKernel, OutputColorId, outputHandle);
            Dispatch(cmd, m_CombineFastTilesKernel, m_Width, m_Height);
        }

        private void SetGatherParams(CommandBuffer cmd, int sourceWidth, int sourceHeight, int fullResWidth, int fullResHeight, float resolutionScale)
        {
            var adaptiveWeights = ResolveAdaptiveSamplingWeights();
            var usePointSampling = !m_Settings.highQualityFiltering || m_ResolutionDivisor == (int)DepthOfFieldResolution.Quarter
                ? 1f
                : 0f;
            var blurLimits = ResolveBlurLimits();

            SetSizeParams(
                cmd,
                new Vector4(sourceWidth, sourceHeight, 1f / Mathf.Max(1, sourceWidth), 1f / Mathf.Max(1, sourceHeight)),
                new Vector4(fullResWidth, fullResHeight, 1f / Mathf.Max(1, fullResWidth), 1f / Mathf.Max(1, fullResHeight)),
                new Vector4(sourceWidth, sourceHeight, 1f / Mathf.Max(1, sourceWidth), 1f / Mathf.Max(1, sourceHeight)),
                new Vector4(m_TileCountX, m_TileCountY, 1f / Mathf.Max(1, m_TileCountX), 1f / Mathf.Max(1, m_TileCountY)));
            cmd.SetComputeVectorParam(
                m_ComputeShader,
                Params0Id,
                new Vector4(m_NearSampleCount, m_FarSampleCount, resolutionScale, usePointSampling));
            cmd.SetComputeVectorParam(
                m_ComputeShader,
                Params1Id,
                new Vector4(adaptiveWeights.x, adaptiveWeights.y, blurLimits.x, blurLimits.y));
        }

        private void SetSizeParams(CommandBuffer cmd, Vector4 sourceSize, Vector4 fullResSize, Vector4 currentSize, Vector4 tileSize)
        {
            cmd.SetComputeVectorParam(m_ComputeShader, SourceSizeId, sourceSize);
            cmd.SetComputeVectorParam(m_ComputeShader, FullResSizeId, fullResSize);
            cmd.SetComputeVectorParam(m_ComputeShader, CurrentSizeId, currentSize);
            cmd.SetComputeVectorParam(m_ComputeShader, TileSizeId, tileSize);
        }

        private void Dispatch(CommandBuffer cmd, int kernel, int width, int height)
        {
            cmd.DispatchCompute(
                m_ComputeShader,
                kernel,
                CoreUtils.DivRoundUp(Mathf.Max(1, width), ThreadGroupSize),
                CoreUtils.DivRoundUp(Mathf.Max(1, height), ThreadGroupSize),
                1);
        }

        private int FindKernel(string kernelName)
        {
            return m_ComputeShader != null && m_ComputeShader.HasKernel(kernelName)
                ? m_ComputeShader.FindKernel(kernelName)
                : -1;
        }

        private void UpdateOutputDescriptor(RenderGraphTexture sourceTexture)
        {
            if (output == null)
                output = CreateTexture("DepthOfFieldOutput", 1, 1, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, true);

            var sourceDesc = sourceTexture?.desc;
            if (sourceDesc == null)
                return;

            output.desc = sourceDesc.Clone();
            output.desc.Name = "DepthOfFieldOutput";
            output.desc.ColorFormat = ResolveComputeColorFormat(sourceDesc);
            output.desc.DepthBufferBits = DepthBits.None;
            output.desc.MsaaSamples = MSAASamples.None;
            output.desc.ClearBuffer = false;
            output.desc.FilterMode = FilterMode.Bilinear;
            output.desc.WrapMode = TextureWrapMode.Clamp;
            output.desc.UseMipMap = false;
            output.desc.AutoGenerateMips = false;
            output.desc.MipCount = 1;
            output.desc.EnableRandomWrite = true;
            output.desc.BindTextureMS = false;
        }

        private RenderGraphTextureDesc CreateHistoryDescriptor()
        {
            var desc = m_CoCHistoryPrevious?.desc?.Clone()
                ?? RenderGraphTextureDesc.CreateColorTarget(m_Width, m_Height, GraphicsFormat.R16_SFloat);
            desc.Name = "DepthOfFieldCoCHistoryCurrent";
            desc.Width = m_Width;
            desc.Height = m_Height;
            desc.ColorFormat = GraphicsFormat.R16_SFloat;
            desc.DepthBufferBits = DepthBits.None;
            desc.MsaaSamples = MSAASamples.None;
            desc.FilterMode = FilterMode.Point;
            desc.WrapMode = TextureWrapMode.Clamp;
            desc.UseMipMap = false;
            desc.AutoGenerateMips = false;
            desc.MipCount = 1;
            desc.ClearBuffer = false;
            desc.EnableRandomWrite = true;
            desc.BindTextureMS = false;
            return desc;
        }

        private void ConfigureColorTexture(
            RenderGraphTexture texture,
            RenderGraphTextureDesc sourceDescriptor,
            string name,
            int width,
            int height,
            GraphicsFormat format,
            bool enableRandomWrite,
            FilterMode filterMode,
            float scaleFactor)
        {
            if (texture?.desc == null)
                return;

            if (sourceDescriptor != null)
                texture.desc = sourceDescriptor.Clone();

            texture.desc.Name = name;
            texture.desc.Width = Mathf.Max(1, width);
            texture.desc.Height = Mathf.Max(1, height);
            texture.desc.ColorFormat = format;
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.MsaaSamples = MSAASamples.None;
            texture.desc.FilterMode = filterMode;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.UseMipMap = false;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = 1;
            texture.desc.ClearBuffer = false;
            texture.desc.EnableRandomWrite = enableRandomWrite;
            texture.desc.BindTextureMS = false;
            texture.desc.Slices = sourceDescriptor != null ? Mathf.Max(1, sourceDescriptor.Slices) : 1;
            texture.desc.Dimension = sourceDescriptor?.Dimension ?? TextureDimension.Tex2D;
            texture.desc.UseDynamicScale = sourceDescriptor?.UseDynamicScale ?? false;
            texture.desc.UseDynamicScaleExplicit = sourceDescriptor?.UseDynamicScaleExplicit ?? false;
            texture.desc.ScaleFactor = sourceDescriptor?.ScaleFactor ?? Vector2.one;

            if (Mathf.Approximately(scaleFactor, 1f))
                return;

            if (texture.desc.UseDynamicScale || texture.desc.UseDynamicScaleExplicit)
            {
                texture.desc.UseDynamicScaleExplicit = true;
                texture.desc.ScaleFactor = new Vector2(
                    Mathf.Max(0.001f, texture.desc.ScaleFactor.x * scaleFactor),
                    Mathf.Max(0.001f, texture.desc.ScaleFactor.y * scaleFactor));
            }
        }

        private void EnsureApertureShapeBuffer()
        {
            if (m_ApertureShapeBuffer == null || !m_ApertureShapeBuffer.IsValid())
            {
                m_ApertureShapeBuffer?.Release();
                m_ApertureShapeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, ApertureShapeSampleCount, sizeof(float) * 2);
                m_ApertureShapeDirty = true;
            }
        }

        private void UpdateApertureShapeBuffer()
        {
            if (m_ApertureShapeBuffer == null || m_Camera == null)
                return;

            var curvature = m_Camera.curvature;
            var aperture = Mathf.Max(m_Camera.aperture, Camera.kMinAperture);
            var anamorphism = m_Camera.anamorphism;
            var barrelClipping = m_Camera.barrelClipping;
            var bladeCount = Mathf.Max(3, m_Camera.bladeCount);

            if (!m_ApertureShapeDirty
                && curvature == m_LastApertureCurvature
                && Mathf.Approximately(aperture, m_LastAperture)
                && Mathf.Approximately(anamorphism, m_LastAnamorphism)
                && Mathf.Approximately(barrelClipping, m_LastBarrelClipping)
                && bladeCount == m_LastBladeCount)
            {
                return;
            }

            var rotation = (aperture - Camera.kMinAperture) / (Camera.kMaxAperture - Camera.kMinAperture);
            rotation *= (360f / bladeCount) * Mathf.Deg2Rad;

            var ngonFactor = 1f;
            if (curvature.y - curvature.x > 0f)
                ngonFactor = (aperture - curvature.x) / (curvature.y - curvature.x);

            ngonFactor = Mathf.Clamp01(ngonFactor);
            ngonFactor = Mathf.Lerp(ngonFactor, 0f, Mathf.Abs(anamorphism));
            var anamorphismScale = anamorphism / 4f;

            for (var index = 0; index < ApertureShapeSampleCount; index++)
            {
                var angle = (index / (float)ApertureShapeSampleCount) * Mathf.PI * 2f;
                m_ApertureShapeSamples[index] = ComputeApertureShapePoint(angle, bladeCount, ngonFactor, rotation, anamorphismScale);
            }

            m_ApertureShapeBuffer.SetData(m_ApertureShapeSamples);
            m_LastApertureCurvature = curvature;
            m_LastAperture = aperture;
            m_LastAnamorphism = anamorphism;
            m_LastBarrelClipping = barrelClipping;
            m_LastBladeCount = bladeCount;
            m_ApertureShapeDirty = false;
        }

        private static Vector2 ComputeApertureShapePoint(
            float angle,
            int bladeCount,
            float ngonFactor,
            float rotation,
            float anamorphism)
        {
            var blades = Mathf.Max(3, bladeCount);
            var nt = Mathf.Cos(Mathf.PI / blades);
            var dt = Mathf.Cos(angle - ((Mathf.PI * 2f) / blades) * Mathf.Floor((blades * angle + Mathf.PI) / (Mathf.PI * 2f)));
            var radius = Mathf.Pow(Mathf.Max(1e-6f, nt / Mathf.Max(1e-6f, dt)), ngonFactor);

            var u = radius * Mathf.Cos(angle - rotation);
            var v = radius * Mathf.Sin(angle - rotation);

            v *= 1f + anamorphism;
            u *= 1f - anamorphism;

            return new Vector2(u, v);
        }

        private float ResolveFocusDistance()
        {
            if (m_Settings.focusDistanceMode == FocusDistanceMode.Camera && m_Camera != null)
                return Mathf.Max(m_Camera.focusDistance, 1e-4f);

            return Mathf.Max(m_Settings.focusDistance, 1e-4f);
        }

        private Vector2 ResolveBlurLimits()
        {
            var scale = new Vector2(
                Mathf.Max(1f, m_Width) / 1920f,
                Mathf.Max(1f, m_Height) / 1080f);
            var resolutionScale = Mathf.Min(scale.x, scale.y) * 2f;
            var radiusMultiplier = m_Settings.focusMode == DepthOfFieldMode.UsePhysicalCamera ? 4f : 1f;

            var nearLimit = IsNearLayerActive()
                ? Mathf.Max(radiusMultiplier * resolutionScale * m_Settings.nearMaxBlur, 0.01f)
                : 0f;
            var farLimit = IsFarLayerActive()
                ? Mathf.Max(radiusMultiplier * resolutionScale * m_Settings.farMaxBlur, 0.01f)
                : 0f;

            return new Vector2(nearLimit, farLimit);
        }

        private float ResolvePhysicalMaxCoC(float focusDistance)
        {
            if (m_Camera == null)
                return 0f;

            var aperture = Mathf.Max(m_Camera.aperture, 1e-4f);
            var focalLength = Mathf.Max(m_Camera.focalLength, 1e-4f);
            var focalLengthMeters = focalLength / 1000f;
            var apertureDiameter = focalLength / aperture;
            var sensorSize = m_Camera.sensorSize;
            var useHorizontalGate = m_Camera.gateFit == Camera.GateFitMode.Horizontal;
            var sensorDimension = Mathf.Max(useHorizontalGate ? sensorSize.x : sensorSize.y, 1e-4f);
            var viewportDimension = Mathf.Max(useHorizontalGate ? m_Width : m_Height, 1);
            var sensorScale = (0.5f / sensorDimension) * viewportDimension;

            return sensorScale * (apertureDiameter * focalLengthMeters) / Mathf.Max(focusDistance - focalLengthMeters, 1e-6f);
        }

        private void ResolveManualFocusRanges(out float nearStart, out float nearEnd, out float farStart, out float farEnd)
        {
            nearEnd = Mathf.Max(1e-5f, m_Settings.nearFocusEnd);
            nearStart = Mathf.Min(m_Settings.nearFocusStart, nearEnd - 1e-5f);
            farStart = Mathf.Max(m_Settings.farFocusStart, nearEnd);
            farEnd = Mathf.Max(m_Settings.farFocusEnd, farStart + 1e-5f);
        }

        private Vector2 ResolveAdaptiveSamplingWeights()
        {
            var weight = Mathf.Max(0.5f, m_Settings.adaptiveSamplingWeight);
            return new Vector2(
                weight <= 1f ? weight : 1f,
                weight > 1f ? weight : 1f);
        }

        private bool IsNearLayerActive()
        {
            return m_Settings.nearMaxBlur > 0f && m_Settings.nearFocusEnd > 0f;
        }

        private bool IsFarLayerActive()
        {
            return m_Settings.farMaxBlur > 0f;
        }

        private int ResolveSampleCount(int baseCount)
        {
            var scale = new Vector2(
                Mathf.Max(1f, m_Width) / 1920f,
                Mathf.Max(1f, m_Height) / 1080f);
            var resolutionScale = Mathf.Min(scale.x, scale.y) * 2f;
            return Mathf.Max(3, Mathf.CeilToInt(baseCount * resolutionScale));
        }

        private static int ResolveWidth(VividCameraData cameraData)
        {
            if (cameraData == null)
                return Mathf.Max(1, Screen.width);

            if (cameraData.actualWidth > 0)
                return cameraData.actualWidth;

            if (cameraData.pixelWidth > 0)
                return cameraData.pixelWidth;

            return Mathf.Max(1, Screen.width);
        }

        private static int ResolveHeight(VividCameraData cameraData)
        {
            if (cameraData == null)
                return Mathf.Max(1, Screen.height);

            if (cameraData.actualHeight > 0)
                return cameraData.actualHeight;

            if (cameraData.pixelHeight > 0)
                return cameraData.pixelHeight;

            return Mathf.Max(1, Screen.height);
        }

        private static GraphicsFormat ResolveComputeColorFormat(RenderGraphTextureDesc descriptor)
        {
            var format = descriptor != null && descriptor.ColorFormat != GraphicsFormat.None
                ? descriptor.ColorFormat
                : GraphicsFormat.R16G16B16A16_SFloat;

            return SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.LoadStore)
                ? format
                : GraphicsFormat.R16G16B16A16_SFloat;
        }

        private static RenderGraphTexture CreateTexture(
            string name,
            int width,
            int height,
            GraphicsFormat format,
            FilterMode filterMode,
            bool enableRandomWrite)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(width, height, format)
            };
            texture.desc.Name = name;
            texture.desc.ClearBuffer = false;
            texture.desc.FilterMode = filterMode;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.EnableRandomWrite = enableRandomWrite;
            texture.desc.UseMipMap = false;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = 1;
            return texture;
        }
    }
}
