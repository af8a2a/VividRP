using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature]
    public class CoreFeature : ScriptableRendererFeature
    {
        private readonly string[] m_GBufferPassNames = new string[] { "UniversalGBuffer" };

        ColorPyramidPass colorPyramid;
        ForwardGBufferPass forwardGBufferPass;
        HistoryCapturePass historyCapturePass;
        HistoryValidityPass historyValidityPass;
        public override void Create()
        {
            colorPyramid = new ColorPyramidPass(RenderPassEvent.AfterRenderingSkybox);
            forwardGBufferPass = new ForwardGBufferPass(m_GBufferPassNames);

            historyCapturePass = new HistoryCapturePass()
            {
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
            };

            historyValidityPass = new HistoryValidityPass()
            {
                renderPassEvent = RenderPassEvent.AfterRenderingPrePasses,
            };
        }
        

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var deferred = renderingData.universalRenderingData.renderingMode is RenderingMode.Deferred;

            if (HistoryBufferCaptureManager.instance.EnableHistoryPasses())
            {
                renderer.EnqueuePass(historyCapturePass);

            }
            
            if (ForwardGBufferManager.instance.EnableGBufferPasses() && !deferred)
            {
                renderer.EnqueuePass(forwardGBufferPass);
            }
            colorPyramid.Setup();

            // renderer.EnqueuePass(pass);
            renderer.EnqueuePass(colorPyramid);
            
            historyValidityPass.Setup(deferred);
            renderer.EnqueuePass(historyValidityPass);

        }
    }
}