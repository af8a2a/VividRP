using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Sampling;

namespace VividRP.Runtime
{
    public class BlueNoise : IDisposable
    {
        static BlueNoise s_Instance;

        public static BlueNoise Instance => s_Instance;

        static readonly int s_ScramblingTileId = Shader.PropertyToID("_SobolScramblingTile");
        static readonly int s_RankingTileId = Shader.PropertyToID("_SobolRankingTile");
        static readonly int s_ScramblingTile1SPPId = Shader.PropertyToID("_SobolScramblingTile1SPP");
        static readonly int s_RankingTile1SPPId = Shader.PropertyToID("_SobolRankingTile1SPP");
        static readonly int s_ScramblingTile8SPPId = Shader.PropertyToID("_SobolScramblingTile8SPP");
        static readonly int s_RankingTile8SPPId = Shader.PropertyToID("_SobolRankingTile8SPP");
        static readonly int s_ScramblingTile256SPPId = Shader.PropertyToID("_SobolScramblingTile256SPP");
        static readonly int s_RankingTile256SPPId = Shader.PropertyToID("_SobolRankingTile256SPP");
        static readonly int s_OwenScrambledSequenceId = Shader.PropertyToID("_SobolOwenScrambledSequence");
        static readonly int s_SobolMatricesBufferId = Shader.PropertyToID("_SobolMatricesBuffer");

        RTHandle m_ScramblingTile1SPP;
        RTHandle m_RankingTile1SPP;
        RTHandle m_ScramblingTile8SPP;
        RTHandle m_RankingTile8SPP;
        RTHandle m_ScramblingTile;
        RTHandle m_RankingTile;
        RTHandle m_OwenScrambledSequence;
        GraphicsBuffer m_SobolMatricesBuffer;

        TextureHandle m_ScramblingTile1SPPHandle;
        TextureHandle m_RankingTile1SPPHandle;
        TextureHandle m_ScramblingTile8SPPHandle;
        TextureHandle m_RankingTile8SPPHandle;
        TextureHandle m_ScramblingTileHandle;
        TextureHandle m_RankingTileHandle;
        TextureHandle m_OwenScrambledSequenceHandle;
        BufferHandle m_SobolMatricesBufferHandle;
        BlueNoise() { }

        public static void Initialize()
        {
            if (s_Instance != null)
                return;

            var resources = PipelineResourceManager.Get<BlueNoiseResources>();

            var instance = new BlueNoise();

            if (resources.ScramblingTile1SPP != null)
                instance.m_ScramblingTile1SPP = RTHandles.Alloc(resources.ScramblingTile1SPP);
            if (resources.RankingTile1SPP != null)
                instance.m_RankingTile1SPP = RTHandles.Alloc(resources.RankingTile1SPP);
            if (resources.ScramblingTile8SPP != null)
                instance.m_ScramblingTile8SPP = RTHandles.Alloc(resources.ScramblingTile8SPP);
            if (resources.RankingTile8SPP != null)
                instance.m_RankingTile8SPP = RTHandles.Alloc(resources.RankingTile8SPP);
            if (resources.ScramblingTile != null)
                instance.m_ScramblingTile = RTHandles.Alloc(resources.ScramblingTile);
            if (resources.RankingTile != null)
                instance.m_RankingTile = RTHandles.Alloc(resources.RankingTile);
            if (resources.OwenScrambledSequence != null)
                instance.m_OwenScrambledSequence = RTHandles.Alloc(resources.OwenScrambledSequence);

            int sobolBufferSize = (int)(SobolData.SobolDims * SobolData.SobolSize);
            instance.m_SobolMatricesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, sobolBufferSize, Marshal.SizeOf<uint>());
            instance.m_SobolMatricesBuffer.SetData(SobolData.SobolMatrices);

            s_Instance = instance;
        }

        public static void Cleanup()
        {
            s_Instance?.Dispose();
            s_Instance = null;
        }

        public void ImportResources(RenderGraph renderGraph)
        {
            m_ScramblingTile1SPPHandle = default;
            m_RankingTile1SPPHandle = default;
            m_ScramblingTile8SPPHandle = default;
            m_RankingTile8SPPHandle = default;
            m_ScramblingTileHandle = default;
            m_RankingTileHandle = default;
            m_OwenScrambledSequenceHandle = default;
            m_SobolMatricesBufferHandle = default;

            if (m_ScramblingTile1SPP != null)
                m_ScramblingTile1SPPHandle = renderGraph.ImportTexture(m_ScramblingTile1SPP);
            if (m_RankingTile1SPP != null)
                m_RankingTile1SPPHandle = renderGraph.ImportTexture(m_RankingTile1SPP);
            if (m_ScramblingTile8SPP != null)
                m_ScramblingTile8SPPHandle = renderGraph.ImportTexture(m_ScramblingTile8SPP);
            if (m_RankingTile8SPP != null)
                m_RankingTile8SPPHandle = renderGraph.ImportTexture(m_RankingTile8SPP);
            if (m_ScramblingTile != null)
                m_ScramblingTileHandle = renderGraph.ImportTexture(m_ScramblingTile);
            if (m_RankingTile != null)
                m_RankingTileHandle = renderGraph.ImportTexture(m_RankingTile);
            if (m_OwenScrambledSequence != null)
                m_OwenScrambledSequenceHandle = renderGraph.ImportTexture(m_OwenScrambledSequence);

            if (m_SobolMatricesBuffer != null)
            {
                m_SobolMatricesBufferHandle = renderGraph.ImportBuffer(m_SobolMatricesBuffer);
            }
        }

