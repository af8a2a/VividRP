using System;

namespace UnityEngine.Rendering.Universal
{
    public partial class UniversalRenderPipelineDebugShaders : IRenderPipelineResources
    {
        [Header("Debug")] [SerializeField, ResourcePath("Runtime/Extension/RayTracing/Shaders/RTASDebug.raytrace")]
        public RayTracingShader m_RtasDebugRT;

        public RayTracingShader debugRTASRT
        {
            get => m_RtasDebugRT;
            set => this.SetValueAndNotify(ref m_RtasDebugRT, value);
        }

    }
}