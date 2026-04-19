using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public class BloomPass : UnsafePass, IPostProcessSourceOverridePass
    {
        private const int k_MaxBloomMipCount = 16;

        private static readonly int TexelSizeId          = Shader.PropertyToID("_TexelSize");
        private static readonly int InputTexelSizeId     = Shader.PropertyToID("_InputTexelSize");
        private static readonly int BloomThresholdId     = Shader.PropertyToID("_BloomThreshold");
        private static readonly int InputTextureId       = Shader.PropertyToID("_InputTexture");
        private static readonly int InputLowTextureId    = Shader.PropertyToID("_InputLowTexture");
        private static readonly int InputHighTextureId   = Shader.PropertyToID("_InputHighTexture");
        private static readonly int OutputTextureId      = Shader.PropertyToID("_OutputTexture");
        private static readonly int ParamsId             = Shader.PropertyToID("_Params");
        private static readonly int BloomBicubicParamsId = Shader.PropertyToID("_BloomBicubicParams");
        private static readonly int VividBloomTextureId  = Shader.PropertyToID("_VividBloomTexture");
        private static readonly int VividBloomParamsId   = Shader.PropertyToID("_VividBloomParams");
        private static readonly int VividBloomTintId     = Shader.PropertyToID("_VividBloomTint");
        private static readonly int VividBloomDirtTextureId = Shader.PropertyToID("_VividBloomDirtTexture");
        private static readonly int VividBloomDirtScaleId   = Shader.PropertyToID("_VividBloomDirtScale");

        [RenderGraphResource(Access = AccessFlags.Read)]
        private RenderGraphTexture source = new();

        [RenderGraphResource(
            Name = "BloomTexture",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture bloomTexture = new();

        private ComputeShader m_PrefilterCS;
        private ComputeShader m_BlurCS;
        private ComputeShader m_UpsampleCS;

        private int m_PrefilterKernel;
        private int m_BlurKernel;
        private int m_DownsampleKernel;
        private int m_UpsampleKernel;

        // RTHandle arrays — allocated once, resized on demand, released in Dispose().
        private readonly RTHandle[] m_MipDownHandles = new RTHandle[k_MaxBloomMipCount];
        private readonly RTHandle[] m_MipUpHandles   = new RTHandle[k_MaxBloomMipCount];

        // TextureHandles imported each frame in Prepare(); valid only during Record().
        private readonly TextureHandle[] m_MipDownTH = new TextureHandle[k_MaxBloomMipCount];
        private readonly TextureHandle[] m_MipUpTH   = new TextureHandle[k_MaxBloomMipCount];

        private BloomSettingsData m_Settings;
        private int m_MipCount;
        private int m_ScreenWidth;
        private int m_ScreenHeight;
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
            }
            if (m_UpsampleCS != null)
                m_UpsampleKernel = m_UpsampleCS.FindKernel("KMain");
        }

        public override void Dispose()
        {
            for (int i = 0; i < k_MaxBloomMipCount; i++)
            {
                m_MipDownHandles[i]?.Release();
                m_MipDownHandles[i] = null;
                m_MipUpHandles[i]?.Release();
                m_MipUpHandles[i] = null;
            }
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var camera = cameraData?.camera;
            bool ppAllowed = camera != null && CoreUtils.ArePostProcessesEnabled(camera);
            m_Settings = ppAllowed ? BloomSettingsResolver.Resolve() : BloomSettingsData.CreateDefault();

            m_ScreenWidth  = ResolveWidth(cameraData);
            m_ScreenHeight = ResolveHeight(cameraData);

            bloomTexture.desc.Width         = 1;
            bloomTexture.desc.Height        = 1;
            bloomTexture.desc.ColorFormat   = GraphicsFormat.R16G16B16A16_SFloat;
            bloomTexture.desc.EnableRandomWrite = true;
            bloomTexture.desc.FilterMode    = FilterMode.Bilinear;
            bloomTexture.desc.WrapMode      = TextureWrapMode.Clamp;
            bloomTexture.desc.Name          = "BloomTexture";

            m_MipCount = 0;

            if (!m_Settings.enabled
                || m_PrefilterCS == null || m_BlurCS == null || m_UpsampleCS == null
                || m_ScreenWidth <= 0 || m_ScreenHeight <= 0)
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

            for (int i = 0; i < m_MipCount; i++)
            {
                int mw = Mathf.Max(1, baseW >> i);
                int mh = Mathf.Max(1, baseH >> i);
                EnsureMipHandle(ref m_MipDownHandles[i], mw, mh, $"BloomMipDown{i}");
                EnsureMipHandle(ref m_MipUpHandles[i],   mw, mh, $"BloomMipUp{i}");
                // Import into RenderGraph so the pass declares read/write access.
                m_MipDownTH[i] = Import(m_MipDownHandles[i]);
                m_MipUpTH[i]   = Import(m_MipUpHandles[i]);
            }
        }

        public override void Record(UnsafeGraphContext context)
        {
            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

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
            for (int i = 0; i < m_MipCount - 1; i++)
            {
                int sw = m_MipDownHandles[i].rt.width,     sh = m_MipDownHandles[i].rt.height;
                int dw = m_MipDownHandles[i+1].rt.width,   dh = m_MipDownHandles[i+1].rt.height;
                cmd.SetComputeVectorParam(m_BlurCS, TexelSizeId,      new Vector4(dw, dh, 1f/dw, 1f/dh));
                cmd.SetComputeVectorParam(m_BlurCS, InputTexelSizeId, new Vector4(sw, sh, 1f/sw, 1f/sh));
                cmd.SetComputeTextureParam(m_BlurCS, m_DownsampleKernel, InputTextureId,  m_MipDownTH[i]);
                cmd.SetComputeTextureParam(m_BlurCS, m_DownsampleKernel, OutputTextureId, m_MipDownTH[i+1]);
                cmd.DispatchCompute(m_BlurCS, m_DownsampleKernel, DivUp(dw, 8), DivUp(dh, 8), 1);
            }

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
            float bloomIntensity = Mathf.Pow(2f, m_Settings.intensity) - 1f;
            bool  hasDirt        = m_Settings.dirtTexture != null && m_Settings.dirtIntensity > 0f;
            var   tint           = (Vector4)m_Settings.tint.linear;

            cmd.SetGlobalTexture(VividBloomTextureId, m_MipUpTH[0]);
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

        private void SetBloomDisabled(CommandBuffer cmd)
        {
            cmd.SetGlobalVector(VividBloomParamsId, Vector4.zero);
            cmd.SetGlobalTexture(VividBloomTextureId, Texture2D.blackTexture);
        }

        // -------------------------------------------------------------------------

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
