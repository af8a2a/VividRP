using System;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class CorePostProcessPass : ScriptableRenderPass
    {
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

        #region ToneMapping

        ToneMappingPass _toneMappingPass = new ToneMappingPass();

        #endregion

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();

            TextureHandle currentRT = resourceData.activeColorTexture;

            #region CMAA2

            currentRT = _cmaa2Pass.Render(renderGraph, frameData, currentRT);

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

            resourceData.cameraColor = currentRT;
        }
    }
}