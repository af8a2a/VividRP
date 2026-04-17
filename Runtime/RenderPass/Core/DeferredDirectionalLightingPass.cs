using UnityEngine;

namespace VividRP.Runtime.RenderPass.Core
{
    public class DeferredDirectionalLightingPass : DeferredLightingPass
    {
        public DeferredDirectionalLightingPass()
            : base(nameof(DeferredDirectionalLightingPass))
        {
        }

        internal static new Vector4 BuildSkyIblParams(Cubemap skyCubemap, float intensityMultiplier, float rotation)
        {
            return DeferredLightingPass.BuildSkyIblParams(skyCubemap, intensityMultiplier, rotation);
        }
    }
}
