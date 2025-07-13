using System;

namespace UnityEngine.Rendering.Universal
{
    
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
 
    public class ScreenSpaceReflectionRuntimeShader:IRenderPipelineResources
    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;

        
        [SerializeField, ResourcePath("Runtime/Extension/ScreenSpaceLighting/ScreenSpaceReflection/Shader/ScreenSpaceReflections.compute")]
        private ComputeShader m_ScreenSpaceReflectionsCS;

        public ComputeShader screenSpaceReflectionsCS
        {
            get => m_ScreenSpaceReflectionsCS;
            set => this.SetValueAndNotify(ref m_ScreenSpaceReflectionsCS, value);
        }
        
        
        [SerializeField, ResourcePath("Runtime/Extension/ScreenSpaceLighting/ScreenSpaceReflection/Shader/RayTracingReflections.raytrace")]
        private RayTracingShader m_RayTracingReflections;

        public RayTracingShader rayTracingReflections
        {
            get => m_RayTracingReflections;
            set => this.SetValueAndNotify(ref m_RayTracingReflections, value);
        }


    }
}