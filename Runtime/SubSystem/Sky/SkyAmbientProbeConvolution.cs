using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class SkyAmbientProbeConvolution
    {
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
        private static readonly int SkyConvolutionTintId = Shader.PropertyToID("_SkyConvolutionTint");
        private static readonly int SkyConvolutionParamsId = Shader.PropertyToID("_SkyConvolutionParams");
        private static readonly ProfilingSampler s_ConvolutionSampler = new("SkyAmbientProbeConvolution.Convolve");
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
        private int m_ConvolvedSkyHash;

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
            m_ConvolvedSkyHash = 0;
        }

        internal void RequestUpdate(
            CommandBuffer cmd,
            Texture sourceCubemap,
            Color tint,
            float exposureStops,
            float rotation,
            int skyHash,
            bool forceRebuild = false)
        {
            if (!IsSupported || cmd == null || sourceCubemap == null)
                return;

            var needsRebuild = forceRebuild
                || !HasValidBuffers()
                || m_ConvolvedSkyHash != skyHash;

            if (!needsRebuild)
                return;

            EnsureAmbientProbeBuffers();

            using (new ProfilingScope(cmd, s_ConvolutionSampler))
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

                cmd.SetComputeVectorParam(m_ComputeShader, SkyConvolutionTintId, new Vector4(tint.r, tint.g, tint.b, tint.a));
                cmd.SetComputeVectorParam(
                    m_ComputeShader,
                    SkyConvolutionParamsId,
                    new Vector4(HDRISkyVolume.ResolveExposureMultiplier(exposureStops), -rotation, 0.0f, 0.0f));
                Hammersley.BindConstants(cmd, m_ComputeShader);
                cmd.DispatchCompute(m_ComputeShader, m_Kernel, 1, 1, 1);
            }

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
    }
}
