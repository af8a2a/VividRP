using System;
using UnityEngine.Categorization;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [CategoryInfo(Name = "R: Screen Probes", Order = 1000)]
    partial class ScreenProbeRenderPipelineResourceSet : IRenderPipelineResources
    {
        [SerializeField, HideInInspector]
        int m_Version = 1;

        int IRenderPipelineGraphicsSettings.version => m_Version;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/ScreenProbes/Shaders/ScreenProbeTracing.compute")]
        public ComputeShader m_ProbeTracingShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/ScreenProbes/Shaders/ScreenProbeFiltering.compute")]
        public ComputeShader m_ProbeFilteringShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/ScreenProbes/Shaders/ScreenProbeUpsampling.compute")]
        public ComputeShader m_ProbeUpsamplingShader;

        public ComputeShader probeTracingShader
        {
            get => m_ProbeTracingShader;
            set => this.SetValueAndNotify(ref m_ProbeTracingShader, value, nameof(m_ProbeTracingShader));
        }

        public ComputeShader probeFilteringShader
        {
            get => m_ProbeFilteringShader;
            set => this.SetValueAndNotify(ref m_ProbeFilteringShader, value, nameof(m_ProbeFilteringShader));
        }

        public ComputeShader probeUpsamplingShader
        {
            get => m_ProbeUpsamplingShader;
            set => this.SetValueAndNotify(ref m_ProbeUpsamplingShader, value, nameof(m_ProbeUpsamplingShader));
        }
    }
}
