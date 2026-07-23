#if VIVIDRP_HAS_UNITY_DENOISING && (UNITY_EDITOR || UNITY_STANDALONE)
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Denoising;

namespace VividRP.Runtime.RenderPass.Core
{
    /// <summary>
    /// Adapter for com.unity.rendering.denoising. Requests are intentionally asynchronous so the
    /// RenderGraph command stream is never flushed from inside the pass.
    /// </summary>
    internal sealed class UnityOpenImageDenoiserBackend : IReferencedPathTracingDenoiserBackend
    {
        private CommandBufferDenoiser m_Denoiser = new();
        private RenderTexture m_ResultTexture;
        private bool m_SupportChecked;
        private bool m_IsSupported;
        private bool m_RequestActive;
        private bool m_DiscardActiveResult;
        private bool m_HasValidResult;
        private bool m_HasLoggedFailure;

        public bool IsSupported
        {
            get
            {
                EnsureSupportStatus();
                return m_IsSupported;
            }
        }

        public void Invalidate()
        {
            m_HasValidResult = false;
            m_DiscardActiveResult |= m_RequestActive;
        }

        public bool Process(
            CommandBuffer commandBuffer,
            RenderTexture source,
            RenderTexture destination,
            int width,
            int height)
        {
            if (commandBuffer == null
                || source == null
                || destination == null
                || width <= 0
                || height <= 0
                || !IsSupported)
            {
                return false;
            }

            try
            {
                CompleteFinishedRequest(commandBuffer);

                if (!m_RequestActive)
                    BeginRequest(commandBuffer, source, width, height);

                if (!m_HasValidResult
                    || m_ResultTexture == null
                    || m_ResultTexture.width != width
                    || m_ResultTexture.height != height)
                {
                    return false;
                }

                commandBuffer.CopyTexture(m_ResultTexture, destination);
                return true;
            }
            catch (Exception exception)
            {
                DisableBackend(exception);
                return false;
            }
        }

        public void Dispose()
        {
            m_HasValidResult = false;

            try
            {
                // Do not dispose native state while a worker thread is executing. The non-temporal
                // OIDN request releases its native backend when that worker completes.
                if (!m_RequestActive || m_Denoiser.QueryCompletion() != Denoiser.State.Executing)
                    m_Denoiser.DisposeDenoiser();
            }
            catch (Exception)
            {
                // Package teardown must not prevent the render pipeline from disposing.
            }

            if (m_ResultTexture != null)
            {
                CoreUtils.Destroy(m_ResultTexture);
                m_ResultTexture = null;
            }
        }

        private void EnsureSupportStatus()
        {
            if (m_SupportChecked)
                return;

            m_SupportChecked = true;
            try
            {
                m_IsSupported = Denoiser.IsDenoiserTypeSupported(DenoiserType.OpenImageDenoise);
                if (!m_IsSupported)
                {
                    Debug.LogWarning(
                        "[VividRP] Intel Open Image Denoise is not supported on the current platform. "
                        + "The reference path tracer will display its accumulated input without denoising.");
                }
            }
            catch (Exception exception)
            {
                DisableBackend(exception);
            }
        }

        private void CompleteFinishedRequest(CommandBuffer commandBuffer)
        {
            if (!m_RequestActive)
                return;

            var completion = m_Denoiser.QueryCompletion();
            if (completion == Denoiser.State.Executing)
                return;

            var result = m_Denoiser.GetResults(commandBuffer, m_ResultTexture);
            m_RequestActive = false;
            m_HasValidResult = completion == Denoiser.State.Success
                && result == Denoiser.State.Success
                && !m_DiscardActiveResult;
            m_DiscardActiveResult = false;

            if (!m_HasValidResult && completion == Denoiser.State.Failure)
                LogFailure("Open Image Denoise failed to process the accumulated path-tracing frame.");
        }

        private void BeginRequest(
            CommandBuffer commandBuffer,
            RenderTexture source,
            int width,
            int height)
        {
            EnsureResultTexture(width, height);
            if (m_ResultTexture == null)
                return;

            if (m_Denoiser.Init(DenoiserType.OpenImageDenoise, width, height) != Denoiser.State.Success)
            {
                LogFailure("Open Image Denoise initialization failed.");
                return;
            }

            if (m_Denoiser.DenoiseRequest(commandBuffer, "color", source) != Denoiser.State.Success)
            {
                LogFailure("Open Image Denoise rejected the path-tracing color input.");
                return;
            }

            m_RequestActive = true;
            m_DiscardActiveResult = false;
        }

        private void EnsureResultTexture(int width, int height)
        {
            if (m_ResultTexture != null
                && m_ResultTexture.width == width
                && m_ResultTexture.height == height)
            {
                return;
            }

            // An in-flight request must finish against the texture dimensions it was created with.
            if (m_RequestActive)
                return;

            if (m_ResultTexture != null)
                CoreUtils.Destroy(m_ResultTexture);

            var descriptor = new RenderTextureDescriptor(
                width,
                height,
                GraphicsFormat.R32G32B32A32_SFloat,
                0)
            {
                msaaSamples = 1,
                volumeDepth = 1,
                dimension = TextureDimension.Tex2D,
                enableRandomWrite = false,
                useMipMap = false,
                autoGenerateMips = false,
                sRGB = false
            };

            m_ResultTexture = new RenderTexture(descriptor)
            {
                name = "ReferencedPathTracing.OpenImageDenoised",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            m_ResultTexture.Create();
            m_HasValidResult = false;
        }

        private void DisableBackend(Exception exception)
        {
            m_IsSupported = false;
            m_SupportChecked = true;
            m_RequestActive = false;
            m_HasValidResult = false;
            LogFailure(
                $"Open Image Denoise was disabled after a package or native plugin error: "
                + $"{exception.GetType().Name}: {exception.Message}");
        }

        private void LogFailure(string message)
        {
            if (m_HasLoggedFailure)
                return;

            m_HasLoggedFailure = true;
            Debug.LogWarning($"[VividRP] {message} Falling back to the accumulated path-tracing input.");
        }
    }
}
#endif
