namespace UnityEngine.Rendering.Universal
{
    public class ScreenSpaceReflectionFeature: ScriptableRendererFeature
    {
        ScreenSpaceReflectionPass m_ScriptablePass;
        public override void Create()
        {
            m_ScriptablePass = new ScreenSpaceReflectionPass(RenderPassEvent.BeforeRenderingDeferredLights);

        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            m_ScriptablePass.Setup();
            
            renderer.EnqueuePass(m_ScriptablePass);
        }
    }
}