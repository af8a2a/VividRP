using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    /// <summary>
    /// Preserves serialized RenderGraph connections authored before meshlet shadow rendering
    /// was merged into <see cref="CSMShadowPass"/>. This pass intentionally records no work.
    /// </summary>
    public sealed class MeshletShadowPass : UnsafePass
    {
        [RenderGraphResource(Name = "CSMShadowAtlas", Access = AccessFlags.ReadWrite)]
        private RenderGraphTexture m_CSMShadowAtlas;

        public MeshletShadowPass()
        {
            profilingSampler = new ProfilingSampler(nameof(MeshletShadowPass));
        }

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
        }

        public override void Record(UnsafePassContext context)
        {
            // Keep the legacy atlas field alive for pass-field resource forwarding.
            _ = m_CSMShadowAtlas;
        }

        public override void Dispose()
        {
        }
    }
}
