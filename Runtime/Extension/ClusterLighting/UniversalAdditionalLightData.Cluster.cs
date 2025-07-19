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

     partial class UniversalAdditionalLightData 
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

        
        /// <summary>
        /// Only for Punctual/Sphere/Disc. Default shape radius is not 0 so that specular highlight is visible by default, it matches the previous default of 0.99 for MaxSmoothness.
        /// </summary>
        [SerializeField] float m_ShapeRadius = 0.025f;
        public float shapeRadius
        {
            get => m_ShapeRadius;
            set { m_ShapeRadius = value; }
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

        

    }
}