#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class VividPreIntegratedFGDSystem : VividSubsystem<VividPreIntegratedFGDSystem>
    {
        private VividPreIntegratedFGDTextures m_Textures;
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
            m_Textures?.Dispose();
            m_Textures = null;
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
            m_Textures ??= new VividPreIntegratedFGDTextures();
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_Textures.Create(resources, cmd);
            m_SlabLut ??= new VividSlabLut();
            bool slabLutReady = m_SlabLut.Create(
                resources?.SlabLutCompute, cmd);

            var data = frameData.GetOrCreate<VividPreIntegratedFGDData>();
            data.SetTextures(m_Textures.GGXDisneyDiffuseTexture, m_Textures.CharlieAndFabricTexture);
            data.slabLutTexture = slabLutReady ? m_SlabLut.Texture : null;
        }
    }
}
