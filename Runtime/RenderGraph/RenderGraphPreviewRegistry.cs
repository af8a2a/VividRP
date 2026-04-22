using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    internal static class RenderGraphPreviewRegistry
    {
        internal static bool IsAvailable => false;

        internal static void SetAvailabilityOverrideForTests(bool? isAvailable)
        {
        }

        internal static void Clear()
        {
        }

        internal static bool TryGetPreview(Type passType, string fieldName, out Texture texture)
        {
            texture = null;
            return false;
        }

        internal static bool TryGetSinglePreview(out Type passType, out string fieldName, out Texture texture)
        {
            passType = null;
            fieldName = null;
            texture = null;
            return false;
        }

        internal static void SetPreview(Type passType, string fieldName, Texture texture)
        {
        }

        internal static RTHandle GetOrCreatePreviewTarget(
            Type passType,
            string fieldName,
            in RenderTargetInfo sourceInfo,
            RenderGraphTextureDesc sourceDesc)
        {
            return null;
        }
    }
}
