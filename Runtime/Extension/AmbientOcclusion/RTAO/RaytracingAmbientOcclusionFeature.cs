using System;

namespace UnityEngine.Rendering.Universal
{
    public class RaytracingAmbientOcclusionFeature : ScriptableRendererFeature
    {
        RaytracingAmbientOcclusionPass pass;

        public override void Create()
        {
            pass = new RaytracingAmbientOcclusionPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            pass.Setup();
            renderer.EnqueuePass(pass);
        }


        public override void OnEnable()
        {
            HistoryBufferCaptureManager.instance.AcquireHistoryPasses();
        }

        private void OnDisable()
        {
            HistoryBufferCaptureManager.instance.ReleaseHistoryPasses();
        }
    }
}