        public void RegisterPassResources(IRenderPass pass)
        {
            if (pass == null)
                return;

            if (m_ScramblingTile1SPPHandle.IsValid())
                PassRecorder.RegisterImportedTextureForPass(pass, m_ScramblingTile1SPPHandle);
            if (m_RankingTile1SPPHandle.IsValid())
                PassRecorder.RegisterImportedTextureForPass(pass, m_RankingTile1SPPHandle);
            if (m_ScramblingTile8SPPHandle.IsValid())
                PassRecorder.RegisterImportedTextureForPass(pass, m_ScramblingTile8SPPHandle);
            if (m_RankingTile8SPPHandle.IsValid())
                PassRecorder.RegisterImportedTextureForPass(pass, m_RankingTile8SPPHandle);
            if (m_ScramblingTileHandle.IsValid())
                PassRecorder.RegisterImportedTextureForPass(pass, m_ScramblingTileHandle);
            if (m_RankingTileHandle.IsValid())
                PassRecorder.RegisterImportedTextureForPass(pass, m_RankingTileHandle);
            if (m_OwenScrambledSequenceHandle.IsValid())
                PassRecorder.RegisterImportedTextureForPass(pass, m_OwenScrambledSequenceHandle);
            if (m_SobolMatricesBufferHandle.IsValid())
                PassRecorder.RegisterImportedBufferForPass(pass, m_SobolMatricesBufferHandle);
        }

        public void Bind(CommandBuffer cmd)
        {
            if (m_ScramblingTile1SPP != null)
                cmd.SetGlobalTexture(s_ScramblingTile1SPPId, m_ScramblingTile1SPP);
            if (m_RankingTile1SPP != null)
                cmd.SetGlobalTexture(s_RankingTile1SPPId, m_RankingTile1SPP);
            if (m_ScramblingTile8SPP != null)
                cmd.SetGlobalTexture(s_ScramblingTile8SPPId, m_ScramblingTile8SPP);
            if (m_RankingTile8SPP != null)
                cmd.SetGlobalTexture(s_RankingTile8SPPId, m_RankingTile8SPP);
            if (m_ScramblingTile != null)
            {
                cmd.SetGlobalTexture(s_ScramblingTileId, m_ScramblingTile);
                cmd.SetGlobalTexture(s_ScramblingTile256SPPId, m_ScramblingTile);
            }
            if (m_RankingTile != null)
            {
                cmd.SetGlobalTexture(s_RankingTileId, m_RankingTile);
                cmd.SetGlobalTexture(s_RankingTile256SPPId, m_RankingTile);
            }
            if (m_OwenScrambledSequence != null)
                cmd.SetGlobalTexture(s_OwenScrambledSequenceId, m_OwenScrambledSequence);
            if (m_SobolMatricesBuffer != null)
                cmd.SetGlobalBuffer(s_SobolMatricesBufferId, m_SobolMatricesBuffer);
        }

        public void Bind(ComputeCommandBuffer cmd)
        {
            if (m_ScramblingTile1SPPHandle.IsValid())
                cmd.SetGlobalTexture(s_ScramblingTile1SPPId, m_ScramblingTile1SPPHandle);
            if (m_RankingTile1SPPHandle.IsValid())
                cmd.SetGlobalTexture(s_RankingTile1SPPId, m_RankingTile1SPPHandle);
            if (m_ScramblingTile8SPPHandle.IsValid())
                cmd.SetGlobalTexture(s_ScramblingTile8SPPId, m_ScramblingTile8SPPHandle);
            if (m_RankingTile8SPPHandle.IsValid())
                cmd.SetGlobalTexture(s_RankingTile8SPPId, m_RankingTile8SPPHandle);
            if (m_ScramblingTileHandle.IsValid())
            {
                cmd.SetGlobalTexture(s_ScramblingTileId, m_ScramblingTileHandle);
                cmd.SetGlobalTexture(s_ScramblingTile256SPPId, m_ScramblingTileHandle);
            }
            if (m_RankingTileHandle.IsValid())
            {
                cmd.SetGlobalTexture(s_RankingTileId, m_RankingTileHandle);
                cmd.SetGlobalTexture(s_RankingTile256SPPId, m_RankingTileHandle);
            }
            if (m_OwenScrambledSequenceHandle.IsValid())
                cmd.SetGlobalTexture(s_OwenScrambledSequenceId, m_OwenScrambledSequenceHandle);
            if (m_SobolMatricesBufferHandle.IsValid())
                cmd.SetGlobalBuffer(s_SobolMatricesBufferId, m_SobolMatricesBufferHandle);
        }


