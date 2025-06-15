using Features.Shadow.UberScreenSpaceShadow;

namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature("Screen Space Shadow")]
    public class ScreenspaceShadowFeature : ScriptableRendererFeature
    {
        DirectionalLightsShadowCasterPass m_DirectionalLightsShadowCasterPass;
        ScreenspaceShadowPass m_ScreenSpaceShadowPass;
        ScreenSpaceShadowsPostPass m_ScreenSpaceShadowsPostPass;

        public override void Create()
        {
            m_DirectionalLightsShadowCasterPass = new DirectionalLightsShadowCasterPass(RenderPassEvent.BeforeRenderingShadows);
            m_ScreenSpaceShadowPass = new ScreenspaceShadowPass()
            {
                renderPassEvent = RenderPassEvent.AfterRenderingPrePasses
            };
            m_ScreenSpaceShadowsPostPass = new ScreenSpaceShadowsPostPass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents,
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            
            renderer.EnqueuePass(m_DirectionalLightsShadowCasterPass);
            renderer.EnqueuePass(m_ScreenSpaceShadowPass);
            renderer.EnqueuePass(m_ScreenSpaceShadowsPostPass);
        }
    }
}