using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace VividRP.Runtime
{
    internal static class RenderGraphTextureDescUtility
    {
        internal static bool HasExplicitSize(RenderGraphTextureDesc descriptor)
        {
            return descriptor != null
                && descriptor.Width > 0
                && descriptor.Height > 0
                && !(descriptor.Width == 1 && descriptor.Height == 1);
        }

        internal static int ResolveMaxExplicitDimension(
            Func<RenderGraphTextureDesc, int> selector,
            int actualCameraDimension,
            int cameraDimension,
            int screenDimension,
            params RenderGraphTextureDesc[] descriptors)
        {
            var resolved = 0;

            for (var i = 0; i < descriptors.Length; i++)
            {
                var descriptor = descriptors[i];
                if (!HasExplicitSize(descriptor))
                    continue;

                resolved = Mathf.Max(resolved, selector(descriptor));
            }

            if (resolved > 0)
                return resolved;

            return CameraDimensionUtility.ResolveCameraDimension(actualCameraDimension, cameraDimension, screenDimension);
        }

        internal static GraphicsFormat ResolveColorFormat(
            RenderGraphTextureDesc descriptor,
            GraphicsFormat fallbackFormat = GraphicsFormat.R8G8B8A8_UNorm)
        {
            if (descriptor != null && descriptor.ColorFormat != GraphicsFormat.None)
                return descriptor.ColorFormat;

            return fallbackFormat;
        }

        internal static void Copy(RenderGraphTextureDesc source, RenderGraphTextureDesc destination)
        {
            if (source == null || destination == null)
                return;

            destination.Width = source.Width;
            destination.Height = source.Height;
            destination.Slices = source.Slices;
            destination.Dimension = source.Dimension;
            destination.ColorFormat = source.ColorFormat;
            destination.DepthBufferBits = source.DepthBufferBits;
            destination.MsaaSamples = source.MsaaSamples;
            destination.FilterMode = source.FilterMode;
            destination.WrapMode = source.WrapMode;
            destination.AnisoLevel = source.AnisoLevel;
            destination.MipMapBias = source.MipMapBias;
            destination.UseMipMap = source.UseMipMap;
            destination.AutoGenerateMips = source.AutoGenerateMips;
            destination.MipCount = source.MipCount;
            destination.ClearBuffer = source.ClearBuffer;
            destination.ClearColor = source.ClearColor;
            destination.IsShadowMap = source.IsShadowMap;
            destination.EnableRandomWrite = source.EnableRandomWrite;
            destination.BindTextureMS = source.BindTextureMS;
            destination.UseDynamicScale = source.UseDynamicScale;
            destination.UseDynamicScaleExplicit = source.UseDynamicScaleExplicit;
            destination.ScaleFactor = source.ScaleFactor;
            destination.Name = source.Name;
        }
    }
}
