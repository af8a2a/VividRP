using System;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class CorePostProcessPass : ScriptableRenderPass
    {
        public CorePostProcessPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        }

        
        #region StopNaN

        StopNaNPass _stopNaNPass = new StopNaNPass();

        #endregion

        #region CMAA2

        CMAA2Pass _cmaa2Pass = new CMAA2Pass();

        #endregion

        #region SMAA

        SMAAPass _smaaPass = new SMAAPass();

        #endregion


        #region PhysicallyDepthOfField

        DiaphragmDoFPass _diaphragmDoFPass = new DiaphragmDoFPass();

        #endregion
        
        #region Upscaler
        private SuperResolutionPass _superResolutionPass = new SuperResolutionPass();
        
        private TemporalAAPass _temporalAAPass = new TemporalAAPass();

        #endregion
        
        #region MotionBlur

        MotionBlurPass _motionBlurPass = new MotionBlurPass();

        #endregion
        
        #region PaniniProjection

        PaniniProjectionPass _paniniProjectionPass = new PaniniProjectionPass();

        #endregion

        #region Bloom

        URPBloomPass _urpBloomPass = new URPBloomPass();

        MobileBloomPass _mobileBloomPass = new MobileBloomPass();

        
        #endregion
        
        
        
        #region LensFlareDataDriven

        LensFlareDataDrivenPass _lensFlareDataDrivenPass = new LensFlareDataDrivenPass();

        #endregion
        
        #region LensFlareScreenSpace

        LensFlareScreenSpacePass _lensFlareScreenSpacePass = new LensFlareScreenSpacePass();

        #endregion

        
        #region Diffusion

        DiffusionPass _diffusionPass = new DiffusionPass();

        #endregion

        
        #region UberPost

        UberPostPass _uberPostPass = new UberPostPass();
        #endregion

        // #region FinalBlit
        //
        // UberFinalPass _uberFinalPass = new UberFinalPass();
        // #endregion

        
        
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

            #region SMAA

            currentRT = _smaaPass.Render(renderGraph, frameData, currentRT);

            #endregion

            #region PhysicallyDepthOfField

            currentRT = _diaphragmDoFPass.Render(renderGraph, frameData, currentRT);

            #endregion

            
            #region Upscaler

            currentRT = _superResolutionPass.Render(renderGraph, frameData, currentRT);

            #endregion

            #region TemporalAA

            currentRT = _temporalAAPass.Render(renderGraph, frameData, currentRT);

            #endregion

            #region MotionBlur

            currentRT = _motionBlurPass.Render(renderGraph, frameData, currentRT);

            #endregion

            #region PaniniProjection

            currentRT = _paniniProjectionPass.Render(renderGraph, frameData, currentRT);

            #endregion


            #region Bloom

            resourceData.bloomTexture = _urpBloomPass.Render(renderGraph, frameData, currentRT);
            resourceData.bloomTexture = _mobileBloomPass.Render(renderGraph, frameData, currentRT);

            #endregion

            #region LensFlareDataDriven

            currentRT = _lensFlareDataDrivenPass.Render(renderGraph, frameData, currentRT);

            #endregion

            #region LensFlareScreenSpace

            resourceData.bloomTexture = _lensFlareScreenSpacePass.Render(renderGraph, frameData, currentRT);

            #endregion

            

            // #region ToneMapping
            //
            // currentRT = _toneMappingPass.Render(renderGraph, frameData, currentRT);
            //
            // #endregion

            #region Diffusion

            currentRT = _diffusionPass.Render(renderGraph, frameData, currentRT);

            #endregion

            #region UberPost
            currentRT = _uberPostPass.Render(renderGraph, frameData, currentRT);
            
            #endregion
            
            
            resourceData.cameraColor = currentRT;
            
            
            // _uberFinalPass.Render(renderGraph, frameData, currentRT);
        }
    }
}