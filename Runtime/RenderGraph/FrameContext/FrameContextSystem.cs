using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class VividCameraShaderData : ContextItem
    {
        public ShaderVariablesGlobal shaderVariablesGlobal;
        public bool hasShaderVariablesGlobal;

        public override void Reset()
        {
            shaderVariablesGlobal = default;
            hasShaderVariablesGlobal = false;
        }
    }

    internal sealed class FrameContextSystem : CameraRelativeSystem<CameraTemporalData>
    {
        private static readonly FrameContextSystem s_Instance = new();
        public static event Action<ContextContainer, CommandBuffer> SubsystemPreRender;
        public static event Action<ContextContainer, CommandBuffer> SubsystemPostRender;
        public static event Action SubsystemDispose;

            
        private static readonly int CameraWorldClipPlanesId = Shader.PropertyToID("unity_CameraWorldClipPlanes");
        private static readonly int FrustumPlanesId = Shader.PropertyToID("_FrustumPlanes");

        public static void Update(ContextContainer frameData, CommandBuffer cmd)
        {
            using (RenderPassProfilingUtility.PrepareFrameContextPurgeDestroyedCamerasMarker.Auto())
            {
                s_Instance.PurgeDestroyedCameras();
            }

            VividCameraData cameraData;
            CameraTemporalData temporalData;
            using (RenderPassProfilingUtility.PrepareFrameContextResolveDataMarker.Auto())
            {
                cameraData = frameData.Get<VividCameraData>();
                temporalData = GetOrCreate(cameraData.camera);
            }

            // 1. Advance temporal state
            using (RenderPassProfilingUtility.PrepareFrameContextAdvanceTemporalMarker.Auto())
            {
                temporalData.Update(cameraData);
                RTHandles.SetReferenceSize(cameraData.actualWidth, cameraData.actualHeight);
            }

            // 2. Populate ContextItem for passes
            using (RenderPassProfilingUtility.PrepareFrameContextPopulateTemporalMarker.Auto())
            {
                var vividTemporalData = frameData.GetOrCreate<VividTemporalData>();
                vividTemporalData.previousViewProjectionMatrix = temporalData.PreviousViewProjection;
                vividTemporalData.nonJitteredViewProjectionMatrix = temporalData.ViewProjection;
                vividTemporalData.previousViewMatrix = temporalData.PreviousViewMatrix;
                vividTemporalData.previousProjectionMatrix = temporalData.PreviousProjectionMatrix;
                vividTemporalData.jitter = temporalData.Jitter;
                vividTemporalData.previousJitter = temporalData.PreviousJitter;
                vividTemporalData.isFirstFrame = temporalData.IsFirstFrame;
            }

            using (RenderPassProfilingUtility.PrepareFrameContextSubsystemPreRenderMarker.Auto())
            {
                SubsystemPreRender?.Invoke(frameData, cmd);
            }

            // 3. Build and set all shader globals in one place
            VividCameraData.ShaderVariables shaderVariables;
            VividSkyData skyData;
            using (RenderPassProfilingUtility.PrepareFrameContextBuildShaderVariablesMarker.Auto())
            {
                shaderVariables = cameraData.BuildShaderVariables(temporalData);
            }

            skyData = frameData.GetOrCreate<VividSkyData>();

            using (RenderPassProfilingUtility.PrepareFrameContextSetShaderGlobalsMarker.Auto())
            {
                SetShaderGlobals(cmd, frameData, cameraData, shaderVariables, temporalData, skyData);
            }

            using (RenderPassProfilingUtility.PrepareFrameContextAdaptiveProbeVolumeMarker.Auto())
            {
                VividAdaptiveProbeVolumeUtility.UpdatePerCamera(
                    VividRenderPipelineAsset.GetActiveAsset(),
                    cameraData.camera,
                    cmd,
                    cameraData.frameIndex);
            }

        }

        public static void ExecutePostRender(ContextContainer frameData, CommandBuffer cmd)
        {
            if (frameData == null || cmd == null)
                return;

            SubsystemPostRender?.Invoke(frameData, cmd);
        }

        public static CameraTemporalData GetOrCreate(Camera camera)
        {
            if (camera == null)
                return null;

            return s_Instance.GetOrCreateBase(camera);
        }

        public static void Clear()
        {
            s_Instance.Dispose();
            VividAutoExposureSystem.Deinitialize();
            SubsystemDispose?.Invoke();
#if VIVIDRP_DEBUG
            CameraShaderVariablesGlobalComparisonLogger.Reset();
#endif
        }

        private static void SetShaderGlobals(
            CommandBuffer cmd,
            ContextContainer frameData,
            VividCameraData cameraData,
            VividCameraData.ShaderVariables sv,
            CameraTemporalData temporalData,
            VividSkyData skyData)
        {
            var shaderVariablesGlobal = ShaderVariablesGlobal.Create(sv, temporalData, skyData);
            var cameraShaderData = frameData.GetOrCreate<VividCameraShaderData>();
            cameraShaderData.shaderVariablesGlobal = shaderVariablesGlobal;
            cameraShaderData.hasShaderVariablesGlobal = true;
#if VIVIDRP_DEBUG
            CameraShaderVariablesGlobalComparisonLogger.CaptureAndCompare(cameraData, shaderVariablesGlobal);
#endif
            ConstantBuffer.PushGlobal(cmd, shaderVariablesGlobal, ShaderVariablesGlobal.ConstantBufferShaderId);

            cmd.SetGlobalVectorArray(CameraWorldClipPlanesId, sv.cameraWorldClipPlanes);
            cmd.SetGlobalVectorArray(FrustumPlanesId, sv.frustumPlanes);
        }
    }
}
