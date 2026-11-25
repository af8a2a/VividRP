using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public sealed class VividScreenSpaceReflectionData : ContextItem
    {
        public bool hasValidTexture;
        public RenderGraphTexture reflectionTexture;
        public int width;
        public int height;

        public override void Reset()
        {
            hasValidTexture = false;
            reflectionTexture = null;
            width = 0;
            height = 0;
        }
    }

    internal static class VividScreenSpaceReflectionRuntimeUtility
    {
        internal static void ClearFrameCache(ContextContainer frameData)
        {
            if (frameData == null || !frameData.Contains<VividScreenSpaceReflectionData>())
                return;

            frameData.Get<VividScreenSpaceReflectionData>().Reset();
        }
    }
}
