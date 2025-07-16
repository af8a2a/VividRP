namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Shape of a spot light(HDRP Preset)
    /// </summary>
    public enum SpotLightShape
    {
        /// <summary>Cone shape. The default shape of the spot light.</summary>
        Cone,

        /// <summary>Pyramid shape.</summary>
        Pyramid,

        /// <summary>Box shape. Similar to a directional light but with bounds.</summary>
        Box
    }

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

        // Light contributions
        /// <summary>
        /// Base Light contribution.
        /// </summary>
        [SerializeField] float m_BaseContribution = 1.0f;

        public float baseContribution
        {
            get => m_BaseContribution;
            set { m_BaseContribution = value; }
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