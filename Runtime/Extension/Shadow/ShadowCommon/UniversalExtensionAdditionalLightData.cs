using UnityEngine;
using UnityEngine.Rendering;

namespace Features.Shadow.ScreenSpaceShadow
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    public class UniversalExtensionAdditionalLightData : MonoBehaviour, ISerializationCallbackReceiver, IAdditionalData
    {
        
        /// <summary>
        /// Angular diameter of the emissive celestial body represented by the light as seen from the camera (in degrees).
        /// Used to render the sun/moon disk.
        /// </summary>
        [SerializeField] float m_AngularDiameter = 0.5f;
        public float angularDiameter
        {
            get => m_AngularDiameter;
            set { m_AngularDiameter = value; }
        }

        
        
        // Version 0 means serialized data before the version field.
        [SerializeField] int m_Version = 3;
        internal int version
        {
            get => m_Version;
        }

        
        
        public void OnBeforeSerialize()
        {
        }

        /// <inheritdoc/>
        public void OnAfterDeserialize()
        {
        }
    }
}