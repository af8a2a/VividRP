using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Render pass that builds and binds the world light cluster for GPU-side light queries.
    /// Used by path tracing and multi-bounce global illumination.
    /// </summary>
    public class WorldLightClusterPass : ScriptableRenderPass, IDisposable
    {
        private static readonly ProfilingSampler s_ProfilingSampler = new ProfilingSampler("World Light Cluster");

        private WorldLightCluster m_WorldLightCluster;
        private Bounds m_WorldBounds;
        private bool m_IsEnabled = true;

        // Settings
        private int m_MaxLights = 512;
        private int m_GridResolution = 32;
        private float m_CellSize = 10.0f;

        /// <summary>
        /// Gets the world light cluster instance.
        /// </summary>
        public WorldLightCluster Cluster => m_WorldLightCluster;

        /// <summary>
        /// Gets or sets whether the pass is enabled.
        /// </summary>
        public bool IsEnabled
        {
            get => m_IsEnabled;
            set => m_IsEnabled = value;
        }

        public WorldLightClusterPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
            m_WorldLightCluster = new WorldLightCluster();
        }

        /// <summary>
        /// Configures the world light cluster settings.
        /// </summary>
        public void Setup(int maxLights = 512, int gridResolution = 32, float cellSize = 10.0f)
        {
            m_MaxLights = maxLights;
            m_GridResolution = gridResolution;
            m_CellSize = cellSize;

            if (!m_WorldLightCluster.IsInitialized)
            {
                m_WorldLightCluster.Initialize(m_MaxLights, m_GridResolution, m_CellSize);
            }
        }

        /// <summary>
        /// Sets the world bounds for the spatial grid.
        /// </summary>
        public void SetWorldBounds(Bounds bounds)
        {
            m_WorldBounds = bounds;
            if (m_WorldLightCluster.IsInitialized)
            {
                m_WorldLightCluster.SetWorldBounds(bounds);
            }
        }

        /// <summary>
        /// Automatically calculates world bounds from scene.
        /// </summary>
        public void AutoCalculateWorldBounds()
        {
            // Calculate bounds from all renderers in scene
            var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            if (renderers.Length == 0)
            {
                m_WorldBounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
                return;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            // Expand bounds slightly
            bounds.Expand(m_CellSize * 2);
            m_WorldBounds = bounds;

            if (m_WorldLightCluster.IsInitialized)
            {
                m_WorldLightCluster.SetWorldBounds(bounds);
            }
        }


        #region RenderGraph

        private class PassData
        {
            public WorldLightCluster worldLightCluster;
            public Bounds worldBounds;
        }


        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!m_IsEnabled)
                return;

            // Initialize if needed
            if (!m_WorldLightCluster.IsInitialized)
            {
                m_WorldLightCluster.Initialize(m_MaxLights, m_GridResolution, m_CellSize);
            }

            // Auto-calculate bounds if not set
            if (m_WorldBounds.size == Vector3.zero)
            {
                AutoCalculateWorldBounds();
            }

            if (!m_WorldLightCluster.IsInitialized)
                return;

            using (var builder = renderGraph.AddUnsafePass<PassData>(s_ProfilingSampler.name, out var passData, s_ProfilingSampler))
            {
                passData.worldLightCluster = m_WorldLightCluster;
                passData.worldBounds = m_WorldBounds;

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                    // Update cluster with current lights
                    data.worldLightCluster.SetWorldBounds(data.worldBounds);
                    data.worldLightCluster.UpdateCluster();

                    // Bind to shaders
                    data.worldLightCluster.BindGlobalShaderResources(cmd);
                });
            }
        }

        #endregion
        
        public void Dispose()
        {
            m_WorldLightCluster?.Dispose();
            m_WorldLightCluster = null;
        }

        /// <summary>
        /// Cleans up resources.
        /// </summary>
        public void Cleanup()
        {
            m_WorldLightCluster?.Cleanup();
        }
    }
}



