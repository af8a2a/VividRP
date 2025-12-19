using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

namespace UnityEngine.Rendering.Universal
{
    public class ShadowClassifyDebugPass : ScriptableRenderPass
    {
        private ComputeShader m_ClassifyDebugCS;
        private int m_MainKernel;

        private ComputeShader m_HitMaskDebugCS;
        private int m_HitKernel;

        class ShadowClassifyDebugPassData
        {
            // Camera data
            public int actualWidth;
            public int actualHeight;

            // Debug parameters
            public int debugMode;

            // Input buffers / textures
            public BufferHandle tilesBuffer;
            public BufferHandle tileCountBuffer;
            public TextureHandle shadowMaskTexture; // tile-resolution shadow mask

            // Output
            public TextureHandle outputTexture;

            // Blit to Backbuffer
            public UniversalCameraData cameraData;
            public TextureHandle source;
            public TextureHandle destination;
        }

        static ProfilingSampler ShadowClassifyDebugSampler = new ProfilingSampler("Shadow Classify Debug");

        static readonly int s_DebugBufferID = Shader.PropertyToID("cb_debug");
        static readonly int s_TilesBufferID = Shader.PropertyToID("sb_tiles");
        static readonly int s_OutputTextureID = Shader.PropertyToID("rwt2d_output");
        static readonly int s_HitMaskResultsID = Shader.PropertyToID("t2d_hitMaskResults");

        struct DebugConstants
        {
            public uint debugMode;
        }

