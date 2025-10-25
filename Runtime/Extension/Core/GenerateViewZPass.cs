using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class GenerateViewZPass : ScriptableRenderPass
    {
        ComputeShader m_GenerateViewZ;

        class PassData
        {
            internal ComputeShader GenerateViewZ;

            internal int width, height;
            internal TextureHandle DepthTexture;
            internal TextureHandle LinearDepthTexture;
        }

        static int _DepthTexture = Shader.PropertyToID("_DepthTexture");
        static int _LinearDepthOutput = Shader.PropertyToID("_LinearDepthOutput");

        public GenerateViewZPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
        }


        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!m_GenerateViewZ)
            {
                var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<VividRuntimeShader>();

                m_GenerateViewZ = runtimeShader.generateViewZ;
            }


            using (var builder = renderGraph.AddComputePass<PassData>("GenerateViewZPass", out var passData))
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();

                passData.LinearDepthTexture = renderGraph.CreateTexture(new TextureDesc(cameraData.actualWidth, cameraData.actualHeight)
                {
                    enableRandomWrite = true,
                    format = GraphicsFormat.R32_SFloat
                });
                passData.DepthTexture = resourceData.cameraDepthTexture;
                passData.GenerateViewZ = m_GenerateViewZ;

                passData.width = cameraData.actualWidth;
                passData.height = cameraData.actualHeight;


                builder.UseTexture(passData.DepthTexture);
                builder.UseTexture(passData.LinearDepthTexture, AccessFlags.Write);


                // builder.AllowPassCulling(false);
                
                //in Unity 6000.3.0a5,crash :(
                // builder.EnableAsyncCompute(true);

                builder.SetRenderFunc<PassData>((data, ctx) =>
                {
                    var cmd = ctx.cmd;


                    var cs = data.GenerateViewZ;
                    var kernel = 0;

                    cmd.SetComputeTextureParam(cs, kernel, _DepthTexture, data.DepthTexture);
                    cmd.SetComputeTextureParam(cs, kernel, _LinearDepthOutput, data.LinearDepthTexture);


                    var tx = CoreUtils.DivRoundUp(data.width, 8);
                    var ty = CoreUtils.DivRoundUp(data.height, 8);
                    cmd.DispatchCompute(cs, kernel, tx, ty, 1);
                });
                resourceData.linearDepthTexture = passData.LinearDepthTexture;
            }
        }
    }
}