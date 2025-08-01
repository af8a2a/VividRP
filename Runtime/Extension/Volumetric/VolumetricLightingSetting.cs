namespace UnityEngine.Rendering.Universal {
    class VolumetricLightingSetting : VolumeComponent {
        public BoolParameter enable = new BoolParameter(false);
        public MinFloatParameter meanFreePath = new MinFloatParameter(1000, 1);
        public ColorParameter albedo = new ColorParameter(Color.white);

        public FloatParameter range = new FloatParameter(64.0f);
        public ClampedFloatParameter sliceDistrubutionUniform = new ClampedFloatParameter(0.5f, 0.0f, 1.0f);
        public ClampedIntParameter sliceCount = new ClampedIntParameter(256, 64, 512);
    }
}