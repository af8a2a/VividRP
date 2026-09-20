#if DLSS_PLUGIN_INTEGRATE

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    /// <summary>Feature-18 preset exposed by nvngx_dlssnr.dll.</summary>
    public enum DLSSNeuralRenderingPreset : int
    {
        Default = 0,
        Preset1 = 1,
        Preset2 = 2,
        Preset3 = 3
    }

    /// <summary>Image style exposed by nvngx_dlssnr.dll.</summary>
    public enum DLSSNeuralRenderingStyle : int
    {
        Default = 0,
        Natural = 1,
        Cinematic = 2
    }

    /// <summary>Per-frame controls for DLSS 5 Neural Rendering.</summary>
    public sealed class DLSSNeuralRenderingSettings
    {
        public DLSSNeuralRenderingPreset Preset = DLSSNeuralRenderingPreset.Default;
        public DLSSNeuralRenderingStyle Style = DLSSNeuralRenderingStyle.Default;
        public float Intensity = 1.0f;
        public float LocalToneStrength = 1.0f;
        public float LocalStructureStrength = 1.0f;
        public float SkinStructureStrength = -1.0f;
        public bool DepthInverted = SystemInfo.usesReversedZBuffer;
        public bool UseAutoMask;
        public bool UICorrection;
        public bool Upscaling;
        public Vector2 MotionVectorScale = Vector2.one;

        internal bool TryValidate(out string error)
        {
            int preset = (int)Preset;
            if (preset < (int)DLSSNeuralRenderingPreset.Default
                || preset > (int)DLSSNeuralRenderingPreset.Preset3)
            {
                error = "Unknown Neural Rendering preset.";
                return false;
            }

            int style = (int)Style;
            if (style < (int)DLSSNeuralRenderingStyle.Default
                || style > (int)DLSSNeuralRenderingStyle.Cinematic)
            {
                error = "Unknown Neural Rendering style.";
                return false;
            }

            if (!IsFinite(Intensity) || Intensity < 0.0f || Intensity > 2.0f
                || !IsFinite(LocalToneStrength) || LocalToneStrength < 0.0f || LocalToneStrength > 2.0f
                || !IsFinite(LocalStructureStrength) || LocalStructureStrength < 0.0f || LocalStructureStrength > 2.0f
                || !IsFinite(SkinStructureStrength) || SkinStructureStrength < -1.0f || SkinStructureStrength > 2.0f)
            {
                error = "Neural Rendering controls are outside their supported ranges.";
                return false;
            }

            if (!IsFinite(MotionVectorScale.x) || !IsFinite(MotionVectorScale.y))
            {
                error = "Neural Rendering motion-vector scale must be finite.";
                return false;
            }

            error = null;
            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>Owns one standalone DLSS 5 Neural Rendering feature.</summary>
    public sealed class DLSSNeuralRendering : IDisposable
    {
        private int m_Handle = DLSSExtension.DLSS_INVALID_FEATURE_HANDLE;
        private bool m_Initialized;
        private bool m_CreateFailed;
        private bool m_Disposed;
        private uint m_InputWidth;
        private uint m_InputHeight;
        private uint m_OutputWidth;
        private uint m_OutputHeight;
        private DLSSNeuralRenderingPreset m_Preset;
        private bool m_Upscaling;
        private DLSSExtension m_Extension;

        private DLSSExtension Extension
        {
            get
            {
                if (m_Extension == null)
                    m_Extension = DLSSExtension.Instance;
                return m_Extension;
            }
        }

        public bool IsSupported => Extension?.IsNRSupported ?? false;

        /// <summary>Records Neural Rendering into a Unity command buffer.</summary>
        public bool Render(
            CommandBuffer cmd,
            RenderTexture colorInput,
            RenderTexture colorOutput,
            RenderTexture depth,
            RenderTexture motionVectors,
            DLSSMotionVectorEncoding motionVectorEncoding,
            DLSSNeuralRenderingSettings settings,
            bool reset = false)
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(DLSSNeuralRendering));

            if (!IsSupported || Extension == null)
            {
                RecordFallback(cmd, colorInput, colorOutput);
                return false;
            }

            if (!ValidateInputs(colorInput, colorOutput, depth, motionVectors, settings, out string error))
            {
                Debug.LogError(error);
                RecordFallback(cmd, colorInput, colorOutput);
                return false;
            }

            uint inputWidth = (uint)colorInput.width;
            uint inputHeight = (uint)colorInput.height;
            uint outputWidth = (uint)colorOutput.width;
            uint outputHeight = (uint)colorOutput.height;
            bool createParametersChanged =
                m_InputWidth != inputWidth
                || m_InputHeight != inputHeight
                || m_OutputWidth != outputWidth
                || m_OutputHeight != outputHeight
                || m_Preset != settings.Preset
                || m_Upscaling != settings.Upscaling;

            if (createParametersChanged)
            {
                if (!DisposeResources(cmd))
                {
                    RecordFallback(cmd, colorInput, colorOutput);
                    return false;
                }

                m_InputWidth = inputWidth;
                m_InputHeight = inputHeight;
                m_OutputWidth = outputWidth;
                m_OutputHeight = outputHeight;
                m_Preset = settings.Preset;
                m_Upscaling = settings.Upscaling;
                m_CreateFailed = false;
            }

            if (!EnsureInitialized(cmd))
            {
                RecordFallback(cmd, colorInput, colorOutput);
                return false;
            }

            Vector2 motionVectorScale = motionVectorEncoding.GetNGXPixelScale(
                colorInput.width,
                colorInput.height);
            motionVectorScale = Vector2.Scale(motionVectorScale, settings.MotionVectorScale);

            RecordFallback(cmd, colorInput, colorOutput);
            return Extension.EvaluateNeuralRenderingFeature(
                cmd,
                m_Handle,
                colorInput,
                colorOutput,
                depth,
                motionVectors,
                motionVectorScale.x,
                motionVectorScale.y,
                settings,
                reset);
        }

        private bool EnsureInitialized(CommandBuffer cmd)
        {
            if (m_Initialized)
                return true;
            if (m_CreateFailed)
                return false;

            DLSSExtension extension = Extension;
            if (extension == null)
                return false;

            if (m_Handle != DLSSExtension.DLSS_INVALID_FEATURE_HANDLE)
            {
                DLSSFeatureStatus status = extension.GetFeatureStatus(m_Handle, out NVSDK_NGX_Result result);
                if (status == DLSSFeatureStatus.Pending)
                    return false;
                if (status == DLSSFeatureStatus.Ready)
                {
                    m_Initialized = true;
                    return true;
                }

                if (status == DLSSFeatureStatus.Failed)
                {
                    Debug.LogError("DLSS 5 Neural Rendering feature creation failed: " + result);
                    extension.ReleaseFeatureHandle(m_Handle);
                }

                m_Handle = DLSSExtension.DLSS_INVALID_FEATURE_HANDLE;
                m_CreateFailed = true;
                return false;
            }

            m_Handle = extension.CreateNeuralRenderingFeature(
                cmd,
                m_InputWidth,
                m_InputHeight,
                m_OutputWidth,
                m_OutputHeight,
                m_Preset,
                m_Upscaling);
            if (m_Handle == DLSSExtension.DLSS_INVALID_FEATURE_HANDLE)
            {
                m_CreateFailed = true;
                return false;
            }

            return false;
        }

        private bool DisposeResources(CommandBuffer cmd)
        {
            if (m_Handle == DLSSExtension.DLSS_INVALID_FEATURE_HANDLE)
            {
                m_Initialized = false;
                return true;
            }

            DLSSExtension extension = Extension;
            if (extension == null || cmd == null)
                return false;

            DLSSFeatureStatus status = extension.GetFeatureStatus(m_Handle, out _);
            if (status == DLSSFeatureStatus.Pending || status == DLSSFeatureStatus.Ready)
            {
                if (!extension.DestroyFeature(cmd, m_Handle))
                    return false;
            }
            else if (status == DLSSFeatureStatus.Failed)
            {
                extension.ReleaseFeatureHandle(m_Handle);
            }

            m_Handle = DLSSExtension.DLSS_INVALID_FEATURE_HANDLE;
            m_Initialized = false;
            return true;
        }

        private static bool ValidateInputs(
            RenderTexture colorInput,
            RenderTexture colorOutput,
            RenderTexture depth,
            RenderTexture motionVectors,
            DLSSNeuralRenderingSettings settings,
            out string error)
        {
            if (colorInput == null || colorOutput == null || depth == null || motionVectors == null)
            {
                error = "DLSS 5 Neural Rendering requires color, output, depth, and motion-vector textures.";
                return false;
            }

            if (!colorInput.IsCreated() || !colorOutput.IsCreated()
                || !depth.IsCreated() || !motionVectors.IsCreated())
            {
                error = "DLSS 5 Neural Rendering textures must be created before evaluation.";
                return false;
            }

            if (ReferenceEquals(colorInput, colorOutput)
                || colorInput.GetNativeTexturePtr() == colorOutput.GetNativeTexturePtr())
            {
                error = "DLSS 5 Neural Rendering input and output must be distinct textures.";
                return false;
            }

            if (!colorOutput.enableRandomWrite)
            {
                error = "DLSS 5 Neural Rendering output must enable random write.";
                return false;
            }

            if (depth.width != colorInput.width || depth.height != colorInput.height
                || motionVectors.width != colorInput.width || motionVectors.height != colorInput.height)
            {
                error = "DLSS 5 Neural Rendering depth and motion dimensions must match color input.";
                return false;
            }

            if (settings == null)
            {
                error = "DLSS 5 Neural Rendering settings cannot be null.";
                return false;
            }

            if (!settings.TryValidate(out error))
                return false;

            long expectedWidth = settings.Upscaling ? (long)colorInput.width * 2L : colorInput.width;
            long expectedHeight = settings.Upscaling ? (long)colorInput.height * 2L : colorInput.height;
            if (colorOutput.width != expectedWidth || colorOutput.height != expectedHeight)
            {
                error = settings.Upscaling
                    ? "DLSS 5 Neural Rendering upscaling requires an exact 2x output."
                    : "DLSS 5 Neural Rendering full-resolution input and output must match.";
                return false;
            }

            error = null;
            return true;
        }

        private static void RecordFallback(
            CommandBuffer cmd,
            RenderTexture colorInput,
            RenderTexture colorOutput)
        {
            if (cmd != null && colorInput != null && colorOutput != null)
                cmd.Blit(colorInput, colorOutput);
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;

            using (var cmd = new CommandBuffer())
            {
                cmd.name = "DLSS Neural Rendering Cleanup";
                DisposeResources(cmd);
                Graphics.ExecuteCommandBuffer(cmd);
            }

            m_Disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}

#endif
