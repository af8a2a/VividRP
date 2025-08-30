namespace UnityEngine.Rendering.Universal
{
    partial class UniversalRenderer
    {
        GPULights m_GPULights = new GPULights(RenderPassEvent.AfterRenderingPrePasses);

        ClusterDeferredLighting m_ClusterDeferredLights;



        void VividInit()
        {
            m_ClusterDeferredLights = new ClusterDeferredLighting(RenderPassEvent.BeforeRenderingDeferredLights, deferredLights);

        }
    }
    
    
}