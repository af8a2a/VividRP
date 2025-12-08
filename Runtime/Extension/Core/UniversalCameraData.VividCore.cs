using System.Collections.Generic;
using Unity.Collections;

namespace UnityEngine.Rendering.Universal
{
    partial class UniversalCameraData
    {
        //for integrate HDRP Feature ..
        public int actualWidth => scaledWidth;

        public int actualHeight => scaledHeight;


        private HistoryFrameRTSystem _historyFrameRTSystem;
        private RayTracingSystem _rayTracingSystem;
        private DenoiseSystem _denoiseSystem;
        private VividCameraExtension _cameraExtension;

        public HistoryFrameRTSystem historyFrameRTSystem => _historyFrameRTSystem ??= HistoryFrameRTSystem.GetOrCreate(camera);



        internal DenoiseSystem denoiseSystem => _denoiseSystem ??= DenoiseSystem.GetOrCreate(camera);

        internal VividCameraExtension cameraExtension => _cameraExtension ??= VividCameraExtension.GetOrCreate(camera);

        internal Vector4 resolution => new Vector4(actualWidth, actualHeight, 1.0f / actualWidth, 1.0f / actualHeight);



        public Vector2 jitter => cameraExtension.jitter;
        public Vector2 previousJitter => cameraExtension.previousJitter;


        #region Exposure

        private float m_GpuExposureValue = 1.0f;
        private float m_GpuDeExposureValue = 1.0f;

        private struct ExposureGpuReadbackRequest
        {
            public bool isDeExposure;
            public AsyncGPUReadbackRequest request;
        }

        // This member and function allow us to fetch the exposure value that was used to render the realtime HDProbe
        // without forcing a sync between the c# and the GPU code.
        private Queue<ExposureGpuReadbackRequest> m_ExposureAsyncRequest = new Queue<ExposureGpuReadbackRequest>();


        internal void RequestGpuExposureValue(RTHandle exposureTexture)
        {
            RequestGpuTexelValue(exposureTexture, false);
        }

        internal void RequestGpuDeExposureValue(RTHandle exposureTexture)
        {
            RequestGpuTexelValue(exposureTexture, true);
        }

        private void RequestGpuTexelValue(RTHandle exposureTexture, bool isDeExposure)
        {
            var readbackRequest = new ExposureGpuReadbackRequest();
            readbackRequest.request = AsyncGPUReadback.Request(exposureTexture.rt, 0, 0, 1, 0, 1, 0, 1);
            readbackRequest.isDeExposure = isDeExposure;
            m_ExposureAsyncRequest.Enqueue(readbackRequest);
        }

        private void PumpReadbackQueue()
        {
            while (m_ExposureAsyncRequest.Count != 0)
            {
                ExposureGpuReadbackRequest requestState = m_ExposureAsyncRequest.Peek();
                ref AsyncGPUReadbackRequest request = ref requestState.request;
#if UNITY_EDITOR
                //HACK: when we are in the unity editor, requests get updated very very infrequently
                // by the runtime. This can cause the m_ExposureAsyncRequest to become super bloated:
                // sometimes up to 800 requests get accumulated.
                // This hack forces an update of the request when in editor mode, now the m_ExposureAsyncRequest averages
                // 3 elements. Not necesary when running in player mode, since the requests get updated properly (due to swap chain complexities)
                request.Update();
#endif
                if (!request.done && !request.hasError)
                    break;

                // If this has an error, just skip it
                if (!request.hasError)
                {
                    // Grab the native array from this readback
                    NativeArray<float> exposureValue = request.GetData<float>();
                    if (requestState.isDeExposure)
                        m_GpuDeExposureValue = exposureValue[0];
                    else
                        m_GpuExposureValue = exposureValue[0];
                }

                m_ExposureAsyncRequest.Dequeue();
            }
        }

        // This function processes the asynchronous read-back requests for the exposure and updates the last known exposure value.
        internal float GpuExposureValue()
        {
            PumpReadbackQueue();
            return m_GpuExposureValue;
        }

        // This function processes the asynchronous read-back requests for the exposure and updates the last known exposure value.
        internal float GpuDeExposureValue()
        {
            PumpReadbackQueue();
            return m_GpuDeExposureValue;
        }

        internal RTHandle GetExposureTexture()
        {
            return historyFrameRTSystem.GetCurrentFrameRT(HistoryFrameType.Exposure);
        }

        #endregion
    }
}