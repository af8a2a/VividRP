using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class SkyAmbientProbeConvolution
    {
        private const string KernelName = "AmbientProbeConvolution";
        private const int AmbientProbeCoefficientCount = 27;

        private static readonly int AmbientProbeInputCubemapId = Shader.PropertyToID("_AmbientProbeInputCubemap");
        private static readonly int AmbientProbeOutputBufferId = Shader.PropertyToID("_AmbientProbeOutputBuffer");
        private static readonly int SkyConvolutionTintId = Shader.PropertyToID("_SkyConvolutionTint");
        private static readonly int SkyConvolutionParamsId = Shader.PropertyToID("_SkyConvolutionParams");

        private ComputeShader m_ComputeShader;
        private int m_Kernel = -1;
        private GraphicsBuffer m_AmbientProbeResultBuffer;
        private bool m_HasPendingReadback;
        private int m_PendingSkyHash;
        private bool m_HasReadyProbe;
        private int m_ReadySkyHash;
        private SphericalHarmonicsL2 m_ReadyProbe;

        internal bool IsSupported =>
            m_ComputeShader != null
            && m_Kernel >= 0
            && SystemInfo.supportsComputeShaders
            && SystemInfo.supportsAsyncGPUReadback;

        internal void Build(VividRPCoreResources resources)
        {
            Cleanup();

            m_ComputeShader = resources?.SkyAmbientProbeConvolutionCompute;
            m_Kernel = m_ComputeShader != null ? m_ComputeShader.FindKernel(KernelName) : -1;
        }

        internal void Cleanup()
        {
            m_AmbientProbeResultBuffer?.Release();
            m_AmbientProbeResultBuffer = null;
            m_ComputeShader = null;
            m_Kernel = -1;
            m_HasPendingReadback = false;
            m_PendingSkyHash = 0;
            m_HasReadyProbe = false;
            m_ReadySkyHash = 0;
            m_ReadyProbe = default;
        }

        internal void RequestUpdate(
            CommandBuffer cmd,
            Cubemap sourceCubemap,
            Color tint,
            float exposure,
            float rotation,
            int skyHash)
        {
            if (!IsSupported || cmd == null || sourceCubemap == null)
                return;

            if (m_HasReadyProbe && m_ReadySkyHash == skyHash)
                return;

            if (m_HasPendingReadback)
                return;

            EnsureResultBuffer();

            cmd.SetComputeTextureParam(m_ComputeShader, m_Kernel, AmbientProbeInputCubemapId, sourceCubemap);
            cmd.SetComputeBufferParam(m_ComputeShader, m_Kernel, AmbientProbeOutputBufferId, m_AmbientProbeResultBuffer);
            cmd.SetComputeVectorParam(m_ComputeShader, SkyConvolutionTintId, new Vector4(tint.r, tint.g, tint.b, tint.a));
            cmd.SetComputeVectorParam(m_ComputeShader, SkyConvolutionParamsId, new Vector4(Mathf.Max(exposure, 0.0f), -rotation, 0.0f, 0.0f));
            Hammersley.BindConstants(cmd, m_ComputeShader);
            cmd.DispatchCompute(m_ComputeShader, m_Kernel, 1, 1, 1);

            m_HasPendingReadback = true;
            m_PendingSkyHash = skyHash;
            cmd.RequestAsyncReadback(m_AmbientProbeResultBuffer, request => OnAmbientProbeReadback(request, skyHash));
        }

        internal bool TryGetReadyProbe(int skyHash, out SphericalHarmonicsL2 probe)
        {
            if (m_HasReadyProbe && m_ReadySkyHash == skyHash)
            {
                probe = m_ReadyProbe;
                return true;
            }

            probe = default;
            return false;
        }

        internal bool TryGetLastReadyProbe(out SphericalHarmonicsL2 probe)
        {
            if (m_HasReadyProbe)
            {
                probe = m_ReadyProbe;
                return true;
            }

            probe = default;
            return false;
        }

        internal static bool TryPopulateProbeFromCoefficients(float[] coefficients, out SphericalHarmonicsL2 probe)
        {
            probe = default;

            if (coefficients == null || coefficients.Length < AmbientProbeCoefficientCount)
                return false;

            for (var channel = 0; channel < 3; channel++)
            {
                for (var coefficient = 0; coefficient < 9; coefficient++)
                    probe[channel, coefficient] = coefficients[(channel * 9) + coefficient];
            }

            return true;
        }

        internal static bool TryPopulateProbeFromCoefficients(NativeArray<float> coefficients, out SphericalHarmonicsL2 probe)
        {
            probe = default;

            if (!coefficients.IsCreated || coefficients.Length < AmbientProbeCoefficientCount)
                return false;

            for (var channel = 0; channel < 3; channel++)
            {
                for (var coefficient = 0; coefficient < 9; coefficient++)
                    probe[channel, coefficient] = coefficients[(channel * 9) + coefficient];
            }

            return true;
        }

        private void EnsureResultBuffer()
        {
            if (m_AmbientProbeResultBuffer != null)
                return;

            m_AmbientProbeResultBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                AmbientProbeCoefficientCount,
                sizeof(float));
        }

        private void OnAmbientProbeReadback(AsyncGPUReadbackRequest request, int skyHash)
        {
            var isExpectedRequest = m_HasPendingReadback && skyHash == m_PendingSkyHash;
            m_HasPendingReadback = false;
            m_PendingSkyHash = 0;

            if (!isExpectedRequest || request.hasError)
                return;

            if (!TryPopulateProbeFromCoefficients(request.GetData<float>(), out var probe))
                return;

            m_ReadyProbe = probe;
            m_ReadySkyHash = skyHash;
            m_HasReadyProbe = true;
        }
    }
}
