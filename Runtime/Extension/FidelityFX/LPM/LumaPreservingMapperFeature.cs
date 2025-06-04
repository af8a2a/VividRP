
namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature]
    public class LumaPreservingMapperFeature : ScriptableRendererFeature
    {
        [SerializeField] ComputeShader computeShader;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        LumaPreservingMapperPSPass lpmPS;
        LumaPreservingMapperCSPass lpmCS;

        /// <inheritdoc/>
        public override void Create()
        {
            lpmPS = new LumaPreservingMapperPSPass();
            lpmCS = new LumaPreservingMapperCSPass();
            // Configures where the render pass should be injected.
            lpmPS.renderPassEvent = renderPassEvent;
        }

        // Here you can inject one or multiple render passes in the renderer.
        // This method is called when setting up the renderer once per-camera.
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var volume = VolumeManager.instance.stack.GetComponent<LumaPreservingMapper>();
            if (volume == null || !volume.IsActive())
            {
                return;
            }

            if (volume.computeShaderMode.value)
            {
                lpmCS.Setup(computeShader);
                renderer.EnqueuePass(lpmCS);
            }
            else
            {
                renderer.EnqueuePass(lpmPS);
            }
        }
    }
}