namespace UnityEngine.Rendering.Universal
{
    public class ScreenSpaceGlobalIlluminationFeature:ScriptableRendererFeature
    {
        // ScreenSpaceGlobalIlluminationPass m_ScreenSpaceGlobalIlluminationPass;
        public override void Create()
        {
            // m_ScreenSpaceGlobalIlluminationPass = new ScreenSpaceGlobalIlluminationPass()
            // {
            //     renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
            // };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // m_ScreenSpaceGlobalIlluminationPass.Setup();
            // renderer.EnqueuePass(m_ScreenSpaceGlobalIlluminationPass);
        }
    }
}