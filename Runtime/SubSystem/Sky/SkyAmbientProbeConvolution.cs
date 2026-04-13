using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class SkyAmbientProbeConvolution
    {
        private const bool EnableAmbientProbeDebugReadback = true;

        private enum AmbientProbeConvolutionRebuildReason
        {
            None,
            MissingBuffer,
            SkyChanged
        }

        private const string DiffuseKernelName = "AmbientProbeConvolutionDiffuse";
        private const string LegacyKernelName = "AmbientProbeConvolution";
        private const int AmbientProbeCoefficientCount = 27;
        private const int AmbientProbeCoefficientStride = sizeof(float);
        private const int AmbientProbeScratchStride = sizeof(uint);
        private const int AmbientProbePackedCoefficientCount = 7;
        private const int AmbientProbePackedCoefficientStride = sizeof(float) * 4;

        private static readonly int AmbientProbeInputCubemapId = Shader.PropertyToID("_AmbientProbeInputCubemap");
        private static readonly int AmbientProbeOutputBufferId = Shader.PropertyToID("_AmbientProbeOutputBuffer");
        private static readonly int DiffuseAmbientProbeOutputBufferId = Shader.PropertyToID("_DiffuseAmbientProbeOutputBuffer");
        private static readonly int ScratchBufferId = Shader.PropertyToID("_ScratchBuffer");
        private static readonly int VividAmbientProbeDataId = Shader.PropertyToID("_VividAmbientProbeData");
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
        private bool m_UsesHdrpDiffuseKernel;
        private GraphicsBuffer m_AmbientProbeBuffer;
        private GraphicsBuffer m_AmbientProbeCoefficientBuffer;
        private GraphicsBuffer m_AmbientProbeScratchBuffer;
        private GraphicsBuffer m_DefaultAmbientProbeBuffer;
        private bool m_HasConvolvedSkyHash;
        private int m_ConvolvedSkyHash;
        private bool m_DebugReadbackPending;
        private int m_DebugReadbackSkyHash;

        internal bool IsSupported =>
            m_ComputeShader != null
            && m_Kernel >= 0
            && SystemInfo.supportsComputeShaders;

        internal void Build(VividRPCoreResources resources)
        {
            Cleanup();

            m_ComputeShader = resources?.SkyAmbientProbeConvolutionCompute;
            m_Kernel = FindKernel();
            EnsureDefaultAmbientProbeBuffer();
        }

        internal void Cleanup()
        {
            m_AmbientProbeBuffer?.Release();
            m_AmbientProbeBuffer = null;
            m_AmbientProbeCoefficientBuffer?.Release();
            m_AmbientProbeCoefficientBuffer = null;
            m_AmbientProbeScratchBuffer?.Release();
            m_AmbientProbeScratchBuffer = null;
            m_DefaultAmbientProbeBuffer?.Release();
            m_DefaultAmbientProbeBuffer = null;
            m_ComputeShader = null;
            m_Kernel = -1;
            m_UsesHdrpDiffuseKernel = false;
            m_HasConvolvedSkyHash = false;
            m_ConvolvedSkyHash = 0;
            m_DebugReadbackPending = false;
            m_DebugReadbackSkyHash = int.MinValue;
        }

        internal void RequestUpdate(
            CommandBuffer cmd,
            Texture sourceCubemap,
            int skyHash,
            bool forceRebuild = false)
        {
            if (!IsSupported || cmd == null || sourceCubemap == null)
                return;

            var rebuildReason = ResolveRebuildReason(skyHash);
            if (forceRebuild && rebuildReason == AmbientProbeConvolutionRebuildReason.None)
                rebuildReason = AmbientProbeConvolutionRebuildReason.SkyChanged;
            if (rebuildReason == AmbientProbeConvolutionRebuildReason.None)
                return;

            EnsureAmbientProbeBuffers();

            using (new ProfilingScope(cmd, GetConvolutionSampler(rebuildReason)))
            {
                cmd.SetComputeTextureParam(m_ComputeShader, m_Kernel, AmbientProbeInputCubemapId, sourceCubemap);
                if (m_UsesHdrpDiffuseKernel)
                {
                    cmd.SetComputeBufferParam(m_ComputeShader, m_Kernel, AmbientProbeOutputBufferId, m_AmbientProbeCoefficientBuffer);
                    cmd.SetComputeBufferParam(m_ComputeShader, m_Kernel, DiffuseAmbientProbeOutputBufferId, m_AmbientProbeBuffer);
                    cmd.SetComputeBufferParam(m_ComputeShader, m_Kernel, ScratchBufferId, m_AmbientProbeScratchBuffer);
                }
                else
                {
                    cmd.SetComputeBufferParam(m_ComputeShader, m_Kernel, AmbientProbeOutputBufferId, m_AmbientProbeBuffer);
                }

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
            var activeBuffer = useDefault || m_AmbientProbeBuffer == null ? m_DefaultAmbientProbeBuffer : m_AmbientProbeBuffer;
            cmd.SetGlobalBuffer(
                VividAmbientProbeDataId,
                activeBuffer);
        }

        private bool HasValidBuffers()
        {
            if (m_AmbientProbeBuffer == null)
                return false;

            return !m_UsesHdrpDiffuseKernel
                || (m_AmbientProbeCoefficientBuffer != null && m_AmbientProbeScratchBuffer != null);
        }

        private void EnsureAmbientProbeBuffers()
        {
            if (m_AmbientProbeBuffer != null)
            {
                EnsureHdrpCompatibilityBuffers();
                return;
            }

            m_AmbientProbeBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                AmbientProbePackedCoefficientCount,
                AmbientProbePackedCoefficientStride);
            EnsureHdrpCompatibilityBuffers();
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

        private int FindKernel()
        {
            m_UsesHdrpDiffuseKernel = false;
            if (m_ComputeShader == null)
                return -1;

            if (m_ComputeShader.HasKernel(DiffuseKernelName))
            {
                m_UsesHdrpDiffuseKernel = true;
                return m_ComputeShader.FindKernel(DiffuseKernelName);
            }

            if (m_ComputeShader.HasKernel(LegacyKernelName))
                return m_ComputeShader.FindKernel(LegacyKernelName);

            return -1;
        }

        private AmbientProbeConvolutionRebuildReason ResolveRebuildReason(int skyHash)
        {
            if (!HasValidBuffers())
                return AmbientProbeConvolutionRebuildReason.MissingBuffer;

            return m_HasConvolvedSkyHash && m_ConvolvedSkyHash == skyHash
                ? AmbientProbeConvolutionRebuildReason.None
                : AmbientProbeConvolutionRebuildReason.SkyChanged;
        }

        private void EnsureHdrpCompatibilityBuffers()
        {
            if (!m_UsesHdrpDiffuseKernel)
                return;

            if (m_AmbientProbeCoefficientBuffer == null)
            {
                m_AmbientProbeCoefficientBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    AmbientProbeCoefficientCount,
                    AmbientProbeCoefficientStride);
            }

            if (m_AmbientProbeScratchBuffer == null)
            {
                m_AmbientProbeScratchBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    AmbientProbeCoefficientCount,
                    AmbientProbeScratchStride);
            }
        }

        private static ProfilingSampler GetConvolutionSampler(AmbientProbeConvolutionRebuildReason reason)
        {
            return reason == AmbientProbeConvolutionRebuildReason.SkyChanged
                ? s_ConvolutionSkyChangedSampler
                : s_ConvolutionMissingBufferSampler;
        }
    }
}
