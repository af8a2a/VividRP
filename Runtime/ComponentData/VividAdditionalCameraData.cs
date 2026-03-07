using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public enum VividCameraRenderType
    {
        Base,
        Overlay,
    }

    public static class VividCameraExtensions
    {
        public static VividAdditionalCameraData GetVividAdditionalCameraData(this Camera camera)
        {
            if (camera == null)
                throw new ArgumentNullException(nameof(camera));

            var gameObject = camera.gameObject;
            if (!gameObject.TryGetComponent<VividAdditionalCameraData>(out var cameraData))
                cameraData = gameObject.AddComponent<VividAdditionalCameraData>();

            return cameraData;
        }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    public class VividAdditionalCameraData : MonoBehaviour, IAdditionalData
    {
        [SerializeField]
        private VividCameraRenderType m_RenderType = VividCameraRenderType.Base;

        [SerializeField]
        private bool m_ClearDepth = true;

        [SerializeField]
        private LayerMask m_VolumeLayerMask = 1;

        [SerializeField]
        private bool m_StopNaNs;

        [SerializeField]
        private bool m_Dithering;

        private Camera m_Camera;

        internal Camera camera
        {
            get
            {
                if (m_Camera == null)
                    TryGetComponent(out m_Camera);

                return m_Camera;
            }
        }

        public VividCameraRenderType renderType
        {
            get => m_RenderType;
            set => m_RenderType = value;
        }

        public bool clearDepth
        {
            get => m_ClearDepth;
            set => m_ClearDepth = value;
        }

        public LayerMask volumeLayerMask
        {
            get => m_VolumeLayerMask;
            set => m_VolumeLayerMask = value;
        }

        public bool stopNaNs
        {
            get => m_StopNaNs;
            set => m_StopNaNs = value;
        }

        public bool dithering
        {
            get => m_Dithering;
            set => m_Dithering = value;
        }

        private void OnValidate()
        {
            m_Camera = camera;
        }
    }
}
