using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// The resolution at which HDRP processes the depth of field effect.
    /// </summary>
    /// <seealso cref="PhysicallyDepthOfField.resolution"/>
    public enum DepthOfFieldResolution : int
    {
        /// <summary>
        /// Quarter resolution.
        /// </summary>
        Quarter = 4,

        /// <summary>
        /// Half resolution.
        /// </summary>
        Half = 2,

        /// <summary>
        /// Full resolution. Should only be set for beauty shots or film uses.
        /// </summary>
        Full = 1
    }


    public sealed class PhysicallyDepthOfField : VolumeComponent, IPostProcessComponent
    {
        // -------------------------------------------
        // Physical settings
        //

        /// <summary>
        /// The distance to the focus plane from the Camera.
        /// </summary>
        [Tooltip("The distance to the focus plane from the Camera.")]
        public MinFloatParameter focusDistance = new MinFloatParameter(10f, 0.1f);


        // -------------------------------------------
        // Shared settings
        //


        [Header("Near Blur")]
        [Tooltip("Sets the number of samples to use for the near field.")]
        [SerializeField, FormerlySerializedAs("nearSampleCount")]
        ClampedIntParameter m_NearSampleCount = new ClampedIntParameter(5, 3, 8);

        [SerializeField, FormerlySerializedAs("nearMaxBlur")]
        [Tooltip("Sets the maximum radius the near blur can reach.")]
        ClampedFloatParameter m_NearMaxBlur = new ClampedFloatParameter(4f, 0f, 8f);

        [Header("Far Blur")]
        [Tooltip("Sets the number of samples to use for the far field.")]
        [SerializeField, FormerlySerializedAs("farSampleCount")]
        ClampedIntParameter m_FarSampleCount = new ClampedIntParameter(7, 3, 16);

        [Tooltip("Sets the maximum radius the far blur can reach.")]
        [SerializeField, FormerlySerializedAs("farMaxBlur")]
        ClampedFloatParameter m_FarMaxBlur = new ClampedFloatParameter(8f, 0f, 16f);

        [SerializeField] ClampedFloatParameter m_Aperture = new ClampedFloatParameter(1f, 1f, 18f);


        public float FocusDistance
        {
            get { return focusDistance.value; }
            set { focusDistance.value = value; }
        }


        /// <summary>
        ///  f number.
        /// </summary>
        public float Aperture
        {
            get { return m_Aperture.value; }
            set { m_Aperture.value = value; }
        }


        /// <summary>
        /// Sets the number of samples to use for the near field.
        /// </summary>
        public int nearSampleCount
        {
            get { return m_NearSampleCount.value; }
            set { m_NearSampleCount.value = value; }
        }

        /// <summary>
        /// Sets the maximum radius the near blur can reach.
        /// </summary>
        public float nearMaxBlur
        {
            get { return m_NearMaxBlur.value; }
            set { m_NearMaxBlur.value = value; }
        }

        /// <summary>
        /// Sets the number of samples to use for the far field.
        /// </summary>
        public int farSampleCount
        {
            get { return m_FarSampleCount.value; }
            set { m_FarSampleCount.value = value; }
        }

        /// <summary>
        /// Sets the maximum radius the far blur can reach.
        /// </summary>
        public float farMaxBlur
        {
            get { return m_FarMaxBlur.value; }
            set { m_FarMaxBlur.value = value; }
        }


        [SerializeField]
        DepthOfFieldResolutionParameter m_Resolution = new DepthOfFieldResolutionParameter(DepthOfFieldResolution.Half);

        /// <summary>
        /// Specifies the resolution at which HDRP processes the depth of field effect.
        /// </summary>
        /// <seealso cref="DepthOfFieldResolution"/>
        public DepthOfFieldResolution resolution
        {
            get { return m_Resolution.value; }
            set { m_Resolution.value = value; }
        }

        [Tooltip(
            "When enabled, HDRP uses a more accurate but slower physically based algorithm to compute the depth of field effect.")]
        [SerializeField]
        FloatParameter m_AdaptiveSamplingWeight = new ClampedFloatParameter(0.75f, 0.5f, 4f);


        /// <summary>
        /// The adaptive sampling weight is a factor that modifies the number of samples in the depth of field depending
        /// on the radius of the blur. Higher values will reduce the noise in the depth of field but increases its cost.
        /// </summary>
        public float adaptiveSamplingWeight
        {
            get { return m_AdaptiveSamplingWeight.value; }
            set { m_AdaptiveSamplingWeight.value = value; }
        }

        
        
        /// <summary>
        /// Enables the Circle of Confusion Reprojection used when anti-aliasing or an upsampling technique requiring jittering (TAA, DLSS, STP, etc.) is enabled. Disabling this option can get rid of ghosting artifacts in the depth of field."
        /// </summary>
        [AdditionalProperty]
        [Tooltip("Enables the CoC Reprojection used when anti-aliasing or an upsampling technique requiring jittering (TAA, DLSS, STP, etc.) is enabled. Disabling this option can get rid of ghosting artifacts in the depth of field.")]
        [SerializeField]
        [InspectorName("CoC Stabilization")]
        public BoolParameter coCStabilization = new BoolParameter(true);

        

        public BoolParameter enabled = new BoolParameter(false);


        public bool IsActive()
        {
            return enabled.value;
        }
    }


    /// <summary>
    /// A <see cref="VolumeParameter"/> that holds a <see cref="DepthOfFieldResolution"/> value.
    /// </summary>
    [Serializable]
    public sealed class DepthOfFieldResolutionParameter : VolumeParameter<DepthOfFieldResolution>
    {
        /// <summary>
        /// Creates a new <see cref="DepthOfFieldResolutionParameter"/> instance.
        /// </summary>
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public DepthOfFieldResolutionParameter(DepthOfFieldResolution value, bool overrideState = false) : base(value,
            overrideState)
        {
        }
    }
}