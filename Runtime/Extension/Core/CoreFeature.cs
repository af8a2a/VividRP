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
        HistoryCapturePass historyCapturePass;
        HistoryValidityPass historyValidityPass;
        SceneViewMotionVectorPass sceneViewMotionVectorPass;

        private GenerateViewZPass generateViewZPass;

        private DirectionalLighting _directionalLighting;
        private ClusterLighting _clusterLighting;
        private WorldLightClusterPass _worldLightClusterPass;

        public override void Create()
        {
            colorPyramid = new ColorPyramidPass(RenderPassEvent.BeforeRenderingPostProcessing);

            historyCapturePass = new HistoryCapturePass()
            {
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
            };

            historyValidityPass = new HistoryValidityPass()
            {
                renderPassEvent = RenderPassEvent.AfterRenderingGbuffer,
            };
            sceneViewMotionVectorPass = new SceneViewMotionVectorPass();
            depthPyramid = new DepthPyramidPass(RenderPassEvent.AfterRenderingPrePasses);
            _directionalLighting = new DirectionalLighting();
            _clusterLighting = new ClusterLighting();
            _worldLightClusterPass = new WorldLightClusterPass();
            _worldLightClusterPass.Setup();

            generateViewZPass = new GenerateViewZPass();
        }


        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(historyCapturePass);
            

            renderer.EnqueuePass(generateViewZPass);

            colorPyramid.Setup();

            renderer.EnqueuePass(colorPyramid);
            renderer.EnqueuePass(depthPyramid);

            renderer.EnqueuePass(historyValidityPass);

            renderer.EnqueuePass(sceneViewMotionVectorPass);
            renderer.EnqueuePass(_directionalLighting);
            renderer.EnqueuePass(_clusterLighting);
            
            // World light cluster for path tracing GI
            renderer.EnqueuePass(_worldLightClusterPass);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _worldLightClusterPass?.Dispose();
        }
    }
}