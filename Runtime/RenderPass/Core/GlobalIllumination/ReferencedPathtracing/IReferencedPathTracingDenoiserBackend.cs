using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.RenderPass.Core
{
    /// <summary>
    /// Compatibility boundary between the reference path-tracing pass and Unity's denoising package.
    /// Keep package-specific types out of the pass so a future precompiled Unity backend only requires
    /// replacing the adapter.
    /// </summary>
    internal interface IReferencedPathTracingDenoiserBackend : IDisposable
    {
        bool IsSupported { get; }

        void Invalidate();

        bool Process(
            CommandBuffer commandBuffer,
            RenderTexture source,
            RenderTexture destination,
            int width,
            int height);
    }

    internal static class ReferencedPathTracingDenoiserBackendFactory
    {
#if VIVIDRP_HAS_UNITY_DENOISING && (UNITY_EDITOR || UNITY_STANDALONE)
        internal static bool IsPlatformSupported => IntPtr.Size == 8;

        internal static IReferencedPathTracingDenoiserBackend Create()
        {
            return IsPlatformSupported
                ? new UnityOpenImageDenoiserBackend()
                : UnsupportedDenoiserBackend.Instance;
        }
#else
        internal static bool IsPlatformSupported => false;

        internal static IReferencedPathTracingDenoiserBackend Create()
        {
            return UnsupportedDenoiserBackend.Instance;
        }
#endif

        private sealed class UnsupportedDenoiserBackend : IReferencedPathTracingDenoiserBackend
        {
            internal static readonly UnsupportedDenoiserBackend Instance = new();

            public bool IsSupported => false;

            public void Invalidate()
            {
            }

            public bool Process(
                CommandBuffer commandBuffer,
                RenderTexture source,
                RenderTexture destination,
                int width,
                int height)
            {
                return false;
            }

            public void Dispose()
            {
            }
        }
    }
}
