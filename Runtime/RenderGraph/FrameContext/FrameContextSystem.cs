using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class FrameContextSystem : CameraRelativeSystem<CameraTemporalData>
    {
        private static readonly FrameContextSystem s_Instance = new();

        private static readonly int PrevViewProjMatrixId = Shader.PropertyToID("_PrevViewProjMatrix");
        private static readonly int NonJitteredViewProjMatrixId = Shader.PropertyToID("_NonJitteredViewProjMatrix");
        private static readonly int PrevViewMatrixId = Shader.PropertyToID("_PrevViewMatrix");
        private static readonly int PrevProjMatrixId = Shader.PropertyToID("_PrevProjMatrix");
        private static readonly int JitterParamsId = Shader.PropertyToID("_JitterParams");

        public static void Update(ContextContainer frameData, CommandBuffer cmd)
        {
            s_Instance.PurgeDestroyedCameras();

            var cameraData = frameData.Get<VividCameraData>();
            var temporalData = GetOrCreate(cameraData.camera);

            // 1. Advance temporal state
            temporalData.Update(cameraData);

            // 2. Populate ContextItem for passes
            var vividTemporalData = frameData.GetOrCreate<VividTemporalData>();
            vividTemporalData.previousViewProjectionMatrix = temporalData.PreviousViewProjection;
            vividTemporalData.nonJitteredViewProjectionMatrix = temporalData.ViewProjection;
            vividTemporalData.previousViewMatrix = temporalData.PreviousViewMatrix;
            vividTemporalData.previousProjectionMatrix = temporalData.PreviousProjectionMatrix;
            vividTemporalData.jitter = temporalData.Jitter;
            vividTemporalData.previousJitter = temporalData.PreviousJitter;
            vividTemporalData.isFirstFrame = temporalData.IsFirstFrame;

            // 3. Set temporal shader globals once, before any pass executes
            SetTemporalGlobals(cmd, temporalData);
        }

        public new static CameraTemporalData GetOrCreate(Camera camera)
        {
            if (camera == null)
                return null;

            return s_Instance.GetOrCreateBase(camera);
        }

        public static void Clear()
        {
            s_Instance.Dispose();
        }

        private static void SetTemporalGlobals(CommandBuffer cmd, CameraTemporalData data)
        {
            cmd.SetGlobalMatrix(PrevViewProjMatrixId, data.PreviousViewProjection);
            cmd.SetGlobalMatrix(NonJitteredViewProjMatrixId, data.ViewProjection);
            cmd.SetGlobalMatrix(PrevViewMatrixId, data.PreviousViewMatrix);
            cmd.SetGlobalMatrix(PrevProjMatrixId, data.PreviousProjectionMatrix);
            cmd.SetGlobalVector(JitterParamsId, new Vector4(
                data.Jitter.x, data.Jitter.y,
                data.PreviousJitter.x, data.PreviousJitter.y));
        }
    }
}
