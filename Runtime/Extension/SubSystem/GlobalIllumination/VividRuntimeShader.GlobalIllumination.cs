using System;

namespace UnityEngine.Rendering.Universal
{
    partial class VividRuntimeShader
    {
        [SerializeField]
        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/Referenced/Shader/ReferencedPathTracing.raytrace")]
        private RayTracingShader m_ReferencedPathTracingRTShader;

        /// <summary>
        /// Referenced Path Tracing ray tracing shader
        /// </summary>
        public RayTracingShader referencedPathTracingRTShader
        {
            get => m_ReferencedPathTracingRTShader;
            set => this.SetValueAndNotify(ref m_ReferencedPathTracingRTShader, value, nameof(m_ReferencedPathTracingRTShader));
        }

        [SerializeField]
        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/Referenced/Shader/SharcResolve.compute")]
        private ComputeShader m_SharcResolveCS;

        /// <summary>
        /// SHARC Resolve compute shader
        /// </summary>
        public ComputeShader sharcResolveCS
        {
            get => m_SharcResolveCS;
            set => this.SetValueAndNotify(ref m_SharcResolveCS, value, nameof(m_SharcResolveCS));
        }

        [SerializeField]
        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/Referenced/Shader/PathTracingRemodulation.compute")]
        private ComputeShader m_PathTracingRemodulationCS;

        /// <summary>
        /// Path Tracing Remodulation compute shader for applying material factors after NRD denoising
        /// </summary>
        public ComputeShader pathTracingRemodulationCS
        {
            get => m_PathTracingRemodulationCS;
            set => this.SetValueAndNotify(ref m_PathTracingRemodulationCS, value, nameof(m_PathTracingRemodulationCS));
        }

        [SerializeField]
        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/Referenced/Shader/PathTracingTemporalReprojection.compute")]
        private ComputeShader m_PathTracingTemporalReprojectionCS;

        /// <summary>
        /// Path Tracing Temporal Reprojection compute shader for history validation and accumulation
        /// </summary>
        public ComputeShader pathTracingTemporalReprojectionCS
        {
            get => m_PathTracingTemporalReprojectionCS;
            set => this.SetValueAndNotify(ref m_PathTracingTemporalReprojectionCS, value, nameof(m_PathTracingTemporalReprojectionCS));
        }
    }
}