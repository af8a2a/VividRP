using System;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public partial class BloomPass : UnsafePass, IPostProcessSourceOverridePass, IStablePassResourceLayout
    {
        private const int k_MaxBloomMipCount = 16;
        private const int k_MaxBloomSpdMipCount = 13;
        private const int k_SpdTileSize = 64;

        private static readonly int TexelSizeId          = Shader.PropertyToID("_TexelSize");
        private static readonly int InputTexelSizeId     = Shader.PropertyToID("_InputTexelSize");
        private static readonly int BloomThresholdId     = Shader.PropertyToID("_BloomThreshold");
        private static readonly int InputTextureId       = Shader.PropertyToID("_InputTexture");
        private static readonly int InputLowTextureId    = Shader.PropertyToID("_InputLowTexture");
        private static readonly int InputHighTextureId   = Shader.PropertyToID("_InputHighTexture");
        private static readonly int OutputTextureId      = Shader.PropertyToID("_OutputTexture");
        private static readonly int ParamsId             = Shader.PropertyToID("_Params");
        private static readonly int BloomBicubicParamsId = Shader.PropertyToID("_BloomBicubicParams");
        private static readonly int SpdMipsId            = Shader.PropertyToID("mips");
        private static readonly int SpdNumWorkGroupsId   = Shader.PropertyToID("numWorkGroups");
        private static readonly int SpdWorkGroupOffsetId = Shader.PropertyToID("workGroupOffset");
        private static readonly int SpdGlobalAtomicBufferId = Shader.PropertyToID("spdGlobalAtomic");
        private static readonly int SpdSourceSizeId      = Shader.PropertyToID("_SpdSourceSize");
        private static readonly int SpdMip6SizeId        = Shader.PropertyToID("_SpdMip6Size");
        private static readonly int VividBloomTextureId  = Shader.PropertyToID("_VividBloomTexture");
        private static readonly int VividBloomParamsId   = Shader.PropertyToID("_VividBloomParams");
        private static readonly int VividBloomTintId     = Shader.PropertyToID("_VividBloomTint");
        private static readonly int VividBloomDirtTextureId = Shader.PropertyToID("_VividBloomDirtTexture");
        private static readonly int VividBloomDirtScaleId   = Shader.PropertyToID("_VividBloomDirtScale");
        private static readonly ProfilerMarker s_PrepareSettingsMarker = new("VividRP.RenderPass.Bloom.Prepare.Settings");
        private static readonly ProfilerMarker s_PrepareOutputsMarker = new("VividRP.RenderPass.Bloom.Prepare.Outputs");
        private static readonly ProfilerMarker s_PrepareMipsMarker = new("VividRP.RenderPass.Bloom.Prepare.Mips");
        private static readonly string[] s_MipDownNames = CreateMipNames("BloomMipDown");
        private static readonly string[] s_MipUpNames = CreateMipNames("BloomMipUp");
        private static readonly int[] s_SpdMipTextureIds = CreateSpdMipTextureIds();
        private static readonly uint[] s_ZeroSpdAtomicCounterData = { 0u };

        [RenderGraphResource(Access = AccessFlags.Read)]
        private RenderGraphTexture source = new();

        [RenderGraphResource(
            Name = "BloomTexture",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture bloomTexture = new();

        [RenderGraphResource(
            Name = "ScreenSpaceLensFlareBloomMipTexture",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture screenSpaceLensFlareBloomMipTexture = new();

        private ComputeShader m_PrefilterCS;
        private ComputeShader m_BlurCS;
        private ComputeShader m_UpsampleCS;

        private int m_PrefilterKernel;
        private int m_BlurKernel;
        private int m_DownsampleKernel;
        private int m_UpsampleKernel;
        private int m_SpdDownsampleKernel = -1;

        // RTHandle arrays — allocated once, resized on demand, released in Dispose().
        private readonly RTHandle[] m_MipDownHandles = new RTHandle[k_MaxBloomMipCount];
        private readonly RTHandle[] m_MipUpHandles   = new RTHandle[k_MaxBloomMipCount];

        // TextureHandles imported each frame in Prepare(); valid only during Record().
        private readonly TextureHandle[] m_MipDownTH = new TextureHandle[k_MaxBloomMipCount];
        private readonly TextureHandle[] m_MipUpTH   = new TextureHandle[k_MaxBloomMipCount];

        private BloomSettingsData m_Settings;
        private ScreenSpaceLensFlareSettingsData m_ScreenSpaceLensFlareSettings;
        private GraphicsBuffer m_SpdGlobalAtomicBuffer;
        private int m_MipCount;
        private int m_ScreenSpaceLensFlareBloomMip;
        private int m_ScreenWidth;
        private int m_ScreenHeight;
        private int m_SpdDispatchGroupCountX;
        private int m_SpdDispatchGroupCountY;
        private int m_SpdNumWorkGroups;
        private bool m_ShouldOutputBloomTexture;
        private bool m_ShouldOutputScreenSpaceLensFlareMip;
        private bool m_UseSpdDownsample;
        private bool m_IsPassResourceLayoutDirty;
        private RenderGraphTexture m_OriginalSource;
        private bool m_HasSourceTextureOverride;

        public bool IsPassResourceLayoutDirty => m_IsPassResourceLayoutDirty;

        public BloomPass()
        {
            profilingSampler = new ProfilingSampler(nameof(BloomPass));
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
            m_PrefilterCS = resources.BloomPrefilterCompute;
            m_BlurCS      = resources.BloomBlurCompute;
            m_UpsampleCS  = resources.BloomUpsampleCompute;

            if (m_PrefilterCS != null)
                m_PrefilterKernel = m_PrefilterCS.FindKernel("KMain");
            if (m_BlurCS != null)
            {
                m_BlurKernel       = m_BlurCS.FindKernel("KMain");
                m_DownsampleKernel = m_BlurCS.FindKernel("KDownsample");
                try
                {
                    m_SpdDownsampleKernel = m_BlurCS.FindKernel("KSpdDownsample");
                }
                catch (ArgumentException)
                {
                    m_SpdDownsampleKernel = -1;
                }
            }
            if (m_UpsampleCS != null)
                m_UpsampleKernel = m_UpsampleCS.FindKernel("KMain");

            InitializeFftKernels(m_BlurCS);
        }

        public override void Dispose()
        {
            ReleaseMipHandles();
            ReleaseSpdAtomicCounterBuffer();
            DisposeFftResources();
        }

        public override void Prepare(ContextContainer frameData)
        {
            VividCameraData cameraData;

            using (s_PrepareSettingsMarker.Auto())
            {
                cameraData = frameData.Get<VividCameraData>();
                var camera = cameraData?.camera;
                bool ppAllowed = camera != null && CoreUtils.ArePostProcessesEnabled(camera);
                m_Settings = ppAllowed ? BloomSettingsResolver.Resolve() : BloomSettingsData.CreateDefault();
                m_ScreenSpaceLensFlareSettings = ppAllowed
                    ? ScreenSpaceLensFlareSettingsResolver.Resolve()
                    : ScreenSpaceLensFlareSettingsData.CreateDefault();
            }

            using (s_PrepareOutputsMarker.Auto())
            {
                m_ScreenWidth  = ResolveWidth(cameraData);
                m_ScreenHeight = ResolveHeight(cameraData);

                ConfigureOutputTexture(bloomTexture, 1, 1, "BloomTexture");
                ConfigureOutputTexture(
                    screenSpaceLensFlareBloomMipTexture,
                    1,
                    1,
                    "ScreenSpaceLensFlareBloomMipTexture");

                m_MipCount = 0;
                m_ScreenSpaceLensFlareBloomMip = 0;
                m_ShouldOutputBloomTexture = m_Settings.enabled;
                m_ShouldOutputScreenSpaceLensFlareMip = m_ScreenSpaceLensFlareSettings.enabled;
                m_UseSpdDownsample = false;
                m_UseFftConvolution = false;
                m_SpdDispatchGroupCountX = 0;
                m_SpdDispatchGroupCountY = 0;
                m_SpdNumWorkGroups = 0;
            }

            bool fftRequested = ShouldUseFftConvolution(
                m_Settings.enabled && m_Settings.mode == BloomMode.ConvolutionFFT,
                m_Settings.convolutionKernel != null,
                m_FftKernelsReady);
            if (!fftRequested)
                DisposeFftResources();

            if ((!m_Settings.enabled && !m_ScreenSpaceLensFlareSettings.enabled)
                || m_ScreenWidth <= 0 || m_ScreenHeight <= 0)
                return;

            using (s_PrepareMipsMarker.Auto())
            {
                if (fftRequested)
                {
                    if (PrepareFftResources())
                    {
                        ReleaseMipHandles();
                        ReleaseSpdAtomicCounterBuffer();
                        m_UseFftConvolution = true;
                        return;
                    }

                    DisposeFftResources();
                }

                if (m_PrefilterCS == null || m_BlurCS == null || m_UpsampleCS == null)
                    return;

                float ana    = m_Settings.anamorphic;
                float scaleW = ana < 0f ? 1f + ana * 0.5f : 1f;
                float scaleH = ana > 0f ? 1f - ana * 0.5f : 1f;
                int div      = (int)m_Settings.resolution;
                int baseW    = Mathf.Max(1, Mathf.FloorToInt(m_ScreenWidth  * scaleW) / div);
                int baseH    = Mathf.Max(1, Mathf.FloorToInt(m_ScreenHeight * scaleH) / div);

                int maxDim = Mathf.Max(baseW, baseH);
                m_MipCount = Mathf.Clamp(
                    Mathf.FloorToInt(Mathf.Log(maxDim, 2f)) - 2 - (m_Settings.resolution == BloomResolution.Half ? 0 : 1),
                    1, k_MaxBloomMipCount);

                m_ScreenSpaceLensFlareBloomMip = Mathf.Clamp(
                    m_ScreenSpaceLensFlareSettings.bloomMip,
                    0,
                    m_MipCount - 1);

                ConfigureOutputTexture(bloomTexture, baseW, baseH, "BloomTexture");
                ConfigureOutputTexture(
                    screenSpaceLensFlareBloomMipTexture,
                    Mathf.Max(1, baseW >> m_ScreenSpaceLensFlareBloomMip),
                    Mathf.Max(1, baseH >> m_ScreenSpaceLensFlareBloomMip),
                    "ScreenSpaceLensFlareBloomMipTexture");

                for (int i = 0; i < m_MipCount; i++)
                {
                    int mw = Mathf.Max(1, baseW >> i);
                    int mh = Mathf.Max(1, baseH >> i);
                    EnsureMipHandle(ref m_MipDownHandles[i], mw, mh, s_MipDownNames[i]);
                    EnsureMipHandle(ref m_MipUpHandles[i],   mw, mh, s_MipUpNames[i]);
                    // Import into RenderGraph so the pass declares read/write access.
                    m_MipDownTH[i] = Import(m_MipDownHandles[i]);
                    m_MipUpTH[i]   = Import(m_MipUpHandles[i]);
                }

                bool spdRequested = m_Settings.experimentalSpdDownsample
                    && m_MipCount > 1
                    && m_MipCount <= k_MaxBloomSpdMipCount
                    && m_SpdDownsampleKernel >= 0;

                if (spdRequested)
                {
                    EnsureSpdAtomicCounterBuffer();
                    ZeroSpdAtomicCounterBuffer();
                    m_SpdDispatchGroupCountX = DivUp(baseW, k_SpdTileSize);
                    m_SpdDispatchGroupCountY = DivUp(baseH, k_SpdTileSize);
                    m_SpdNumWorkGroups = m_SpdDispatchGroupCountX * m_SpdDispatchGroupCountY;
                }

                m_UseSpdDownsample = ShouldUseSpdDownsample(
                    m_Settings.experimentalSpdDownsample,
                    m_MipCount,
                    m_SpdDownsampleKernel >= 0,
                    m_SpdGlobalAtomicBuffer != null);
            }
        }

        public override void Record(UnsafePassContext context)
        {
            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

            if (m_UseFftConvolution)
            {
                if (source?.innerHandle.IsValid() != true)
                {
                    SetBloomDisabled(cmd);
                    return;
                }

                using (new ProfilingScope(context.cmd, profilingSampler))
                    ExecuteFftBloom(cmd);
                return;
            }

            if (m_MipCount == 0 || source?.innerHandle.IsValid() != true)
            {
                SetBloomDisabled(cmd);
                return;
            }

            using (new ProfilingScope(context.cmd, profilingSampler))
                ExecuteBloom(cmd);
        }

        // -------------------------------------------------------------------------

        private void ExecuteBloom(CommandBuffer cmd)
        {
            int tw = m_ScreenWidth;
            int th = m_ScreenHeight;

            float lthresh = Mathf.GammaToLinearSpace(m_Settings.threshold);
            float knee    = lthresh * 0.5f + 1e-5f;
            var threshold = new Vector4(lthresh, lthresh - knee, knee * 2f, 0.25f / knee);

            bool hqPrefilter = m_Settings.highQualityPrefiltering;
            bool hqFilter    = m_Settings.highQualityFiltering;

            // ---- 1. Prefilter: source → mipDown[0] ----
            {
                int w = m_MipDownHandles[0].rt.width;
                int h = m_MipDownHandles[0].rt.height;
                SetKeyword(m_PrefilterCS, "LOW_QUALITY",  !hqPrefilter);
                SetKeyword(m_PrefilterCS, "HIGH_QUALITY",  hqPrefilter);
                cmd.SetComputeVectorParam(m_PrefilterCS, TexelSizeId,      new Vector4(w, h, 1f/w, 1f/h));
                cmd.SetComputeVectorParam(m_PrefilterCS, InputTexelSizeId, new Vector4(tw, th, 1f/tw, 1f/th));
                cmd.SetComputeVectorParam(m_PrefilterCS, BloomThresholdId, threshold);
                cmd.SetComputeTextureParam(m_PrefilterCS, m_PrefilterKernel, InputTextureId,  source.innerHandle);
                cmd.SetComputeTextureParam(m_PrefilterCS, m_PrefilterKernel, OutputTextureId, m_MipDownTH[0]);
                cmd.DispatchCompute(m_PrefilterCS, m_PrefilterKernel, DivUp(w, 8), DivUp(h, 8), 1);
            }

            // ---- 2. Downsample chain ----
            if (m_UseSpdDownsample)
                ExecuteSpdDownsample(cmd);
            else
                ExecuteDownsampleChain(cmd);

            // ---- 3. Seed mipUp[last] from mipDown[last] ----
            float scatter = Mathf.Lerp(0.05f, 0.95f, m_Settings.scatter);
            {
                int last = m_MipCount - 1;
                int w = m_MipDownHandles[last].rt.width, h = m_MipDownHandles[last].rt.height;
                cmd.SetComputeVectorParam(m_BlurCS, TexelSizeId,      new Vector4(w, h, 1f/w, 1f/h));
                cmd.SetComputeVectorParam(m_BlurCS, InputTexelSizeId, new Vector4(w, h, 1f/w, 1f/h));
                cmd.SetComputeTextureParam(m_BlurCS, m_BlurKernel, InputTextureId,  m_MipDownTH[last]);
                cmd.SetComputeTextureParam(m_BlurCS, m_BlurKernel, OutputTextureId, m_MipUpTH[last]);
                cmd.DispatchCompute(m_BlurCS, m_BlurKernel, DivUp(w, 8), DivUp(h, 8), 1);
            }

            // ---- 4. Upsample + scatter ----
            SetKeyword(m_UpsampleCS, "LOW_QUALITY",  !hqFilter);
            SetKeyword(m_UpsampleCS, "HIGH_QUALITY",  hqFilter);
            for (int i = m_MipCount - 2; i >= 0; i--)
            {
                int hw = m_MipDownHandles[i].rt.width,   hh = m_MipDownHandles[i].rt.height;
                int lw = m_MipUpHandles[i+1].rt.width,   lh = m_MipUpHandles[i+1].rt.height;
                cmd.SetComputeVectorParam(m_UpsampleCS, ParamsId,             new Vector4(scatter, 0f, 0f, 0f));
                cmd.SetComputeVectorParam(m_UpsampleCS, BloomBicubicParamsId, new Vector4(lw, lh, 1f/lw, 1f/lh));
                cmd.SetComputeVectorParam(m_UpsampleCS, TexelSizeId,          new Vector4(hw, hh, 1f/hw, 1f/hh));
                cmd.SetComputeTextureParam(m_UpsampleCS, m_UpsampleKernel, InputLowTextureId,  m_MipUpTH[i+1]);
                cmd.SetComputeTextureParam(m_UpsampleCS, m_UpsampleKernel, InputHighTextureId, m_MipDownTH[i]);
                cmd.SetComputeTextureParam(m_UpsampleCS, m_UpsampleKernel, OutputTextureId,    m_MipUpTH[i]);
                cmd.DispatchCompute(m_UpsampleCS, m_UpsampleKernel, DivUp(hw, 8), DivUp(hh, 8), 1);
            }

            // ---- 5. Bind globals for FinalBlitPass ----
            var bloomOutput = (RTHandle)bloomTexture.innerHandle;
            var screenSpaceLensFlareMipOutput = (RTHandle)screenSpaceLensFlareBloomMipTexture.innerHandle;

            if (bloomOutput != null)
            {
                if (m_ShouldOutputBloomTexture)
                    Blitter.BlitCameraTexture(cmd, m_MipUpHandles[0], bloomOutput, 0f, true);
                else
                    ClearTexture(cmd, bloomOutput);
            }

            if (screenSpaceLensFlareMipOutput != null)
            {
                if (m_ShouldOutputScreenSpaceLensFlareMip)
                {
                    Blitter.BlitCameraTexture(
                        cmd,
                        m_MipUpHandles[m_ScreenSpaceLensFlareBloomMip],
                        screenSpaceLensFlareMipOutput,
                        0f,
                        true);
                }
                else
                {
                    ClearTexture(cmd, screenSpaceLensFlareMipOutput);
                }
            }

            float bloomIntensity = m_Settings.enabled
                ? Mathf.Pow(2f, m_Settings.intensity) - 1f
                : m_ScreenSpaceLensFlareSettings.enabled ? 1f : 0f;
            bool  hasDirt        = m_Settings.enabled && m_Settings.dirtTexture != null && m_Settings.dirtIntensity > 0f;
            var   tint           = m_Settings.enabled ? (Vector4)m_Settings.tint.linear : Vector4.one;

            if (bloomOutput != null)
                cmd.SetGlobalTexture(VividBloomTextureId, bloomOutput);
            else
                cmd.SetGlobalTexture(VividBloomTextureId, Texture2D.blackTexture);
            cmd.SetGlobalVector(VividBloomParamsId,
                new Vector4(bloomIntensity, m_Settings.dirtIntensity, 1f, hasDirt ? 1f : 0f));
            cmd.SetGlobalVector(VividBloomTintId, new Vector4(tint.x, tint.y, tint.z, 1f));

            if (hasDirt)
            {
                cmd.SetGlobalTexture(VividBloomDirtTextureId, m_Settings.dirtTexture);
                float dirtRatio   = (float)m_Settings.dirtTexture.width / m_Settings.dirtTexture.height;
                float screenRatio = (float)tw / th;
                Vector4 dirtScale;
                if (dirtRatio > screenRatio)
                {
                    float s = screenRatio / dirtRatio;
                    dirtScale = new Vector4(s, 1f, (1f - s) * 0.5f, 0f);
                }
                else
                {
                    float s = dirtRatio / screenRatio;
                    dirtScale = new Vector4(1f, s, 0f, (1f - s) * 0.5f);
                }
                cmd.SetGlobalVector(VividBloomDirtScaleId, dirtScale);
            }
        }

        private void ExecuteDownsampleChain(CommandBuffer cmd)
        {
            for (int i = 0; i < m_MipCount - 1; i++)
            {
                int sw = m_MipDownHandles[i].rt.width;
                int sh = m_MipDownHandles[i].rt.height;
                int dw = m_MipDownHandles[i + 1].rt.width;
                int dh = m_MipDownHandles[i + 1].rt.height;

                cmd.SetComputeVectorParam(m_BlurCS, TexelSizeId, new Vector4(dw, dh, 1f / dw, 1f / dh));
                cmd.SetComputeVectorParam(m_BlurCS, InputTexelSizeId, new Vector4(sw, sh, 1f / sw, 1f / sh));
                cmd.SetComputeTextureParam(m_BlurCS, m_DownsampleKernel, InputTextureId, m_MipDownTH[i]);
                cmd.SetComputeTextureParam(m_BlurCS, m_DownsampleKernel, OutputTextureId, m_MipDownTH[i + 1]);
                cmd.DispatchCompute(m_BlurCS, m_DownsampleKernel, DivUp(dw, 8), DivUp(dh, 8), 1);
            }
        }

        private void ExecuteSpdDownsample(CommandBuffer cmd)
        {
            if (m_MipCount <= 1 || m_SpdGlobalAtomicBuffer == null)
                return;

            int sourceW = m_MipDownHandles[0].rt.width;
            int sourceH = m_MipDownHandles[0].rt.height;
            int mip6Index = Mathf.Min(6, m_MipCount - 1);
            int mip6W = m_MipDownHandles[mip6Index].rt.width;
            int mip6H = m_MipDownHandles[mip6Index].rt.height;

            cmd.SetComputeIntParam(m_BlurCS, SpdMipsId, m_MipCount - 1);
            cmd.SetComputeIntParam(m_BlurCS, SpdNumWorkGroupsId, m_SpdNumWorkGroups);
            cmd.SetComputeVectorParam(m_BlurCS, SpdWorkGroupOffsetId, Vector4.zero);
            cmd.SetComputeVectorParam(m_BlurCS, SpdSourceSizeId, new Vector4(sourceW, sourceH, 1f / sourceW, 1f / sourceH));
            cmd.SetComputeVectorParam(m_BlurCS, SpdMip6SizeId, new Vector4(mip6W, mip6H, 1f / mip6W, 1f / mip6H));
            cmd.SetComputeBufferParam(m_BlurCS, m_SpdDownsampleKernel, SpdGlobalAtomicBufferId, m_SpdGlobalAtomicBuffer);

            BindSpdMipTextures(cmd);
            cmd.DispatchCompute(
                m_BlurCS,
                m_SpdDownsampleKernel,
                Mathf.Max(1, m_SpdDispatchGroupCountX),
                Mathf.Max(1, m_SpdDispatchGroupCountY),
                1);
        }

        private void SetBloomDisabled(CommandBuffer cmd)
        {
            cmd.SetGlobalVector(VividBloomParamsId, Vector4.zero);
            cmd.SetGlobalTexture(VividBloomTextureId, Texture2D.blackTexture);
        }

        // -------------------------------------------------------------------------

        private static void ConfigureOutputTexture(RenderGraphTexture texture, int width, int height, string name)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Width = Mathf.Max(1, width);
            texture.desc.Height = Mathf.Max(1, height);
            texture.desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            texture.desc.EnableRandomWrite = true;
            texture.desc.FilterMode = FilterMode.Bilinear;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.ClearBuffer = true;
            texture.desc.ClearColor = Color.clear;
            texture.desc.Name = name;
        }

        private void BindSpdMipTextures(CommandBuffer cmd)
        {
            for (int shaderMipIndex = 0; shaderMipIndex < s_SpdMipTextureIds.Length; shaderMipIndex++)
            {
                int boundMipIndex = GetBoundSpdMipIndex(shaderMipIndex, m_MipCount);
                cmd.SetComputeTextureParam(
                    m_BlurCS,
                    m_SpdDownsampleKernel,
                    s_SpdMipTextureIds[shaderMipIndex],
                    m_MipDownTH[boundMipIndex]);
            }
        }

        private void EnsureSpdAtomicCounterBuffer()
        {
            if (m_SpdGlobalAtomicBuffer != null
                && m_SpdGlobalAtomicBuffer.count == 1
                && m_SpdGlobalAtomicBuffer.stride == sizeof(uint))
                return;

            m_SpdGlobalAtomicBuffer?.Dispose();
            m_SpdGlobalAtomicBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
        }

        private void ZeroSpdAtomicCounterBuffer()
        {
            m_SpdGlobalAtomicBuffer?.SetData(s_ZeroSpdAtomicCounterData);
        }

        private void ReleaseSpdAtomicCounterBuffer()
        {
            m_SpdGlobalAtomicBuffer?.Dispose();
            m_SpdGlobalAtomicBuffer = null;
        }

        internal static bool ShouldUseSpdDownsample(
            bool requested,
            int mipCount,
            bool hasKernel,
            bool hasCounterBuffer)
        {
            return requested
                && mipCount > 1
                && mipCount <= k_MaxBloomSpdMipCount
                && hasKernel
                && hasCounterBuffer;
        }

        internal static int GetBoundSpdMipIndex(int shaderMipIndex, int mipCount)
        {
            int clampedMipCount = Mathf.Clamp(mipCount, 1, k_MaxBloomSpdMipCount);
            return Mathf.Clamp(shaderMipIndex, 0, clampedMipCount - 1);
        }

        private static void ClearTexture(CommandBuffer cmd, RTHandle texture)
        {
            CoreUtils.SetRenderTarget(cmd, texture);
            cmd.ClearRenderTarget(false, true, Color.clear);
        }

        private static void EnsureMipHandle(ref RTHandle handle, int width, int height, string name)
        {
            if (handle != null && handle.rt != null
                && handle.rt.width == width && handle.rt.height == height)
                return;

            handle?.Release();
            handle = RTHandles.Alloc(
                width, height,
                colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite: true,
                filterMode: FilterMode.Bilinear,
                wrapMode: TextureWrapMode.Clamp,
                name: name);
        }

        private void ReleaseMipHandles()
        {
            for (int i = 0; i < k_MaxBloomMipCount; i++)
            {
                m_MipDownHandles[i]?.Release();
                m_MipDownHandles[i] = null;
                m_MipUpHandles[i]?.Release();
                m_MipUpHandles[i] = null;
            }
        }

        private static string[] CreateMipNames(string prefix)
        {
            var names = new string[k_MaxBloomMipCount];
            for (int i = 0; i < names.Length; i++)
                names[i] = $"{prefix}{i}";

            return names;
        }

        private static int[] CreateSpdMipTextureIds()
        {
            var ids = new int[k_MaxBloomSpdMipCount];
            for (int i = 0; i < ids.Length; i++)
                ids[i] = Shader.PropertyToID($"_SpdMip{i}");

            return ids;
        }

        private static void SetKeyword(ComputeShader cs, string keyword, bool enabled)
        {
            if (enabled) cs.EnableKeyword(keyword);
            else         cs.DisableKeyword(keyword);
        }

        private static int DivUp(int n, int d) => (n + d - 1) / d;

        private static int ResolveWidth(VividCameraData d)
        {
            if (d == null) return Screen.width;
            if (d.actualWidth  > 0) return d.actualWidth;
            if (d.pixelWidth   > 0) return d.pixelWidth;
            return Screen.width;
        }

        private static int ResolveHeight(VividCameraData d)
        {
            if (d == null) return Screen.height;
            if (d.actualHeight > 0) return d.actualHeight;
            if (d.pixelHeight  > 0) return d.pixelHeight;
            return Screen.height;
        }
    }
}
