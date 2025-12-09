using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class RaytracingCorePass : ScriptableRenderPass
    {
        private static int g_NvidiaExt = Shader.PropertyToID("g_NvidiaExt");
        public RaytracingCorePass()
        {
            renderPassEvent = RenderPassEvent.BeforeRendering;
        }


        static class Profiling
        {
            public static readonly ProfilingSampler RaytracingBuildAccelerationStructure = new ProfilingSampler(nameof(RaytracingBuildAccelerationStructure));
        }

        class RaytracingCorePassData
        {
            public BufferHandle nvidiaExt;
        }


        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (!SystemInfo.supportsRayTracing)
            {
                return;
            }

            if (RayTracingSystem.SupportedCamera(cameraData.camera))
            {
                var rayTracingSystem = RayTracingSystem.instance;

                // TODO: Check HDRP for update. It might change to a single one system, not current per camera.
                using (new ProfilingScope(Profiling.RaytracingBuildAccelerationStructure))
                {
                    rayTracingSystem.BuildRayTracingAccelerationStructure(cameraData);
                }

                if (rayTracingSystem.SupportSER && !rayTracingSystem.SERSetup)
                {
                    rayTracingSystem.SERSetup = false;
                    using (var builder = renderGraph.AddUnsafePass<RaytracingCorePassData>("Raytracing Core", out var data))
                    {
                        data.nvidiaExt = renderGraph.ImportBuffer(rayTracingSystem.NVAPI_Buffer);
                        builder.AllowGlobalStateModification(true);
                        builder.AllowPassCulling(false);
                        builder.SetRenderFunc<RaytracingCorePassData>((passData, ctx) =>
                        {
                            var cmd = ctx.cmd;
                            cmd.SetGlobalBuffer(g_NvidiaExt, passData.nvidiaExt);
                            
                            if (!ShaderExecutionReordering.NvAPI_SetNvShaderExtnSlot(1))
                                Debug.Log("NvAPI_SetNvShaderExtnSlot failed!");

                        });
                    }
                }
                // TODO: builds the ray tracing light cluster
                //RayTracingClusterCull();
            }
        }
    }
}