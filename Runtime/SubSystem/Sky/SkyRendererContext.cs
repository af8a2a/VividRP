namespace VividRP.Runtime
{
    internal readonly struct SkyRendererContext
    {
        internal SkyRendererContext(VividCameraData cameraData, VividLightData lightData)
        {
            this.cameraData = cameraData;
            this.lightData = lightData;
        }

        internal VividCameraData cameraData { get; }

        internal VividLightData lightData { get; }
    }
}
