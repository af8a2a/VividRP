using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    //can provide PipelineResourceManager.Get<VividRPCoreResources>().BlitShader  accessor
    [PipelineResource]
    public class VividRPCoreResources
    {
        [VividResourcePath("Shaders/Core/Private/Blit")]
        public Shader BlitShader;

        [VividResourcePath("Shaders/Core/Private/StopNaN")]
        public Shader StopNaNShader;

        [VividResourcePath("Shaders/Core/Private/FinalBlit")]
        public Shader FinalBlitShader;

        [VividResourcePath("Shaders/Core/Private/PerObjectBufferUpload.compute")]
        public ComputeShader PerObjectBufferUploadCompute;

        [VividResourcePath("Shaders/Material/Hair/HairDotsVertexUpdate.compute")]
        public ComputeShader HairDotsVertexUpdateCompute;

        [VividResourcePath("Shaders/Core/Private/PostProcessing/Diffusion")]
        public Shader DiffusionShader;

        [VividResourcePath("Shaders/Core/Private/PostProcessing/LensFlare/LensFlareDataDriven")]
        public Shader LensFlareDataDrivenShader;

        [VividResourcePath("Shaders/Core/Private/PostProcessing/LensFlare/LensFlareScreenSpace")]
        public Shader LensFlareScreenSpaceShader;

        [VividResourcePath("Shaders/Core/Private/PostProcessing/LensFlare/LensFlareMergeOcclusionDataDriven.compute")]
        public ComputeShader LensFlareMergeOcclusionDataDrivenCompute;

        [VividResourcePath("Shaders/Core/Private/DepthOfField")]
        public Shader DepthOfFieldShader;

        [VividResourcePath("Shaders/Core/Private/DepthOfField.compute")]
        public ComputeShader DepthOfFieldCompute;

        [VividResourcePath("Shaders/Core/Private/FSR3/FSR3PrepareInputs.compute")]
        public ComputeShader FSR3PrepareInputsCompute;

        [VividResourcePath("Shaders/Core/Private/FSR3/FSR3LumaPyramid.compute")]
        public ComputeShader FSR3LumaPyramidCompute;

        [VividResourcePath("Shaders/Core/Private/FSR3/FSR3ShadingChangePyramid.compute")]
        public ComputeShader FSR3ShadingChangePyramidCompute;

        [VividResourcePath("Shaders/Core/Private/FSR3/FSR3ShadingChange.compute")]
        public ComputeShader FSR3ShadingChangeCompute;

        [VividResourcePath("Shaders/Core/Private/FSR3/FSR3PrepareReactivity.compute")]
        public ComputeShader FSR3PrepareReactivityCompute;

        [VividResourcePath("Shaders/Core/Private/FSR3/FSR3LumaInstability.compute")]
        public ComputeShader FSR3LumaInstabilityCompute;

        [VividResourcePath("Shaders/Core/Private/FSR3/FSR3Accumulate.compute")]
        public ComputeShader FSR3AccumulateCompute;

        [VividResourcePath("Shaders/Core/Private/FSR3/FSR3AccumulateSharpen.compute")]
        public ComputeShader FSR3AccumulateSharpenCompute;

        [VividResourcePath("Shaders/Core/Private/FSR3/FSR3RCAS.compute")]
        public ComputeShader FSR3RCASCompute;

        [VividResourcePath("Shaders/Core/Private/TSR/TSRDilateVelocity.compute")]
        public ComputeShader TSRDilateVelocityCompute;

        [VividResourcePath("Shaders/Core/Private/TSR/TSRReprojectHistory.compute")]
        public ComputeShader TSRReprojectHistoryCompute;

        [VividResourcePath("Shaders/Core/Private/TSR/TSRRejectShading.compute")]
        public ComputeShader TSRRejectShadingCompute;

        [VividResourcePath("Shaders/Core/Private/TSR/TSRSpatialAntiAliasing.compute")]
        public ComputeShader TSRSpatialAntiAliasingCompute;

        [VividResourcePath("Shaders/Core/Private/TSR/TSRUpdateHistory.compute")]
        public ComputeShader TSRUpdateHistoryCompute;

        [VividResourcePath("Shaders/Core/Private/TSR/TSRResolveHistory.compute")]
        public ComputeShader TSRResolveHistoryCompute;

        [VividResourcePath("Shaders/Core/Private/TSR/TSRSharpen.compute")]
        public ComputeShader TSRSharpenCompute;

        [VividResourcePath("Shaders/Core/Private/DLSS/DLSSBiasColorMask")]
        public Shader DLSSBiasColorMaskShader;

        [VividResourcePath("Shaders/Core/Private/DLSS/DLSSRRResourcePrep.compute")]
        public ComputeShader DLSSRRResourcePrepCompute;

        [VividResourcePath(
            "Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathTracingDLSSRayReconstructionResolve.compute")]
        public ComputeShader ReferencedPathTracingDLSSRayReconstructionResolveCompute;

        [VividResourcePath("Shaders/Core/Private/AutoExposure/Unreal/AutoExposure.compute")]
        public ComputeShader AutoExposureCompute;

        [VividResourcePath("Shaders/Core/Private/AutoExposure/HDRP/Exposure.compute")]
        public ComputeShader AutoExposureHDRPCompute;

        [VividResourcePath("Shaders/Core/Private/LocalExposure/LocalExposure.compute")]
        public ComputeShader LocalExposureCompute;

        
        [VividResourcePath("Shaders/Core/Private/CoreBlit")]
        public Shader CoreBlitShader;

        [VividResourcePath("Shaders/Core/Private/CoreBlitColorAndDepth")]
        public Shader CoreBlitColorAndDepthShader;

        [VividResourcePath("Shaders/Core/Private/BlitCubeTextureFace")]
        public Shader BlitCubeTextureFaceShader;

        [VividResourcePath("Shaders/FullScreenUV")]
        public Shader FullScreenUVShader;

        [VividResourcePath("Shaders/Core/Private/Sky/HDRISky")]
        public Shader HDRISkyShader;

        [VividResourcePath("Shaders/Core/Private/Sky/PhysicallyBasedSky")]
        public Shader PhysicallyBasedSkyShader;

        [VividResourcePath("Shaders/Core/Private/Sky/SkyLUTGenerator.compute")]
        public ComputeShader AtmosphereLUTCompute;

        [VividResourcePath("Shaders/Core/Private/Sky/GroundIrradiancePrecomputation.compute")]
        public ComputeShader GroundIrradiancePrecomputationCompute;

        [VividResourcePath("Shaders/Core/Private/Sky/InScatteredRadiancePrecomputation.compute")]
        public ComputeShader InScatteredRadiancePrecomputationCompute;

        [VividResourcePath("Shaders/Core/Private/AtmosphericScattering/OpaqueAtmosphericScattering")]
        public Shader AerialPerspectiveShader;

        [VividResourcePath("Shaders/Core/Private/Volumetric/VolumetricDensity.compute")]
        public ComputeShader VolumetricDensityCompute;

        [VividResourcePath("Shaders/Core/Private/Volumetric/VolumetricMaxZ.compute")]
        public ComputeShader VolumetricMaxZCompute;

        [VividResourcePath("Shaders/Core/Private/Volumetric/VolumetricMaterial.compute")]
        public ComputeShader VolumetricMaterialCompute;

        [VividResourcePath("Shaders/Core/Private/Volumetric/LocalVolumetricFogVoxelize")]
        public Shader LocalVolumetricFogVoxelizeShader;

        [VividResourcePath("Shaders/Core/Private/Volumetric/VolumetricLighting.compute")]
        public ComputeShader VolumetricLightingCompute;

        [VividResourcePath("Shaders/Core/Private/Sky/AmbientProbeConvolution.compute")]
        public ComputeShader SkyAmbientProbeConvolutionCompute;

        [VividResourcePath("Shaders/Core/Private/Sky/GGXConvolve")]
        public Shader SkyGGXConvolutionShader;

        [VividResourcePath("Texture/Default/DefaultHDRISky.exr")]
        public Cubemap DefaultHDRISkyCubemap;

        [VividResourcePath("Shaders/Core/Private/PreIntegratedFGD_GGXDisneyDiffuse")]
        public Shader PreIntegratedFGDGGXDisneyDiffuseShader;

        [VividResourcePath("Shaders/Core/Private/PreIntegratedFGD_CharlieFabricLambert")]
        public Shader PreIntegratedFGDCharlieFabricLambertShader;

        [VividResourcePath("Shaders/Core/Private/CopyDepth")]
        public Shader CopyDepthShader;

        [VividResourcePath("Shaders/Core/Private/CameraMotionVectors")]
        public Shader CameraMotionVectorsShader;

        [VividResourcePath("Shaders/Core/Private/ObjectMotionVectorFallback")]
        public Shader ObjectMotionVectorFallbackShader;

        [VividResourcePath("Shaders/Material/MaterialClassification")]
        public ComputeShader MaterialClassificationCompute;

        [VividResourcePath("Shaders/Material/Experimental/Closure/ExperimentalClosureClassification")]
        public ComputeShader ExperimentalClosureClassificationCompute;

        [VividResourcePath("Shaders/Material/Experimental/Closure/ExperimentalClosureDeferredLit")]
        public ComputeShader ExperimentalClosureDeferredLitCompute;

        [VividResourcePath("Shaders/Material/Experimental/Closure/ExperimentalClosureBufferResolve")]
        public Shader ExperimentalClosureBufferResolveShader;

        [VividResourcePath("Shaders/Core/Private/Lighting/scrbound")]
        public ComputeShader BuildScreenAABBCompute;

        [VividResourcePath("Shaders/Core/Private/Lighting/lightlistbuild-bigtile")]
        public ComputeShader BuildPerBigTileLightListCompute;

        [VividResourcePath("Shaders/Core/Private/Lighting/lightlistbuild-clustered")]
        public ComputeShader BuildPerVoxelLightListCompute;

        [VividResourcePath("Shaders/Core/Private/Lighting/ClearLightLists")]
        public ComputeShader ClearLightListsCompute;

        [VividResourcePath("Shaders/Core/Private/Lighting/lightlistbuild-clearatomic")]
        public ComputeShader ClearClusterAtomicIndexCompute;

        [VividResourcePath("Shaders/Core/Private/Lighting/ReGIRGridBuild.compute")]
        public ComputeShader ReGIRGridBuildCompute;

        [VividResourcePath("Shaders/Material/DeferredLit")]
        public ComputeShader DeferredLitCompute;

        [VividResourcePath("Shaders/Core/Private/ColorPyramid/ColorPyramid.compute")]
        public ComputeShader ColorPyramidCompute;

        [VividResourcePath("Shaders/Core/Private/ScreenSpaceReflection/ScreenSpaceReflection.compute")]
        public ComputeShader ScreenSpaceReflectionCompute;

        [VividResourcePath("Shaders/Core/Private/ScreenSpaceReflection/ScreenSpaceReflectionHybrid.raytrace")]
        public RayTracingShader ScreenSpaceReflectionHybridTraceRayTracing;

        [VividResourcePath("Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracing.raytrace")]
        public RayTracingShader ReferencedPathtracingRayTracing;

        [VividResourcePath("Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/RaytracingGBuffer.raytrace")]
        public RayTracingShader RaytracingGBufferRayTracing;

        [VividResourcePath("Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingAccumulation.compute")]
        public ComputeShader ReferencedPathTracingAccumulationCompute;

        [VividResourcePath("Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingEnvironmentSampling.compute")]
        public ComputeShader ReferencedPathTracingEnvironmentSamplingCompute;

        [VividResourcePath("Shaders/Core/Private/GTAO/GTAO.compute")]
        public ComputeShader GTAOCompute;

        [VividResourcePath("Shaders/Core/Private/CACAO/CACAO.compute")]
        public ComputeShader CACAOCompute;


        [VividResourcePath("Shaders/Core/Private/Debug/ClusterDebug")]
        public Shader ClusterDebugShader;

        [VividResourcePath("Shaders/Core/Private/Debug/SliderDebug")]
        public Shader SliderDebugShader;

        [VividResourcePath("Shaders/Core/Private/Debug/OverlayDebug")]
        public Shader OverlayDebugShader;

        [VividResourcePath("Shaders/Core/Private/Debug/ReGIRDebug")]
        public Shader ReGIRDebugShader;

        [VividResourcePath("Shaders/Core/Private/Debug/VisibilityBufferDebug")]
        public Shader VisibilityBufferDebugShader;

        [VividResourcePath("Shaders/Core/Private/Debug/ReflectionProbeAtlasDebug")]
        public Shader ReflectionProbeAtlasDebugShader;

        [VividResourcePath("Shaders/Core/Private/Debug/MaterialDebug")]
        public Shader MaterialDebugShader;

        [VividResourcePath("Shaders/Core/Private/Debug/ExposureDebug")]
        public Shader ExposureDebugShader;

        [VividResourcePath("Shaders/Core/Private/Tools/ColorChecker")]
        public Shader ColorCheckerShader;

        [VividResourcePath("Shaders/Core/Private/Debug/RTASInstanceDebug")]
        public ComputeShader RTASInstanceDebugCompute;

        [VividResourcePath("Shaders/Core/Private/DirectionalRayTracedShadow")]
        public ComputeShader DirectionalRayTracedShadowCompute;

        [VividResourcePath("Shaders/Core/Private/ShadowClassify")]
        public ComputeShader ShadowClassifyCompute;

        [VividResourcePath("Shaders/Core/Private/CSMShadowResolve")]
        public ComputeShader CSMShadowResolveCompute;

        [VividResourcePath("Shaders/Core/Private/GenerateViewZ")]
        public ComputeShader GenerateViewZCompute;

        [VividResourcePath("Shaders/Core/Private/DownSample/HZBGenerate")]
        public ComputeShader HZBGenerateCompute;

        [VividResourcePath("Shaders/Core/Private/DownSample/HDRPHZB.compute")]
        public ComputeShader HDRPHZBCompute;

        [VividResourcePath("Shaders/Core/Private/TemporalAA")]
        public ComputeShader TemporalAACompute;

        [VividResourcePath("Shaders/Core/Private/Bloom/BloomPrefilter.compute")]
        public ComputeShader BloomPrefilterCompute;

        [VividResourcePath("Shaders/Core/Private/Bloom/BloomBlur.compute")]
        public ComputeShader BloomBlurCompute;

        [VividResourcePath("Shaders/Core/Private/Bloom/BloomUpsample.compute")]
        public ComputeShader BloomUpsampleCompute;

        [VividResourcePath("Shaders/Core/Private/NRD/SIGMA/SIGMA_Shadow_ClassifyTiles")]
        public ComputeShader SIGMAClassifyTilesCompute;

        [VividResourcePath("Shaders/Core/Private/NRD/SIGMA/SIGMA_SmoothTiles")]
        public ComputeShader SIGMASmoothTilesCompute;

        [VividResourcePath("Shaders/Core/Private/NRD/SIGMA/SIGMA_Copy")]
        public ComputeShader SIGMAShadowCopyCompute;

        [VividResourcePath("Shaders/Core/Private/NRD/SIGMA/SIGMA_Shadow_Blur")]
        public ComputeShader SIGMAShadowPreBlurCompute;

        [VividResourcePath("Shaders/Core/Private/NRD/SIGMA/SIGMA_Shadow_PostBlur")]
        public ComputeShader SIGMAShadowPostBlurCompute;

        [VividResourcePath("Shaders/Core/Private/NRD/SIGMA/SIGMA_Shadow_TemporalStabilization")]
        public ComputeShader SIGMATemporalStabilizationCompute;

        [VividResourcePath("Shaders/Core/Private/NRD/REBLUR/REBLUR_DiffuseSpecular_ClassifyTiles")]
        public ComputeShader REBLURDiffuseSpecularClassifyTilesCompute;

        [VividResourcePath("Shaders/Core/Private/NRD/REBLUR/REBLUR_DiffuseSpecular_HitDistanceReconstruction3x3")]
        public ComputeShader REBLURDiffuseSpecularHitDistanceReconstruction3x3Compute;

        [VividResourcePath("Shaders/Core/Private/NRD/REBLUR/REBLUR_DiffuseSpecular_HitDistanceReconstruction5x5")]
        public ComputeShader REBLURDiffuseSpecularHitDistanceReconstruction5x5Compute;

        [VividResourcePath("Shaders/Core/Private/NRD/REBLUR/REBLUR_DiffuseSpecular_PrePass")]
        public ComputeShader REBLURDiffuseSpecularPrePassCompute;

        [VividResourcePath("Shaders/Core/Private/NRD/REBLUR/REBLUR_DiffuseSpecular_TemporalAccumulation")]
        public ComputeShader REBLURDiffuseSpecularTemporalAccumulationCompute;

        [VividResourcePath("Shaders/Core/Private/NRD/REBLUR/REBLUR_DiffuseSpecular_HistoryFix")]
        public ComputeShader REBLURDiffuseSpecularHistoryFixCompute;

        [VividResourcePath("Shaders/Core/Private/NRD/REBLUR/REBLUR_DiffuseSpecular_Blur")]
        public ComputeShader REBLURDiffuseSpecularBlurCompute;

        [VividResourcePath("Shaders/Core/Private/NRD/REBLUR/REBLUR_DiffuseSpecular_PostBlur")]
        public ComputeShader REBLURDiffuseSpecularPostBlurCompute;

        [VividResourcePath(
            "Shaders/Core/Private/NRD/REBLUR/REBLUR_DiffuseSpecular_PostBlurTemporalStabilization")]
        public ComputeShader REBLURDiffuseSpecularPostBlurTemporalStabilizationCompute;

        [VividResourcePath(
            "Shaders/Core/Private/NRD/REBLUR/REBLUR_DiffuseSpecular_TemporalStabilization")]
        public ComputeShader REBLURDiffuseSpecularTemporalStabilizationCompute;

        [VividResourcePath("Shaders/Core/Private/NRD/REBLUR/REBLUR_DiffuseSpecular_Resolve")]
        public ComputeShader REBLURDiffuseSpecularResolveCompute;

        [VividResourcePath("Shaders/Core/Private/GPUDriven/GPUInstanceCulling")]
        public ComputeShader GPUInstanceCullingCompute;

        [VividResourcePath("Shaders/Core/Private/GPUDriven/MeshletListBuild")]
        public ComputeShader MeshletListBuildCompute;

        [VividResourcePath("Shaders/Core/Private/GPUDriven/GPUMeshletCulling")]
        public ComputeShader GPUMeshletCullingCompute;

        [VividResourcePath("Shaders/Core/Private/GPUDriven/FixupVisibleMeshletIndirectDrawArgs")]
        public ComputeShader FixupVisibleMeshletIndirectDrawArgsCompute;

        [VividResourcePath("Shaders/Core/Private/GPUDriven/GPUDrivenVirtualTexturePageProducer.compute")]
        public ComputeShader GPUDrivenVirtualTexturePageProducerCompute;

        [VividResourcePath("Shaders/Core/Private/GPUDriven/TerrainRuntimeVirtualTexturePageProducer.compute")]
        public ComputeShader TerrainRuntimeVirtualTexturePageProducerCompute;

        [VividResourcePath("Shaders/Core/Private/GPUDriven/VirtualTextureBlockCompress.compute")]
        public ComputeShader VirtualTextureBlockCompressCompute;

        [VividResourcePath("Shaders/Core/Private/GPUDriven/VirtualTexturePageTableScatter.compute")]
        public ComputeShader VirtualTexturePageTableScatterCompute;

        // Film Grain preset textures
        [VividResourcePath("Texture/FilmGrain/Thin01.png")]
        public Texture2D FilmGrainThin1;

        [VividResourcePath("Texture/FilmGrain/Thin02.png")]
        public Texture2D FilmGrainThin2;

        [VividResourcePath("Texture/FilmGrain/Medium01.png")]
        public Texture2D FilmGrainMedium1;

        [VividResourcePath("Texture/FilmGrain/Medium02.png")]
        public Texture2D FilmGrainMedium2;

        [VividResourcePath("Texture/FilmGrain/Medium03.png")]
        public Texture2D FilmGrainMedium3;

        [VividResourcePath("Texture/FilmGrain/Medium04.png")]
        public Texture2D FilmGrainMedium4;

        [VividResourcePath("Texture/FilmGrain/Medium05.png")]
        public Texture2D FilmGrainMedium5;

        [VividResourcePath("Texture/FilmGrain/Medium06.png")]
        public Texture2D FilmGrainMedium6;

        [VividResourcePath("Texture/FilmGrain/Large01.png")]
        public Texture2D FilmGrainLarge01;

        [VividResourcePath("Texture/FilmGrain/Large02.png")]
        public Texture2D FilmGrainLarge02;

        public ComputeShader ResolveAutoExposureCompute(VividRenderPipelineAsset pipelineAsset)
        {
            return pipelineAsset != null && pipelineAsset.AutoExposureImplementation == AutoExposureImplementationPath.HDRP
                ? AutoExposureHDRPCompute
                : AutoExposureCompute;
        }
    }
}
