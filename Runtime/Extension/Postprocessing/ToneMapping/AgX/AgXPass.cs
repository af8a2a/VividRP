using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class AgXPass : ScriptableRenderPass
    {
        private Material material;


        private static int _MaxLuminance = Shader.PropertyToID("_MaxLuminance");

        public void Setup()
        {
            if (!material)
            {
                var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<TonemappingRuntimeShader>();


                material = CoreUtils.CreateEngineMaterial(runtimeShader.AgX);
            }
        }

        class PassData
        {
            internal Material material;
            internal bool Approx;
            internal TextureHandle cameraTexture;
        }


        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("AgX ToneMapping", out var data))
            {
                var toneMapping = VolumeManager.instance.stack.GetComponent<AgXSetting>();

                if (!toneMapping.enable.value)
                {
                    return;
                }

                data.material = material;
                data.Approx = toneMapping.approx.value;
                var resourceData = frameData.Get<UniversalResourceData>();
                var desc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
                var targetTexture = renderGraph.CreateTexture(desc);

                data.cameraTexture = resourceData.activeColorTexture;

                builder.SetRenderAttachment(targetTexture, 0);
                builder.UseTexture(data.cameraTexture);

                builder.SetRenderFunc((PassData passData, RasterGraphContext ctx) =>
                {
                    var cmd = ctx.cmd;


                    CoreUtils.SetKeyword(cmd, "_APPROX", passData.Approx);

                    Blitter.BlitTexture(cmd, data.cameraTexture, new Vector4(1, 1, 0, 0), passData.material, 0);
                });
                resourceData.cameraColor = targetTexture;
            }
        }

        public override void FrameCleanup(CommandBuffer cmd)
        {
            CoreUtils.SetKeyword(cmd, "_APPROX", false);
        }
    }
}