using System.Runtime.InteropServices;
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
        public float minSolidAngle;
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
            minSolidAngle = 4f;
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

    [StructLayout(LayoutKind.Sequential)]
    internal struct ShaderVariablesRayTracing
    {
        public float _RayTracingRayBias;
        public float _RayTracingDistantRayBias;
        public float _RayTracingMinSolidAngle;
        public float _RayTracingPadding0;
    }

    internal static class ShaderVariablesRayTracingUtility
    {
        internal static readonly int ConstantBufferShaderId = Shader.PropertyToID("ShaderVariablesRayTracing");

        internal static ShaderVariablesRayTracing Create(VividRayTracingSettingsData settings)
        {
            return new ShaderVariablesRayTracing
            {
                _RayTracingRayBias = Mathf.Max(0f, settings != null ? settings.rayBias : 0f),
                _RayTracingDistantRayBias = Mathf.Max(0f, settings != null ? settings.distantRayBias : 0f),
                _RayTracingMinSolidAngle = Mathf.Max(0f, settings != null ? settings.minSolidAngle : 0f),
                _RayTracingPadding0 = 0f,
            };
        }

        internal static void OverrideBiases(
            ref ShaderVariablesRayTracing shaderVariables,
            float rayBias,
            float distantRayBias)
        {
            shaderVariables._RayTracingRayBias = Mathf.Max(0f, rayBias);
            shaderVariables._RayTracingDistantRayBias = Mathf.Max(0f, distantRayBias);
        }
    }
}
