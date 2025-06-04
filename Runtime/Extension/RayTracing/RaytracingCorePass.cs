using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class RaytracingCorePass : ScriptableRenderPass
    {
        public RaytracingCorePass()
        {
            renderPassEvent = RenderPassEvent.BeforeRendering;
        }

        
        static class Profiling
        {
            public static ProfilingSampler RaytracingBuildAccelerationStructure = new ProfilingSampler(nameof(RaytracingBuildAccelerationStructure));
        }
        
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            var raytracingData = frameData.GetOrCreate<RaytracingData>();

            if (!SystemInfo.supportsRayTracing)
            {
                return;
            }

            if (RayTracingSystem.SupportedCamera(cameraData.camera))
            {
                // TODO: Check HDRP for update. It might change to a single one system, not current per camera.
                using (new ProfilingScope(Profiling.RaytracingBuildAccelerationStructure))
                {
                    raytracingData.rayTracingSystem.BuildRayTracingAccelerationStructure();
                }

                // TODO: builds the ray tracing light cluster
                //RayTracingClusterCull();
            }
        }
    }
}