using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public sealed class VividRayTracingSettingsData : ContextItem
    {
        public bool supportsRayTracing;
        public VividRTASBuildMode buildMode;
        public VividRTASCullingMode cullingMode;
        public float cullingDistance;
        public bool extendShadowCulling;
        public bool extendCameraCulling;
        public float rayBias;
        public float distantRayBias;
        public LayerMask layerMask;
        public RayTracingAccelerationStructure.RayTracingModeMask rayTracingModeMask;
        public RayTracingAccelerationStructureBuildFlags buildFlagsStaticGeometries;
        public RayTracingAccelerationStructureBuildFlags buildFlagsDynamicGeometries;
        public bool enableCompaction;

        public override void Reset()
        {
            supportsRayTracing = false;
            buildMode = VividRTASBuildMode.Automatic;
            cullingMode = VividRTASCullingMode.ExtendedFrustum;
            cullingDistance = 1000f;
            extendShadowCulling = false;
            extendCameraCulling = false;
            rayBias = 0.001f;
            distantRayBias = 0.001f;
            layerMask = ~0;
            rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything;
            buildFlagsStaticGeometries = RayTracingAccelerationStructureBuildFlags.None;
            buildFlagsDynamicGeometries = RayTracingAccelerationStructureBuildFlags.None;
            enableCompaction = false;
        }
    }
}
