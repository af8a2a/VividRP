using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public enum ColorGradingSpace
    {
        AcesCg,
        [InspectorName("sRGB")]
        sRGB
    }

    public enum AutoExposureImplementationPath
    {
        Unreal,
        HDRP,
    }

    [CreateAssetMenu(menuName = "VividRP/Vivid Render Pipeline")]
    public class VividRenderPipelineAsset : RenderPipelineAsset<VividRenderPipeline>, IProbeVolumeEnabledRenderPipeline, ISTPEnabledRenderPipeline
    {
        private const string DefaultShaderName = "VividRP/Material/StandardLit";
        private const string DefaultMaterialResourcePath = "DefaultMaterial";

        public RenderGraphData RenderGraphAsset;

        [SerializeField]
        private bool m_EnableAsyncCompute = true;

        [SerializeField]
        private bool m_EnableGPUDriven;

        [SerializeField]
        private bool m_EnableGPUDrivenDebugOverlay;

        [SerializeField]
        private bool m_EnableSRPBatcher = true;

        [SerializeField]
        private bool m_SupportProbeVolume;

        [SerializeField]
        private ProbeVolumeSHBands m_ProbeVolumeSHBands = ProbeVolumeSHBands.SphericalHarmonicsL2;

        [SerializeField]
        private ColorGradingSpace m_ColorGradingSpace = ColorGradingSpace.sRGB;

        [SerializeField]
        private AutoExposureImplementationPath m_AutoExposureImplementation = AutoExposureImplementationPath.Unreal;

        public bool EnableAsyncCompute
        {
            get => m_EnableAsyncCompute;
            set => m_EnableAsyncCompute = value;
        }

        public bool EnableSRPBatcher
        {
            get => m_EnableSRPBatcher;
            set => m_EnableSRPBatcher = value;
        }

        public bool SupportProbeVolume
        {
            get => m_SupportProbeVolume;
            set => m_SupportProbeVolume = value;
        }

        public ProbeVolumeSHBands ProbeVolumeSHBands
        {
            get => m_ProbeVolumeSHBands;
            set => m_ProbeVolumeSHBands = value;
        }

        public ColorGradingSpace ColorGradingSpace
        {
            get => m_ColorGradingSpace;
            set => m_ColorGradingSpace = value;
        }

        public bool EnableGPUDriven
        {
            get => m_EnableGPUDriven;
            set => m_EnableGPUDriven = value;
        }

        public bool EnableGPUDrivenDebugOverlay
        {
            get => m_EnableGPUDrivenDebugOverlay;
            set => m_EnableGPUDrivenDebugOverlay = value;
        }

        public AutoExposureImplementationPath AutoExposureImplementation
        {
            get => m_AutoExposureImplementation;
            set => m_AutoExposureImplementation = value;
        }

        public override Shader defaultShader => Shader.Find(DefaultShaderName);

        public override Material defaultMaterial => Resources.Load<Material>(DefaultMaterialResourcePath);

        bool IProbeVolumeEnabledRenderPipeline.supportProbeVolume => m_SupportProbeVolume;

        ProbeVolumeSHBands IProbeVolumeEnabledRenderPipeline.maxSHBands => m_ProbeVolumeSHBands;

        bool ISTPEnabledRenderPipeline.isStpUsed => true;

#pragma warning disable 618
        ProbeVolumeSceneData IProbeVolumeEnabledRenderPipeline.probeVolumeSceneData =>
            VividRenderPipelineGlobalSettings.instance?.GetOrCreateAPVSceneData();
#pragma warning restore 618

        internal static VividRenderPipelineAsset GetActiveAsset()
        {
            return GraphicsSettings.currentRenderPipeline as VividRenderPipelineAsset
                ?? QualitySettings.renderPipeline as VividRenderPipelineAsset
                ?? GraphicsSettings.defaultRenderPipeline as VividRenderPipelineAsset;
        }

        protected override UnityEngine.Rendering.RenderPipeline CreatePipeline()
        {
#if UNITY_EDITOR
            VividRenderPipelineGlobalSettings.Ensure();
#endif
            return new VividRenderPipeline(this);
        }
    }
}
