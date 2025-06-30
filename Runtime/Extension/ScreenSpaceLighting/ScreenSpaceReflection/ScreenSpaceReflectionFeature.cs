namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature]
    public class ScreenSpaceReflectionFeature : ScriptableRendererFeature
    {
        ForwardGBufferPass m_GBufferPass;
        BackfaceDepthPass m_BackfaceDepthPass;
        ScreenSpaceReflectionPass m_ScreenSpaceReflectionPass;

        public override void Create()
        {
            m_BackfaceDepthPass = new BackfaceDepthPass();
            m_ScreenSpaceReflectionPass = new ScreenSpaceReflectionPass();
        }

        public override void OnEnable()
        {
            ForwardGBufferManager.instance.AcquireGBufferPasses();
            base.OnEnable();
        }

        private void OnDisable()
        {
            ForwardGBufferManager.instance.ReleaseGBufferPasses();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(m_BackfaceDepthPass);
            renderer.EnqueuePass(m_ScreenSpaceReflectionPass);
        }
    }
}