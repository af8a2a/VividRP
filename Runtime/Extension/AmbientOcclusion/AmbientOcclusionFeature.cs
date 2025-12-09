namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature("Ambient Occlusion")]
    public class AmbientOcclusionFeature : ScriptableRendererFeature
    {
        XeGTAOPass _xeGtaoPass;
        HBAOPass _hbaoPass;
        RaytracingAmbientOcclusionPass _rtaoPass;

        public override void Create()
        {
            _xeGtaoPass = new XeGTAOPass();
            _hbaoPass = new HBAOPass();
            _rtaoPass = new RaytracingAmbientOcclusionPass();
        }


        
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            
            renderer.EnqueuePass(_xeGtaoPass);
            renderer.EnqueuePass(_hbaoPass);
            renderer.EnqueuePass(_rtaoPass);
        }
    }
}