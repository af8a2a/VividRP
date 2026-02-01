using System;
using UnityEngine.Serialization;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    public enum GlobalIlluminationTechnique
    {
        Disabled = 0,
        ReferencedPathTracing,
        PrecomputedRadianceTransfer,
        SurfaceCache
    }

    [Serializable]
    public sealed class GlobalIlluminationTechniqueParameter : VolumeParameter<GlobalIlluminationTechnique>
    {
        public GlobalIlluminationTechniqueParameter(GlobalIlluminationTechnique value, bool overrideState = false) : base(value, overrideState)
        {
        }
    }


    /// <summary>
    /// A volume component that holds settings for the global illumination (screen space and ray traced).
    /// </summary>
    [Serializable, VolumeComponentMenu("Lighting/Global Illumination")]
    public sealed partial class GlobalIllumination : VolumeComponent
    {
        public GlobalIlluminationTechniqueParameter technique = new GlobalIlluminationTechniqueParameter(GlobalIlluminationTechnique.Disabled);
        
        
        
        
        // bool UsesQualityMode()
        // {
        //     // The default value is set to quality. So we should be in quality if not overriden or we have an override set to quality
        //     return (tracing.overrideState && tracing == RayCastingMode.RayTracing &&
        //             (!mode.overrideState || (mode.overrideState && mode == RayTracingMode.Quality)));
        // }
        //
        // #region General
        //
        // /// <summary>
        // /// Enable screen space global illumination.
        // /// </summary>
        // [Tooltip("Enable screen space global illumination.")]
        // public BoolParameter enable = new BoolParameter(false, BoolParameter.DisplayType.EnumPopup);
        //
        // /// <summary>
        // /// </summary>
        // [Tooltip(
        //     "Controls the casting technique used to evaluate the effect. Ray marching uses a ray-marched screen-space solution, Ray tracing uses a hardware accelerated world-space solution. Mixed uses first Ray marching, then Ray tracing if it fails to intersect on-screen geometry.")]
        // public RayCastingModeParameter tracing = new RayCastingModeParameter(RayCastingMode.RayMarching);
        //
        // /// <summary>
        // /// Controls the fallback hierarchy for indirect diffuse in case the ray misses.
        // /// </summary>
        // [Tooltip("Controls the fallback hierarchy for indirect diffuse in case the ray misses.")]
        // [FormerlySerializedAs("fallbackHierarchy")]
        // [AdditionalProperty]
        // public RayMarchingFallbackHierarchyParameter rayMiss = new RayMarchingFallbackHierarchyParameter(RayMarchingFallbackHierarchy.ReflectionProbesAndSky);
        //
        // /// <summary>
        // /// Controls the fallback hierarchy for indirect diffuse in case the ray misses.
        // /// </summary>
        // [Tooltip(
        //     "Rendering Layer Mask to use when sampling the Adaptive Probe Volumes.\nThis is only used if Rendering Layers Masks are enabled for the active Baking Set.")]
        // [AdditionalProperty]
        // public RenderingLayerMaskParameter adaptiveProbeVolumesLayerMask =
        //     new RenderingLayerMaskParameter(UnityEngine.RenderingLayerMask.defaultRenderingLayerMask);
        //
        // #endregion
        //
        //
        // #region RayTracing
        //
        // /// <summary>
        // /// Controls the fallback hierarchy for lighting the last bounce.
        // /// </summary>
        // [Tooltip("Controls the fallback hierarchy for lighting the last bounce.")] [AdditionalProperty]
        // public RayMarchingFallbackHierarchyParameter lastBounceFallbackHierarchy =
        //     new RayMarchingFallbackHierarchyParameter(RayMarchingFallbackHierarchy.ReflectionProbesAndSky);
        //
        // /// <summary>
        // /// Controls the dimmer applied to the ambient and legacy light probes.
        // /// </summary>
        // [Tooltip("Controls the dimmer applied to the ambient and legacy light probes.")] [AdditionalProperty]
        // public ClampedFloatParameter ambientProbeDimmer = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);
        //
        // /// <summary>
        // /// Defines the layers that GI should include.
        // /// </summary>
        // [Tooltip("Defines the layers that GI should include.")]
        // public LayerMaskParameter layerMask = new LayerMaskParameter(-1);
        //
        // /// <summary>
        // /// The LOD Bias that HDRP adds to texture sampling in the global illumination.
        // /// </summary>
        // [Tooltip(
        //     "The LOD Bias that HDRP adds to texture sampling in the global illumination. A higher value increases performance and makes denoising easier, but it might reduce visual fidelity.")]
        // public ClampedFloatParameter textureLodBias = new ClampedFloatParameter(7.0f, 0.0f, 7.0f);
        //
        // /// <summary>
        // /// Controls the length of GI rays in meters.
        // /// </summary>
        // public float rayLength
        // {
        //     get { return m_RayLength.value; }
        //     set { m_RayLength.value = value; }
        // }
        //
        // [SerializeField, FormerlySerializedAs("rayLength")]
        // private MinFloatParameter m_RayLength = new MinFloatParameter(50.0f, 0.01f);
        //
        // /// <summary>
        // /// Controls the clamp of intensity.
        // /// </summary>
        // public float clampValue
        // {
        //     get { return m_ClampValue.value; }
        //     set { m_ClampValue.value = value; }
        // }
        //
        // [SerializeField, FormerlySerializedAs("clampValue")] [Tooltip("Controls the clamp of intensity.")]
        // private MinFloatParameter m_ClampValue = new MinFloatParameter(100.0f, 0.001f);
        //
        // /// <summary>
        // /// Controls which version of the effect should be used.
        // /// </summary>
        // [Tooltip("Controls which version of the effect should be used.")]
        // public RayTracingModeParameter mode = new RayTracingModeParameter(RayTracingMode.Quality);
        //
        // // Performance
        // /// <summary>
        // /// Defines if the effect should be evaluated at full resolution.
        // /// </summary>
        // public bool fullResolution
        // {
        //     get { return m_FullResolution.value; }
        //     set { m_FullResolution.value = value; }
        // }
        //
        // [SerializeField, FormerlySerializedAs("fullResolution")] [Tooltip("Full Resolution")]
        // private BoolParameter m_FullResolution = new BoolParameter(false);
        //
        // // Quality
        // /// <summary>
        // /// Number of samples for evaluating the effect.
        // /// </summary>
        // [Tooltip("Number of samples for GI.")] public ClampedIntParameter sampleCount = new ClampedIntParameter(2, 1, 32);
        //
        // /// <summary>
        // /// Number of bounces for evaluating the effect.
        // /// </summary>
        // [Tooltip("Number of bounces for GI.")] public ClampedIntParameter bounceCount = new ClampedIntParameter(1, 1, 8);
        //
        // // Filtering
        // /// <summary>
        // /// Defines if the ray traced global illumination should be denoised.
        // /// </summary>
        // public bool denoise
        // {
        //     get { return m_Denoise.value; }
        //     set { m_Denoise.value = value; }
        // }
        //
        // [SerializeField, FormerlySerializedAs("denoise")] [Tooltip("Denoise the ray-traced GI.")]
        // private BoolParameter m_Denoise = new BoolParameter(true);
        //
        // /// <summary>
        // /// Defines if the denoiser should be evaluated at half resolution.
        // /// </summary>
        // public bool halfResolutionDenoiser
        // {
        //     get { return m_HalfResolutionDenoiser.value; }
        //     set { m_HalfResolutionDenoiser.value = value; }
        // }
        //
        // [SerializeField, FormerlySerializedAs("halfResolutionDenoiser")] [Tooltip("Use a half resolution denoiser.")]
        // private BoolParameter m_HalfResolutionDenoiser = new BoolParameter(false);
        //
        // /// <summary>
        // /// Controls the radius of the global illumination denoiser (First Pass).
        // /// </summary>
        // public float denoiserRadius
        // {
        //     get { return m_DenoiserRadius.value; }
        //     set { m_DenoiserRadius.value = value; }
        // }
        //
        // [SerializeField, FormerlySerializedAs("denoiserRadius")] [Tooltip("Controls the radius of the GI denoiser (First Pass).")]
        // private ClampedFloatParameter m_DenoiserRadius = new ClampedFloatParameter(0.6f, 0.001f, 1.0f);
        //
        // /// <summary>
        // /// Defines if the second denoising pass should be enabled.
        // /// </summary>
        // public bool secondDenoiserPass
        // {
        //     get { return m_SecondDenoiserPass.value; }
        //     set { m_SecondDenoiserPass.value = value; }
        // }
        //
        // [SerializeField, FormerlySerializedAs("secondDenoiserPass")] [Tooltip("Enable second denoising pass.")]
        // private BoolParameter m_SecondDenoiserPass = new BoolParameter(true);
        //
        // /// <summary>
        // /// Controls the number of steps used for the mixed tracing
        // /// </summary>
        // public int maxMixedRaySteps
        // {
        //     get { return m_MaxMixedRaySteps.value; }
        //     set { m_MaxMixedRaySteps.value = value; }
        // }
        //
        // [SerializeField] [Tooltip("Controls the number of steps HDRP uses for mixed tracing.")]
        // private MinIntParameter m_MaxMixedRaySteps = new MinIntParameter(48, 0);
        //
        // /// <summary>
        // /// When enabled, global illumination generated by moving objects will not be accumulated, generating less ghosting but introducing additional noise.
        // /// </summary>
        // [AdditionalProperty]
        // [Tooltip(
        //     "When enabled, global illumination generated by moving objects will not be accumulated, generating less ghosting but introducing additional noise.")]
        // public BoolParameter receiverMotionRejection = new BoolParameter(true);
        //
        // #endregion
        //
        // internal static bool RayTracingActive(GlobalIllumination volume)
        // {
        //     return volume.tracing.value != RayCastingMode.RayMarching;
        // }
    }
}