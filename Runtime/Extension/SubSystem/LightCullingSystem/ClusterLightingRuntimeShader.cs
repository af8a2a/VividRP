using System;


namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class ClusterLightingRuntimeShader : IRenderPipelineResources

    {
        [SerializeField] [HideInInspector] private int _version = 1;
        public int version => _version;

        #region Cluster

        /// <summary>
        /// GPU lights list compute shader.
        /// </summary>
        [SerializeField, ResourcePath("Runtime/Extension/SubSystem/LightCullingSystem/Shader/GPULightsClearLists.compute")]
        private ComputeShader m_GpuLightsClearLists;

        public ComputeShader gpuLightsClearLists
        {
            get => m_GpuLightsClearLists;
            set => this.SetValueAndNotify(ref m_GpuLightsClearLists, value);
        }

        [SerializeField, ResourcePath("Runtime/Extension/SubSystem/LightCullingSystem/Shader/GPULightsCoarseCulling.compute")]
        private ComputeShader m_GpuLightsCoarseCullingCS;

        public ComputeShader gpuLightsCoarseCullingCS
        {
            get => m_GpuLightsCoarseCullingCS;
            set => this.SetValueAndNotify(ref m_GpuLightsCoarseCullingCS, value);
        }


        [SerializeField, ResourcePath("Runtime/Extension/SubSystem/LightCullingSystem/Shader/GPULightsCluster.compute")]
        private ComputeShader m_GpuLightsCluster;

        public ComputeShader gpuLightsCluster
        {
            get => m_GpuLightsCluster;
            set => this.SetValueAndNotify(ref m_GpuLightsCluster, value);
        }

        #endregion


        #region FPTL

        [SerializeField, ResourcePath("Runtime/Extension/SubSystem/LightCullingSystem/Shader/GPULightsBigTile.compute")]
        private ComputeShader m_GPULightsBigTile;

        public ComputeShader gpuLightsBigTile
        {
            get => m_GPULightsBigTile;
            set => this.SetValueAndNotify(ref m_GPULightsBigTile, value);
        }

        [SerializeField, ResourcePath("Runtime/Extension/SubSystem/LightCullingSystem/Shader/GPULightListBuild.compute")]
        private ComputeShader m_GPULightListBuild;

        public ComputeShader gpuLightListBuild
        {
            get => m_GPULightListBuild;
            set => this.SetValueAndNotify(ref m_GPULightListBuild, value);
        }

        #endregion

        /// <summary>
        /// Deferred lighting compute shader.
        /// </summary>
        [SerializeField, ResourcePath("ShaderLibrary/Extension/Lighting/Lit/DeferredLit.compute")]
        private ComputeShader m_DeferredLightingCS;

        public ComputeShader deferredLightingCS
        {
            get => m_DeferredLightingCS;
            set => this.SetValueAndNotify(ref m_DeferredLightingCS, value);
        }

    }
}