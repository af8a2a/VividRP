namespace UnityEngine.Rendering.Universal
{
    public partial class Shadows 
    {
        #region Raytracing Shadow

        [Tooltip("Use RayTracing for opaques.")]
        public BoolParameter rayTracing = new BoolParameter(false, BoolParameter.DisplayType.EnumPopup);

        [Tooltip("Controls the ray length for ray traced directional shadows.")]
        public MinFloatParameter dirShadowsRayLength = new MinFloatParameter(1000.0f, 0.01f);

        [Tooltip("Shadow sample count for soft Shadow.")]
        public ClampedIntParameter sampleCount = new ClampedIntParameter(1,1,32);

        [Tooltip("Shadow sample radius for soft Shadow.")]
        public ClampedFloatParameter radius = new ClampedFloatParameter(0.1f,0f,0.5f);

        [Tooltip("Controls character self shadows layer.")]
        public LayerMaskParameter characterLayerMask = new LayerMaskParameter(0);

        #endregion
    }
}