using DLSS;
using UnityEngine.NVIDIA;

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

        internal DLSSQuality dlssQuality = DLSSQuality.MaxQuality;


        internal bool IsDLSSEnabled()
        {

            return  (upscalingTechnique == UpscalingTechnique.DLSS);
        }

        #endregion
    }
}