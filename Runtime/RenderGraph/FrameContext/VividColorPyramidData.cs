using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public sealed class VividColorPyramidData : ContextItem
    {
        public bool hasValidHistory;
        public TextureHandle previousColorPyramid;
        public RenderGraphTexture currentColorPyramid;
        public int width;
        public int height;
        public int previousWidth;
        public int previousHeight;
        public int mipCount;
        public Vector4 previousColorPyramidUvScaleAndLimit;

        public override void Reset()
        {
            hasValidHistory = false;
            previousColorPyramid = default;
            currentColorPyramid = null;
            width = 0;
            height = 0;
            previousWidth = 0;
            previousHeight = 0;
            mipCount = 0;
            previousColorPyramidUvScaleAndLimit = Vector4.zero;
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
