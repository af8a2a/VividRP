namespace UnityEngine.Rendering.Universal
{
    public enum UpscalingTechnique
    {
        /// <summary>
        ///  Bilinear filter
        /// </summary>
        Linear = 0,
        
        /// <summary>
        ///  Spatial-Temporal Post-Processing
        /// </summary>
        STP,
        /// <summary>
        /// HDRP TAAU
        /// </summary>
        TAAU,
        
    }
}