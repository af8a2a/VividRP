using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.RenderGraph.Data
{
    [Serializable]
    public struct RendererFilterSettings
    {
        public LayerMask LayerMask;
        public int RenderQueueMin;
        public int RenderQueueMax;
        public string[] ShaderPassNames;
        public SortingCriteria SortingCriteria;
        public bool RequireDepthBuffer;

        public static RendererFilterSettings CreateDefault()
        {
            return new RendererFilterSettings
            {
                LayerMask = -1,
                RenderQueueMin = RenderQueueRange.opaque.lowerBound,
                RenderQueueMax = RenderQueueRange.opaque.upperBound,
                ShaderPassNames = new[] { "SRPDefaultUnlit" },
                SortingCriteria = SortingCriteria.CommonOpaque,
                RequireDepthBuffer = true
            };
        }

        public RenderQueueRange ToRenderQueueRange()
        {
            int min = Mathf.Clamp(RenderQueueMin, 0, 5000);
            int max = Mathf.Clamp(RenderQueueMax, min, 5000);

            var range = RenderQueueRange.all;
            range.lowerBound = min;
            range.upperBound = max;
            return range;
        }

        public void EnsureDefaults()
        {
            if (ShaderPassNames == null || ShaderPassNames.Length == 0)
                ShaderPassNames = new[] { "SRPDefaultUnlit" };

            if (RenderQueueMax < RenderQueueMin)
                RenderQueueMax = RenderQueueMin;
        }
    }
}
