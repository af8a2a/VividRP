namespace VividRP.Runtime
{
    internal readonly struct TAASettings
    {
        public readonly bool Enabled;
        public readonly float JitterSpread;
        public readonly int SampleCount;
        public readonly float BaseBlendFactor;
        public readonly float MotionWeightDecay;
        public readonly float AntiFlickerIntensity;

        public TAASettings(
            bool enabled,
            float jitterSpread,
            int sampleCount,
            float baseBlendFactor,
            float motionWeightDecay,
            float antiFlickerIntensity)
        {
            Enabled = enabled;
            JitterSpread = jitterSpread;
            SampleCount = sampleCount;
            BaseBlendFactor = baseBlendFactor;
            MotionWeightDecay = motionWeightDecay;
            AntiFlickerIntensity = antiFlickerIntensity;
        }

        public static TAASettings Disabled => new(false, 1.0f, 8, 0.95f, 3.0f, 0.5f);

        public static TAASettings FromCamera(VividAdditionalCameraData data)
        {
            if (data == null)
                return Disabled;

            return new TAASettings(
                data.antialiasing == VividAntialiasingMode.TemporalAntiAliasing,
                data.taaJitterSpread,
                data.taaSampleCount,
                data.taaBaseBlendFactor,
                data.taaMotionWeightDecay,
                data.taaAntiFlickerIntensity);
        }
    }
}
