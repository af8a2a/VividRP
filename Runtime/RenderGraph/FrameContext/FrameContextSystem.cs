using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class FrameContextSystem : CameraRelativeSystem<CameraTemporalData>
    {
        private static readonly FrameContextSystem s_Instance = new();
        public static event Action<ContextContainer, CommandBuffer> SubsystemPreRender;
        public static event Action<ContextContainer, CommandBuffer> SubsystemDispose;

            
        private static readonly int CameraWorldClipPlanesId = Shader.PropertyToID("unity_CameraWorldClipPlanes");
        private static readonly int FrustumPlanesId = Shader.PropertyToID("_FrustumPlanes");

        public static void Update(ContextContainer frameData, CommandBuffer cmd)
        {
            s_Instance.PurgeDestroyedCameras();

            var cameraData = frameData.Get<VividCameraData>();
            var temporalData = GetOrCreate(cameraData.camera);

            // 1. Advance temporal state
            temporalData.Update(cameraData);
            RTHandles.SetReferenceSize(cameraData.actualWidth, cameraData.actualHeight);

            // 2. Populate ContextItem for passes
            var vividTemporalData = frameData.GetOrCreate<VividTemporalData>();
            vividTemporalData.previousViewProjectionMatrix = temporalData.PreviousViewProjection;
            vividTemporalData.nonJitteredViewProjectionMatrix = temporalData.ViewProjection;
            vividTemporalData.previousViewMatrix = temporalData.PreviousViewMatrix;
            vividTemporalData.previousProjectionMatrix = temporalData.PreviousProjectionMatrix;
            vividTemporalData.jitter = temporalData.Jitter;
            vividTemporalData.previousJitter = temporalData.PreviousJitter;
            vividTemporalData.isFirstFrame = temporalData.IsFirstFrame;

            
            SubsystemPreRender?.Invoke(frameData, cmd);
            
            // SkyManager.Update(frameData, cmd);
            AutoExposureRuntimeManager.PrepareFrame(frameData);
            AutoExposureShaderBindings.BindFrameGlobals(cmd, frameData.Get<VividExposureData>());

            // 3. Build and set all shader globals in one place
            var shaderVariables = cameraData.BuildShaderVariables(temporalData);
            var skyData = frameData.GetOrCreate<VividSkyData>();
            SetShaderGlobals(cmd, cameraData, shaderVariables, temporalData, skyData);
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
            AutoExposureRuntimeManager.Clear();
#if VIVIDRP_DEBUG
            CameraShaderVariablesGlobalComparisonLogger.Reset();
#endif
        }

        private static void SetShaderGlobals(
            CommandBuffer cmd,
            VividCameraData cameraData,
            VividCameraData.ShaderVariables sv,
            CameraTemporalData temporalData,
            VividSkyData skyData)
        {
            var shaderVariablesGlobal = ShaderVariablesGlobal.Create(sv, temporalData, skyData);
#if VIVIDRP_DEBUG
            CameraShaderVariablesGlobalComparisonLogger.CaptureAndCompare(cameraData, shaderVariablesGlobal);
#endif
            ConstantBuffer.PushGlobal(cmd, shaderVariablesGlobal, ShaderVariablesGlobal.ConstantBufferShaderId);

            cmd.SetGlobalVectorArray(CameraWorldClipPlanesId, sv.cameraWorldClipPlanes);
            cmd.SetGlobalVectorArray(FrustumPlanesId, sv.frustumPlanes);
        }
    }
}
