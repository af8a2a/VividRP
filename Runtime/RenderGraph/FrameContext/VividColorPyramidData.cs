using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public sealed class VividColorPyramidData : ContextItem
    {
        public bool hasValidHistory;
        public RenderGraphTexture previousColorPyramid;
        public RenderGraphTexture currentColorPyramid;
        public int width;
        public int height;
        public int mipCount;

        public override void Reset()
        {
            hasValidHistory = false;
            previousColorPyramid = null;
            currentColorPyramid = null;
            width = 0;
            height = 0;
            mipCount = 0;
        }
    }

    internal static class VividColorPyramidRuntimeUtility
    {
        internal static void ClearFrameCache(ContextContainer frameData)
        {
            if (frameData == null || !frameData.Contains<VividColorPyramidData>())
                return;

            frameData.Get<VividColorPyramidData>().Reset();
        }
    }
}
