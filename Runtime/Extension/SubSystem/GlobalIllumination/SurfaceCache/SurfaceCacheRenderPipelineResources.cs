using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed partial class SurfaceCacheRenderPipelineResourceSet : IRenderPipelineResources
    {

        
        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/Shader/TemporalFiltering.compute")]
        public ComputeShader m_TemporalFilteringShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/Shader/SpatialFiltering.compute")]
        public ComputeShader m_SpatialFilteringShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/Shader/RestirEstimation.compute")]
        public ComputeShader m_RestirEstimationShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/Shader/RisEstimation.urtshader")]
        public ComputeShader m_RisEstimationComputeShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/Shader/RisEstimation.urtshader")]
        public RayTracingShader m_RisEstimationRayTracingShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/Shader/Scrolling.compute")]
        public ComputeShader m_ScrollingShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/Shader/Defrag.compute")]
        public ComputeShader m_DefragShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/Shader/Eviction.compute")]
        public ComputeShader m_EvictionShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/Shader/PunctualLightSampling.urtshader")]
        public ComputeShader m_PunctualLightSamplingComputeShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/Shader/PunctualLightSampling.urtshader")]
        public RayTracingShader m_PunctualLightSamplingRayTracingShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/Shader/UniformEstimation.urtshader")]
        public ComputeShader m_UniformEstimationComputeShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/Shader/UniformEstimation.urtshader")]
        public RayTracingShader m_UniformEstimationRayTracingShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/Shader/RestirCandidateTemporal.urtshader")]
        public ComputeShader m_RestirCandidateTemporalComputeShader;

        
        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/Shader/RestirCandidateTemporal.urtshader")]
        public RayTracingShader m_RestirCandidateTemporalRayTracingShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/Shader/RestirSpatial.compute")]
        public ComputeShader m_RestirSpatialShader;

        
        
                [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/Shader/FallbackMaterial.mat")]
        public Material m_FallbackMaterial;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/Shader/PatchAllocation.compute")]
        public ComputeShader m_AllocationShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/Shader/ScreenResolveLookup.compute")]
        public ComputeShader m_ScreenResolveLookupShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/Shader/ScreenResolveUpsampling.compute")]
        public ComputeShader m_ScreenResolveUpsamplingShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/Shader/Debug.compute")]
        public ComputeShader m_DebugShader;

        [ResourcePath("Runtime/Extension/SubSystem/GlobalIllumination/SurfaceCache/Shader/FlatNormalResolution.compute")]
        public ComputeShader m_FlatNormalResolutionShader;

        public Material fallbackMaterial
        {
            get => m_FallbackMaterial;
            set => this.SetValueAndNotify(ref m_FallbackMaterial, value, nameof(m_FallbackMaterial));
        }

        public ComputeShader allocationShader
        {
            get => m_AllocationShader;
            set => this.SetValueAndNotify(ref m_AllocationShader, value, nameof(m_AllocationShader));
        }

        public ComputeShader screenResolveLookupShader
        {
            get => m_ScreenResolveLookupShader;
            set => this.SetValueAndNotify(ref m_ScreenResolveLookupShader, value, nameof(m_ScreenResolveLookupShader));
        }

        public ComputeShader screenResolveUpsamplingShader
        {
            get => m_ScreenResolveUpsamplingShader;
            set => this.SetValueAndNotify(ref m_ScreenResolveUpsamplingShader, value, nameof(m_ScreenResolveUpsamplingShader));
        }

        public ComputeShader debugShader
        {
            get => m_DebugShader;
            set => this.SetValueAndNotify(ref m_DebugShader, value, nameof(m_DebugShader));
        }

        public ComputeShader flatNormalResolutionShader
        {
            get => m_FlatNormalResolutionShader;
            set => this.SetValueAndNotify(ref m_FlatNormalResolutionShader, value, nameof(m_FlatNormalResolutionShader));
        }

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

        public int version { get; }
    }
}