using System;

namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature("Vivid Shadow")]
    public class ScreenspaceShadowFeature : ScriptableRendererFeature
    {
        DirectionalLightsShadowCasterPass m_DirectionalLightsShadowCasterPass;
        ScreenspaceShadowPass m_ScreenSpaceShadowPass;
        ScreenSpaceShadowsPostPass m_ScreenSpaceShadowsPostPass;

        RaytracingShadowPass m_RaytracingShadowPass;

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
            m_RaytracingShadowPass = new RaytracingShadowPass()
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
            var pathTracing =
                VolumeManager.instance.stack.GetComponent<GlobalIllumination>().technique.value is GlobalIlluminationTechnique.ReferencedPathTracing;
            if (pathTracing)
            {
                return;
            }

            var setting = VolumeManager.instance.stack.GetComponent<Shadows>();
            var shadowMode = setting.shadowMode.value;
            switch (shadowMode)
            {
                case ShadowMode.FullRasterShadow:
                    renderer.EnqueuePass(m_DirectionalLightsShadowCasterPass);
                    renderer.EnqueuePass(m_ScreenSpaceShadowPass);
                    renderer.EnqueuePass(m_ScreenSpaceShadowsPostPass);
                    break;
                case ShadowMode.HybridShadow:
                    renderer.EnqueuePass(m_DirectionalLightsShadowCasterPass);
                    renderer.EnqueuePass(m_RaytracingShadowPass);
                    renderer.EnqueuePass(m_ScreenSpaceShadowPass);
                    renderer.EnqueuePass(m_ScreenSpaceShadowsPostPass);
                    break;
                case ShadowMode.FullRaytraceShadow:
                    renderer.EnqueuePass(fullRaytracingShadowPass);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}