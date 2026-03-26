using System;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Runtime
{
    [Serializable]
    [VolumeComponentMenu("VividRP/GPU Driven/Settings")]
    public sealed class GPUDrivenSettingsVolume : VolumeComponent
    {
        public IntParameter forcedMeshLODNodeDepth =
            new(VividGPUDrivenDefaults.ForcedMeshLODNodeDepth);

        public MinFloatParameter meshLODErrorThreshold =
            new(VividGPUDrivenDefaults.MeshLODErrorThreshold, 0f);

        public bool IsActive()
        {
            return active
                && ((forcedMeshLODNodeDepth != null && forcedMeshLODNodeDepth.overrideState)
                    || (meshLODErrorThreshold != null && meshLODErrorThreshold.overrideState));
        }

        internal static GPUDrivenSettingsData ResolveSettings(GPUDrivenSettingsVolume volume)
        {
            int forcedMeshLODNodeDepth = VividGPUDrivenDefaults.ForcedMeshLODNodeDepth;
            float meshLODErrorThreshold = VividGPUDrivenDefaults.MeshLODErrorThreshold;

            if (volume == null || !volume.active)
            {
                return new GPUDrivenSettingsData(forcedMeshLODNodeDepth, meshLODErrorThreshold);
            }

            if (volume.forcedMeshLODNodeDepth != null && volume.forcedMeshLODNodeDepth.overrideState)
            {
                forcedMeshLODNodeDepth = volume.forcedMeshLODNodeDepth.value;
            }

            if (volume.meshLODErrorThreshold != null && volume.meshLODErrorThreshold.overrideState)
            {
                meshLODErrorThreshold = volume.meshLODErrorThreshold.value;
            }

            return new GPUDrivenSettingsData(
                forcedMeshLODNodeDepth,
                Mathf.Max(0f, meshLODErrorThreshold));
        }

        internal readonly struct GPUDrivenSettingsData
        {
            public GPUDrivenSettingsData(int forcedMeshLODNodeDepth, float meshLODErrorThreshold)
            {
                this.forcedMeshLODNodeDepth = forcedMeshLODNodeDepth;
                this.meshLODErrorThreshold = meshLODErrorThreshold;
            }

            public readonly int forcedMeshLODNodeDepth;
            public readonly float meshLODErrorThreshold;
        }
    }
}
