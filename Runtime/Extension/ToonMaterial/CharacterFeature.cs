using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    public class CharacterFeature : ScriptableRendererFeature
    {
        private CharacterLightingPass _pass;
        public override void Create()
        {
            _pass = new CharacterLightingPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(_pass);
        }
    }
}