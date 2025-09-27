using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature("Vivid Postprocessing")]
    public class CorePostProcessingFeature : ScriptableRendererFeature
    {
        CorePostProcessPass _corePostProcessPass = new CorePostProcessPass();
        
        
        ExposurePass exposurePass;
        ExposureSetupPass exposureSetupPass;

        #region ColorGrading

        private VividColorGradingLutPass _colorGradingLutPass = new VividColorGradingLutPass();

        #endregion

        #region FinalBlit

        UberFinalPass _uberFinalPass = new UberFinalPass();

        #endregion

        public override void Create()
        {
            exposurePass = new ExposurePass();
            exposureSetupPass = new ExposureSetupPass();

        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(_colorGradingLutPass);
            renderer.EnqueuePass(exposureSetupPass);
            renderer.EnqueuePass(exposurePass);
            renderer.EnqueuePass(_corePostProcessPass);
            renderer.EnqueuePass(_uberFinalPass);
        }
    }
}