        public void Bind(ComputeCommandBuffer cmd, ComputeShader cs, int kernel)
        {
            if (m_ScramblingTile1SPPHandle.IsValid())
                cmd.SetComputeTextureParam(cs, kernel, s_ScramblingTile1SPPId, m_ScramblingTile1SPPHandle);
            if (m_RankingTile1SPPHandle.IsValid())
                cmd.SetComputeTextureParam(cs, kernel, s_RankingTile1SPPId, m_RankingTile1SPPHandle);
            if (m_ScramblingTile8SPPHandle.IsValid())
                cmd.SetComputeTextureParam(cs, kernel, s_ScramblingTile8SPPId, m_ScramblingTile8SPPHandle);
            if (m_RankingTile8SPPHandle.IsValid())
                cmd.SetComputeTextureParam(cs, kernel, s_RankingTile8SPPId, m_RankingTile8SPPHandle);
            if (m_ScramblingTileHandle.IsValid())
            {
                cmd.SetComputeTextureParam(cs, kernel, s_ScramblingTileId, m_ScramblingTileHandle);
                cmd.SetComputeTextureParam(cs, kernel, s_ScramblingTile256SPPId, m_ScramblingTileHandle);
            }
            if (m_RankingTileHandle.IsValid())
            {
                cmd.SetComputeTextureParam(cs, kernel, s_RankingTileId, m_RankingTileHandle);
                cmd.SetComputeTextureParam(cs, kernel, s_RankingTile256SPPId, m_RankingTileHandle);
            }
            if (m_OwenScrambledSequenceHandle.IsValid())
                cmd.SetComputeTextureParam(cs, kernel, s_OwenScrambledSequenceId, m_OwenScrambledSequenceHandle);
            if (m_SobolMatricesBufferHandle.IsValid())
                cmd.SetComputeBufferParam(cs, kernel, s_SobolMatricesBufferId, m_SobolMatricesBufferHandle);
        }

        public void Bind(ComputeCommandBuffer cmd, RayTracingShader shader)
        {
            if (m_ScramblingTile1SPPHandle.IsValid())
                cmd.SetRayTracingTextureParam(shader, s_ScramblingTile1SPPId, m_ScramblingTile1SPPHandle);
            if (m_RankingTile1SPPHandle.IsValid())
                cmd.SetRayTracingTextureParam(shader, s_RankingTile1SPPId, m_RankingTile1SPPHandle);
            if (m_ScramblingTile8SPPHandle.IsValid())
                cmd.SetRayTracingTextureParam(shader, s_ScramblingTile8SPPId, m_ScramblingTile8SPPHandle);
            if (m_RankingTile8SPPHandle.IsValid())
                cmd.SetRayTracingTextureParam(shader, s_RankingTile8SPPId, m_RankingTile8SPPHandle);
            if (m_ScramblingTileHandle.IsValid())
            {
                cmd.SetRayTracingTextureParam(shader, s_ScramblingTileId, m_ScramblingTileHandle);
                cmd.SetRayTracingTextureParam(shader, s_ScramblingTile256SPPId, m_ScramblingTileHandle);
            }
            if (m_RankingTileHandle.IsValid())
            {
                cmd.SetRayTracingTextureParam(shader, s_RankingTileId, m_RankingTileHandle);
                cmd.SetRayTracingTextureParam(shader, s_RankingTile256SPPId, m_RankingTileHandle);
            }
            if (m_OwenScrambledSequenceHandle.IsValid())
                cmd.SetRayTracingTextureParam(shader, s_OwenScrambledSequenceId, m_OwenScrambledSequenceHandle);
            if (m_SobolMatricesBufferHandle.IsValid())
                cmd.SetRayTracingBufferParam(shader, s_SobolMatricesBufferId, m_SobolMatricesBufferHandle);
        }


        public void Dispose()
        {
            m_ScramblingTile1SPP?.Release();
            m_RankingTile1SPP?.Release();
            m_ScramblingTile8SPP?.Release();
            m_RankingTile8SPP?.Release();
            m_ScramblingTile?.Release();
            m_RankingTile?.Release();
            m_OwenScrambledSequence?.Release();
            m_SobolMatricesBuffer?.Dispose();

            m_ScramblingTile1SPP = null;
            m_RankingTile1SPP = null;
            m_ScramblingTile8SPP = null;
            m_RankingTile8SPP = null;
            m_ScramblingTile = null;
            m_RankingTile = null;
            m_OwenScrambledSequence = null;
            m_SobolMatricesBuffer = null;
        }
    }
}
