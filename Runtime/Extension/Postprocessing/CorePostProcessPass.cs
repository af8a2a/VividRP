using System;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class CorePostProcessPass : ScriptableRenderPass
    {
        #region StopNaN

        StopNaNPass _stopNaNPass = new StopNaNPass();

        #endregion

        #region Bloom

        URPBloomPass _urpBloomPass = new URPBloomPass();

        MobileBloomPass _mobileBloomPass = new MobileBloomPass();

        BloomApplyPass _bloomApplyPass = new BloomApplyPass();

        public CorePostProcessPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        }


        public TextureHandle RenderBloom(RenderGraph renderGraph, ContextContainer frameData, in TextureHandle source)
        {
            var bloom = VolumeManager.instance.stack.GetComponent<MobileBloom>();
            var result = TextureHandle.nullHandle;
            switch (bloom.mode.value)
            {
                case BloomMode.None:
                    return result;
                    break;
                case BloomMode.URP:
                    result = _urpBloomPass.Render(renderGraph, frameData, source);
                    break;
                case BloomMode.Moblie:
                    result = _mobileBloomPass.Render(renderGraph, frameData, source);
                    break;
            }

            return result;
        }


        public TextureHandle ApplyBloom(RenderGraph renderGraph, ContextContainer frameData, in TextureHandle source)
        {
            var bloom = VolumeManager.instance.stack.GetComponent<MobileBloom>();
            var result = source;


            switch (bloom.mode.value)
            {
                case BloomMode.None:
                    return source;
                    break;
                default:
                    result = _bloomApplyPass.Render(renderGraph, frameData, source);
                    break;
            }

            return result;
        }

        #endregion

        #region CMAA2

        CMAA2Pass _cmaa2Pass = new CMAA2Pass();

        #endregion

        #region PhysicallyDepthOfField

        DiaphragmDoFPass _diaphragmDoFPass = new DiaphragmDoFPass();

        #endregion


        #region ToneMapping

        ToneMappingPass _toneMappingPass = new ToneMappingPass();

        #endregion

        #region TemporalAA

        private TemporalAAPass _temporalAAPass = new TemporalAAPass();

        #endregion

        #region Diffusion

        DiffusionPass _diffusionPass = new DiffusionPass();

        #endregion

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();

            TextureHandle currentRT = resourceData.activeColorTexture;

            #region StopNaN

            currentRT = _stopNaNPass.Render(renderGraph, frameData, currentRT);

            #endregion


            #region CMAA2

            currentRT = _cmaa2Pass.Render(renderGraph, frameData, currentRT);

            #endregion


            #region PhysicallyDepthOfField

            currentRT = _diaphragmDoFPass.Render(renderGraph, frameData, currentRT);

            #endregion

            #region TemporalAA

            currentRT = _temporalAAPass.Render(renderGraph, frameData, currentRT);

            #endregion

            #region Bloom

            {
                resourceData.bloomTexture = RenderBloom(renderGraph, frameData, currentRT);
            }

            #endregion


            #region ApplyBloom

            currentRT = ApplyBloom(renderGraph, frameData, currentRT);

            #endregion

            #region ToneMapping

            currentRT = _toneMappingPass.Render(renderGraph, frameData, currentRT);

            #endregion

            #region Diffusion

            currentRT = _diffusionPass.Render(renderGraph, frameData, currentRT);

            #endregion

            resourceData.cameraColor = currentRT;
        }
    }
}