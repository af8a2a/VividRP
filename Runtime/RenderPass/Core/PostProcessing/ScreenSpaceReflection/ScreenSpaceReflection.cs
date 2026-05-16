using System;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Runtime
{
    [Serializable]
    [VolumeComponentMenu("Post-processing/Screen Space Reflection")]
    public sealed class ScreenSpaceReflection : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Whether screen space reflections are enabled.")]
        public BoolParameter enabled = new(false);

        [Tooltip("Controls which screen space reflection implementation is executed.")]
        public EnumParameter<ScreenSpaceReflectionExecutionPath> executionPath =
            new(ScreenSpaceReflectionExecutionPath.Vivid);

        [Tooltip("Scales the traced reflection contribution.")]
        public ClampedFloatParameter intensity = new(1.0f, 0.0f, 2.0f);

        [Tooltip("Smoothness at which screen space reflections reach full strength.")]
        public ClampedFloatParameter minSmoothness = new(0.9f, 0.0f, 1.0f);

        [Tooltip("Smoothness at which screen space reflections start fading in.")]
        public ClampedFloatParameter smoothnessFadeStart = new(0.9f, 0.0f, 1.0f);

        [Tooltip("Allows screen-space rays to resolve against visible sky pixels.")]
        public BoolParameter reflectSky = new(true);

        [Tooltip("Clamps exposed reflection intensity before deferred lighting accumulation.")]
        public MinFloatParameter clampValue = new(100.0f, 0.001f);

        [Tooltip("Typical thickness of objects that reflection rays may pass behind.")]
        public ClampedFloatParameter depthBufferThickness = new(0.01f, 0.0001f, 1.0f);

        [Tooltip("Distance over which reflections fade near screen edges.")]
        public ClampedFloatParameter screenFadeDistance = new(0.1f, 0.0001f, 1.0f);

        [Tooltip("Maximum HiZ ray marching steps.")]
        public ClampedIntParameter rayMaxIterations = new(32, 1, 128);

        [Tooltip("Scales the ReBLUR filter radius used by DXR reflections.")]
        public ClampedFloatParameter reBlurDenoiserRadius = new(1.0f, 0.0f, 1.0f);

        [Tooltip("Controls ReBLUR temporal stabilization strength for DXR reflections.")]
        public ClampedFloatParameter reBlurAntiFlickeringStrength = new(0.5f, 0.0f, 1.0f);

        public bool IsActive()
        {
            return enabled.value && intensity.value > 0.0f && rayMaxIterations.value > 0;
        }
    }
}
