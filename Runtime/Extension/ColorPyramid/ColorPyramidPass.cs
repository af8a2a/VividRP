using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class ColorPyramidPass : ScriptableRenderPass
    {
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resource = frameData.GetOrCreate<ColorPyramidData>();
            resource.ColorTexture = MipGenerator.Instance.RenderColorPyramid(renderGraph, frameData);
        }
    }
}