using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class RaytracingShadowRuntimeShaders:IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;
        
        
        [SerializeField] [ResourcePath("Runtime/Extension/Shadow/RaytraceShadow/Shader/RayTracingShadows.raytrace")]
        private RayTracingShader m_RayTracingShadowShader;


        public RayTracingShader rayTracingShadowShader
        {
            get => m_RayTracingShadowShader;
            set => this.SetValueAndNotify(ref m_RayTracingShadowShader, value, nameof(m_RayTracingShadowShader));
        }

    }
}