namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature("Vivid Shadow")]
    public class ScreenspaceShadowFeature : ScriptableRendererFeature
    {
        DirectionalLightsShadowCasterPass m_DirectionalLightsShadowCasterPass;
        ScreenspaceShadowPass m_ScreenSpaceShadowPass;
        ScreenSpaceShadowsPostPass m_ScreenSpaceShadowsPostPass;

        RaytracingShadowPass raytracingShadowPass;

        FullRaytracingShadowPass fullRaytracingShadowPass;

        public override void Create()
        {
            m_DirectionalLightsShadowCasterPass = new DirectionalLightsShadowCasterPass(RenderPassEvent.BeforeRenderingShadows);
            m_ScreenSpaceShadowPass = new ScreenspaceShadowPass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingDeferredLights
            };
            m_ScreenSpaceShadowsPostPass = new ScreenSpaceShadowsPostPass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents,
            };
            raytracingShadowPass = new RaytracingShadowPass()
            {
                renderPassEvent = RenderPassEvent.AfterRenderingGbuffer,
            };

            fullRaytracingShadowPass = new FullRaytracingShadowPass()
            {
                renderPassEvent = RenderPassEvent.AfterRenderingGbuffer,
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var setting = VolumeManager.instance.stack.GetComponent<Shadows>();
            bool hybridShadow = setting.rayTracing.value && !setting.useFullRTShadow.value;
            bool fullRT = setting.useFullRTShadow.value;
            bool onlyCSM = !setting.rayTracing.value && !setting.useFullRTShadow.value;

            if (fullRT)
            {
                renderer.EnqueuePass(fullRaytracingShadowPass);
            }
            else
            {
                renderer.EnqueuePass(m_DirectionalLightsShadowCasterPass);
                if (hybridShadow)
                {
                    renderer.EnqueuePass(raytracingShadowPass);
                }
                renderer.EnqueuePass(m_ScreenSpaceShadowPass);
                renderer.EnqueuePass(m_ScreenSpaceShadowsPostPass);
            }
        }
    }
}