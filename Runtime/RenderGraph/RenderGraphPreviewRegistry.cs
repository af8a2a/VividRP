using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    internal static class RenderGraphPreviewRegistry
    {
        private static bool? s_AvailabilityOverride;

        private sealed class PreviewEntry
        {
            public RTHandle Handle;
            public Texture ExternalTexture;
            public PreviewTargetSettings Settings;
        }

        private struct PreviewTargetSettings
        {
            public RenderTextureDescriptor Descriptor;
            public FilterMode FilterMode;
            public TextureWrapMode WrapMode;
            public int AnisoLevel;
            public float MipMapBias;
        }

        private static readonly Dictionary<string, PreviewEntry> s_TexturePreviews = new(StringComparer.Ordinal);

        internal static bool IsAvailable
        {
            get
            {
                if (s_AvailabilityOverride.HasValue)
                    return s_AvailabilityOverride.Value;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return true;
#else
                return false;
#endif
            }
        }

        internal static void SetAvailabilityOverrideForTests(bool? isAvailable)
        {
            s_AvailabilityOverride = isAvailable;
            if (isAvailable == false)
                Clear();
        }

        internal static void Clear()
        {
            foreach (var entry in s_TexturePreviews.Values)
            {
                entry?.Handle?.Release();
            }

            s_TexturePreviews.Clear();
        }

        internal static bool TryGetPreview(Type passType, string fieldName, out Texture texture)
        {
            texture = null;
            if (!IsAvailable)
                return false;

            if (passType == null || string.IsNullOrEmpty(fieldName))
                return false;

            if (!s_TexturePreviews.TryGetValue(BuildKey(passType, fieldName), out var entry) || entry == null)
                return false;

            texture = GetPreviewTexture(entry);
            return texture != null;
        }

        internal static bool TryGetSinglePreview(out Type passType, out string fieldName, out Texture texture)
        {
            passType = null;
            fieldName = null;
            texture = null;

            if (!IsAvailable)
                return false;

            string singleKey = null;
            foreach (var pair in s_TexturePreviews)
            {
                var candidateTexture = GetPreviewTexture(pair.Value);
                if (candidateTexture == null)
                    continue;

                if (singleKey != null)
                    return false;

                singleKey = pair.Key;
                texture = candidateTexture;
            }

            if (string.IsNullOrEmpty(singleKey))
                return false;

            var separatorIndex = singleKey.LastIndexOf('|');
            if (separatorIndex <= 0 || separatorIndex >= singleKey.Length - 1)
            {
                texture = null;
                return false;
            }

            var passTypeName = singleKey.Substring(0, separatorIndex);
            fieldName = singleKey.Substring(separatorIndex + 1);
            passType = Type.GetType(passTypeName, throwOnError: false);
            if (passType == null || string.IsNullOrEmpty(fieldName))
            {
                texture = null;
                fieldName = null;
                return false;
            }

            return true;
        }

        internal static void SetPreview(Type passType, string fieldName, Texture texture)
        {
            if (!IsAvailable)
                return;

            if (passType == null || string.IsNullOrEmpty(fieldName))
                return;

            var key = BuildKey(passType, fieldName);
            if (texture == null)
            {
                if (!s_TexturePreviews.TryGetValue(key, out var entry) || entry == null)
                    return;

                entry.ExternalTexture = null;
                if (entry.Handle == null)
                {
                    s_TexturePreviews.Remove(key);
                }

                return;
            }

            var previewEntry = GetOrCreateEntry(key);
            previewEntry.ExternalTexture = texture;
        }

        internal static RTHandle GetOrCreatePreviewTarget(
            Type passType,
            string fieldName,
            in RenderTargetInfo sourceInfo,
            RenderGraphTextureDesc sourceDesc)
        {
            if (!IsAvailable)
                return null;

            if (passType == null || string.IsNullOrEmpty(fieldName))
                return null;

            if (sourceInfo.width <= 0 || sourceInfo.height <= 0 || sourceInfo.format == GraphicsFormat.None)
                return null;

            if (GraphicsFormatUtility.IsDepthFormat(sourceInfo.format))
                return null;

            var settings = CreateSettings(sourceInfo, sourceDesc);
            var key = BuildKey(passType, fieldName);
            var entry = GetOrCreateEntry(key);
            if (entry.Handle != null && SettingsMatch(entry.Settings, settings))
                return entry.Handle;

            entry.Handle?.Release();
            entry.Handle = RTHandles.Alloc(
                settings.Descriptor,
                settings.FilterMode,
                settings.WrapMode,
                isShadowMap: false,
                settings.AnisoLevel,
                settings.MipMapBias,
                $"{passType.Name}.{fieldName} Preview");
            entry.ExternalTexture = null;
            entry.Settings = settings;
            return entry.Handle;
        }

        private static PreviewEntry GetOrCreateEntry(string key)
        {
            if (!s_TexturePreviews.TryGetValue(key, out var entry) || entry == null)
            {
                entry = new PreviewEntry();
                s_TexturePreviews[key] = entry;
            }

            return entry;
        }

        private static Texture GetPreviewTexture(PreviewEntry entry)
        {
            if (entry == null)
                return null;

            if (entry.Handle != null)
            {
                if (entry.Handle.rt != null)
                    return entry.Handle.rt;

                if (entry.Handle.externalTexture != null)
                    return entry.Handle.externalTexture;
            }

            return entry.ExternalTexture;
        }

        private static PreviewTargetSettings CreateSettings(
            in RenderTargetInfo sourceInfo,
            RenderGraphTextureDesc sourceDesc)
        {
            var descriptor = new RenderTextureDescriptor(
                Mathf.Max(1, sourceInfo.width),
                Mathf.Max(1, sourceInfo.height))
            {
                graphicsFormat = sourceInfo.format,
                depthStencilFormat = GraphicsFormat.None,
                msaaSamples = Mathf.Max(1, sourceInfo.msaaSamples),
                volumeDepth = Mathf.Max(1, sourceInfo.volumeDepth),
                dimension = ResolveDimension(sourceDesc, sourceInfo),
                bindMS = sourceInfo.bindMS,
                useMipMap = sourceDesc?.UseMipMap ?? false,
                autoGenerateMips = sourceDesc?.AutoGenerateMips ?? false,
                enableRandomWrite = sourceDesc?.EnableRandomWrite ?? false,
                useDynamicScale = false,
            };

            return new PreviewTargetSettings
            {
                Descriptor = descriptor,
                FilterMode = sourceDesc?.FilterMode ?? FilterMode.Bilinear,
                WrapMode = sourceDesc?.WrapMode ?? TextureWrapMode.Clamp,
                AnisoLevel = sourceDesc?.AnisoLevel ?? 1,
                MipMapBias = sourceDesc?.MipMapBias ?? 0f,
            };
        }

        private static TextureDimension ResolveDimension(RenderGraphTextureDesc sourceDesc, in RenderTargetInfo sourceInfo)
        {
            if (sourceDesc != null)
                return sourceDesc.Dimension;

            return sourceInfo.volumeDepth > 1 ? TextureDimension.Tex2DArray : TextureDimension.Tex2D;
        }

        private static bool SettingsMatch(PreviewTargetSettings left, PreviewTargetSettings right)
        {
            return left.Descriptor.width == right.Descriptor.width
                && left.Descriptor.height == right.Descriptor.height
                && left.Descriptor.graphicsFormat == right.Descriptor.graphicsFormat
                && left.Descriptor.depthStencilFormat == right.Descriptor.depthStencilFormat
                && left.Descriptor.msaaSamples == right.Descriptor.msaaSamples
                && left.Descriptor.volumeDepth == right.Descriptor.volumeDepth
                && left.Descriptor.dimension == right.Descriptor.dimension
                && left.Descriptor.bindMS == right.Descriptor.bindMS
                && left.Descriptor.useMipMap == right.Descriptor.useMipMap
                && left.Descriptor.autoGenerateMips == right.Descriptor.autoGenerateMips
                && left.Descriptor.enableRandomWrite == right.Descriptor.enableRandomWrite
                && left.FilterMode == right.FilterMode
                && left.WrapMode == right.WrapMode
                && left.AnisoLevel == right.AnisoLevel
                && Mathf.Approximately(left.MipMapBias, right.MipMapBias);
        }

        private static string BuildKey(Type passType, string fieldName)
        {
            return $"{passType.AssemblyQualifiedName}|{fieldName}";
        }
    }
}
