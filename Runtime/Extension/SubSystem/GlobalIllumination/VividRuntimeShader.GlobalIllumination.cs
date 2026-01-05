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
    }
}