        public ShadowClassifyDebugPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        internal void RenderShadowClassifyDebug(RenderGraph renderGraph,
            UniversalCameraData cameraData,
            ShadowClassifyDebugMode debugMode,
            BufferHandle tilesBuffer,
            BufferHandle tileCountBuffer,
            TextureHandle shadowMaskTexture,
            TextureHandle dstColor
        )
        {
            if (debugMode == ShadowClassifyDebugMode.None)
                return;

            var runtimeShaders = GraphicsSettings.GetRenderPipelineSettings<RaytracingShadowRuntimeShaders>();
            if (debugMode == ShadowClassifyDebugMode.HitMask)
            {
                if (m_HitMaskDebugCS == null && runtimeShaders != null)
                {
                    m_HitMaskDebugCS = runtimeShaders.fidelityFXShadowHitDebug;
                    if (m_HitMaskDebugCS != null)
                        m_HitKernel = m_HitMaskDebugCS.FindKernel("main");
                }
                if (m_HitMaskDebugCS == null || m_HitKernel < 0)
                    return;
            }
            else
            {
                if (m_ClassifyDebugCS == null && runtimeShaders != null)
                {
                    m_ClassifyDebugCS = runtimeShaders.fidelityFXShadowClassifyDebug;
                    if (m_ClassifyDebugCS != null)
                    {
                        m_MainKernel = m_ClassifyDebugCS.FindKernel("main");
                    }
                }

                if (m_ClassifyDebugCS == null || m_MainKernel < 0)
                    return;
            }

            TextureHandle debugOutput;
            using (var builder = renderGraph.AddComputePass<ShadowClassifyDebugPassData>("Shadow Classify Debug", out var passData,
                       ShadowClassifyDebugSampler))
            {
                builder.EnableAsyncCompute(false);

                // Camera data
                passData.actualWidth = cameraData.actualWidth;
                passData.actualHeight = cameraData.actualHeight;

                // Debug parameters
                passData.debugMode = (int)debugMode - 1; // for classify modes; hit mask uses 0

                
                var bufferSystem = GraphicsBufferSystem.instance;
                var dispatchIndirectBuffer = bufferSystem.GetGraphicsBuffer<uint>(GraphicsBufferSystemBufferID.ShadowTileCountBuffer, 
                    4,
                    "ShadowClassifyTileCount", GraphicsBuffer.Target.IndirectArguments);


                // Input buffers
                passData.tilesBuffer = tilesBuffer;
                passData.tileCountBuffer = renderGraph.ImportBuffer(dispatchIndirectBuffer);
                passData.shadowMaskTexture = shadowMaskTexture;

                // Create output texture
                passData.outputTexture = renderGraph.CreateTexture(new TextureDesc(cameraData.pixelWidth, cameraData.pixelHeight)
                {
                    format = GraphicsFormat.R16G16B16A16_SFloat,
                    enableRandomWrite = true,
                    name = "Shadow Classify Debug",
                    clearBuffer = true,
                });

                builder.UseBuffer(passData.tilesBuffer, AccessFlags.Read);
                builder.UseBuffer(passData.tileCountBuffer, AccessFlags.Read);
                if (passData.shadowMaskTexture.IsValid())
                    builder.UseTexture(passData.shadowMaskTexture, AccessFlags.Read);
                builder.UseTexture(passData.outputTexture, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc<ShadowClassifyDebugPassData>((data, ctx) =>
                {
                    var cmd = ctx.cmd;
                    

                    if ((ShadowClassifyDebugMode)(data.debugMode + 1) == ShadowClassifyDebugMode.HitMask)
                    {
                        // Hit mask debug: uses tile-resolution shadow mask and tile buffer
                        cmd.SetComputeTextureParam(m_HitMaskDebugCS, m_HitKernel, s_HitMaskResultsID, data.shadowMaskTexture);
                        cmd.SetComputeBufferParam(m_HitMaskDebugCS, m_HitKernel, s_TilesBufferID, data.tilesBuffer);
                        cmd.SetComputeTextureParam(m_HitMaskDebugCS, m_HitKernel, s_OutputTextureID, data.outputTexture);

                        int tileWidth = CoreUtils.DivRoundUp(data.actualWidth, 8);
                        int tileHeight = CoreUtils.DivRoundUp(data.actualHeight, 4);
                        cmd.DispatchCompute(m_HitMaskDebugCS, m_HitKernel, tileWidth, tileHeight, 1);
                    }
                    else
                    {
                        // Set debug mode constant buffer
                        var debugConstants = new DebugConstants { debugMode = (uint)data.debugMode };
                        ConstantBuffer.Push(cmd, debugConstants, m_ClassifyDebugCS, s_DebugBufferID);

                        // Set buffers
                        cmd.SetComputeBufferParam(m_ClassifyDebugCS, m_MainKernel, s_TilesBufferID, data.tilesBuffer);
                        cmd.SetComputeTextureParam(m_ClassifyDebugCS, m_MainKernel, s_OutputTextureID, data.outputTexture);

                        // Dispatch compute shader using indirect tile count
                        cmd.DispatchCompute(m_ClassifyDebugCS, m_MainKernel, data.tileCountBuffer, 0);
                    }
                    MipGenerator.instance.Clear(cmd,data.shadowMaskTexture,data.actualWidth,data.actualHeight);

                });

                debugOutput = passData.outputTexture;
            }

            // Blit to backbuffer
            using (var builder = renderGraph.AddRasterRenderPass<ShadowClassifyDebugPassData>("Copy Shadow Classify Debug View", out var passData,
                       ShadowClassifyDebugSampler))
            {
                passData.source = debugOutput;
                passData.destination = dstColor;
                passData.cameraData = cameraData;
                builder.SetRenderAttachment(dstColor, 0);
                builder.UseTexture(passData.source);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc<ShadowClassifyDebugPassData>((data, ctx) =>
                {
                    var cmd = ctx.cmd;
                    bool isRenderToBackBufferTarget = !data.cameraData.isSceneViewCamera;
#if ENABLE_VR && ENABLE_XR_MODULE
                    if (data.cameraData.xr.enabled)
                        isRenderToBackBufferTarget = new RenderTargetIdentifier(((RTHandle)data.destination).nameID, 0, CubemapFace.Unknown, -1) ==
                                                     new RenderTargetIdentifier(data.cameraData.xr.renderTarget, 0, CubemapFace.Unknown, -1);
#endif
                    Vector4 scaleBias = RenderingUtils.GetFinalBlitScaleBias(ctx, data.source, data.destination);
                    if (isRenderToBackBufferTarget)
                        cmd.SetViewport(data.cameraData.pixelRect);

                    Blitter.BlitTexture(cmd, data.source, scaleBias, 0, true);
                });
            }
        }
    }
}
