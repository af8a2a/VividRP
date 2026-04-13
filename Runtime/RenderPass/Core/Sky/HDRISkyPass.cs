namespace VividRP.Runtime.RenderPass.Core
{
    public class HDRISkyPass : SkyInjectionPass
    {
        protected override bool CanInjectSky(SkyType activeSkyType)
        {
            return activeSkyType == SkyType.HDRI;
        }
    }
}
