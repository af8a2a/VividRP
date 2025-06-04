using System;
using Features.Shadow.CascadeShadow;
using Features.Shadow.ShadowCommon;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Features.Shadow.UberScreenSpaceShadow
{
    //prepare for global setting
    [Serializable]
    internal class ScreenSpaceShadowsSettings
    {
    }


    [DisallowMultipleRendererFeature("Custom Screen Space Shadows")]
    public class UberScreenSpaceShadowsFeature : ScriptableRendererFeature
    {
#if UNITY_EDITOR
        [UnityEditor.ShaderKeywordFilter.SelectIf(true, keywordNames: ShaderKeywordStrings.MainLightShadowScreen)]
        private const bool k_RequiresScreenSpaceShadowsKeyword = true;
#endif

        [SerializeField] private ScreenSpaceShadowsSettings m_Settings = new ScreenSpaceShadowsSettings();

        private ScreenSpaceShadowsPass m_SSShadowsPass = null;
        private ScreenSpaceShadowsPostPass m_SSShadowsPostPass = null;

        private CascadeShadowCaster m_cascadeShadowCaster = null;


        /// <inheritdoc/>
        public override void Create()
        {
            if (m_SSShadowsPass == null)
                m_SSShadowsPass = new ScreenSpaceShadowsPass();
            if (m_SSShadowsPostPass == null)
                m_SSShadowsPostPass = new ScreenSpaceShadowsPostPass();

            m_cascadeShadowCaster = new CascadeShadowCaster(RenderPassEvent.BeforeRenderingShadows);


            m_SSShadowsPass.renderPassEvent = RenderPassEvent.AfterRenderingGbuffer;
            m_SSShadowsPostPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        }


        /// <inheritdoc/>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (UniversalRenderer.IsOffscreenDepthTexture(ref renderingData.cameraData))
                return;

            var stack = VolumeManager.instance.stack;
            var shadowSettings = stack.GetComponent<Shadows>();

            
            bool usesDeferredLighting = renderer is UniversalRenderer { usesDeferredLighting: true };


            bool allowMainLightShadows = renderingData.shadowData.supportsMainLightShadows && renderingData.lightData.mainLightIndex != -1;

            bool shouldEnqueue = allowMainLightShadows && m_SSShadowsPass.Setup(m_Settings);

            if (shouldEnqueue)
            {
                m_SSShadowsPass.renderPassEvent = usesDeferredLighting
                    ? RenderPassEvent.AfterRenderingGbuffer
                    : RenderPassEvent.AfterRenderingPrePasses +
                      1; // We add 1 to ensure this happens after depth priming depth copy pass that might be scheduled

                if (m_cascadeShadowCaster.Setup(ref renderingData))
                {
                    renderer.EnqueuePass(m_cascadeShadowCaster);
                }


                renderer.EnqueuePass(m_SSShadowsPass);
                renderer.EnqueuePass(m_SSShadowsPostPass);
            }
        }
    }
}