using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    internal class TileClusterDebugPass : ScriptableRenderPass
    {
        // Profiling tag

        // Public Variables

        // Private Variables
        private Material m_Material;
        private RTHandle m_RenderTarget;
        private ShaderVariablesLightList m_LightCBuffer;
        private DebugTileClusterMode m_DebugMode;
        private int m_ClusterDebugID;
        // Constants

        // Statics


        public TileClusterDebugPass(Material mat)
        {
            base.profilingSampler = new ProfilingSampler(nameof(TileClusterDebugPass));
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            m_Material = mat;
            m_DebugMode = DebugTileClusterMode.None;
            m_ClusterDebugID = 0;
        }



        public void Setup(UniversalCameraData cameraData, DebugTileClusterMode debugMode, int clusterDebugID, ShaderVariablesLightList lightCBuffer)
        {
            m_DebugMode = debugMode;
            m_LightCBuffer = lightCBuffer;
            m_ClusterDebugID = clusterDebugID;
        }

        /// <inheritdoc/>
        //public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        //{
        //}

        /// <inheritdoc/>
        //public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        //{
        //    var cmd = renderingData.commandBuffer;
        //    var sourceTexture = renderingData.cameraData.renderer.cameraColorTargetHandle;
        //    Vector2 viewportScale = sourceTexture.useScaling ? new Vector2(sourceTexture.rtHandleProperties.rtHandleScale.x, sourceTexture.rtHandleProperties.rtHandleScale.y) : Vector2.one;

        //    using (new ProfilingScope(cmd, new ProfilingSampler("TileClusterDebug")))
        //    {
        //        m_Material.SetFloat("_DebugTileClusterMode", (float)m_DebugMode);
        //        m_Material.SetInteger("_ClusterDebugID", m_ClusterDebugID);
        //        ConstantBuffer.Push(cmd, m_LightCBuffer, m_Material, Shader.PropertyToID("ShaderVariablesLightList"));

        //        Blitter.BlitTexture(cmd, viewportScale, m_Material, 0);
        //    }
        //}

        private class PassData
        {
            internal Material material;
            internal ShaderVariablesLightList lightCBuffer;
            internal Vector2 viewportScale;
        }

        static void ExecutePass(PassData data, UnsafeGraphContext context)
        {
            CommandBuffer unsafeCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

            ConstantBuffer.Push(unsafeCmd, data.lightCBuffer, data.material, Shader.PropertyToID("ShaderVariablesLightList"));

            Blitter.BlitTexture(unsafeCmd, data.viewportScale, data.material, 0);
        }

        internal void RenderTileClusterDebug(RenderGraph renderGraph, ContextContainer frameData, UniversalCameraData cameraData, DebugTileClusterMode debugMode, int clusterDebugID, DebugClusterCategory debugCategory)
        {
            using (var builder = renderGraph.AddUnsafePass<PassData>("Tile Cluster Debug", out var passData, base.profilingSampler))
            {
                // Access resources.
                if (!frameData.Contains<GPULights.GPULightsOutPassData>())
                    return;

                var gpuLightsOutData = frameData.Get<GPULights.GPULightsOutPassData>();

                LightCategory _DebugCategory = LightCategory.Punctual;
                switch (debugCategory)
                {
                    case DebugClusterCategory.PunctualLights:
                        _DebugCategory = LightCategory.Punctual;
                        break;
                    case DebugClusterCategory.ReflectionProbes:
                        _DebugCategory = LightCategory.Env;
                        break;
                }

                m_Material.SetFloat("_DebugTileClusterMode", (float)debugMode);
                m_Material.SetInteger("_ClusterDebugID", clusterDebugID);
                m_Material.SetFloat("_YFilp", cameraData.cameraType == CameraType.Game ? 1.0f : 0.0f);
                m_Material.SetInteger("_DebugCategory", (int)_DebugCategory);
                Vector2 viewportScale = Vector2.one;
                passData.material = m_Material;
                passData.lightCBuffer = gpuLightsOutData.lightListCB;
                passData.viewportScale = viewportScale;


                // Declare input/output textures

                // Setup builder state
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
            }
        }

        /// <summary>
        /// Clean up resources used by this pass.
        /// </summary>
        public void Dispose()
        {

        }


        internal class ShaderConstants
        {
            public static readonly int _DebugHDRModeId = Shader.PropertyToID("_DebugHDRMode");
        }

    }
}
