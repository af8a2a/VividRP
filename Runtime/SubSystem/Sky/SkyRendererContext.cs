namespace VividRP.Runtime
{
    internal readonly struct SkyRendererContext
    {
        internal SkyRendererContext(
            VividCameraData cameraData,
            VividLightData lightData,
            VividExposureData exposureData = null)
        {
            this.cameraData = cameraData;
            this.lightData = lightData;
            this.exposureData = exposureData;
        }

        internal VividCameraData cameraData { get; }

        internal VividLightData lightData { get; }

        internal VividExposureData exposureData { get; }
    }
}
