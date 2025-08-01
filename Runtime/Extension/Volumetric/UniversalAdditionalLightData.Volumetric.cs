using UnityEngine.Serialization;

namespace UnityEngine.Rendering.Universal
{
    partial class UniversalAdditionalLightData
    {
        [Range(0.0f, 16.0f), SerializeField] float m_VolumetricDimmer = 1.0f;

        [SerializeField] bool m_AffectsVolumetric = true;

        // Not used for directional lights.
        [SerializeField] float m_VolumetricFadeDistance = 10000.0f;

        /// <summary>
        /// Get/Set the light dimmer / multiplier on volumetric effects, between 0 and 16.
        /// </summary>
        public float volumetricDimmer
        {
            get => m_AffectsVolumetric ? m_VolumetricDimmer : 0.0f;
            set => m_VolumetricDimmer = Mathf.Clamp(value, 0.0f, 16.0f);
        }


        public bool affectsVolumetric
        {
            get => m_AffectsVolumetric;
            set => m_AffectsVolumetric = value;
        }


        /// <summary>
        /// Get/Set the light fade distance for volumetrics.
        /// </summary>
        public float volumetricFadeDistance
        {
            get => m_VolumetricFadeDistance;
            set => m_VolumetricFadeDistance = Mathf.Clamp(value, 0, float.MaxValue);
        }
    }
}