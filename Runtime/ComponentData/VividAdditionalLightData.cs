using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public static class VividLightExtensions
    {
        public static VividAdditionalLightData GetVividAdditionalLightData(this Light light)
        {
            if (light == null)
                throw new ArgumentNullException(nameof(light));

            var gameObject = light.gameObject;
            if (!gameObject.TryGetComponent<VividAdditionalLightData>(out var lightData))
                lightData = gameObject.AddComponent<VividAdditionalLightData>();

            return lightData;
        }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    [ExecuteAlways]
    public class VividAdditionalLightData : MonoBehaviour, IAdditionalData
    {
        [SerializeField]
        private bool m_UsePipelineSettings = true;

        [SerializeField]
        private bool m_CustomShadowLayers;

        [SerializeField]
        private RenderingLayerMask m_ShadowRenderingLayersMask = RenderingLayerMask.defaultRenderingLayerMask;

        private Light m_Light;

        internal Light light
        {
            get
            {
                if (m_Light == null)
                    TryGetComponent(out m_Light);

                return m_Light;
            }
        }

        public bool usePipelineSettings
        {
            get => m_UsePipelineSettings;
            set => m_UsePipelineSettings = value;
        }

        public bool customShadowLayers
        {
            get => m_CustomShadowLayers;
            set => m_CustomShadowLayers = value;
        }

        public RenderingLayerMask shadowRenderingLayers
        {
            get => m_ShadowRenderingLayersMask;
            set => m_ShadowRenderingLayersMask = value;
        }

        public RenderingLayerMask effectiveShadowRenderingLayers
        {
            get
            {
                if (m_CustomShadowLayers)
                    return m_ShadowRenderingLayersMask;

                return light != null ? (RenderingLayerMask)light.renderingLayerMask : RenderingLayerMask.defaultRenderingLayerMask;
            }
        }

        private void OnValidate()
        {
            m_Light = light;
        }
    }
}
