using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class VisibilityBufferPass : ScriptableRenderPass
    {
        ComputeShader m_VisibilityBufferCS;


        public VisibilityBufferPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingShadows;
        }


        class PassData
        {
            internal ComputeShader VisibilityBufferCS;
            internal RayTracingAccelerationStructure AccelerationStructure;
            internal int width, height;

            internal TextureHandle visibilityBufferHandle;
        }

        static int _VisibilityBuffer=Shader.PropertyToID("_VisibilityBuffer");
        static int _AccelerationStructure = Shader.PropertyToID("_AccelerationStructure");

        
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!m_VisibilityBufferCS)
            {
                var runtimeShader = GraphicsSettings.GetRenderPipelineSettings<GPUDrivenRuntimeShader>();
                m_VisibilityBufferCS = runtimeShader.visibilityBufferCS;
            }


            using (var builder = renderGraph.AddComputePass<PassData>("VisibilityBuffer", out var passData))
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                if (!cameraData.rayTracingSystem.GetRayTracingState())
                {
                    return;
                }
                passData.AccelerationStructure = cameraData.rayTracingSystem.RequestAccelerationStructure();
                
                
                passData.visibilityBufferHandle = renderGraph.CreateTexture(new TextureDesc(cameraData.scaledWidth,cameraData.scaledHeight)
                {
                    format = GraphicsFormat.R32G32B32A32_SFloat,
                    name = "VisibilityBuffer",
                    enableRandomWrite = true
                });
                passData.VisibilityBufferCS = m_VisibilityBufferCS;
                passData.width = cameraData.actualWidth;
                passData.height= cameraData.actualHeight;
                
                builder.UseTexture(passData.visibilityBufferHandle,AccessFlags.Write);
                builder.AllowPassCulling(false);
                
                builder.SetRenderFunc<PassData>((data, context) =>
                {
                    var cmd = context.cmd;
                    var cs = data.VisibilityBufferCS;
                    var kernel = 0;
                    cmd.SetRayTracingAccelerationStructure(cs,kernel,_AccelerationStructure,data.AccelerationStructure);
                    cmd.SetComputeTextureParam(cs,kernel,_VisibilityBuffer,data.visibilityBufferHandle);

                    var tx = CoreUtils.DivRoundUp(data.width, 16);
                    var ty = CoreUtils.DivRoundUp(data.height, 16);

                    cmd.DispatchCompute(cs, kernel, tx, ty, 1);

                });

            }
        }
    }
}