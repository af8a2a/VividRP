namespace UnityEngine.Rendering.Universal
{
    public partial class AmbientOcclusion 
    {
        public FloatParameter radius = new ClampedFloatParameter(0f, 0, 1);
        public FloatParameter maxRadiusPixels = new ClampedFloatParameter(128f, 16f, 256f);

        public FloatParameter intensity = new ClampedFloatParameter(0f, 0, 4);
        public FloatParameter bias = new ClampedFloatParameter(0f, 0, 1);
        public FloatParameter sharpness = new ClampedFloatParameter(0f, 0, 1);

        public FloatParameter maxDistance = new MinFloatParameter(150,10);
        public FloatParameter distanceFalloff = new MinFloatParameter(50,0);

    }
}