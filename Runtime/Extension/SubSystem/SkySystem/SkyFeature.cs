using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature("Sky Render Feature")]
    public sealed class SkyFeature : ScriptableRendererFeature
    {
        private SkyPass _pass;
        private SkyPrePass _skyPrePass;

        public override void Create()
        {
            _pass = new SkyPass();
            _skyPrePass = new SkyPrePass();
        }


        // void OnEnable()
        // {
        //     var shaders = GraphicsSettings.GetRenderPipelineSettings<SkyRuntimeResources>();
        //     
        //     SkySystem.instance.Build(UniversalRenderPipeline.asset, shaders);
        //     
        // }
        //
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(_skyPrePass);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            SkySystem.ClearAll();
        }
    }
}