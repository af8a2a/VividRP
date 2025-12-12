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

        
        static RTHandle HistoryAccumulateTextureAllocator(Vector2Int viewport,GraphicsFormat graphicsFormat, string viewName, int frameIndex, RTHandleSystem rtHandleSystem)
        {

            frameIndex &= 1;

            return rtHandleSystem.Alloc(viewport.x,viewport.y, colorFormat: graphicsFormat,
                filterMode: FilterMode.Point, enableRandomWrite: true,
                useDynamicScale:false,
                name: string.Format("{0}ViewSpace Depth{1}", viewName, frameIndex));
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

                RTHandle  nextHistory;

                cameraData.historyFrameRTSystem.ReAllocatedAccumulateTextureIfNeeded(
                    HistoryAccumulateTextureAllocator, new Vector2Int(cameraData.actualWidth, cameraData.actualHeight), GraphicsFormat.R32_SFloat,
                    HistoryFrameType.ViewZ, out _,
                    out nextHistory);


                passData.LinearDepthTexture = renderGraph.ImportTexture(nextHistory);
                    
                passData.DepthTexture = resourceData.cameraDepthTexture;
                passData.GenerateViewZ = m_GenerateViewZ;

                passData.width = cameraData.actualWidth;
                passData.height = cameraData.actualHeight;


                builder.UseTexture(passData.DepthTexture);
                builder.UseTexture(passData.LinearDepthTexture, AccessFlags.Write);


                
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