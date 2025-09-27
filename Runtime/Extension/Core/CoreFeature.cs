using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature("Vivid Core")]
    public class CoreFeature : ScriptableRendererFeature
    {
        private readonly string[] m_GBufferPassNames = new string[] { "UniversalGBuffer" };

        ColorPyramidPass colorPyramid;
        DepthPyramidPass depthPyramid;
        ForwardGBufferPass forwardGBufferPass;
        HistoryCapturePass historyCapturePass;
        HistoryValidityPass historyValidityPass;
        SceneViewMotionVectorPass sceneViewMotionVectorPass;

        private DirectionalLighting _directionalLighting;
        private ClusterLighting _clusterLighting;
        public override void Create()
        {
            colorPyramid = new ColorPyramidPass(RenderPassEvent.BeforeRenderingPostProcessing);
            forwardGBufferPass = new ForwardGBufferPass(m_GBufferPassNames);

            historyCapturePass = new HistoryCapturePass()
            {
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
            };

            historyValidityPass = new HistoryValidityPass()
            {
                renderPassEvent = RenderPassEvent.AfterRenderingPrePasses,
            };
            sceneViewMotionVectorPass = new SceneViewMotionVectorPass();
            depthPyramid = new DepthPyramidPass(RenderPassEvent.AfterRenderingPrePasses);
            _directionalLighting = new DirectionalLighting();
            _clusterLighting = new ClusterLighting();
        }
        

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var deferred = renderingData.universalRenderingData.renderingMode is RenderingMode.Deferred;

            if (HistoryBufferCaptureManager.instance.EnableHistoryPasses())
            {
                renderer.EnqueuePass(historyCapturePass);
            }
            
            // if (ForwardGBufferManager.instance.EnableGBufferPasses() && !deferred)
            // {
            //     renderer.EnqueuePass(forwardGBufferPass);
            // }
            colorPyramid.Setup();

            // renderer.EnqueuePass(pass);
            renderer.EnqueuePass(colorPyramid);
            renderer.EnqueuePass(depthPyramid);

            historyValidityPass.Setup(deferred);
            renderer.EnqueuePass(historyValidityPass);
            
            renderer.EnqueuePass(sceneViewMotionVectorPass);
            renderer.EnqueuePass(_directionalLighting);
            renderer.EnqueuePass(_clusterLighting);
        }
    }
}