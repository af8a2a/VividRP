using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public sealed class ScreenSpaceLensFlarePass : UnsafePass, IAllowGlobalStateModificationPass
    {
        [RenderGraphResource(Name = "BloomTexture", Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture bloomTexture = new();

        [RenderGraphResource(Name = "ScreenSpaceLensFlareBloomMipTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture bloomMipTexture = new();

        [RenderGraphResource(
            Name = "ScreenSpaceLensFlareResult",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture resultTexture = new();

        [RenderGraphResource(
            Name = "ScreenSpaceLensFlareStreakTmp",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture streakTmpTexture = new();

        [RenderGraphResource(
            Name = "ScreenSpaceLensFlareStreakTmp2",
            Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture streakTmpTexture2 = new();

        private Material m_LensFlareMaterial;
        private ScreenSpaceLensFlareSettingsData m_Settings;
        private Texture2D m_InternalSpectralLut;
        private Camera m_Camera;
        private int m_Width;
        private int m_Height;
        private bool m_ShouldRender;

        public ScreenSpaceLensFlarePass()
        {
            profilingSampler = new ProfilingSampler(nameof(ScreenSpaceLensFlarePass));
            ConfigureTexture(bloomTexture, 1, 1, "BloomTexture");
            ConfigureTexture(bloomMipTexture, 1, 1, "ScreenSpaceLensFlareBloomMipTexture");
            ConfigureTexture(resultTexture, 1, 1, "ScreenSpaceLensFlareResult");
            ConfigureTexture(streakTmpTexture, 1, 1, "ScreenSpaceLensFlareStreakTmp");
            ConfigureTexture(streakTmpTexture2, 1, 1, "ScreenSpaceLensFlareStreakTmp2");
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            if (resources?.LensFlareScreenSpaceShader == null)
                return;

            m_LensFlareMaterial = CoreUtils.CreateEngineMaterial(resources.LensFlareScreenSpaceShader);
            m_LensFlareMaterial.SetOverrideTag("RenderType", "Transparent");
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            m_Camera = cameraData?.camera;
            m_Width = ResolveWidth(cameraData);
            m_Height = ResolveHeight(cameraData);

            var postProcessingAllowed = m_Camera != null && CoreUtils.ArePostProcessesEnabled(m_Camera);
            m_Settings = postProcessingAllowed
                ? ScreenSpaceLensFlareSettingsResolver.Resolve()
                : ScreenSpaceLensFlareSettingsData.CreateDefault();

            var ratio = Mathf.Max(1, (int)m_Settings.resolution);
            var effectWidth = Mathf.Max(1, m_Width / ratio);
            var effectHeight = Mathf.Max(1, m_Height / ratio);

            ConfigureTexture(resultTexture, effectWidth, effectHeight, "ScreenSpaceLensFlareResult");
            ConfigureTexture(streakTmpTexture, effectWidth, effectHeight, "ScreenSpaceLensFlareStreakTmp");
            ConfigureTexture(streakTmpTexture2, effectWidth, effectHeight, "ScreenSpaceLensFlareStreakTmp2");

            m_ShouldRender = m_Settings.enabled && m_LensFlareMaterial != null;
        }

        public override void Record(UnsafePassContext context)
        {
            if (!m_ShouldRender
                || bloomTexture?.innerHandle.IsValid() != true
                || bloomMipTexture?.innerHandle.IsValid() != true
                || resultTexture?.innerHandle.IsValid() != true)
            {
                return;
            }

            using (new ProfilingScope(context.cmd, profilingSampler))
            {
                var spectralLut = m_Settings.spectralLut != null
                    ? m_Settings.spectralLut
                    : GetOrCreateDefaultInternalSpectralLut();

                LensFlareCommonSRP.DoLensFlareScreenSpaceCommon(
                    m_LensFlareMaterial,
                    m_Camera,
                    m_Width,
                    m_Height,
                    m_Settings.tintColor,
                    bloomTexture.innerHandle,
                    bloomMipTexture.innerHandle,
                    spectralLut,
                    streakTmpTexture.innerHandle,
                    streakTmpTexture2.innerHandle,
                    new Vector4(
                        m_Settings.intensity,
                        m_Settings.firstFlareIntensity,
                        m_Settings.secondaryFlareIntensity,
                        m_Settings.warpedFlareIntensity),
                    new Vector4(
                        m_Settings.vignetteEffect,
                        m_Settings.startingPosition,
                        m_Settings.scale,
                        0f),
                    new Vector4(
                        m_Settings.samples,
                        m_Settings.sampleDimmer,
                        m_Settings.chromaticAbberationIntensity,
                        m_Settings.chromaticAbberationSampleCount),
                    new Vector4(
                        m_Settings.streaksIntensity,
                        m_Settings.streaksLength,
                        m_Settings.streaksOrientation,
                        m_Settings.streaksThreshold),
                    new Vector4(
                        Mathf.Max(1, (int)m_Settings.resolution),
                        m_Settings.warpedFlareScale.x,
                        m_Settings.warpedFlareScale.y,
                        0f),
                    context.cmd,
                    (RTHandle)resultTexture.innerHandle,
                    false);
            }
        }

        public override void Dispose()
        {
            if (m_LensFlareMaterial != null)
            {
                CoreUtils.Destroy(m_LensFlareMaterial);
                m_LensFlareMaterial = null;
            }

            if (m_InternalSpectralLut != null)
            {
                CoreUtils.Destroy(m_InternalSpectralLut);
                m_InternalSpectralLut = null;
            }
        }

        private Texture2D GetOrCreateDefaultInternalSpectralLut()
        {
            if (m_InternalSpectralLut != null)
                return m_InternalSpectralLut;

            m_InternalSpectralLut = new Texture2D(3, 1, GraphicsFormat.R8G8B8A8_SRGB, TextureCreationFlags.None)
            {
                name = "Screen Space Lens Flare Spectral LUT",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
                hideFlags = HideFlags.DontSave,
            };

            m_InternalSpectralLut.SetPixels(new[]
            {
                new Color(1f, 0f, 0f, 1f),
                new Color(0f, 1f, 0f, 1f),
                new Color(0f, 0f, 1f, 1f),
            });
            m_InternalSpectralLut.Apply();
            return m_InternalSpectralLut;
        }

        private static void ConfigureTexture(RenderGraphTexture texture, int width, int height, string name)
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

        private static int ResolveWidth(VividCameraData data)
        {
            if (data == null)
                return Mathf.Max(1, Screen.width);
            if (data.actualWidth > 0)
                return data.actualWidth;
            if (data.pixelWidth > 0)
                return data.pixelWidth;
            return Mathf.Max(1, Screen.width);
        }

        private static int ResolveHeight(VividCameraData data)
        {
            if (data == null)
                return Mathf.Max(1, Screen.height);
            if (data.actualHeight > 0)
                return data.actualHeight;
            if (data.pixelHeight > 0)
                return data.pixelHeight;
            return Mathf.Max(1, Screen.height);
        }
    }
}
