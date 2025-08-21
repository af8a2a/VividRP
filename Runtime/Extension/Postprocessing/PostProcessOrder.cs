using System;

namespace UnityEngine.Rendering.Universal
{
    public enum PostProcessExecutionOrder
    {
        ColorGrading = 0,
        StopNaN,
        SMAA,
        CMAA2,
        DepthofField,
        TAA,
        MotionBlur,
        PaniniProjection,
        Bloom,
        LensFlareDataDriven,
        ApplyBloom,
        ApplyVignette,
        ApplyColorGrading,
        FXAA,
        ApplyGrain,
        ColorSpace,
        ApplyDithering,
        OETF,
        HDRUIComposition,
        AlphaOutput,
        FullScreenDebug,
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class PostProcessOrder : Attribute
    {
        public int order { private set; get; }

        public PostProcessOrder(PostProcessExecutionOrder order)
        {
            this.order = (int)order;
        }
    }
}