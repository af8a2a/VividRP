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
        
        [SerializeField] [ResourcePath("Runtime/Extension/SubSystem/ShadowSystem/HybridShadow/Shader/HybridShadow.compute")]
        private ComputeShader m_HybridShadowCS;


        public ComputeShader hybridShadowShader
        {
            get => m_HybridShadowCS;
            set => this.SetValueAndNotify(ref m_HybridShadowCS, value, nameof(m_HybridShadowCS));
        }

        
        
        [SerializeField] [ResourcePath("Runtime/Extension/SubSystem/ShadowSystem/FullRaytraceShadow/Shader/FullRaytraceShadow.compute")]
        private ComputeShader m_FullRayTracingShadowShader;


        public ComputeShader fullRayTracingShadowShader
        {
            get => m_FullRayTracingShadowShader;
            set => this.SetValueAndNotify(ref m_FullRayTracingShadowShader, value, nameof(m_FullRayTracingShadowShader));
        }

        
        [SerializeField, ResourcePath("Runtime/Extension/Filter/Denoiser/FidelityFXShadowDenoiser/Shader/ShadowClassify.compute")]
        private ComputeShader m_FidelityFXShadowClassify;
        
        public ComputeShader fidelityFXShadowClassify
        {
            get => m_FidelityFXShadowClassify;
            set => this.SetValueAndNotify(ref m_FidelityFXShadowClassify, value);
        }

        [SerializeField, ResourcePath("Runtime/Extension/Filter/Denoiser/FidelityFXShadowDenoiser/Shader/ClassifyDebug.compute")]
        private ComputeShader m_FidelityFXShadowClassifyDebug;

        public ComputeShader fidelityFXShadowClassifyDebug
        {
            get => m_FidelityFXShadowClassifyDebug;
            set => this.SetValueAndNotify(ref m_FidelityFXShadowClassifyDebug, value);
        }

        [SerializeField, ResourcePath("Runtime/Extension/Filter/Denoiser/FidelityFXShadowDenoiser/Shader/HitResultDebug.compute")]
        private ComputeShader m_FidelityFXShadowHitDebug;

        public ComputeShader fidelityFXShadowHitDebug
        {
            get => m_FidelityFXShadowHitDebug;
            set => this.SetValueAndNotify(ref m_FidelityFXShadowHitDebug, value);
        }

    }
}