using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    [DisallowMultipleRendererFeature]
    public sealed class CMAA2Feature : ScriptableRendererFeature
    {
        private CMAA2Pass _pass;

        public override void Create()
        {
            _pass = new CMAA2Pass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var cmaa2 = VolumeManager.instance.stack.GetComponent<CMAA2Volume>();
            if (cmaa2 == null || !cmaa2.IsActive())
            {
                return;
            }

            renderer.EnqueuePass(_pass);
        }
    }
}