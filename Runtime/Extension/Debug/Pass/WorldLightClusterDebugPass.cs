using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Debug pass for visualizing WorldLightCluster data.
    /// Integrates with Unity's Render Debugger to provide various visualization modes
    /// for the world-space light culling system used in path tracing.
    /// </summary>
    internal class WorldLightClusterDebugPass : ScriptableRenderPass
    {
        // Private Variables
        private Material m_Material;
        private DebugWorldLightClusterMode m_DebugMode;
        private int m_MaxLightsPerCellDisplay;

        // Shader property IDs
        private static readonly int _DebugWorldLightClusterMode = Shader.PropertyToID("_DebugWorldLightClusterMode");
        private static readonly int _YFlip = Shader.PropertyToID("_YFlip");
        private static readonly int _MaxLightsPerCellDisplay = Shader.PropertyToID("_MaxLightsPerCellDisplay");
        private static readonly int _BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");

        /// <summary>
        /// Constructor for WorldLightClusterDebugPass.
        /// </summary>
        /// <param name="material">The debug visualization material.</param>
        public WorldLightClusterDebugPass(Material material)
        {
            base.profilingSampler = new ProfilingSampler(nameof(WorldLightClusterDebugPass));
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            m_Material = material;
            m_DebugMode = DebugWorldLightClusterMode.None;
            m_MaxLightsPerCellDisplay = 32;
        }

        /// <summary>
        /// Setup the debug pass with current settings.
        /// </summary>
        public void Setup(DebugWorldLightClusterMode debugMode, int maxLightsPerCellDisplay = 32)
        {
            m_DebugMode = debugMode;
            m_MaxLightsPerCellDisplay = maxLightsPerCellDisplay;
        }

        /// <summary>
        /// Pass data for RenderGraph execution.
        /// </summary>
        private class PassData
        {
            internal Material material;
            internal Vector2 viewportScale;
            internal DebugWorldLightClusterMode debugMode;
            internal int maxLightsPerCellDisplay;
            internal float yFlip;
        }

        /// <summary>
        /// Execute the debug visualization pass.
        /// </summary>
        private static void ExecutePass(PassData data, RasterGraphContext context)
        {
            if (data.material == null)
                return;

            // Set shader properties
            data.material.SetFloat(_DebugWorldLightClusterMode, (float)data.debugMode);
            data.material.SetFloat(_YFlip, data.yFlip);
            data.material.SetInteger(_MaxLightsPerCellDisplay, data.maxLightsPerCellDisplay);

            // Blit fullscreen
            Blitter.BlitTexture(context.cmd, data.viewportScale, data.material, 0);
        }

        /// <summary>
        /// Render the WorldLightCluster debug visualization using RenderGraph.
        /// </summary>
        /// <param name="renderGraph">The render graph instance.</param>
        /// <param name="cameraData">Camera data for the current frame.</param>
        /// <param name="debugMode">The debug visualization mode to use.</param>
        /// <param name="maxLightsPerCellDisplay">Maximum lights per cell for heatmap scaling.</param>
        /// <param name="dstColor">The destination color texture.</param>
        internal void RenderWorldLightClusterDebug(
            RenderGraph renderGraph,
            UniversalCameraData cameraData,
            DebugWorldLightClusterMode debugMode,
            int maxLightsPerCellDisplay,
            TextureHandle dstColor)
        {
            if (m_Material == null || debugMode == DebugWorldLightClusterMode.None)
                return;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("World Light Cluster Debug", out var passData, base.profilingSampler))
            {
                // Configure pass data
                passData.material = m_Material;
                passData.viewportScale = Vector2.one;
                passData.debugMode = debugMode;
                passData.maxLightsPerCellDisplay = maxLightsPerCellDisplay;
                passData.yFlip = cameraData.cameraType == CameraType.Game ? 1.0f : 0.0f;

                // Set render attachment
                builder.SetRenderAttachment(dstColor, 0);

                // Prevent pass culling
                builder.AllowPassCulling(false);

                // Set render function
                builder.SetRenderFunc<PassData>(ExecutePass);
            }
        }

        /// <summary>
        /// Clean up resources used by this pass.
        /// </summary>
        public void Dispose()
        {
            // Material is managed by DebugHandler, no cleanup needed here
        }
    }
}
