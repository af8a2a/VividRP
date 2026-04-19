using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    //can provide PipelineResourceManager.Get<VividRPCoreResources>().BlitShader  accessor
    [PipelineResource]
    public class VividRPCoreResources
    {
        [ResourcePath("Shaders/Core/Private/Blit")]
        public Shader BlitShader;

        [ResourcePath("Shaders/Core/Private/FinalBlit")]
        public Shader FinalBlitShader;

        [ResourcePath("Shaders/Core/Private/AutoExposure/Unreal/AutoExposure.compute")]
        public ComputeShader AutoExposureCompute;

        [ResourcePath("Shaders/Core/Private/AutoExposure/HDRP/Exposure.compute")]
        public ComputeShader AutoExposureHDRPCompute;

        
        [ResourcePath("Shaders/Core/Private/CoreBlit")]
        public Shader CoreBlitShader;

        [ResourcePath("Shaders/Core/Private/CoreBlitColorAndDepth")]
        public Shader CoreBlitColorAndDepthShader;

        [ResourcePath("Shaders/FullScreenUV")]
        public Shader FullScreenUVShader;

        [ResourcePath("Shaders/Core/Private/Sky/HDRISky")]
        public Shader HDRISkyShader;

        [ResourcePath("Shaders/Core/Private/Sky/PhysicallyBasedSky")]
        public Shader PhysicallyBasedSkyShader;

        [ResourcePath("Shaders/Core/Private/Sky/SkyLUTGenerator.compute")]
        public ComputeShader AtmosphereLUTCompute;

        [ResourcePath("Shaders/Core/Private/Sky/GroundIrradiancePrecomputation.compute")]
        public ComputeShader GroundIrradiancePrecomputationCompute;

        [ResourcePath("Shaders/Core/Private/Sky/InScatteredRadiancePrecomputation.compute")]
        public ComputeShader InScatteredRadiancePrecomputationCompute;

        [ResourcePath("Shaders/Core/Private/AtmosphericScattering/OpaqueAtmosphericScattering")]
        public Shader AerialPerspectiveShader;

        [ResourcePath("Shaders/Core/Private/Sky/AmbientProbeConvolution.compute")]
        public ComputeShader SkyAmbientProbeConvolutionCompute;

        [ResourcePath("Shaders/Core/Private/Sky/GGXConvolve")]
        public Shader SkyGGXConvolutionShader;

        [ResourcePath("Texture/Default/DefaultHDRISky.exr")]
        public Cubemap DefaultHDRISkyCubemap;

        [ResourcePath("Shaders/Core/Private/PreIntegratedFGD_GGXDisneyDiffuse")]
        public Shader PreIntegratedFGDGGXDisneyDiffuseShader;

        [ResourcePath("Shaders/Core/Private/PreIntegratedFGD_CharlieFabricLambert")]
        public Shader PreIntegratedFGDCharlieFabricLambertShader;

        [ResourcePath("Shaders/Core/Private/CopyDepth")]
        public Shader CopyDepthShader;

        [ResourcePath("Shaders/Core/Private/CameraMotionVectors")]
        public Shader CameraMotionVectorsShader;

        [ResourcePath("Shaders/Core/Private/ObjectMotionVectorFallback")]
        public Shader ObjectMotionVectorFallbackShader;

        [ResourcePath("Shaders/Material/MaterialClassification")]
        public ComputeShader MaterialClassificationCompute;


        [ResourcePath("Shaders/Core/Private/Lighting/scrbound")]
        public ComputeShader BuildScreenAABBCompute;

        [ResourcePath("Shaders/Core/Private/Lighting/lightlistbuild-bigtile")]
        public ComputeShader BuildPerBigTileLightListCompute;

        [ResourcePath("Shaders/Core/Private/Lighting/lightlistbuild-clustered")]
        public ComputeShader BuildPerVoxelLightListCompute;

        [ResourcePath("Shaders/Core/Private/Lighting/ClearLightLists")]
        public ComputeShader ClearLightListsCompute;

        [ResourcePath("Shaders/Core/Private/Lighting/lightlistbuild-clearatomic")]
        public ComputeShader ClearClusterAtomicIndexCompute;

        [ResourcePath("Shaders/Material/DeferredLit")]
        public ComputeShader DeferredLitCompute;


        [ResourcePath("Shaders/Core/Private/Debug/ClusterDebug")]
        public Shader ClusterDebugShader;

        [ResourcePath("Shaders/Core/Private/Debug/SliderDebug")]
        public Shader SliderDebugShader;

        [ResourcePath("Shaders/Core/Private/Debug/OverlayDebug")]
        public Shader OverlayDebugShader;

        [ResourcePath("Shaders/Core/Private/Debug/ExposureDebug")]
        public Shader ExposureDebugShader;

        [ResourcePath("Shaders/Core/Private/Debug/RTASInstanceDebug")]
        public ComputeShader RTASInstanceDebugCompute;

        [ResourcePath("Shaders/Core/Private/DirectionalRayTracedShadow")]
        public ComputeShader DirectionalRayTracedShadowCompute;

        [ResourcePath("Shaders/Core/Private/ShadowClassify")]
        public ComputeShader ShadowClassifyCompute;

        [ResourcePath("Shaders/Core/Private/CSMShadowResolve")]
        public ComputeShader CSMShadowResolveCompute;

        [ResourcePath("Shaders/Core/Private/GenerateViewZ")]
        public ComputeShader GenerateViewZCompute;

        [ResourcePath("Shaders/Core/Private/DownSample/HZBGenerate")]
        public ComputeShader HZBGenerateCompute;

        [ResourcePath("Shaders/Core/Private/TemporalAA")]
        public ComputeShader TemporalAACompute;

        [ResourcePath("Shaders/Core/Private/Bloom/BloomPrefilter.compute")]
        public ComputeShader BloomPrefilterCompute;

        [ResourcePath("Shaders/Core/Private/Bloom/BloomBlur.compute")]
        public ComputeShader BloomBlurCompute;

        [ResourcePath("Shaders/Core/Private/Bloom/BloomUpsample.compute")]
        public ComputeShader BloomUpsampleCompute;

        [ResourcePath("Shaders/Core/Private/NRD/SIGMA/SIGMA_Shadow_ClassifyTiles")]
        public ComputeShader SIGMAClassifyTilesCompute;

        [ResourcePath("Shaders/Core/Private/NRD/SIGMA/SIGMA_SmoothTiles")]
        public ComputeShader SIGMASmoothTilesCompute;

        [ResourcePath("Shaders/Core/Private/NRD/SIGMA/SIGMA_Copy")]
        public ComputeShader SIGMAShadowCopyCompute;

        [ResourcePath("Shaders/Core/Private/NRD/SIGMA/SIGMA_Shadow_Blur")]
        public ComputeShader SIGMAShadowPreBlurCompute;

        [ResourcePath("Shaders/Core/Private/NRD/SIGMA/SIGMA_Shadow_PostBlur")]
        public ComputeShader SIGMAShadowPostBlurCompute;

        [ResourcePath("Shaders/Core/Private/NRD/SIGMA/SIGMA_Shadow_TemporalStabilization")]
        public ComputeShader SIGMATemporalStabilizationCompute;

        [ResourcePath("Shaders/Core/Private/GPUDriven/GPUInstanceCulling")]
        public ComputeShader GPUInstanceCullingCompute;

        [ResourcePath("Shaders/Core/Private/GPUDriven/MeshletListBuild")]
        public ComputeShader MeshletListBuildCompute;

        [ResourcePath("Shaders/Core/Private/GPUDriven/GPUMeshletCulling")]
        public ComputeShader GPUMeshletCullingCompute;

        [ResourcePath("Shaders/Core/Private/GPUDriven/FixupVisibleMeshletIndirectDrawArgs")]
        public ComputeShader FixupVisibleMeshletIndirectDrawArgsCompute;

        [ResourcePath("Shaders/Core/Private/GPUDriven/GPUDrivenMeshletDebug")]
        public Shader GPUDrivenMeshletDebugShader;

        // Film Grain preset textures
        [ResourcePath("Texture/FilmGrain/Thin01.png")]
        public Texture2D FilmGrainThin1;

        [ResourcePath("Texture/FilmGrain/Thin02.png")]
        public Texture2D FilmGrainThin2;

        [ResourcePath("Texture/FilmGrain/Medium01.png")]
        public Texture2D FilmGrainMedium1;

        [ResourcePath("Texture/FilmGrain/Medium02.png")]
        public Texture2D FilmGrainMedium2;

        [ResourcePath("Texture/FilmGrain/Medium03.png")]
        public Texture2D FilmGrainMedium3;

        [ResourcePath("Texture/FilmGrain/Medium04.png")]
        public Texture2D FilmGrainMedium4;

        [ResourcePath("Texture/FilmGrain/Medium05.png")]
        public Texture2D FilmGrainMedium5;

        [ResourcePath("Texture/FilmGrain/Medium06.png")]
        public Texture2D FilmGrainMedium6;

        [ResourcePath("Texture/FilmGrain/Large01.png")]
        public Texture2D FilmGrainLarge01;

        [ResourcePath("Texture/FilmGrain/Large02.png")]
        public Texture2D FilmGrainLarge02;

        public ComputeShader ResolveAutoExposureCompute(VividRenderPipelineAsset pipelineAsset)
        {
            return pipelineAsset != null && pipelineAsset.AutoExposureImplementation == AutoExposureImplementationPath.HDRP
                ? AutoExposureHDRPCompute
                : AutoExposureCompute;
        }
    }
}
