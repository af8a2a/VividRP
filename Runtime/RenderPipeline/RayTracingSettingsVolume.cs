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
        Sphere
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
        public ClampedFloatParameter rayBias = new(0.001f, 0f, 1f);
        public ClampedFloatParameter distantRayBias = new(0.001f, 0f, 10f);
        public BoolParameter extendShadowCulling = new(false);
        public BoolParameter extendCameraCulling = new(false);
        public VividRTASBuildModeParameter buildMode = new(VividRTASBuildMode.Automatic);
        public VividRTASCullingModeParameter cullingMode = new(VividRTASCullingMode.ExtendedFrustum);
        public MinFloatParameter cullingDistance = new(1000f, 0f);
        public LayerMaskParameter layerMask = new(~0);
        public VividRayTracingModeMaskParameter rayTracingModeMask =
            new(RayTracingAccelerationStructure.RayTracingModeMask.Everything);
        public VividRayTracingBuildFlagsParameter buildFlagsStaticGeometries =
            new(RayTracingAccelerationStructureBuildFlags.None);
        public VividRayTracingBuildFlagsParameter buildFlagsDynamicGeometries =
            new(RayTracingAccelerationStructureBuildFlags.None);
        public BoolParameter enableCompaction = new(false);

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
                    || (layerMask != null && layerMask.overrideState)
                    || (rayTracingModeMask != null && rayTracingModeMask.overrideState)
                    || (buildFlagsStaticGeometries != null && buildFlagsStaticGeometries.overrideState)
                    || (buildFlagsDynamicGeometries != null && buildFlagsDynamicGeometries.overrideState)
                    || (enableCompaction != null && enableCompaction.overrideState));
        }
    }
}
