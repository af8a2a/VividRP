using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public enum ReferencedPathTracingEnvironmentSamplingMode
    {
        BsdfOnly = 0,
        ImportanceSampling = 1,
        UniformSphere = 2
    }

    public enum ReferencedPathTracingEnvironmentDebugMode
    {
        Combined = 0,
        EnvironmentOnly = 1,
        PrimaryBackgroundOnly = 2,
        IndirectMissOnly = 3
    }

    [Serializable]
    public sealed class ReferencedPathTracingEnvironmentSamplingModeParameter
        : VolumeParameter<ReferencedPathTracingEnvironmentSamplingMode>
    {
        public ReferencedPathTracingEnvironmentSamplingModeParameter(
            ReferencedPathTracingEnvironmentSamplingMode value,
            bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class ReferencedPathTracingEnvironmentDebugModeParameter
        : VolumeParameter<ReferencedPathTracingEnvironmentDebugMode>
    {
        public ReferencedPathTracingEnvironmentDebugModeParameter(
            ReferencedPathTracingEnvironmentDebugMode value,
            bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    [VolumeComponentMenu("VividRP/Path Tracing/Reference Path Tracing")]
    public sealed class ReferencedPathTracingSettingsVolume : VolumeComponent
    {
        [Tooltip("Allows the active HDRI Sky to contribute scene-linear environment lighting.")]
        public BoolParameter environmentLighting = new(true);

        [Tooltip(
            "Allows primary camera rays to see the active HDRI Sky. " +
            "This does not disable environment lighting.")]
        public BoolParameter environmentCameraVisible = new(true);

        [Tooltip(
            "Selects the environment-light proposal. BSDF Only disables environment NEE, " +
            "Importance Sampling uses the HDRI distribution, and Uniform Sphere is a validation mode.")]
        public ReferencedPathTracingEnvironmentSamplingModeParameter environmentSamplingMode =
            new(ReferencedPathTracingEnvironmentSamplingMode.ImportanceSampling);

        [Tooltip(
            "Selects the reference environment contribution shown in the resolved path-tracing output. " +
            "The physical AOVs remain unchanged.")]
        public ReferencedPathTracingEnvironmentDebugModeParameter environmentDebugMode =
            new(ReferencedPathTracingEnvironmentDebugMode.Combined);

        protected override void OnEnable()
        {
            environmentLighting ??= new BoolParameter(true);
            environmentCameraVisible ??= new BoolParameter(true);
            environmentSamplingMode ??=
                new ReferencedPathTracingEnvironmentSamplingModeParameter(
                    ReferencedPathTracingEnvironmentSamplingMode.ImportanceSampling);
            environmentDebugMode ??=
                new ReferencedPathTracingEnvironmentDebugModeParameter(
                    ReferencedPathTracingEnvironmentDebugMode.Combined);
            base.OnEnable();
        }
    }
}
