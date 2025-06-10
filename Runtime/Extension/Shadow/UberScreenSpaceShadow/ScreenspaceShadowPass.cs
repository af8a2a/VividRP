using Features.Shadow.ScreenSpaceShadow.PCSSShadow;
using Features.Shadow.UberScreenSpaceShadow;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class ScreenspaceShadowPass : ScriptableRenderPass
    {
        // Public Variables

        // Private Variables
        // private ComputeShader m_ScreenSpaceDirectionalShadowsCS;
        // private int m_ClassifyTilesKernel;
        // private int m_SSShadowsKernel;
        // private int m_BilateralHKernel;
        // private int m_BilateralVKernel;

        // Constants
        private const int c_screenSpaceShadowsTileSize = 16;

        // Statics


        // public ScreenSpaceDirectionalShadowsPass(RenderPassEvent evt, ComputeShader ssDirectionalShadowsCS)
        // {
        //     base.renderPassEvent = evt;
        //     m_ScreenSpaceDirectionalShadowsCS = ssDirectionalShadowsCS;
        //
        //     m_ClassifyTilesKernel = m_ScreenSpaceDirectionalShadowsCS.FindKernel("ShadowClassifyTiles");
        //     m_SSShadowsKernel = m_ScreenSpaceDirectionalShadowsCS.FindKernel("ScreenSpaceShadowmap");
        //     m_BilateralHKernel = m_ScreenSpaceDirectionalShadowsCS.FindKernel("BilateralFilterH");
        //     m_BilateralVKernel = m_ScreenSpaceDirectionalShadowsCS.FindKernel("BilateralFilterV");
        // }

        private int m_ScreenSpaceShadowmapTextureID;

        public ScreenspaceShadowPass()
        {
            profilingSampler = new ProfilingSampler(nameof(ScreenspaceShadowPass));
        }

        private class PassData
        {
            // Compute shader
            internal ComputeShader classifyShader;
            internal ComputeShader resolveShader;
            internal ComputeShader bilateralShader;

            internal int classifyTilesKernel;
            internal int shadowmapKernel;
            internal int bilateralHKernel;
            internal int bilateralVKernel;

            internal int numTilesX;
            internal int numTilesY;

            // Compute Buffers
            internal BufferHandle dispatchIndirectBuffer;
            internal BufferHandle tileListBuffer;

            // Texture
            internal TextureHandle dirShadowmapTex;
            internal TextureHandle screenSpaceShadowmapTex;
            internal Vector2Int screenSpaceShadowmapSize;
            internal TextureHandle normalGBuffer;

            internal int camHistoryFrameCount;
            internal UniversalShadowData shadowData;

        }

        /// <summary>
        /// Initialize the shared pass data.
        /// </summary>
        /// <param name="passData"></param>
        private void InitPassData(RenderGraph renderGraph, PassData passData,
            UniversalCameraData cameraData,
            UniversalResourceData resourceData,
            int historyFramCount)
        {
            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<ShadowRuntimeResource>();

            
            
            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.graphicsFormat = GraphicsFormat.R32_SFloat;
            desc.depthBufferBits = 0;
            desc.enableRandomWrite = true;

            // m_ClassifyTilesKernel = passData.classifyShader.FindKernel("ShadowClassifyTiles");
            // m_SSShadowsKernel = m_ScreenSpaceDirectionalShadowsCS.FindKernel("ScreenSpaceShadowmap");
            // m_BilateralHKernel = m_ScreenSpaceDirectionalShadowsCS.FindKernel("BilateralFilterH");
            // m_BilateralVKernel = m_ScreenSpaceDirectionalShadowsCS.FindKernel("BilateralFilterV");

            passData.classifyShader = runtimeShaders.shadowClassifyShader;
            passData.classifyTilesKernel = passData.classifyShader.FindKernel("ShadowClassifyTiles");
            passData.resolveShader = runtimeShaders.shadowmapResolveShader;
            passData.shadowmapKernel = passData.resolveShader.FindKernel("ShadowResolve");;

            passData.bilateralShader = runtimeShaders.shadowmapFilterShader;
            passData.bilateralHKernel =passData.bilateralShader.FindKernel("BilateralFilterH");
            passData.bilateralVKernel =passData.bilateralShader.FindKernel("BilateralFilterV");

            passData.camHistoryFrameCount = historyFramCount;

            var width = cameraData.cameraTargetDescriptor.width;
            var height = cameraData.cameraTargetDescriptor.height;
            passData.numTilesX = RenderingUtilsExt.DivRoundUp(width, c_screenSpaceShadowsTileSize);
            passData.numTilesY = RenderingUtilsExt.DivRoundUp(height, c_screenSpaceShadowsTileSize);

            
            var bufferSystem = GraphicsBufferSystem.instance;
            var dispatchIndirectBuffer = bufferSystem.GetGraphicsBuffer<uint>(GraphicsBufferSystemBufferID.ScreenSpaceShadowIndirect, 3,
                "dispatchIndirectBuffer", GraphicsBuffer.Target.IndirectArguments);
            passData.dispatchIndirectBuffer = renderGraph.ImportBuffer(dispatchIndirectBuffer);
            var tileListBufferDesc = new BufferDesc(passData.numTilesX * passData.numTilesY, sizeof(uint), "tileListBuffer")
            {
                target = GraphicsBuffer.Target.Structured
            };
            passData.tileListBuffer = renderGraph.CreateBuffer(tileListBufferDesc);


            passData.dirShadowmapTex = resourceData.mainShadowsTexture;
            passData.screenSpaceShadowmapTex = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_ScreenSpaceShadowmapTexture", true, Color.white);
            passData.screenSpaceShadowmapSize = new Vector2Int(desc.width, desc.height);

            // passData.normalGBuffer = resourceData.cameraNormalsTexture; 
        }


        private static void ExecutePass(PassData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;


            cmd.SetComputeFloatParam(data.classifyShader, ShaderConstants._CamHistoryFrameCount, data.camHistoryFrameCount);

            // BuildIndirect
            {
                cmd.SetComputeBufferParam(data.classifyShader, data.classifyTilesKernel, ShaderConstants.g_DispatchIndirectBuffer, data.dispatchIndirectBuffer);
                cmd.SetComputeBufferParam(data.classifyShader, data.classifyTilesKernel, ShaderConstants.g_TileList, data.tileListBuffer);

                cmd.SetComputeTextureParam(data.classifyShader, data.classifyTilesKernel, ShaderConstants._DirShadowmapTexture, data.dirShadowmapTex);
                cmd.SetComputeTextureParam(data.classifyShader, data.classifyTilesKernel, ShaderConstants._SSDirShadowmapTexture, data.screenSpaceShadowmapTex);

                cmd.DispatchCompute(data.classifyShader, data.classifyTilesKernel, data.numTilesX, data.numTilesY, 1);
            }

            // PCSS ScreenSpaceShadowmap
            {
                cmd.SetComputeTextureParam(data.resolveShader, data.shadowmapKernel, ShaderConstants._DirShadowmapTexture, data.dirShadowmapTex);
                cmd.SetComputeTextureParam(data.resolveShader, data.shadowmapKernel, ShaderConstants._Shadowmap, data.screenSpaceShadowmapTex);
            
                // Indirect buffer & dispatch
                cmd.SetComputeBufferParam(data.resolveShader, data.shadowmapKernel, ShaderConstants.g_TileList, data.tileListBuffer);
                cmd.DispatchCompute(data.resolveShader, data.shadowmapKernel, data.dispatchIndirectBuffer, argsOffset: 0);
            }
            
            {
                cmd.SetComputeTextureParam(data.bilateralShader, data.bilateralHKernel, ShaderConstants._DirShadowmapTexture, data.dirShadowmapTex);
                cmd.SetComputeTextureParam(data.bilateralShader, data.bilateralHKernel, ShaderConstants._BilateralTexture, data.screenSpaceShadowmapTex);

                // Indirect buffer & dispatch
                cmd.SetComputeBufferParam(data.bilateralShader, data.bilateralHKernel, ShaderConstants.g_TileList, data.tileListBuffer);
                cmd.DispatchCompute(data.bilateralShader, data.bilateralHKernel, data.dispatchIndirectBuffer, argsOffset: 0);
                
                
                cmd.SetComputeTextureParam(data.bilateralShader, data.bilateralVKernel, ShaderConstants._DirShadowmapTexture, data.dirShadowmapTex);
                cmd.SetComputeTextureParam(data.bilateralShader, data.bilateralVKernel, ShaderConstants._BilateralTexture, data.screenSpaceShadowmapTex);

                // Indirect buffer & dispatch
                cmd.SetComputeBufferParam(data.bilateralShader, data.bilateralVKernel, ShaderConstants.g_TileList, data.tileListBuffer);
                cmd.DispatchCompute(data.bilateralShader, data.bilateralVKernel, data.dispatchIndirectBuffer, argsOffset: 0);

            }


            cmd.SetKeyword(ShaderGlobalKeywords.MainLightShadows, false);
            cmd.SetKeyword(ShaderGlobalKeywords.MainLightShadowCascades, false);
            cmd.SetKeyword(ShaderGlobalKeywords.MainLightShadowScreen, true);

            
        }

        internal TextureHandle Render(RenderGraph renderGraph, ContextContainer frameData)
        {
            int historyFramCount = 0;
            var historyRTSystem = HistoryFrameRTSystem.GetOrCreate(frameData.Get<UniversalCameraData>().camera);
            if (historyRTSystem != null)
                historyFramCount = historyRTSystem.historyFrameCount;

            using (var builder = renderGraph.AddComputePass<PassData>("Render SS Shadow", out var passData, profilingSampler))
            {
                // Access resources
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();
                UniversalShadowData shadowData = frameData.Get<UniversalShadowData>();


                // Setup passData
                InitPassData(renderGraph, passData, cameraData, resourceData, historyFramCount);
                passData.shadowData = shadowData;
                // Setup builder state
                builder.UseBuffer(passData.dispatchIndirectBuffer, AccessFlags.ReadWrite);
                builder.UseBuffer(passData.tileListBuffer, AccessFlags.ReadWrite);
                builder.UseTexture(passData.dirShadowmapTex, AccessFlags.Read);
                builder.UseTexture(passData.screenSpaceShadowmapTex, AccessFlags.ReadWrite);

                builder.AllowPassCulling(true);
                builder.AllowGlobalStateModification(true);

                builder.EnableAsyncCompute(true);

                builder.SetRenderFunc((PassData data, ComputeGraphContext context) => { ExecutePass(data, context); });
                builder.SetGlobalTextureAfterPass(passData.screenSpaceShadowmapTex, ShaderConstants._ScreenSpaceShadowmapTexture);

                return passData.screenSpaceShadowmapTex;
            }
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            resourceData.mainShadowsTexture = Render(renderGraph, frameData);
        }

        static class ShaderConstants
        {
            public static readonly int g_DispatchIndirectBuffer = Shader.PropertyToID("g_DispatchIndirectBuffer");
            public static readonly int g_TileList = Shader.PropertyToID("g_TileList");

            public static readonly int _DirShadowmapTexture = Shader.PropertyToID("_DirShadowmapTexture");
            public static readonly int _SSDirShadowmapTexture = Shader.PropertyToID("_SSDirShadowmapTexture");
            public static readonly int _ScreenSpaceShadowmapTexture = Shader.PropertyToID("_ScreenSpaceShadowmapTexture");
            public static readonly int _Shadowmap = Shader.PropertyToID("_Shadowmap");
            public static readonly int _BilateralTexture = Shader.PropertyToID("_BilateralTexture");
            public static readonly int _CamHistoryFrameCount = Shader.PropertyToID("_CamHistoryFrameCount");
            public static readonly int _GBuffer2 = Shader.PropertyToID("_GBuffer2");

            public static readonly int _RayTracingShadowsTextureRW = Shader.PropertyToID("_RayTracingShadowsTextureRW");
            public static readonly int _StencilTexture = Shader.PropertyToID("_StencilTexture");
        }
    }
}