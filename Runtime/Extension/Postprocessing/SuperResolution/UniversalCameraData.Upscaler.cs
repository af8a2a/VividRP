using DLSS;

namespace UnityEngine.Rendering.Universal
{

    partial class UniversalCameraData
    {
        internal UpscalingTechnique upscalingTechnique;


        /// <summary>
        /// Returns true if the TAAU upscaler has been requested
        /// Use IsTAAUEnabled() to ensure that TAAU upscaler is active at runtime, it necessitates TAA pre-processing
        /// </summary>
        /// <returns>True if TAAU is requested</returns>
        internal bool IsTAAUEnabled()
        {
            return (imageScalingMode == ImageScalingMode.Upscaling) && (upscalingTechnique == UpscalingTechnique.TAAU);
        }


        #region DLSS

        /// <summary>
        /// DLSS quality level for this camera.
        /// </summary>
        internal DLSSQuality dlssQuality = DLSSQuality.Balanced;


        internal bool IsDLSSEnabled()
        {
            return (upscalingTechnique == UpscalingTechnique.DLSS);
        }
        
        #endregion
    }
}