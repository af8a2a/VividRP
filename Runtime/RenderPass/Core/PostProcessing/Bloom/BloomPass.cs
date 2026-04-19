using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public class BloomPass : UnsafePass
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

        private RenderTexture[] m_MipDown;
        private RenderTexture[] m_MipUp;

        private BloomSettingsData m_Settings;
        private int m_ScreenWidth;
        private int m_ScreenHeight;

        public BloomPass()
        {
            profilingSampler = new ProfilingSampler(nameof(BloomPass));
        }

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
                m_BlurKernel      = m_BlurCS.FindKernel("KMain");
                m_DownsampleKernel = m_BlurCS.FindKernel("KDownsample");
            }
            if (m_UpsampleCS != null)
                m_UpsampleKernel = m_UpsampleCS.FindKernel("KMain");

            m_MipDown = new RenderTexture[k_MaxBloomMipCount];
            m_MipUp   = new RenderTexture[k_MaxBloomMipCount];
        }

        public override void Dispose()
        {
            ReleaseMipChain();
            m_MipDown = null;
            m_MipUp   = null;
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var camera = cameraData?.camera;
            bool ppAllowed = camera != null && CoreUtils.ArePostProcessesEnabled(camera);
            m_Settings = ppAllowed ? BloomSettingsResolver.Resolve() : BloomSettingsData.CreateDefault();

            m_ScreenWidth  = ResolveWidth(cameraData);
            m_ScreenHeight = ResolveHeight(cameraData);

            // bloomTexture is a 1x1 placeholder; actual bloom lives in m_MipUp[0] (scratch RT).
            bloomTexture.desc.Width         = 1;
            bloomTexture.desc.Height        = 1;
            bloomTexture.desc.ColorFormat   = GraphicsFormat.R16G16B16A16_SFloat;
            bloomTexture.desc.EnableRandomWrite = true;
            bloomTexture.desc.FilterMode    = FilterMode.Bilinear;
            bloomTexture.desc.WrapMode      = TextureWrapMode.Clamp;
            bloomTexture.desc.Name          = "BloomTexture";
        }

        public override void Record(UnsafeGraphContext context)
        {
            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

            if (!m_Settings.enabled
                || m_PrefilterCS == null || m_BlurCS == null || m_UpsampleCS == null
                || source?.innerHandle.IsValid() != true)
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
            if (tw <= 0 || th <= 0) { SetBloomDisabled(cmd); return; }

            // Anamorphic stretch (replaces HDRP camera.anamorphism).
            float ana = m_Settings.anamorphic;
            float scaleW = ana < 0f ? 1f + ana * 0.5f : 1f;
            float scaleH = ana > 0f ? 1f - ana * 0.5f : 1f;

            int div   = (int)m_Settings.resolution;
            int baseW = Mathf.Max(1, Mathf.FloorToInt(tw * scaleW) / div);
            int baseH = Mathf.Max(1, Mathf.FloorToInt(th * scaleH) / div);

            int maxDim   = Mathf.Max(baseW, baseH);
            int mipCount = Mathf.Clamp(
                Mathf.FloorToInt(Mathf.Log(maxDim, 2f)) - 2 - (m_Settings.resolution == BloomResolution.Half ? 0 : 1),
                1, k_MaxBloomMipCount);

            // Allocate / reuse scratch mip chain.
            for (int i = 0; i < mipCount; i++)
            {
                int mw = Mathf.Max(1, baseW >> i);
                int mh = Mathf.Max(1, baseH >> i);
                EnsureMip(ref m_MipDown[i], mw, mh, $"BloomMipDown{i}");
                EnsureMip(ref m_MipUp[i],   mw, mh, $"BloomMipUp{i}");
            }

            // Threshold curve (HDRP PrepareUberBloomParameters).
            float lthresh = Mathf.GammaToLinearSpace(m_Settings.threshold);
            float knee    = lthresh * 0.5f + 1e-5f;
            var threshold = new Vector4(lthresh, lthresh - knee, knee * 2f, 0.25f / knee);

            bool hqPrefilter = m_Settings.highQualityPrefiltering;
            bool hqFilter    = m_Settings.highQualityFiltering;

            // ---- 1. Prefilter: source → mipDown[0] ----
            {
                int w = m_MipDown[0].width, h = m_MipDown[0].height;
                SetKeyword(m_PrefilterCS, "LOW_QUALITY",  !hqPrefilter);
                SetKeyword(m_PrefilterCS, "HIGH_QUALITY",  hqPrefilter);

                cmd.SetComputeVectorParam(m_PrefilterCS, TexelSizeId,
                    new Vector4(w, h, 1f / w, 1f / h));
                cmd.SetComputeVectorParam(m_PrefilterCS, InputTexelSizeId,
                    new Vector4(tw, th, 1f / tw, 1f / th));
                cmd.SetComputeVectorParam(m_PrefilterCS, BloomThresholdId, threshold);
                cmd.SetComputeTextureParam(m_PrefilterCS, m_PrefilterKernel, InputTextureId,  source.innerHandle);
                cmd.SetComputeTextureParam(m_PrefilterCS, m_PrefilterKernel, OutputTextureId, m_MipDown[0]);
                cmd.DispatchCompute(m_PrefilterCS, m_PrefilterKernel,
                    DivUp(w, 8), DivUp(h, 8), 1);
            }

            // ---- 2. Downsample chain: mipDown[i] → mipDown[i+1] ----
            for (int i = 0; i < mipCount - 1; i++)
            {
                var src = m_MipDown[i];
                var dst = m_MipDown[i + 1];
                int sw = src.width, sh = src.height;
                int dw = dst.width, dh = dst.height;

                cmd.SetComputeVectorParam(m_BlurCS, TexelSizeId,
                    new Vector4(dw, dh, 1f / dw, 1f / dh));
                cmd.SetComputeVectorParam(m_BlurCS, InputTexelSizeId,
                    new Vector4(sw, sh, 1f / sw, 1f / sh));
                cmd.SetComputeTextureParam(m_BlurCS, m_DownsampleKernel, InputTextureId,  src);
                cmd.SetComputeTextureParam(m_BlurCS, m_DownsampleKernel, OutputTextureId, dst);
                cmd.DispatchCompute(m_BlurCS, m_DownsampleKernel,
                    DivUp(dw, 8), DivUp(dh, 8), 1);
            }

            // ---- 3. Upsample + scatter: mipDown[last] seeds mipUp[last], then upsample ----
            float scatter = Mathf.Lerp(0.05f, 0.95f, m_Settings.scatter);

            // Seed: copy mipDown[last] into mipUp[last] via blur kernel (no downsample).
            {
                int last = mipCount - 1;
                int w = m_MipDown[last].width, h = m_MipDown[last].height;
                cmd.SetComputeVectorParam(m_BlurCS, TexelSizeId,
                    new Vector4(w, h, 1f / w, 1f / h));
                cmd.SetComputeVectorParam(m_BlurCS, InputTexelSizeId,
                    new Vector4(w, h, 1f / w, 1f / h));
                cmd.SetComputeTextureParam(m_BlurCS, m_BlurKernel, InputTextureId,  m_MipDown[last]);
                cmd.SetComputeTextureParam(m_BlurCS, m_BlurKernel, OutputTextureId, m_MipUp[last]);
                cmd.DispatchCompute(m_BlurCS, m_BlurKernel,
                    DivUp(w, 8), DivUp(h, 8), 1);
            }

            for (int i = mipCount - 2; i >= 0; i--)
            {
                var low  = m_MipUp[i + 1];
                var high = m_MipDown[i];
                var dst  = m_MipUp[i];
                int hw = high.width, hh = high.height;
                int lw = low.width,  lh = low.height;

                SetKeyword(m_UpsampleCS, "LOW_QUALITY",  !hqFilter);
                SetKeyword(m_UpsampleCS, "HIGH_QUALITY",  hqFilter);

                cmd.SetComputeVectorParam(m_UpsampleCS, ParamsId,
                    new Vector4(scatter, 0f, 0f, 0f));
                cmd.SetComputeVectorParam(m_UpsampleCS, BloomBicubicParamsId,
                    new Vector4(lw, lh, 1f / lw, 1f / lh));
                cmd.SetComputeVectorParam(m_UpsampleCS, TexelSizeId,
                    new Vector4(hw, hh, 1f / hw, 1f / hh));
                cmd.SetComputeTextureParam(m_UpsampleCS, m_UpsampleKernel, InputLowTextureId,  low);
                cmd.SetComputeTextureParam(m_UpsampleCS, m_UpsampleKernel, InputHighTextureId, high);
                cmd.SetComputeTextureParam(m_UpsampleCS, m_UpsampleKernel, OutputTextureId,    dst);
                cmd.DispatchCompute(m_UpsampleCS, m_UpsampleKernel,
                    DivUp(hw, 8), DivUp(hh, 8), 1);
            }

            // ---- 4. Bind globals for FinalBlitPass ----
            // intensity: Pow(2, intensity) - 1  (HDRP PrepareUberBloomParameters)
            float bloomIntensity = Mathf.Pow(2f, m_Settings.intensity) - 1f;
            bool  hasDirt        = m_Settings.dirtTexture != null && m_Settings.dirtIntensity > 0f;
            var   tint           = (Vector4)(m_Settings.tint.linear);

            cmd.SetGlobalTexture(VividBloomTextureId, m_MipUp[0]);
            cmd.SetGlobalVector(VividBloomParamsId,
                new Vector4(bloomIntensity, m_Settings.dirtIntensity, 1f, hasDirt ? 1f : 0f));
            cmd.SetGlobalVector(VividBloomTintId, new Vector4(tint.x, tint.y, tint.z, 1f));

            if (hasDirt)
            {
                cmd.SetGlobalTexture(VividBloomDirtTextureId, m_Settings.dirtTexture);
                // Aspect-ratio-corrected dirt tiling (HDRP UberPost pattern).
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

        private static void EnsureMip(ref RenderTexture rt, int width, int height, string name)
        {
            if (rt != null && rt.IsCreated() && rt.width == width && rt.height == height)
                return;

            ReleaseMipRT(ref rt);
            rt = new RenderTexture(width, height, 0)
            {
                name             = name,
                graphicsFormat   = GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite = true,
                filterMode       = FilterMode.Bilinear,
                wrapMode         = TextureWrapMode.Clamp,
                hideFlags        = HideFlags.HideAndDontSave
            };
            rt.Create();
        }

        private static void ReleaseMipRT(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            CoreUtils.Destroy(rt);
            rt = null;
        }

        private void ReleaseMipChain()
        {
            if (m_MipDown != null)
                for (int i = 0; i < m_MipDown.Length; i++)
                    ReleaseMipRT(ref m_MipDown[i]);
            if (m_MipUp != null)
                for (int i = 0; i < m_MipUp.Length; i++)
                    ReleaseMipRT(ref m_MipUp[i]);
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
