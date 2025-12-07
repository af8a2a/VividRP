using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class RaytracingShadowRuntimeShaders:IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;
        
        
        [SerializeField] [ResourcePath("Runtime/Extension/SubSystem/ShadowSystem/RaytraceShadow/Shader/RayTracingShadows.raytrace")]
        private RayTracingShader m_RayTracingShadowShader;


        public RayTracingShader rayTracingShadowShader
        {
            get => m_RayTracingShadowShader;
            set => this.SetValueAndNotify(ref m_RayTracingShadowShader, value, nameof(m_RayTracingShadowShader));
        }
        
        
        
        [SerializeField] [ResourcePath("Runtime/Extension/SubSystem/ShadowSystem/FullRaytraceShadow/Shader/FullRaytraceShadow.compute")]
        private ComputeShader m_FullRayTracingShadowShader;


        public ComputeShader fullRayTracingShadowShader
        {
            get => m_FullRayTracingShadowShader;
            set => this.SetValueAndNotify(ref m_FullRayTracingShadowShader, value, nameof(m_FullRayTracingShadowShader));
        }


    }
}