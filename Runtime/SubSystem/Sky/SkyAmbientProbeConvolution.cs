using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class SkyAmbientProbeConvolution
    {
        private enum AmbientProbeConvolutionRebuildReason
        {
            None,
            MissingBuffer,
            SkyChanged
        }

        private const string KernelName = "AmbientProbeConvolution";
        private const int AmbientProbePackedCoefficientCount = 7;
        private const int AmbientProbePackedCoefficientStride = sizeof(float) * 4;

        private static readonly int AmbientProbeInputCubemapId = Shader.PropertyToID("_AmbientProbeInputCubemap");
        private static readonly int AmbientProbeOutputBufferId = Shader.PropertyToID("_AmbientProbeOutputBuffer");
        private static readonly int VividAmbientProbeDataId = Shader.PropertyToID("_VividAmbientProbeData");
        private static readonly int SkyConvolutionTintId = Shader.PropertyToID("_SkyConvolutionTint");
        private static readonly int SkyConvolutionParamsId = Shader.PropertyToID("_SkyConvolutionParams");
        private static readonly ProfilingSampler s_ConvolutionMissingBufferSampler = new("SkyAmbientProbeConvolution.Convolve (MissingBuffer)");
        private static readonly ProfilingSampler s_ConvolutionSkyChangedSampler = new("SkyAmbientProbeConvolution.Convolve (SkyChanged)");
        private static readonly Vector4[] s_DefaultAmbientProbeData =
        {
            Vector4.zero,
            Vector4.zero,
            Vector4.zero,
            Vector4.zero,
            Vector4.zero,
            Vector4.zero,
            new Vector4(0.0f, 0.0f, 0.0f, 1.0f)
        };

        private ComputeShader m_ComputeShader;
        private int m_Kernel = -1;
        private GraphicsBuffer m_AmbientProbeBuffer;
        private GraphicsBuffer m_DefaultAmbientProbeBuffer;
        private bool m_HasConvolvedSkyHash;
        private int m_ConvolvedSkyHash;

        internal bool IsSupported =>
            m_ComputeShader != null
            && m_Kernel >= 0
            && SystemInfo.supportsComputeShaders;

        internal void Build(VividRPCoreResources resources)
        {
            Cleanup();

            m_ComputeShader = resources?.SkyAmbientProbeConvolutionCompute;
            m_Kernel = m_ComputeShader != null ? m_ComputeShader.FindKernel(KernelName) : -1;
            EnsureDefaultAmbientProbeBuffer();
        }

        internal void Cleanup()
        {
            m_AmbientProbeBuffer?.Release();
            m_AmbientProbeBuffer = null;
            m_DefaultAmbientProbeBuffer?.Release();
            m_DefaultAmbientProbeBuffer = null;
            m_ComputeShader = null;
            m_Kernel = -1;
            m_HasConvolvedSkyHash = false;
            m_ConvolvedSkyHash = 0;
        }

        internal void RequestUpdate(
            CommandBuffer cmd,
            Texture sourceCubemap,
            Color tint,
            float exposureStops,
            float rotation,
            int skyHash)
        {
            if (!IsSupported || cmd == null || sourceCubemap == null)
                return;

            var rebuildReason = ResolveRebuildReason(skyHash);
            if (rebuildReason == AmbientProbeConvolutionRebuildReason.None)
                return;

            EnsureAmbientProbeBuffer();

            using (new ProfilingScope(cmd, GetConvolutionSampler(rebuildReason)))
            {
                cmd.SetComputeTextureParam(m_ComputeShader, m_Kernel, AmbientProbeInputCubemapId, sourceCubemap);
                cmd.SetComputeBufferParam(m_ComputeShader, m_Kernel, AmbientProbeOutputBufferId, m_AmbientProbeBuffer);
                cmd.SetComputeVectorParam(m_ComputeShader, SkyConvolutionTintId, new Vector4(tint.r, tint.g, tint.b, tint.a));
                cmd.SetComputeVectorParam(
                    m_ComputeShader,
                    SkyConvolutionParamsId,
                    new Vector4(HDRISkyVolume.ResolveExposureMultiplier(exposureStops), -rotation, 0.0f, 0.0f));
                Hammersley.BindConstants(cmd, m_ComputeShader);
                cmd.DispatchCompute(m_ComputeShader, m_Kernel, 1, 1, 1);
            }

            m_HasConvolvedSkyHash = true;
            m_ConvolvedSkyHash = skyHash;
        }

        internal void BindGlobalBuffer(CommandBuffer cmd, bool useDefault = false)
        {
            if (cmd == null)
                return;

            EnsureDefaultAmbientProbeBuffer();
            cmd.SetGlobalBuffer(
                VividAmbientProbeDataId,
                useDefault || m_AmbientProbeBuffer == null ? m_DefaultAmbientProbeBuffer : m_AmbientProbeBuffer);
        }

        private void EnsureAmbientProbeBuffer()
        {
            if (m_AmbientProbeBuffer != null)
                return;

            m_AmbientProbeBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                AmbientProbePackedCoefficientCount,
                AmbientProbePackedCoefficientStride);
        }

        private void EnsureDefaultAmbientProbeBuffer()
        {
            if (m_DefaultAmbientProbeBuffer != null)
                return;

            m_DefaultAmbientProbeBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                AmbientProbePackedCoefficientCount,
                AmbientProbePackedCoefficientStride);
            m_DefaultAmbientProbeBuffer.SetData(s_DefaultAmbientProbeData);
        }

        private AmbientProbeConvolutionRebuildReason ResolveRebuildReason(int skyHash)
        {
            if (m_AmbientProbeBuffer == null)
                return AmbientProbeConvolutionRebuildReason.MissingBuffer;

            return m_HasConvolvedSkyHash && m_ConvolvedSkyHash == skyHash
                ? AmbientProbeConvolutionRebuildReason.None
                : AmbientProbeConvolutionRebuildReason.SkyChanged;
        }

        private static ProfilingSampler GetConvolutionSampler(AmbientProbeConvolutionRebuildReason reason)
        {
            return reason == AmbientProbeConvolutionRebuildReason.SkyChanged
                ? s_ConvolutionSkyChangedSampler
                : s_ConvolutionMissingBufferSampler;
        }
    }
}
