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
        static readonly int s_OwenScrambledSequenceId = Shader.PropertyToID("_SobolOwenScrambledSequence");
        static readonly int s_SobolMatricesBufferId = Shader.PropertyToID("_SobolMatricesBuffer");

        RTHandle m_ScramblingTile;
        RTHandle m_RankingTile;
        RTHandle m_OwenScrambledSequence;
        GraphicsBuffer m_SobolMatricesBuffer;

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
            m_ScramblingTileHandle = default;
            m_RankingTileHandle = default;
            m_OwenScrambledSequenceHandle = default;
            m_SobolMatricesBufferHandle = default;

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
            if (m_ScramblingTile != null)
                cmd.SetGlobalTexture(s_ScramblingTileId, m_ScramblingTile);
            if (m_RankingTile != null)
                cmd.SetGlobalTexture(s_RankingTileId, m_RankingTile);
            if (m_OwenScrambledSequence != null)
                cmd.SetGlobalTexture(s_OwenScrambledSequenceId, m_OwenScrambledSequence);
            if (m_SobolMatricesBuffer != null)
                cmd.SetGlobalBuffer(s_SobolMatricesBufferId, m_SobolMatricesBuffer);
        }

        public void Bind(ComputeCommandBuffer cmd)
        {
            if (m_ScramblingTileHandle.IsValid())
                cmd.SetGlobalTexture(s_ScramblingTileId, m_ScramblingTileHandle);
            if (m_RankingTileHandle.IsValid())
                cmd.SetGlobalTexture(s_RankingTileId, m_RankingTileHandle);
            if (m_OwenScrambledSequenceHandle.IsValid())
                cmd.SetGlobalTexture(s_OwenScrambledSequenceId, m_OwenScrambledSequenceHandle);
            if (m_SobolMatricesBufferHandle.IsValid())
                cmd.SetGlobalBuffer(s_SobolMatricesBufferId, m_SobolMatricesBufferHandle);
        }


        public void Bind(ComputeCommandBuffer cmd, ComputeShader cs, int kernel)
        {
            if (m_ScramblingTileHandle.IsValid())
                cmd.SetComputeTextureParam(cs, kernel, s_ScramblingTileId, m_ScramblingTileHandle);
            if (m_RankingTileHandle.IsValid())
                cmd.SetComputeTextureParam(cs, kernel, s_RankingTileId, m_RankingTileHandle);
            if (m_OwenScrambledSequenceHandle.IsValid())
                cmd.SetComputeTextureParam(cs, kernel, s_OwenScrambledSequenceId, m_OwenScrambledSequenceHandle);
            if (m_SobolMatricesBufferHandle.IsValid())
                cmd.SetComputeBufferParam(cs, kernel, s_SobolMatricesBufferId, m_SobolMatricesBufferHandle);
        }

        public void Bind(ComputeCommandBuffer cmd, RayTracingShader shader)
        {
            if (m_ScramblingTileHandle.IsValid())
                cmd.SetRayTracingTextureParam(shader, s_ScramblingTileId, m_ScramblingTileHandle);
            if (m_RankingTileHandle.IsValid())
                cmd.SetRayTracingTextureParam(shader, s_RankingTileId, m_RankingTileHandle);
            if (m_OwenScrambledSequenceHandle.IsValid())
                cmd.SetRayTracingTextureParam(shader, s_OwenScrambledSequenceId, m_OwenScrambledSequenceHandle);
            if (m_SobolMatricesBufferHandle.IsValid())
                cmd.SetRayTracingBufferParam(shader, s_SobolMatricesBufferId, m_SobolMatricesBufferHandle);
        }


        public void Dispose()
        {
            m_ScramblingTile?.Release();
            m_RankingTile?.Release();
            m_OwenScrambledSequence?.Release();
            m_SobolMatricesBuffer?.Dispose();

            m_ScramblingTile = null;
            m_RankingTile = null;
            m_OwenScrambledSequence = null;
            m_SobolMatricesBuffer = null;
        }
    }
}
