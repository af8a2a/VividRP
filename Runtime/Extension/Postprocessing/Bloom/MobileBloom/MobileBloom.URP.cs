namespace UnityEngine.Rendering.Universal
{
    public partial class MobileBloom
    {
        /// <summary>
        /// Controls the extent of the veiling effect.
        /// </summary>
        [Tooltip("Set the radius of the bloom effect.")]
        public ClampedFloatParameter scatter = new ClampedFloatParameter(0.7f, 0f, 1f);

        /// <summary>
        /// Set the maximum intensity that Unity uses to calculate Bloom.
        /// If pixels in your Scene are more intense than this, URP renders them at their current intensity, but uses this intensity value for the purposes of Bloom calculations.
        /// </summary>
        [Tooltip(
            "Set the maximum intensity that Unity uses to calculate Bloom. If pixels in your Scene are more intense than this, URP renders them at their current intensity, but uses this intensity value for the purposes of Bloom calculations.")]
        public MinFloatParameter clamp = new MinFloatParameter(65472f, 0f);

        /// <summary>
        /// Controls whether to use bicubic sampling instead of bilinear sampling for the upsampling passes.
        /// This is slightly more expensive but helps getting smoother visuals.
        /// </summary>
        [Tooltip(
            "Use bicubic sampling instead of bilinear sampling for the upsampling passes. This is slightly more expensive but helps getting smoother visuals.")]
        public BoolParameter highQualityFiltering = new BoolParameter(false);

        /// <summary>
        /// Controls the starting resolution that this effect begins processing.
        /// </summary>
        [Tooltip("The starting resolution that this effect begins processing."), AdditionalProperty]
        public DownscaleParameter downscale = new DownscaleParameter(BloomDownscaleMode.Half);

        /// <summary>
        /// Controls the maximum number of iterations in the effect processing sequence.
        /// </summary>
        [Tooltip("The maximum number of iterations in the effect processing sequence."), AdditionalProperty]
        public ClampedIntParameter maxIterations = new ClampedIntParameter(6, 2, 8);

        /// <summary>
        /// Specifies a Texture to add smudges or dust to the bloom effect.
        /// </summary>
        [Header("Lens Dirt")] [Tooltip("Dirtiness texture to add smudges or dust to the bloom effect.")]
        public TextureParameter dirtTexture = new TextureParameter(null);

        /// <summary>
        /// Controls the strength of the lens dirt.
        /// </summary>
        [Tooltip("Amount of dirtiness.")] public MinFloatParameter dirtIntensity = new MinFloatParameter(0f, 0f);
    }
}