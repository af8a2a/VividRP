using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed partial class SurfaceCacheRenderPipelineResourceSet : IRenderPipelineResources
    {

        
        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/TemporalFiltering.compute")]
        public ComputeShader m_TemporalFilteringShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/SpatialFiltering.compute")]
        public ComputeShader m_SpatialFilteringShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/RestirEstimation.compute")]
        public ComputeShader m_RestirEstimationShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/RisEstimation.urtshader")]
        public ComputeShader m_RisEstimationComputeShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/RisEstimation.urtshader")]
        public RayTracingShader m_RisEstimationRayTracingShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/Scrolling.compute")]
        public ComputeShader m_ScrollingShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/Defrag.compute")]
        public ComputeShader m_DefragShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/Eviction.compute")]
        public ComputeShader m_EvictionShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/PunctualLightSampling.urtshader")]
        public ComputeShader m_PunctualLightSamplingComputeShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/PunctualLightSampling.urtshader")]
        public RayTracingShader m_PunctualLightSamplingRayTracingShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/UniformEstimation.urtshader")]
        public ComputeShader m_UniformEstimationComputeShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/UniformEstimation.urtshader")]
        public RayTracingShader m_UniformEstimationRayTracingShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/RestirCandidateTemporal.urtshader")]
        public ComputeShader m_RestirCandidateTemporalComputeShader;

        
        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/RestirCandidateTemporal.urtshader")]
        public RayTracingShader m_RestirCandidateTemporalRayTracingShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/RestirSpatial.compute")]
        public ComputeShader m_RestirSpatialShader;

        public ComputeShader spatialFilteringShader
        {
            get => m_SpatialFilteringShader;
            set => this.SetValueAndNotify(ref m_SpatialFilteringShader, value, nameof(m_SpatialFilteringShader));
        }

        public ComputeShader temporalFilteringShader
        {
            get => m_TemporalFilteringShader;
            set => this.SetValueAndNotify(ref m_TemporalFilteringShader, value, nameof(m_TemporalFilteringShader));
        }

        public ComputeShader punctualLightSamplingComputeShader
        {
            get => m_PunctualLightSamplingComputeShader;
            set => this.SetValueAndNotify(ref m_PunctualLightSamplingComputeShader, value, nameof(m_PunctualLightSamplingComputeShader));
        }

        public RayTracingShader punctualLightSamplingRayTracingShader
        {
            get => m_PunctualLightSamplingRayTracingShader;
            set => this.SetValueAndNotify(ref m_PunctualLightSamplingRayTracingShader, value, nameof(m_PunctualLightSamplingRayTracingShader));
        }

        public ComputeShader uniformEstimationComputeShader
        {
            get => m_UniformEstimationComputeShader;
            set => this.SetValueAndNotify(ref m_UniformEstimationComputeShader, value, nameof(m_UniformEstimationComputeShader));
        }

        public RayTracingShader uniformEstimationRayTracingShader
        {
            get => m_UniformEstimationRayTracingShader;
            set => this.SetValueAndNotify(ref m_UniformEstimationRayTracingShader, value, nameof(m_UniformEstimationRayTracingShader));
        }

        public ComputeShader restirCandidateTemporalComputeShader
        {
            get => m_RestirCandidateTemporalComputeShader;
            set => this.SetValueAndNotify(ref m_RestirCandidateTemporalComputeShader, value, nameof(m_RestirCandidateTemporalComputeShader));
        }

        public RayTracingShader restirCandidateTemporalRayTracingShader
        {
            get => m_RestirCandidateTemporalRayTracingShader;
            set => this.SetValueAndNotify(ref m_RestirCandidateTemporalRayTracingShader, value, nameof(m_RestirCandidateTemporalRayTracingShader));
        }

        public ComputeShader restirSpatialShader
        {
            get => m_RestirSpatialShader;
            set => this.SetValueAndNotify(ref m_RestirSpatialShader, value, nameof(m_RestirSpatialShader));
        }

        public ComputeShader restirEstimationShader
        {
            get => m_RestirEstimationShader;
            set => this.SetValueAndNotify(ref m_RestirEstimationShader, value, nameof(m_RestirEstimationShader));
        }

        public ComputeShader defragShader
        {
            get => m_DefragShader;
            set => this.SetValueAndNotify(ref m_DefragShader, value, nameof(m_DefragShader));
        }

        public ComputeShader risEstimationComputeShader
        {
            get => m_RisEstimationComputeShader;
            set => this.SetValueAndNotify(ref m_RisEstimationComputeShader, value, nameof(m_RisEstimationComputeShader));
        }

        public RayTracingShader risEstimationRayTracingShader
        {
            get => m_RisEstimationRayTracingShader;
            set => this.SetValueAndNotify(ref m_RisEstimationRayTracingShader, value, nameof(m_RisEstimationRayTracingShader));
        }

        public ComputeShader scrollingShader
        {
            get => m_ScrollingShader;
            set => this.SetValueAndNotify(ref m_ScrollingShader, value, nameof(m_ScrollingShader));
        }

        public ComputeShader evictionShader
        {
            get => m_EvictionShader;
            set => this.SetValueAndNotify(ref m_EvictionShader, value, nameof(m_EvictionShader));
        }
    }
}