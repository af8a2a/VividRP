using System;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Runtime
{
    [Serializable]
    [VolumeComponentMenu("VividRP/GPU Driven Settings")]
    public sealed class GPUDrivenSettingsVolume : VolumeComponent
    {
        public ClampedIntParameter forcedMeshLODNodeDepth =
            new(0,VividGPUDrivenDefaults.ForcedMeshLODNodeDepth,32);

        [Tooltip("Maximum projected mesh LOD error in pixels (shadow texels in clipmap mode). Higher values allow coarser geometry.")]
        public MinFloatParameter meshLODPixelError =
            new(VividGPUDrivenDefaults.MeshLODErrorThreshold, 0f);

        [Obsolete("Use meshLODPixelError. The legacy heuristic threshold has been replaced by a pixel budget.")]
        public MinFloatParameter meshLODErrorThreshold
        {
            get => meshLODPixelError;
            set => meshLODPixelError = value;
        }

        public bool IsActive()
        {
            return active
                && ((forcedMeshLODNodeDepth != null && forcedMeshLODNodeDepth.overrideState)
                    || (meshLODPixelError != null && meshLODPixelError.overrideState));
        }

        internal static GPUDrivenSettingsData ResolveSettings(GPUDrivenSettingsVolume volume)
        {
            int forcedMeshLODNodeDepth = VividGPUDrivenDefaults.ForcedMeshLODNodeDepth;
            float meshLODPixelError = VividGPUDrivenDefaults.MeshLODErrorThreshold;

            if (volume == null || !volume.active)
            {
                return new GPUDrivenSettingsData(forcedMeshLODNodeDepth, meshLODPixelError);
            }

            if (volume.forcedMeshLODNodeDepth != null && volume.forcedMeshLODNodeDepth.overrideState)
            {
                forcedMeshLODNodeDepth = volume.forcedMeshLODNodeDepth.value;
            }

            if (volume.meshLODPixelError != null && volume.meshLODPixelError.overrideState)
            {
                meshLODPixelError = volume.meshLODPixelError.value;
            }

            return new GPUDrivenSettingsData(
                forcedMeshLODNodeDepth,
                Mathf.Max(0f, meshLODPixelError));
        }

        internal readonly struct GPUDrivenSettingsData
        {
            public GPUDrivenSettingsData(int forcedMeshLODNodeDepth, float meshLODPixelError)
            {
                this.forcedMeshLODNodeDepth = forcedMeshLODNodeDepth;
                this.meshLODPixelError = meshLODPixelError;
            }

            public readonly int forcedMeshLODNodeDepth;
            public readonly float meshLODPixelError;
        }
    }
}
