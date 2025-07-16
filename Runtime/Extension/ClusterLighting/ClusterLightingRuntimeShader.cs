using System;


namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class ClusterLightingRuntimeShader : IRenderPipelineResources

    {
        [SerializeField] [HideInInspector] private int _version;
        public int version => _version;

        /// <summary>
        /// GPU lights list compute shader.
        /// </summary>
        [SerializeField, ResourcePath("Runtime/Extension/ClusterLighting/Shader/GPULightsClearLists.compute")]
        private ComputeShader m_GpuLightsClearLists;

        public ComputeShader gpuLightsClearLists
        {
            get => m_GpuLightsClearLists;
            set => this.SetValueAndNotify(ref m_GpuLightsClearLists, value);
        }

        [SerializeField, ResourcePath("Runtime/Extension/ClusterLighting/Shader/GPULightsCoarseCulling.compute")]
        private ComputeShader m_GpuLightsCoarseCullingCS;

        public ComputeShader gpuLightsCoarseCullingCS
        {
            get => m_GpuLightsCoarseCullingCS;
            set => this.SetValueAndNotify(ref m_GpuLightsCoarseCullingCS, value);
        }


        [SerializeField, ResourcePath("Runtime/Extension/ClusterLighting/Shader/GPULightsCluster.compute")]
        private ComputeShader m_GpuLightsCluster;

        public ComputeShader gpuLightsCluster
        {
            get => m_GpuLightsCluster;
            set => this.SetValueAndNotify(ref m_GpuLightsCluster, value);
        }
        
        
        /// <summary>
        /// Deferred lighting compute shader.
        /// </summary>
        [SerializeField, ResourcePath("ShaderLibrary/Extension/Lighting/DeferredLit.compute")]
        private ComputeShader m_DeferredLightingCS;

        public ComputeShader deferredLightingCS
        {
            get => m_DeferredLightingCS;
            set => this.SetValueAndNotify(ref m_DeferredLightingCS, value);
        }

    }
}