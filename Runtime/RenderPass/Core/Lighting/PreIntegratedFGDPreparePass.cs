using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class PreIntegratedFGDPreparePass : UnsafePass
    {
        [RenderGraphResource(
            Name = "PreIntegratedFGD_GGXDisneyDiffuse",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_GGXDisneyDiffuseTexture;

        [RenderGraphResource(
            Name = "PreIntegratedFGD_CharlieAndFabric",
            Access = AccessFlags.Write,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_CharlieAndFabricTexture;

        private VividPreIntegratedFGDTextures m_PreIntegratedFGDTextures;

        public PreIntegratedFGDPreparePass()
        {
            profilingSampler = new ProfilingSampler(nameof(PreIntegratedFGDPreparePass));
            m_GGXDisneyDiffuseTexture = VividPreIntegratedFGD.CreateTexture("PreIntegratedFGD_GGXDisneyDiffuse");
            m_CharlieAndFabricTexture = VividPreIntegratedFGD.CreateTexture("PreIntegratedFGD_CharlieAndFabric");
        }

        public override void Create()
        {
            m_PreIntegratedFGDTextures = new VividPreIntegratedFGDTextures();
            m_PreIntegratedFGDTextures.Create(PipelineResourceManager.Get<VividRPCoreResources>());
        }

        public override void Prepare(ContextContainer frameData)
        {
            if (m_PreIntegratedFGDTextures == null || !PassRecorder.IsPassTextureImportActive)
                return;

            if (m_PreIntegratedFGDTextures.GGXDisneyDiffuseTexture != null)
                PassRecorder.ImportTexture(m_GGXDisneyDiffuseTexture, m_PreIntegratedFGDTextures.GGXDisneyDiffuseTexture);

            if (m_PreIntegratedFGDTextures.CharlieAndFabricTexture != null)
                PassRecorder.ImportTexture(m_CharlieAndFabricTexture, m_PreIntegratedFGDTextures.CharlieAndFabricTexture);
        }

        public override void Record(UnsafeGraphContext context)
        {
        }

        public override void Dispose()
        {
            m_PreIntegratedFGDTextures?.Dispose();
            m_PreIntegratedFGDTextures = null;
            m_GGXDisneyDiffuseTexture?.ClearImportedHandle();
            m_CharlieAndFabricTexture?.ClearImportedHandle();
        }
    }
}
