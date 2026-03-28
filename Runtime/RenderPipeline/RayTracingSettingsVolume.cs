using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public enum VividRTASBuildMode
    {
        Automatic,
        Manual
    }

    public enum VividRTASCullingMode
    {
        ExtendedFrustum,
        Sphere,
        SolidAngle
    }

    [Serializable]
    public sealed class VividRTASBuildModeParameter : VolumeParameter<VividRTASBuildMode>
    {
        public VividRTASBuildModeParameter(VividRTASBuildMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class VividRTASCullingModeParameter : VolumeParameter<VividRTASCullingMode>
    {
        public VividRTASCullingModeParameter(VividRTASCullingMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class VividRayTracingModeMaskParameter : VolumeParameter<RayTracingAccelerationStructure.RayTracingModeMask>
    {
        public VividRayTracingModeMaskParameter(
            RayTracingAccelerationStructure.RayTracingModeMask value,
            bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class VividRayTracingBuildFlagsParameter : VolumeParameter<RayTracingAccelerationStructureBuildFlags>
    {
        public VividRayTracingBuildFlagsParameter(
            RayTracingAccelerationStructureBuildFlags value,
            bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    [VolumeComponentMenu("VividRP/Ray Tracing/Settings")]
    public sealed class RayTracingSettingsVolume : VolumeComponent
    {
        public const float DefaultSigmaDenoisingRange = 500000.0f;
        public const float DefaultSigmaPlaneDistanceSensitivity = 0.02f;
        public const int DefaultSigmaMaxStabilizedFrameNum = 5;
        public const int MaxSigmaStabilizedFrameNum = 7;
        public const bool DefaultSigmaUseNativePluginConstantBuffer = false;

        public ClampedFloatParameter rayBias = new(0.001f, 0f, 1f);
        public ClampedFloatParameter distantRayBias = new(0.001f, 0f, 10f);
        public BoolParameter extendShadowCulling = new(false);
        public BoolParameter extendCameraCulling = new(false);
        public VividRTASBuildModeParameter buildMode = new(VividRTASBuildMode.Automatic);
        public VividRTASCullingModeParameter cullingMode = new(VividRTASCullingMode.ExtendedFrustum);
        public MinFloatParameter cullingDistance = new(1000f, 0f);
        public ClampedFloatParameter minSolidAngle = new(4f, 0.01f, 180f);
        public LayerMaskParameter layerMask = new(~0);
        public VividRayTracingModeMaskParameter rayTracingModeMask =
            new(RayTracingAccelerationStructure.RayTracingModeMask.Everything);
        public VividRayTracingBuildFlagsParameter buildFlagsStaticGeometries =
            new(RayTracingAccelerationStructureBuildFlags.None);
        public VividRayTracingBuildFlagsParameter buildFlagsDynamicGeometries =
            new(RayTracingAccelerationStructureBuildFlags.None);
        public BoolParameter enableCompaction = new(false);
        public BoolParameter sigmaUseNativePluginConstantBuffer = new(DefaultSigmaUseNativePluginConstantBuffer);
        public MinFloatParameter sigmaDenoisingRange = new(DefaultSigmaDenoisingRange, 0f);
        public ClampedFloatParameter sigmaPlaneDistanceSensitivity = new(DefaultSigmaPlaneDistanceSensitivity, 0f, 1f);
        public ClampedIntParameter sigmaMaxStabilizedFrameNum =
            new(DefaultSigmaMaxStabilizedFrameNum, 0, MaxSigmaStabilizedFrameNum);

        public bool IsActive()
        {
            return active
                && ((rayBias != null && rayBias.overrideState)
                    || (distantRayBias != null && distantRayBias.overrideState)
                    || (extendShadowCulling != null && extendShadowCulling.overrideState)
                    || (extendCameraCulling != null && extendCameraCulling.overrideState)
                    || (buildMode != null && buildMode.overrideState)
                    || (cullingMode != null && cullingMode.overrideState)
                    || (cullingDistance != null && cullingDistance.overrideState)
                    || (minSolidAngle != null && minSolidAngle.overrideState)
                    || (layerMask != null && layerMask.overrideState)
                    || (rayTracingModeMask != null && rayTracingModeMask.overrideState)
                    || (buildFlagsStaticGeometries != null && buildFlagsStaticGeometries.overrideState)
                    || (buildFlagsDynamicGeometries != null && buildFlagsDynamicGeometries.overrideState)
                    || (enableCompaction != null && enableCompaction.overrideState)
                    || (sigmaUseNativePluginConstantBuffer != null && sigmaUseNativePluginConstantBuffer.overrideState)
                    || (sigmaDenoisingRange != null && sigmaDenoisingRange.overrideState)
                    || (sigmaPlaneDistanceSensitivity != null && sigmaPlaneDistanceSensitivity.overrideState)
                    || (sigmaMaxStabilizedFrameNum != null && sigmaMaxStabilizedFrameNum.overrideState));
        }
    }
}
