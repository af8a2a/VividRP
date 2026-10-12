#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class VividPreIntegratedFGDSystem : VividSubsystem<VividPreIntegratedFGDSystem>
    {
        private VividSlabLut m_SlabLut;

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod]
#endif
        private static void AutoInitialize()
        {
            Initialize();
        }

        protected override void OnInitialize()
        {
            FrameContextSystem.SubsystemDispose -= OnSubsystemDispose;
            FrameContextSystem.SubsystemDispose += OnSubsystemDispose;
        }

        protected override void OnDeinitialize()
        {
            FrameContextSystem.SubsystemDispose -= OnSubsystemDispose;
            m_SlabLut?.Dispose();
            m_SlabLut = null;
        }

        public new static void Deinitialize()
        {
            VividSubsystem<VividPreIntegratedFGDSystem>.Deinitialize();

#if UNITY_EDITOR
            // Keep the callback wired in editor so preview rendering can lazily rebuild
            // the LUTs after a render-graph or assembly lifecycle reset.
            EnsurePreRenderSubscribed();
            FrameContextSystem.SubsystemDispose -= OnSubsystemDispose;
            FrameContextSystem.SubsystemDispose += OnSubsystemDispose;
#endif
        }

        protected override void OnUpdate(ContextContainer frameData, CommandBuffer cmd)
        {
            using (RenderPassProfilingUtility.PrepareFrameSubsystemPreIntegratedFGDMarker.Auto())
            {
                PrepareFrame(frameData, cmd);
            }
        }

        private static void OnSubsystemDispose()
        {
            Deinitialize();
        }

        internal static void PrepareFrame(ContextContainer frameData, CommandBuffer cmd)
        {
            if (!IsInitialized)
                Initialize();

            if (frameData == null)
                return;

            Instance.PrepareFrameCore(frameData, cmd);
        }

        private void PrepareFrameCore(ContextContainer frameData, CommandBuffer cmd)
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            var openPbrLuts = resources?.OpenPBRLuts;
            bool openPbrReady = openPbrLuts != null && openPbrLuts.IsReady;
            cmd.SetGlobalInt(VividOpenPBRLuts.ReadyId, openPbrReady ? 1 : 0);
            if (openPbrReady)
                openPbrLuts.Bind(cmd);
            else
            {
                // Compute validates declared resources even when all pixels
                // take the Slab branch. Bind inert resources; Ready=0 still
                // rejects native shading instead of using zero-energy LUTs.
                cmd.SetGlobalTexture(VividOpenPBRLuts.TextureIds[0], CoreUtils.blackVolumeTexture);
                cmd.SetGlobalTexture(VividOpenPBRLuts.TextureIds[1], CoreUtils.blackVolumeTexture);
                cmd.SetGlobalTexture(VividOpenPBRLuts.TextureIds[2], TextureXR.GetBlackTextureArray());
            }
            m_SlabLut ??= new VividSlabLut();
            bool slabLutReady = m_SlabLut.Create(
                resources?.SlabLutCompute, cmd);

            var data = frameData.GetOrCreate<VividPreIntegratedFGDData>();
            // Compatibility Slab pixels still need their LUT. Native OpenPBR
            // uses the immutable vendor textures bound above, without Slab FGD.
            data.SetTextures(null, null);
            data.slabLutTexture = slabLutReady ? m_SlabLut.Texture : null;
        }

        internal static void BindOpenPBRLuts(CommandBuffer cmd)
        {
            var luts = PipelineResourceManager.Get<VividRPCoreResources>()?.OpenPBRLuts;
            if (luts == null)
                throw new System.InvalidOperationException("OpenPBR LUT asset is missing. Re-sync PipelineResources.");
            luts.Bind(cmd);
        }
    }
}
