namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature]
    public class ScreenSpaceReflectionMobileFeature : ScriptableRendererFeature
    {
        ForwardGBufferPass m_GBufferPass;
        BackfaceDepthPass m_BackfaceDepthPass;
        ScreenSpaceReflectionMobilePass _ScreenSpaceReflectionMobilePass;

        public override void Create()
        {
            m_BackfaceDepthPass = new BackfaceDepthPass();
            _ScreenSpaceReflectionMobilePass = new ScreenSpaceReflectionMobilePass();
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
            renderer.EnqueuePass(_ScreenSpaceReflectionMobilePass);
        }
    }
}