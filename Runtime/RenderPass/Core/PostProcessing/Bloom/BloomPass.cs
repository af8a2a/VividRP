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

        private static readonly int TexelSizeId = Shader.PropertyToID("_TexelSize");
        private static readonly int InputTexelSizeId = Shader.PropertyToID("_InputTexelSize");
        private static readonly int BloomThresholdId = Shader.PropertyToID("_BloomThreshold");
        private static readonly int InputTextureId = Shader.PropertyToID("_InputTexture");
        private static readonly int InputLowTextureId = Shader.PropertyToID("_InputLowTexture");
        private static readonly int InputHighTextureId = Shader.PropertyToID("_InputHighTexture");
        private static readonly int OutputTextureId = Shader.PropertyToID("_OutputTexture");
        private static readonly int ParamsId = Shader.PropertyToID("_Params");
        private static readonly int BloomBicubicParamsId = Shader.PropertyToID("_BloomBicubicParams");
        private static readonly int VividBloomTextureId = Shader.PropertyToID("_VividBloomTexture");
        private static readonly int VividBloomParamsId = Shader.PropertyToID("_VividBloomParams");
        private static readonly int VividBloomTintId = Shader.PropertyToID("_VividBloomTint");
        private static readonly int VividBloomDirtTextureId = Shader.PropertyToID("_VividBloomDirtTexture");
        private static readonly int VividBloomDirtScaleId = Shader.PropertyToID("_VividBloomDirtScale");

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
        private bool m_PostProcessingAllowed;
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
            m_BlurCS = resources.BloomBlurCompute;
            m_UpsampleCS = resources.BloomUpsampleCompute;

            if (m_PrefilterCS != null)
                m_PrefilterKernel = m_PrefilterCS.FindKernel("KMain");
            if (m_BlurCS != null)
            {
                m_BlurKernel = m_BlurCS.FindKernel("KMain");
                m_DownsampleKernel = m_BlurCS.FindKernel("KDownsample");
            }
            if (m_UpsampleCS != null)
                m_UpsampleKernel = m_UpsampleCS.FindKernel("KMain");

            m_MipDown = new RenderTexture[k_MaxBloomMipCount];
            m_MipUp = new RenderTexture[k_MaxBloomMipCount];
        }

        public override void Dispose()
        {
            ReleaseMipChain();
            m_MipDown = null;
            m_MipUp = null;
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var camera = cameraData?.camera;
            m_PostProcessingAllowed = camera != null && CoreUtils.ArePostProcessesEnabled(camera);
            m_Settings = m_PostProcessingAllowed
                ? BloomSettingsResolver.Resolve()
                : BloomSettingsData.CreateDefault();

            m_ScreenWidth = ResolveScreenDimension(cameraData);
            m_ScreenHeight = ResolveScreenDimension(cameraData, vertical: true);

            bloomTexture.desc.Width = 1;
            bloomTexture.desc.Height = 1;
            bloomTexture.desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            bloomTexture.desc.EnableRandomWrite = true;
            bloomTexture.desc.FilterMode = FilterMode.Bilinear;
            bloomTexture.desc.WrapMode = TextureWrapMode.Clamp;
            bloomTexture.desc.Name = "BloomTexture";
        }

        public override void Record(UnsafeGraphContext context)
        {
            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

            if (!m_Settings.enabled
                || m_PrefilterCS == null
                || m_BlurCS == null
                || m_UpsampleCS == null
                || source?.innerHandle?.IsValid() != true)
            {
                SetBloomDisabled(cmd);
                return;
            }

            using (new ProfilingScope(context.cmd, profilingSampler))
            {
                ExecuteBloom(cmd);
            }
        }

        private void ExecuteBloom(CommandBuffer cmd)
        {
            int tw = m_ScreenWidth;
            int th = m_ScreenHeight;
            if (tw <= 0 || th <= 0)
            {
                SetBloomDisabled(cmd);
                return;
            }

            int div = (int)m_Settings.resolution;
            float anamorphic = m_Settings.anamorphic;
            float scaleW = anamorphic < 0f ? 1f + anamorphic * 0.5f : 1f;
            float scaleH = anamorphic > 0f ? 1f - anamorphic * 0.5f : 1f;
            int baseW = Mathf.Max(1, Mathf.FloorToInt(tw * scaleW) / div);
            int baseH = Mathf.Max(1, Mathf.FloorToInt(th * scaleH) / div);

            int maxDim = Mathf.Max(baseW, baseH);
            int mipCount = Mathf.Clamp(
                Mathf.FloorToInt(Mathf.Log(maxDim, 2f)) - 1,
                1, k_MaxBloomMipCount);

            EnsureMipChain(baseW, baseH, mipCount);

            float scatter = Mathf.Lerp(0.05f, 0.95f, m_Settings.scatter);

            // --- Threshold curve ---
            float lthresh = Mathf.GammaToLinearSpace(m_Settings.threshold);
            float knee = lthresh * 0.5f + 1e-5f;
            var thresholdVec = new Vector4(lthresh, lthresh - knee, knee * 2f, 0.25f / knee);

            // --- Prefilter: source -> mipDown[0] ---
            {
                var prefilterKeyword = m_Settings.highQualityPrefiltering ? "HIGH_QUALITY" : "LOW_QUALITY";
                SetKeyword(m_PrefilterCS, "LOW_QUALITY", !m_Settings.highQualityPrefiltering);
                SetKeyword(m_PrefilterCS, "HIGH_QUALITY", m_Settings.highQualityPrefiltering);

                int mw = m_MipDown[0].width;
                int mh = m_MipDown[0].height;
                cmd.SetComputeVectorParam(m_PrefilterCS, TexelSizeId,
                    new Vector4(mw, mh, 1f / mw, 1f / mh));
                cmd.SetComputeVectorParam(m_PrefilterCS, InputTexelSizeId,
                    new Vector4(tw, th, 1f / tw, 1f / th));
                cmd.SetComputeVectorParam(m_PrefilterCS, BloomThresholdId, thresholdVec);
                cmd.SetComputeTextureParam(m_PrefilterCS, m_PrefilterKernel, InputTextureId, source.innerHandle);
                cmd.SetComputeTextureParam(m_PrefilterCS, m_PrefilterKernel, OutputTextureId, m_MipDown[0]);
                cmd.DispatchCompute(m_PrefilterCS, m_PrefilterKernel,
                    DivRoundUp(mw, 8), DivRoundUp(mh, 8), 1);
            }

            // --- Blur mipDown[0] in-place ---
            {
                int mw = m_MipDown[0].width;
                int mh = m_MipDown[0].height;
                cmd.SetComputeVectorParam(m_BlurCS, TexelSizeId,
                    new Vector4(mw, mh, 1f / mw, 1f / mh));
                cmd.SetComputeVectorParam(m_BlurCS, InputTexelSizeId,
                    new Vector4(mw, mh, 1f / mw, 1f / mh));
                cmd.SetComputeTextureParam(m_BlurCS, m_BlurKernel, InputTextureId, m_MipDown[0]);
                cmd.SetComputeTextureParam(m_BlurCS, m_BlurKernel, OutputTextureId, m_MipDown[0]);
                cmd.DispatchCompute(m_BlurCS, m_BlurKernel,
                    DivRoundUp(mw, 8), DivRoundUp(mh, 8), 1);
            }

            // --- Downsample chain: mipDown[i] -> mipDown[i+1] ---
            for (int i = 0; i < mipCount - 1; i++)
            {
                int srcW = m_MipDown[i].width;
                int srcH = m_MipDown[i].height;
                int dstW = m_MipDown[i + 1].width;
                int dstH = m_MipDown[i + 1].height;

                cmd.SetComputeVectorParam(m_BlurCS, TexelSizeId,
                    new Vector4(dstW, dstH, 1f / dstW, 1f / dstH));
                cmd.SetComputeVectorParam(m_BlurCS, InputTexelSizeId,
                    new Vector4(srcW, srcH, 1f / srcW, 1f / srcH));
                cmd.SetComputeTextureParam(m_BlurCS, m_DownsampleKernel, InputTextureId, m_MipDown[i]);
                cmd.SetComputeTextureParam(m_BlurCS, m_DownsampleKernel, OutputTextureId, m_MipDown[i + 1]);
                cmd.DispatchCompute(m_BlurCS, m_DownsampleKernel,
                    DivRoundUp(dstW, 8), DivRoundUp(dstH, 8), 1);
            }

            // --- Upsample chain: mipDown[last] -> mipUp[last]; then mipUp[i+1] + mipDown[i] -> mipUp[i] ---
            Graphics.CopyTexture(m_MipDown[mipCount - 1], m_MipUp[mipCount - 1]);

            SetKeyword(m_UpsampleCS, "LOW_QUALITY", !m_Settings.highQualityFiltering);
            SetKeyword(m_UpsampleCS, "HIGH_QUALITY", m_Settings.highQualityFiltering);

            for (int i = mipCount - 2; i >= 0; i--)
            {
                int highW = m_MipDown[i].width;
                int highH = m_MipDown[i].height;
                int lowW = m_MipUp[i + 1].width;
                int lowH = m_MipUp[i + 1].height;

                cmd.SetComputeVectorParam(m_UpsampleCS, TexelSizeId,
                    new Vector4(highW, highH, 1f / highW, 1f / highH));
                cmd.SetComputeVectorParam(m_UpsampleCS, ParamsId,
                    new Vector4(scatter, 0f, 0f, 0f));
                cmd.SetComputeVectorParam(m_UpsampleCS, BloomBicubicParamsId,
                    new Vector4(lowW, lowH, 1f / lowW, 1f / lowH));
                cmd.SetComputeTextureParam(m_UpsampleCS, m_UpsampleKernel, InputLowTextureId, m_MipUp[i + 1]);
                cmd.SetComputeTextureParam(m_UpsampleCS, m_UpsampleKernel, InputHighTextureId, m_MipDown[i]);
                cmd.SetComputeTextureParam(m_UpsampleCS, m_UpsampleKernel, OutputTextureId, m_MipUp[i]);
                cmd.DispatchCompute(m_UpsampleCS, m_UpsampleKernel,
                    DivRoundUp(highW, 8), DivRoundUp(highH, 8), 1);
            }

            // --- Set global bloom parameters for FinalBlitPass ---
            float bloomIntensity = Mathf.Pow(2f, m_Settings.intensity) - 1f;
            float dirtIntensity = m_Settings.dirtIntensity * bloomIntensity;
            bool hasDirt = m_Settings.dirtTexture != null && dirtIntensity > 0f;

            cmd.SetGlobalTexture(VividBloomTextureId, m_MipUp[0]);
            cmd.SetGlobalVector(VividBloomParamsId,
                new Vector4(bloomIntensity, dirtIntensity, 1f, hasDirt ? 1f : 0f));

            Color tint = m_Settings.tint.linear;
            float maxTint = Mathf.Max(tint.r, Mathf.Max(tint.g, tint.b));
            if (maxTint > 0f)
                tint /= maxTint;
            cmd.SetGlobalVector(VividBloomTintId, new Vector4(tint.r, tint.g, tint.b, 0f));

            if (hasDirt)
            {
                var dirt = m_Settings.dirtTexture;
                float dirtRatio = (float)dirt.width / dirt.height;
                float screenRatio = (float)m_ScreenWidth / m_ScreenHeight;
                var dirtScale = new Vector4(1f, 1f, 0f, 0f);
                if (dirtRatio > screenRatio)
                {
                    dirtScale.x = screenRatio / dirtRatio;
                    dirtScale.z = (1f - dirtScale.x) * 0.5f;
                }
                else if (screenRatio > dirtRatio)
                {
                    dirtScale.y = dirtRatio / screenRatio;
                    dirtScale.w = (1f - dirtScale.y) * 0.5f;
                }
                cmd.SetGlobalTexture(VividBloomDirtTextureId, dirt);
                cmd.SetGlobalVector(VividBloomDirtScaleId, dirtScale);
            }
        }

        private void SetBloomDisabled(CommandBuffer cmd)
        {
            cmd.SetGlobalVector(VividBloomParamsId, new Vector4(0f, 0f, 0f, 0f));
        }

        private void EnsureMipChain(int baseW, int baseH, int mipCount)
        {
            for (int i = 0; i < mipCount; i++)
            {
                int w = Mathf.Max(1, baseW >> i);
                int h = Mathf.Max(1, baseH >> i);
                EnsureScratchRT(ref m_MipDown[i], w, h, $"BloomMipDown{i}");
                EnsureScratchRT(ref m_MipUp[i], w, h, $"BloomMipUp{i}");
            }

            for (int i = mipCount; i < k_MaxBloomMipCount; i++)
            {
                ReleaseScratchRT(ref m_MipDown[i]);
                ReleaseScratchRT(ref m_MipUp[i]);
            }
        }

        private static void EnsureScratchRT(ref RenderTexture rt, int width, int height, string name)
        {
            if (rt != null && rt.IsCreated() && rt.width == width && rt.height == height)
                return;

            ReleaseScratchRT(ref rt);
            rt = new RenderTexture(width, height, 0, GraphicsFormat.R16G16B16A16_SFloat)
            {
                name = name,
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            rt.Create();
        }

        private static void ReleaseScratchRT(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            CoreUtils.Destroy(rt);
            rt = null;
        }

        private void ReleaseMipChain()
        {
            if (m_MipDown == null) return;
            for (int i = 0; i < k_MaxBloomMipCount; i++)
            {
                ReleaseScratchRT(ref m_MipDown[i]);
                ReleaseScratchRT(ref m_MipUp[i]);
            }
        }

        private static void SetKeyword(ComputeShader cs, string keyword, bool enabled)
        {
            if (enabled)
                cs.EnableKeyword(keyword);
            else
                cs.DisableKeyword(keyword);
        }

        private static int DivRoundUp(int x, int y) => (x + y - 1) / y;

        private static int ResolveScreenDimension(VividCameraData cameraData, bool vertical = false)
        {
            if (cameraData == null) return vertical ? Screen.height : Screen.width;
            int actual = vertical ? cameraData.actualHeight : cameraData.actualWidth;
            if (actual > 0) return actual;
            int pixel = vertical ? cameraData.pixelHeight : cameraData.pixelWidth;
            return pixel > 0 ? pixel : (vertical ? Screen.height : Screen.width);
        }
    }
}
