using UnityEngine;

namespace VividRP.Runtime
{
    internal sealed class CameraTemporalData : CameraRelativeState
    {
        // Current frame (non-jittered)
        public Matrix4x4 ViewMatrix = Matrix4x4.identity;
        public Matrix4x4 ProjectionMatrix = Matrix4x4.identity;
        public Matrix4x4 ViewProjection = Matrix4x4.identity;
        public Vector2 Jitter;

        // Previous frame
        public Matrix4x4 PreviousViewMatrix = Matrix4x4.identity;
        public Matrix4x4 PreviousProjectionMatrix = Matrix4x4.identity;
        public Matrix4x4 PreviousViewProjection = Matrix4x4.identity;
        public Vector2 PreviousJitter;

        // Frame tracking
        public int LastFrameIndex = -1;
        public float LastAspectRatio = -1f;

        public bool IsFirstFrame => LastFrameIndex < 0;

        public void Update(VividCameraData cameraData)
        {
            if (cameraData?.camera == null)
            {
                Reset();
                return;
            }

            var frameIndex = ResolveFrameIndex(cameraData);
            var aspectRatio = ResolveAspectRatio(cameraData);

            var gpuProjNoJitter = cameraData.GetGPUProjectionMatrixNoJitter();
            var view = cameraData.GetViewMatrix();
            var currentVP = gpuProjNoJitter * view;
            var currentJitter = cameraData.GetJitter();

            var hasValidHistory = LastFrameIndex >= 0
                && Mathf.Abs(LastAspectRatio - aspectRatio) < 0.0001f;

            if (!hasValidHistory)
            {
                // First frame or aspect ratio changed — no valid history
                PreviousViewMatrix = view;
                PreviousProjectionMatrix = gpuProjNoJitter;
                PreviousViewProjection = currentVP;
                PreviousJitter = currentJitter;
            }
            else if (LastFrameIndex != frameIndex)
            {
                // New frame — advance history
                PreviousViewMatrix = ViewMatrix;
                PreviousProjectionMatrix = ProjectionMatrix;
                PreviousViewProjection = ViewProjection;
                PreviousJitter = Jitter;
            }
            // Same frame (e.g. multiple cameras) — previous stays, current updates

            ViewMatrix = view;
            ProjectionMatrix = gpuProjNoJitter;
            ViewProjection = currentVP;
            Jitter = currentJitter;

            LastFrameIndex = frameIndex;
            LastAspectRatio = aspectRatio;
        }

        public override void Dispose()
        {
            Reset();
        }

        public void Reset()
        {
            ViewMatrix = Matrix4x4.identity;
            ProjectionMatrix = Matrix4x4.identity;
            ViewProjection = Matrix4x4.identity;
            Jitter = Vector2.zero;
            PreviousViewMatrix = Matrix4x4.identity;
            PreviousProjectionMatrix = Matrix4x4.identity;
            PreviousViewProjection = Matrix4x4.identity;
            PreviousJitter = Vector2.zero;
            LastFrameIndex = -1;
            LastAspectRatio = -1f;
        }

        private static int ResolveFrameIndex(VividCameraData cameraData)
        {
            return cameraData != null && cameraData.frameIndex >= 0
                ? cameraData.frameIndex
                : Time.frameCount;
        }

        private static float ResolveAspectRatio(VividCameraData cameraData)
        {
            var currentCamera = cameraData?.camera;
            if (currentCamera != null && currentCamera.aspect > 0f)
                return currentCamera.aspect;

            var width = cameraData != null && cameraData.actualWidth > 0
                ? cameraData.actualWidth
                : cameraData?.pixelWidth ?? 0;
            var height = cameraData != null && cameraData.actualHeight > 0
                ? cameraData.actualHeight
                : cameraData?.pixelHeight ?? 0;
            if (width > 0 && height > 0)
                return width / (float)height;

            return 1f;
        }
    }
}
