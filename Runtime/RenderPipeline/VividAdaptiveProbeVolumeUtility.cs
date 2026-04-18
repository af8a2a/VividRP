using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal static class VividAdaptiveProbeVolumeUtility
    {
        private const ProbeVolumeTextureMemoryBudget DefaultMemoryBudget = ProbeVolumeTextureMemoryBudget.MemoryBudgetMedium;
        private const ProbeVolumeBlendingTextureMemoryBudget DefaultBlendingMemoryBudget =
            ProbeVolumeBlendingTextureMemoryBudget.MemoryBudgetLow;

        private static readonly int s_EnableProbeVolumesId = Shader.PropertyToID("_EnableProbeVolumes");

        internal static void Initialize(VividRenderPipelineAsset asset)
        {
            bool supportProbeVolume = asset != null && asset.SupportProbeVolume;

            SupportedRenderingFeatures.active.overridesLightProbeSystem = supportProbeVolume;
            SupportedRenderingFeatures.active.rendererProbes = !supportProbeVolume;
            SupportedRenderingFeatures.active.skyOcclusion = supportProbeVolume;

            if (!supportProbeVolume)
                return;

#pragma warning disable 618
            ProbeVolumeSceneData sceneData = VividRenderPipelineGlobalSettings.instance?.GetOrCreateAPVSceneData();
#pragma warning restore 618

            ProbeReferenceVolume.instance.Initialize(new ProbeVolumeSystemParameters
            {
                memoryBudget = DefaultMemoryBudget,
                blendingMemoryBudget = DefaultBlendingMemoryBudget,
                shBands = asset.ProbeVolumeSHBands,
                supportScenarios = false,
                supportScenarioBlending = false,
                supportGPUStreaming = false,
                supportDiskStreaming = false,
#pragma warning disable 618
                sceneData = sceneData,
#pragma warning restore 618
            });
        }

        internal static void Cleanup(VividRenderPipelineAsset asset)
        {
            if (asset == null || !asset.SupportProbeVolume)
                return;

            ProbeReferenceVolume.instance.Cleanup();
        }

        internal static void UpdatePerCamera(VividRenderPipelineAsset asset, Camera camera, CommandBuffer cmd, int frameIndex)
        {
            bool supportProbeVolume = asset != null && asset.SupportProbeVolume;

            CoreUtils.SetKeyword(
                cmd,
                "PROBE_VOLUMES_L1",
                supportProbeVolume && asset.ProbeVolumeSHBands == ProbeVolumeSHBands.SphericalHarmonicsL1);
            CoreUtils.SetKeyword(
                cmd,
                "PROBE_VOLUMES_L2",
                supportProbeVolume && asset.ProbeVolumeSHBands == ProbeVolumeSHBands.SphericalHarmonicsL2);

            if (!supportProbeVolume)
            {
                ProbeReferenceVolume.instance.SetEnableStateFromSRP(false);
                cmd.SetGlobalInt(s_EnableProbeVolumesId, 0);
                return;
            }

            ProbeReferenceVolume probeReferenceVolume = ProbeReferenceVolume.instance;
            probeReferenceVolume.SetEnableStateFromSRP(true);

            bool enableProbeVolumes = false;
            if (probeReferenceVolume.isInitialized)
            {
                probeReferenceVolume.PerformPendingOperations();

                ProbeVolumesOptions probeVolumeOptions = VividVolumeManagerUtility.GetProbeVolumesOptions();

                if (camera != null
                    && camera.cameraType != CameraType.Reflection
                    && camera.cameraType != CameraType.Preview)
                {
                    if (probeVolumeOptions != null)
                        probeReferenceVolume.UpdateCellStreaming(cmd, camera, probeVolumeOptions);
                    else
                        probeReferenceVolume.UpdateCellStreaming(cmd, camera);
                }

                if (probeVolumeOptions != null)
                {
                    enableProbeVolumes = probeReferenceVolume.UpdateShaderVariablesProbeVolumes(
                        cmd,
                        probeVolumeOptions,
                        frameIndex,
                        false);
                }
            }

            probeReferenceVolume.BindAPVRuntimeResources(cmd, enableProbeVolumes);
            cmd.SetGlobalInt(s_EnableProbeVolumesId, enableProbeVolumes ? 1 : 0);
        }
    }
}
