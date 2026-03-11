using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public class ColorGradingPass : ComputePass
    {
        ComputeShader shader;

        [RenderGraphResource(Name = "ColorGradingTexture", Access = AccessFlags.Write)]
        RenderGraphTexture colorGradingTex;

        public override void Create()
        {
            var engineShader = PipelineResourceManager.Get<PostProcessingShader>();
            shader = engineShader.colorGradingShader;

            colorGradingTex = new RenderGraphTexture();
        }

        public override void Dispose()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
            colorGradingTex.desc.Width = 32;
            colorGradingTex.desc.Height = 32;
            colorGradingTex.desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            colorGradingTex.desc.EnableRandomWrite = true;
        }

        public override void Record(ComputeGraphContext context)
        {
            
        }
    }
}