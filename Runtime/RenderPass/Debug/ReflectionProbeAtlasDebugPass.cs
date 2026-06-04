using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public enum ReflectionProbeAtlasDebugMode
    {
        None = 0,
        Atlas = 1,
    }

    public sealed class ReflectionProbeAtlasDebugPass : RasterPass
    {
        internal const string ReflectionProbeAtlasDebugShaderName = "Hidden/VividRP/ReflectionProbeAtlasDebug";

        private static readonly int ReflectionAtlasId = Shader.PropertyToID("_ReflectionAtlas");
        private static readonly int AtlasAvailableId = Shader.PropertyToID("_ReflectionAtlasDebugAvailable");
        private static readonly int DebugModeId = Shader.PropertyToID("_ReflectionAtlasDebugMode");
        private static readonly int DebugSliceId = Shader.PropertyToID("_ReflectionAtlasDebugSlice");
        private static readonly int DebugMipId = Shader.PropertyToID("_ReflectionAtlasDebugMip");
        private static readonly int DebugMipCountId = Shader.PropertyToID("_ReflectionAtlasDebugMipCount");
        private static readonly int DebugSliceCountId = Shader.PropertyToID("_ReflectionAtlasDebugSliceCount");
        private static readonly int DebugExposureId = Shader.PropertyToID("_ReflectionAtlasDebugExposure");

        [RenderGraphResource(
            Name = "DebugTexture",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_DebugTexture;

        [SerializeField]
        private ReflectionProbeAtlasDebugMode m_Mode = ReflectionProbeAtlasDebugMode.None;

        [SerializeField, Min(0f)]
        private float m_ArraySlice;

        [SerializeField, Min(0f)]
        private float m_MipLevel;

        [SerializeField, Range(-16f, 16f)]
        private float m_Exposure;

        private Material m_Material;
        private ReflectionProbeAtlasDebugSettingsData m_ResolvedSettings;
        private Texture m_AtlasTexture;
        private int m_AtlasMipCount;
        private int m_AtlasSliceCount;
        private bool m_AtlasAvailable;
        private bool m_ShouldSkipExecution;

        internal readonly struct ReflectionProbeAtlasDebugSettingsData
        {
            public readonly ReflectionProbeAtlasDebugMode mode;
            public readonly int arraySlice;
            public readonly int mipLevel;
            public readonly float exposure;

            public ReflectionProbeAtlasDebugSettingsData(
                ReflectionProbeAtlasDebugMode mode,
                int arraySlice,
                int mipLevel,
                float exposure)
            {
                this.mode = mode;
                this.arraySlice = arraySlice;
                this.mipLevel = mipLevel;
                this.exposure = exposure;
            }
        }

        public ReflectionProbeAtlasDebugMode Mode
        {
            get => m_Mode;
            set => m_Mode = NormalizeDebugMode(value);
        }

        public int ArraySlice
        {
            get => Mathf.Max(0, Mathf.RoundToInt(m_ArraySlice));
            set => m_ArraySlice = Mathf.Max(0, value);
        }

        public int MipLevel
        {
            get => Mathf.Max(0, Mathf.RoundToInt(m_MipLevel));
            set => m_MipLevel = Mathf.Max(0, value);
        }

        public float Exposure
        {
            get => m_Exposure;
            set => m_Exposure = Mathf.Clamp(value, -16f, 16f);
        }

        public ReflectionProbeAtlasDebugPass()
        {
            profilingSampler = new ProfilingSampler(nameof(ReflectionProbeAtlasDebugPass));

            m_DebugTexture = RenderGraphTexture.CreateColorTarget(
                "ReflectionProbeAtlasDebug",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_DebugTexture.desc.ClearBuffer = true;
            m_DebugTexture.desc.ClearColor = Color.black;
            m_DebugTexture.desc.FilterMode = FilterMode.Bilinear;
            m_DebugTexture.desc.WrapMode = TextureWrapMode.Clamp;
            m_ResolvedSettings = new ReflectionProbeAtlasDebugSettingsData(
                ReflectionProbeAtlasDebugMode.None,
                0,
                0,
                0f);
        }

        public override bool IsActive(ContextContainer frameData)
        {
            return VividRenderingDebugDisplaySettings.Data.reflectionProbeAtlasDebugMode
                != ReflectionProbeAtlasDebugMode.None;
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            var shader = resources?.ReflectionProbeAtlasDebugShader;
            shader ??= Shader.Find(ReflectionProbeAtlasDebugShaderName);

            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{ReflectionProbeAtlasDebugShaderName}' for {nameof(ReflectionProbeAtlasDebugPass)}.");
                return;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_ResolvedSettings = ResolveSettings(
                VividRenderingDebugDisplaySettings.Data,
                m_Mode,
                ArraySlice,
                MipLevel,
                m_Exposure);

            var cameraData = frameData?.GetOrCreate<VividCameraData>();
            m_ShouldSkipExecution = DebugPassCameraUtility.ShouldSkipExecution(cameraData);
            var width = RenderGraphTextureDescUtility.ResolveMaxExplicitDimension(
                descriptor => descriptor.Width,
                cameraData?.actualWidth ?? 0,
                cameraData?.pixelWidth ?? 0,
                Screen.width);
            var height = RenderGraphTextureDescUtility.ResolveMaxExplicitDimension(
                descriptor => descriptor.Height,
                cameraData?.actualHeight ?? 0,
                cameraData?.pixelHeight ?? 0,
                Screen.height);

            ConfigureDebugTexture(width, height);
            RefreshAtlasState();
        }

        public override void Record(RasterPassContext context)
        {
            if (m_ShouldSkipExecution || m_Material == null || m_DebugTexture?.innerHandle.IsValid() != true)
                return;

            RefreshAtlasState();
            var resolvedSlice = ResolveIndex(m_ResolvedSettings.arraySlice, m_AtlasSliceCount);
            var resolvedMip = ResolveIndex(m_ResolvedSettings.mipLevel, m_AtlasMipCount);

            var mpb = context.renderGraphPool.GetTempMaterialPropertyBlock();
            mpb.SetInt(AtlasAvailableId, m_AtlasAvailable ? 1 : 0);
            mpb.SetInt(DebugModeId, (int)m_ResolvedSettings.mode);
            mpb.SetInt(DebugSliceId, resolvedSlice);
            mpb.SetInt(DebugMipId, resolvedMip);
            mpb.SetInt(DebugMipCountId, Mathf.Max(0, m_AtlasMipCount));
            mpb.SetInt(DebugSliceCountId, Mathf.Max(0, m_AtlasSliceCount));
            mpb.SetFloat(DebugExposureId, m_ResolvedSettings.exposure);

            if (m_AtlasAvailable)
                mpb.SetTexture(ReflectionAtlasId, m_AtlasTexture);

            CoreUtils.DrawFullScreen(context.cmd, m_Material, mpb, 0);
        }

        public override void Dispose()
        {
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }

            m_AtlasTexture = null;
            m_AtlasAvailable = false;
            m_AtlasMipCount = 0;
            m_AtlasSliceCount = 0;
            m_ShouldSkipExecution = false;
        }

        internal static ReflectionProbeAtlasDebugMode NormalizeDebugMode(ReflectionProbeAtlasDebugMode mode)
        {
            return mode switch
            {
                ReflectionProbeAtlasDebugMode.None => ReflectionProbeAtlasDebugMode.None,
                ReflectionProbeAtlasDebugMode.Atlas => ReflectionProbeAtlasDebugMode.Atlas,
                _ => ReflectionProbeAtlasDebugMode.None,
            };
        }

        internal static ReflectionProbeAtlasDebugSettingsData ResolveSettings(
            VividRenderingDebugSettingsData data,
            ReflectionProbeAtlasDebugMode defaultMode,
            int defaultArraySlice,
            int defaultMipLevel,
            float defaultExposure)
        {
            if (data == null)
            {
                return new ReflectionProbeAtlasDebugSettingsData(
                    NormalizeDebugMode(defaultMode),
                    Mathf.Max(0, defaultArraySlice),
                    Mathf.Max(0, defaultMipLevel),
                    Mathf.Clamp(defaultExposure, -16f, 16f));
            }

            return new ReflectionProbeAtlasDebugSettingsData(
                NormalizeDebugMode(data.reflectionProbeAtlasDebugMode),
                Mathf.Max(0, data.reflectionProbeAtlasArraySlice),
                Mathf.Max(0, data.reflectionProbeAtlasMipLevel),
                Mathf.Clamp(data.reflectionProbeAtlasExposure, -16f, 16f));
        }

        internal static int ResolveIndex(int requestedIndex, int count)
        {
            return Mathf.Clamp(requestedIndex, 0, Mathf.Max(0, count - 1));
        }

        private void RefreshAtlasState()
        {
            m_AtlasAvailable = VividReflectionProbeAtlasSystem.TryGetAtlasDebugData(
                out m_AtlasTexture,
                out _,
                out m_AtlasMipCount,
                out m_AtlasSliceCount);
        }

        private void ConfigureDebugTexture(int width, int height)
        {
            if (m_DebugTexture?.desc == null)
                return;

            m_DebugTexture.desc.Width = Mathf.Max(1, width);
            m_DebugTexture.desc.Height = Mathf.Max(1, height);
            m_DebugTexture.desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            m_DebugTexture.desc.DepthBufferBits = DepthBits.None;
            m_DebugTexture.desc.MsaaSamples = MSAASamples.None;
            m_DebugTexture.desc.FilterMode = FilterMode.Bilinear;
            m_DebugTexture.desc.WrapMode = TextureWrapMode.Clamp;
            m_DebugTexture.desc.ClearBuffer = true;
            m_DebugTexture.desc.ClearColor = Color.black;
            m_DebugTexture.desc.UseMipMap = false;
            m_DebugTexture.desc.AutoGenerateMips = false;
            m_DebugTexture.desc.MipCount = 1;
            m_DebugTexture.desc.EnableRandomWrite = false;
            m_DebugTexture.desc.BindTextureMS = false;
            m_DebugTexture.desc.Name = "ReflectionProbeAtlasDebug";
        }
    }
}
