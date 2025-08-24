using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class CorePostProcessingFeature : ScriptableRendererFeature
    {
        CorePostProcessPass _corePostProcessPass = new CorePostProcessPass();

        #region ColorGrading

        private VividColorGradingLutPass _colorGradingLutPass = new VividColorGradingLutPass();

        #endregion

        #region FinalBlit

        UberFinalPass _uberFinalPass = new UberFinalPass();

        #endregion

        public override void Create()
        {
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(_colorGradingLutPass);

            renderer.EnqueuePass(_corePostProcessPass);
            renderer.EnqueuePass(_uberFinalPass);
        }
    }
}