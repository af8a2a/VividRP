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


        public TextureHandle RenderBloom(RenderGraph renderGraph, ContextContainer frameData, in TextureHandle source)
        {
            var bloom = VolumeManager.instance.stack.GetComponent<MobileBloom>();
            var result = TextureHandle.nullHandle;
            if (!bloom.enable.value)
            {
                return result;
            }

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
            var result = TextureHandle.nullHandle;
            if (!bloom.enable.value)
            {
                return result;
            }

            switch (bloom.mode.value)
            {
                case BloomMode.None:
                    return result;
                    break;
                default:
                    result = _bloomApplyPass.Render(renderGraph, frameData, source);
                    break;
            }

            return result;
        }

        #endregion


        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();

            TextureHandle currentRT = resourceData.activeColorTexture;

            #region Bloom

            {
                resourceData.bloomTexture = RenderBloom(renderGraph, frameData, currentRT);
            }

            #endregion


            #region ApplyBloom

            currentRT = ApplyBloom(renderGraph, frameData, currentRT);

            #endregion

            resourceData.cameraColor = currentRT;
        }
    }
}