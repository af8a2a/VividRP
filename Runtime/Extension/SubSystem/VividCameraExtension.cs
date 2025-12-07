using Unity.Mathematics;

namespace UnityEngine.Rendering.Universal
{
    public class VividCameraExtension : CameraRelatedSystem<VividCameraExtension>
    {
        private Matrix4x4 _camVPMatrix;
        private Matrix4x4 _prevCamVPMatrix;
        private Matrix4x4 _camVMatrix;
        private Matrix4x4 _camPMatrix;

        private Matrix4x4 _prevCamVMatrix;
        private Matrix4x4 _prevCamPMatrix;


        private int previousWidth;
        private int previousHeight;


        private Vector2 _previousJitter;
        private Vector2 _jitter;


        public Matrix4x4 gpuViewProjectionMatrix => _camVPMatrix;

        public Matrix4x4 gpuProjectionMatrix => _camPMatrix;
        public Matrix4x4 previousGPUProjectionMatrix => _prevCamPMatrix;

        public Matrix4x4 previousGPUViewProjectionMatrix => _prevCamVPMatrix;
        public Matrix4x4 previousViewMatrix => _prevCamVMatrix;


        public Vector2 previousJitter => _previousJitter;
        public Vector2 jitter => _jitter;


        public Frustum frustum;
        public Frustum previousfrustum;


        protected override void Initialize(Camera camera)
        {
            _camVMatrix = camera.worldToCameraMatrix;
            _prevCamVMatrix = _camVMatrix;
            _camPMatrix = GL.GetGPUProjectionMatrix(camera.nonJitteredProjectionMatrix, true);
            _prevCamPMatrix = _camPMatrix;

            _camVPMatrix = _camPMatrix * camera.cameraToWorldMatrix;
            _prevCamVPMatrix = _camVPMatrix;
            previousWidth = camera.scaledPixelWidth;


            _jitter = (Sequence.Halton2D((uint)Time.frameCount) - 0.5f) / new float2(camera.scaledPixelWidth, camera.scaledPixelHeight);

            
            _previousJitter = _jitter;
            previousfrustum = new Frustum
            {
                corners = new Vector3[8],
                planes = GeometryUtility.CalculateFrustumPlanes(camera)
            };

            frustum = new Frustum
            {
                corners = new Vector3[8],
                planes = GeometryUtility.CalculateFrustumPlanes(camera)
            };
        }


        public void Update()
        {
            _prevCamVMatrix = _camVMatrix;
            _camVMatrix = camera.worldToCameraMatrix;

            
            _prevCamPMatrix = _camPMatrix;
            _camPMatrix = GL.GetGPUProjectionMatrix(camera.nonJitteredProjectionMatrix, true);


            _previousJitter = _jitter;

            _jitter = (Sequence.Halton2D((uint)Time.frameCount) - 0.5f) / new float2(camera.scaledPixelWidth, camera.scaledPixelHeight);
            


            _prevCamVPMatrix = _camVPMatrix;
            _camVPMatrix = _camPMatrix * camera.cameraToWorldMatrix;

            previousfrustum = frustum;


            Vector3 viewDir = -_camVMatrix.GetColumn(2);
            viewDir.Normalize();
            float n = camera.nearClipPlane;
            float f = camera.farClipPlane;

            Frustum.Create(ref frustum, gpuViewProjectionMatrix, camera.worldToCameraMatrix.inverse.GetColumn(3), viewDir,
                n, f);
        }

        public override void Dispose()
        {
        }
    }
}