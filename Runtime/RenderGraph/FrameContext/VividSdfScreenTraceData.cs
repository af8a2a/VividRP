using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class VividSdfScreenTraceData : ContextItem
    {
        internal CameraHistoryTexture radiance;
        internal CameraHistoryTexture geometry;

        public override void Reset()
        {
            radiance = null;
            geometry = null;
        }
    }
}
