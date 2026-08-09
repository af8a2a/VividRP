using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class LocalExposurePass : UnsafePass, IPostProcessSourceOverridePass, IStablePassResourceLayout
    {
        private const int TileSize = 64;
        private const int GridDepth = 32;
        private const int ThreadGroupSize = 8;
        private const int MaxBlurRadius = 32;

        private const string BuildBilateralGridKernelName = "BuildBilateralGrid";
        private const string SetupLogLuminanceKernelName = "SetupLogLuminance";
        private const string BlurLogLuminanceHorizontalKernelName = "BlurLogLuminanceHorizontal";
        private const string BlurLogLuminanceVerticalKernelName = "BlurLogLuminanceVertical";
        private const string ApplyLocalExposureKernelName = "ApplyLocalExposure";
        private const string CopyKernelName = "Copy";

        private static readonly int InputTextureId = Shader.PropertyToID("_LocalExposureInputTexture");
        private static readonly int OutputTextureId = Shader.PropertyToID("_LocalExposureOutputTexture");
        private static readonly int BilateralGridId = Shader.PropertyToID("_LocalExposureBilateralGrid");
        private static readonly int BilateralGridTextureId = Shader.PropertyToID("_LocalExposureBilateralGridTexture");
        private static readonly int LogLuminanceId = Shader.PropertyToID("_LocalExposureLogLuminance");
        private static readonly int LogLuminanceTextureId = Shader.PropertyToID("_LocalExposureLogLuminanceTexture");
        private static readonly int BlurTempId = Shader.PropertyToID("_LocalExposureBlurTemp");
        private static readonly int BlurTempTextureId = Shader.PropertyToID("_LocalExposureBlurTempTexture");
        private static readonly int BlurredLogLuminanceId = Shader.PropertyToID("_LocalExposureBlurredLogLuminance");
        private static readonly int BlurredLogLuminanceTextureId = Shader.PropertyToID("_LocalExposureBlurredLogLuminanceTexture");
        private static readonly int ExposureBufferId = Shader.PropertyToID("_LocalExposureExposureBuffer");
        private static readonly int PreExposureBufferId = Shader.PropertyToID("_LocalExposurePreExposureBuffer");
        private static readonly int HighlightContrastCurveId = Shader.PropertyToID("_LocalExposureHighlightContrastCurve");
        private static readonly int ShadowContrastCurveId = Shader.PropertyToID("_LocalExposureShadowContrastCurve");
        private static readonly int Params0Id = Shader.PropertyToID("_LocalExposureParams0");
        private static readonly int Params1Id = Shader.PropertyToID("_LocalExposureParams1");
        private static readonly int Params2Id = Shader.PropertyToID("_LocalExposureParams2");
        private static readonly int ScreenSizeId = Shader.PropertyToID("_LocalExposureScreenSize");
        private static readonly int GridParamsId = Shader.PropertyToID("_LocalExposureGridParams");
        private static readonly int BlurParamsId = Shader.PropertyToID("_LocalExposureBlurParams");
        private static readonly int HighlightCurveParamsId = Shader.PropertyToID("_LocalExposureHighlightCurveParams");
        private static readonly int ShadowCurveParamsId = Shader.PropertyToID("_LocalExposureShadowCurveParams");

        [RenderGraphResource(Access = AccessFlags.Read)]
        private RenderGraphTexture source = new();

        [RenderGraphResource(
            Name = "LocalExposureOutput",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OutputTexture;

        [RenderGraphResource(Name = "LocalExposureBilateralGrid", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_BilateralGrid;

        [RenderGraphResource(Name = "LocalExposureLogLuminance", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_LogLuminance;

        [RenderGraphResource(Name = "LocalExposureBlurTemp", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_BlurTemp;

        [RenderGraphResource(Name = "LocalExposureBlurredLogLuminance", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_BlurredLogLuminance;

        private ComputeShader m_Compute;
        private LocalExposureSettingsData m_Settings;
        private AutoExposureSettingsData m_AutoExposureSettings;
        private VividExposureData m_ExposureData;
        private int m_BuildBilateralGridKernel = -1;
        private int m_SetupLogLuminanceKernel = -1;
        private int m_BlurLogLuminanceHorizontalKernel = -1;
        private int m_BlurLogLuminanceVerticalKernel = -1;
        private int m_ApplyLocalExposureKernel = -1;
        private int m_CopyKernel = -1;
        private int m_Width = 1;
        private int m_Height = 1;
        private int m_GridWidth = 1;
        private int m_GridHeight = 1;
        private int m_BlurRadius;
        private float m_BlurSigma = 1f;
        private bool m_IsPassResourceLayoutDirty;
        private RenderGraphTexture m_OriginalSource;
        private bool m_HasSourceTextureOverride;

        public bool IsPassResourceLayoutDirty => m_IsPassResourceLayoutDirty;

        public LocalExposurePass()
        {
            profilingSampler = new ProfilingSampler(nameof(LocalExposurePass));

            m_OutputTexture = CreatePassOwnedTexture("LocalExposureOutput", GraphicsFormat.R16G16B16A16_SFloat);
            m_BilateralGrid = CreatePassOwnedTexture("LocalExposureBilateralGrid", GraphicsFormat.R32G32_SFloat);
            m_LogLuminance = CreatePassOwnedTexture("LocalExposureLogLuminance", GraphicsFormat.R32_SFloat);
            m_BlurTemp = CreatePassOwnedTexture("LocalExposureBlurTemp", GraphicsFormat.R32_SFloat);
            m_BlurredLogLuminance = CreatePassOwnedTexture("LocalExposureBlurredLogLuminance", GraphicsFormat.R32_SFloat);
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
            m_Compute = resources?.LocalExposureCompute;
            ResolveKernels();
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var camera = cameraData?.camera;
            var postProcessingAllowed = camera != null && CoreUtils.ArePostProcessesEnabled(camera);

            m_Settings = postProcessingAllowed
                ? LocalExposureSettingsResolver.Resolve()
                : LocalExposureSettingsData.CreateDefault();
            m_ExposureData = frameData.Get<VividExposureData>();
            m_AutoExposureSettings = m_ExposureData != null
                ? m_ExposureData.settings
                : AutoExposureSettingsData.CreateDefault();

            var sourceDescriptor = source?.desc;
            m_Width = RenderGraphTextureDescUtility.ResolveMaxExplicitWidth(
                cameraData?.actualWidth ?? 0,
                cameraData?.pixelWidth ?? 0,
                Screen.width,
                sourceDescriptor);
            m_Height = RenderGraphTextureDescUtility.ResolveMaxExplicitHeight(
                cameraData?.actualHeight ?? 0,
                cameraData?.pixelHeight ?? 0,
                Screen.height,
                sourceDescriptor);
            m_Width = Mathf.Max(1, m_Width);
            m_Height = Mathf.Max(1, m_Height);
            m_GridWidth = DivUp(m_Width, TileSize);
            m_GridHeight = DivUp(m_Height, TileSize);
            m_BlurRadius = ResolveBlurRadius(m_Settings, m_Width, m_Height);
            m_BlurSigma = Mathf.Max(1f, m_BlurRadius / 3f);

            ConfigureSceneColorTexture(m_OutputTexture, sourceDescriptor, "LocalExposureOutput", m_Width, m_Height);
            ConfigureGridTexture(m_BilateralGrid, m_GridWidth, m_GridHeight);
            ConfigureScratchTexture(m_LogLuminance, "LocalExposureLogLuminance", m_Width, m_Height);
            ConfigureScratchTexture(m_BlurTemp, "LocalExposureBlurTemp", m_Width, m_Height);
            ConfigureScratchTexture(m_BlurredLogLuminance, "LocalExposureBlurredLogLuminance", m_Width, m_Height);
        }

        public override void Record(UnsafePassContext context)
        {
            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

            if (source == null
                || !source.innerHandle.IsValid()
                || m_OutputTexture == null
                || !m_OutputTexture.innerHandle.IsValid())
            {
                return;
            }

            using (new ProfilingScope(context.cmd, profilingSampler))
            {
                if (!m_Settings.enabled || !SupportsLocalExposure())
                {
                    CopySource(cmd);
                    return;
                }

                ExecuteLocalExposure(cmd);
            }
        }

        public override void Dispose()
        {
            m_Compute = null;
            m_BuildBilateralGridKernel = -1;
            m_SetupLogLuminanceKernel = -1;
            m_BlurLogLuminanceHorizontalKernel = -1;
            m_BlurLogLuminanceVerticalKernel = -1;
            m_ApplyLocalExposureKernel = -1;
            m_CopyKernel = -1;
            LocalExposureCurveUtility.Dispose();
            m_IsPassResourceLayoutDirty = false;
        }

        internal RenderGraphTexture GetOutputTexture()
        {
            return m_OutputTexture;
        }

        private void ResolveKernels()
        {
            m_BuildBilateralGridKernel = FindKernel(m_Compute, BuildBilateralGridKernelName);
            m_SetupLogLuminanceKernel = FindKernel(m_Compute, SetupLogLuminanceKernelName);
            m_BlurLogLuminanceHorizontalKernel = FindKernel(m_Compute, BlurLogLuminanceHorizontalKernelName);
            m_BlurLogLuminanceVerticalKernel = FindKernel(m_Compute, BlurLogLuminanceVerticalKernelName);
            m_ApplyLocalExposureKernel = FindKernel(m_Compute, ApplyLocalExposureKernelName);
            m_CopyKernel = FindKernel(m_Compute, CopyKernelName);
        }

        private bool SupportsLocalExposure()
        {
            return m_Compute != null
                && m_BuildBilateralGridKernel >= 0
                && m_SetupLogLuminanceKernel >= 0
                && m_BlurLogLuminanceHorizontalKernel >= 0
                && m_BlurLogLuminanceVerticalKernel >= 0
                && m_ApplyLocalExposureKernel >= 0
                && m_BilateralGrid?.innerHandle.IsValid() == true
                && m_LogLuminance?.innerHandle.IsValid() == true
                && m_BlurTemp?.innerHandle.IsValid() == true
                && m_BlurredLogLuminance?.innerHandle.IsValid() == true;
        }

        private void ExecuteLocalExposure(CommandBuffer cmd)
        {
            var exposureBuffer = m_ExposureData?.frameExposureBuffer
                ?? m_ExposureData?.defaultExposureBuffer
                ?? VividAutoExposureSystem.GetOrCreateDefaultExposureBuffer();
            var preExposureBuffer = VividAutoExposureSystem.ResolvePreExposureBuffer(m_ExposureData);

            if (exposureBuffer == null || preExposureBuffer == null)
            {
                CopySource(cmd);
                return;
            }

            BindCommonParameters(cmd, exposureBuffer, preExposureBuffer);

            cmd.SetComputeTextureParam(m_Compute, m_BuildBilateralGridKernel, InputTextureId, source.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, m_BuildBilateralGridKernel, BilateralGridId, m_BilateralGrid.innerHandle);
            cmd.DispatchCompute(m_Compute, m_BuildBilateralGridKernel, m_GridWidth, m_GridHeight, 1);

            cmd.SetComputeTextureParam(m_Compute, m_SetupLogLuminanceKernel, InputTextureId, source.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, m_SetupLogLuminanceKernel, LogLuminanceId, m_LogLuminance.innerHandle);
            cmd.DispatchCompute(m_Compute, m_SetupLogLuminanceKernel, DivUp(m_Width, ThreadGroupSize), DivUp(m_Height, ThreadGroupSize), 1);

            cmd.SetComputeTextureParam(m_Compute, m_BlurLogLuminanceHorizontalKernel, LogLuminanceTextureId, m_LogLuminance.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, m_BlurLogLuminanceHorizontalKernel, BlurTempId, m_BlurTemp.innerHandle);
            cmd.DispatchCompute(m_Compute, m_BlurLogLuminanceHorizontalKernel, DivUp(m_Width, ThreadGroupSize), DivUp(m_Height, ThreadGroupSize), 1);

            cmd.SetComputeTextureParam(m_Compute, m_BlurLogLuminanceVerticalKernel, BlurTempTextureId, m_BlurTemp.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, m_BlurLogLuminanceVerticalKernel, BlurredLogLuminanceId, m_BlurredLogLuminance.innerHandle);
            cmd.DispatchCompute(m_Compute, m_BlurLogLuminanceVerticalKernel, DivUp(m_Width, ThreadGroupSize), DivUp(m_Height, ThreadGroupSize), 1);

            cmd.SetComputeTextureParam(m_Compute, m_ApplyLocalExposureKernel, InputTextureId, source.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, m_ApplyLocalExposureKernel, OutputTextureId, m_OutputTexture.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, m_ApplyLocalExposureKernel, BilateralGridTextureId, m_BilateralGrid.innerHandle);
            cmd.SetComputeTextureParam(m_Compute, m_ApplyLocalExposureKernel, BlurredLogLuminanceTextureId, m_BlurredLogLuminance.innerHandle);
            cmd.DispatchCompute(m_Compute, m_ApplyLocalExposureKernel, DivUp(m_Width, ThreadGroupSize), DivUp(m_Height, ThreadGroupSize), 1);
        }

        private void BindCommonParameters(CommandBuffer cmd, GraphicsBuffer exposureBuffer, GraphicsBuffer preExposureBuffer)
        {
            cmd.SetComputeBufferParam(m_Compute, m_BuildBilateralGridKernel, ExposureBufferId, exposureBuffer);
            cmd.SetComputeBufferParam(m_Compute, m_BuildBilateralGridKernel, PreExposureBufferId, preExposureBuffer);
            cmd.SetComputeBufferParam(m_Compute, m_SetupLogLuminanceKernel, PreExposureBufferId, preExposureBuffer);
            cmd.SetComputeBufferParam(m_Compute, m_ApplyLocalExposureKernel, ExposureBufferId, exposureBuffer);
            cmd.SetComputeBufferParam(m_Compute, m_ApplyLocalExposureKernel, PreExposureBufferId, preExposureBuffer);

            var screenSize = new Vector4(m_Width, m_Height, 1f / m_Width, 1f / m_Height);
            var gridParams = new Vector4(
                m_GridWidth,
                m_GridHeight,
                m_Width / (float)Mathf.Max(TileSize, m_GridWidth * TileSize),
                m_Height / (float)Mathf.Max(TileSize, m_GridHeight * TileSize));
            var blurParams = new Vector4(m_BlurRadius, m_BlurSigma, 0f, 0f);
            var params0 = new Vector4(
                m_Settings.highlightContrastScale,
                m_Settings.shadowContrastScale,
                m_Settings.detailStrength,
                m_Settings.blurredLuminanceBlend);
            var params1 = new Vector4(
                m_Settings.middleGreyExposureCompensation,
                m_Settings.highlightThreshold,
                m_Settings.shadowThreshold,
                m_Settings.highlightThresholdStrength);
            var params2 = new Vector4(
                m_Settings.shadowThresholdStrength,
                m_AutoExposureSettings.histogramScale,
                m_AutoExposureSettings.histogramBias,
                m_AutoExposureSettings.luminanceMin);

            cmd.SetComputeVectorParam(m_Compute, ScreenSizeId, screenSize);
            cmd.SetComputeVectorParam(m_Compute, GridParamsId, gridParams);
            cmd.SetComputeVectorParam(m_Compute, BlurParamsId, blurParams);
            cmd.SetComputeVectorParam(m_Compute, Params0Id, params0);
            cmd.SetComputeVectorParam(m_Compute, Params1Id, params1);
            cmd.SetComputeVectorParam(m_Compute, Params2Id, params2);
            cmd.SetComputeVectorParam(
                m_Compute,
                HighlightCurveParamsId,
                new Vector4(
                    m_Settings.highlightContrastCurveMinEV100,
                    m_Settings.highlightContrastCurveInvRange,
                    m_Settings.highlightContrastCurveEnabled ? 1f : 0f,
                    0f));
            cmd.SetComputeVectorParam(
                m_Compute,
                ShadowCurveParamsId,
                new Vector4(
                    m_Settings.shadowContrastCurveMinEV100,
                    m_Settings.shadowContrastCurveInvRange,
                    m_Settings.shadowContrastCurveEnabled ? 1f : 0f,
                    0f));

            cmd.SetComputeTextureParam(
                m_Compute,
                m_ApplyLocalExposureKernel,
                HighlightContrastCurveId,
                m_Settings.highlightContrastCurveTexture != null
                    ? m_Settings.highlightContrastCurveTexture
                    : Texture2D.blackTexture);
            cmd.SetComputeTextureParam(
                m_Compute,
                m_ApplyLocalExposureKernel,
                ShadowContrastCurveId,
                m_Settings.shadowContrastCurveTexture != null
                    ? m_Settings.shadowContrastCurveTexture
                    : Texture2D.blackTexture);
        }

        private void CopySource(CommandBuffer cmd)
        {
            if (m_Compute != null && m_CopyKernel >= 0)
            {
                cmd.SetComputeVectorParam(m_Compute, ScreenSizeId, new Vector4(m_Width, m_Height, 1f / m_Width, 1f / m_Height));
                cmd.SetComputeTextureParam(m_Compute, m_CopyKernel, InputTextureId, source.innerHandle);
                cmd.SetComputeTextureParam(m_Compute, m_CopyKernel, OutputTextureId, m_OutputTexture.innerHandle);
                cmd.DispatchCompute(m_Compute, m_CopyKernel, DivUp(m_Width, ThreadGroupSize), DivUp(m_Height, ThreadGroupSize), 1);
                return;
            }

            RTHandle sourceHandle = source.innerHandle;
            RTHandle outputHandle = m_OutputTexture.innerHandle;
            if (sourceHandle != null && outputHandle != null)
                Blitter.BlitCameraTexture(cmd, sourceHandle, outputHandle, 0f, true);
        }

        private static void ConfigureSceneColorTexture(
            RenderGraphTexture texture,
            RenderGraphTextureDesc sourceDescriptor,
            string name,
            int width,
            int height)
        {
            if (texture?.desc == null)
                return;

            if (sourceDescriptor != null)
                texture.desc = sourceDescriptor.Clone();

            texture.desc.Name = name;
            texture.desc.Width = Mathf.Max(1, width);
            texture.desc.Height = Mathf.Max(1, height);
            texture.desc.Slices = 1;
            texture.desc.Dimension = TextureDimension.Tex2D;
            texture.desc.ColorFormat = sourceDescriptor.ResolveColorFormat(
                GraphicsFormat.R16G16B16A16_SFloat);
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.MsaaSamples = MSAASamples.None;
            texture.desc.FilterMode = FilterMode.Bilinear;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.UseMipMap = false;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = 1;
            texture.desc.ClearBuffer = false;
            texture.desc.EnableRandomWrite = true;
            texture.desc.BindTextureMS = false;
        }

        private static void ConfigureGridTexture(RenderGraphTexture texture, int width, int height)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Name = "LocalExposureBilateralGrid";
            texture.desc.Width = Mathf.Max(1, width);
            texture.desc.Height = Mathf.Max(1, height);
            texture.desc.Slices = GridDepth;
            texture.desc.Dimension = TextureDimension.Tex3D;
            texture.desc.ColorFormat = GraphicsFormat.R32G32_SFloat;
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.MsaaSamples = MSAASamples.None;
            texture.desc.FilterMode = FilterMode.Bilinear;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.UseMipMap = false;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = 1;
            texture.desc.ClearBuffer = false;
            texture.desc.EnableRandomWrite = true;
            texture.desc.BindTextureMS = false;
            texture.desc.UseDynamicScale = false;
            texture.desc.UseDynamicScaleExplicit = false;
            texture.desc.ScaleFactor = Vector2.one;
        }

        private static void ConfigureScratchTexture(RenderGraphTexture texture, string name, int width, int height)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Name = name;
            texture.desc.Width = Mathf.Max(1, width);
            texture.desc.Height = Mathf.Max(1, height);
            texture.desc.Slices = 1;
            texture.desc.Dimension = TextureDimension.Tex2D;
            texture.desc.ColorFormat = GraphicsFormat.R32_SFloat;
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.MsaaSamples = MSAASamples.None;
            texture.desc.FilterMode = FilterMode.Bilinear;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.UseMipMap = false;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = 1;
            texture.desc.ClearBuffer = false;
            texture.desc.EnableRandomWrite = true;
            texture.desc.BindTextureMS = false;
            texture.desc.UseDynamicScale = false;
            texture.desc.UseDynamicScaleExplicit = false;
            texture.desc.ScaleFactor = Vector2.one;
        }

        private static RenderGraphTexture CreatePassOwnedTexture(string name, GraphicsFormat format)
        {
            var texture = RenderGraphTexture.CreateOutput(name, format);
            texture.desc.ClearBuffer = false;
            texture.desc.EnableRandomWrite = true;
            texture.desc.FilterMode = FilterMode.Bilinear;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        private static int ResolveBlurRadius(in LocalExposureSettingsData settings, int width, int height)
        {
            if (!settings.enabled || settings.blurredLuminanceBlend <= 0f)
                return 0;

            var percent = Mathf.Clamp(settings.blurredLuminanceKernelSizePercent, 0f, 100f) * 0.01f;
            var minDimension = Mathf.Max(1, Mathf.Min(width, height));
            return Mathf.Clamp(Mathf.RoundToInt(minDimension * percent * 0.5f), 0, MaxBlurRadius);
        }

        private static int FindKernel(ComputeShader shader, string kernelName)
        {
            if (shader == null || !shader.HasKernel(kernelName))
                return -1;

            return shader.FindKernel(kernelName);
        }

        private static int DivUp(int value, int divisor)
        {
            return (Mathf.Max(0, value) + divisor - 1) / divisor;
        }
    }
}
