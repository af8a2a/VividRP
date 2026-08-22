using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.GPUDriven.VirtualTexture
{
    internal static class GPUDrivenVirtualTextureBindingUtility
    {
        internal static bool TryGetBinding(
            VividVirtualTextureFrameData frameData,
            out VirtualTextureSpaceBinding binding)
        {
            if (frameData != null
                && VividGPUDrivenSystem.TryGetVirtualTextureAllocationId(out int allocationId)
                && frameData.TryGetBindingForAllocation(allocationId, out binding)
                && binding.IsValid)
            {
                return true;
            }

            binding = default;
            return false;
        }

        internal static bool BindSpaceGlobals(
            CommandBuffer cmd,
            VividVirtualTextureFrameData frameData,
            float[] spaceParams,
            float[] mipOffsets,
            Vector4[] layerFallbacks,
            int frameIndex,
            int feedbackSampleRate,
            out VirtualTextureSpaceBinding binding)
        {
            if (!TryGetBinding(frameData, out binding))
                return false;

            VirtualTextureFeedbackBindingUtility.BindSpaceGlobals(
                cmd,
                binding,
                spaceParams,
                mipOffsets,
                layerFallbacks,
                frameIndex,
                feedbackSampleRate,
                frameData.AdaptiveMipBias,
                VirtualTextureDebugMode.None);
            return true;
        }

        internal static void BindSpaceProperties(
            MaterialPropertyBlock properties,
            in VirtualTextureSpaceBinding binding,
            float[] spaceParams,
            float[] mipOffsets,
            Vector4[] layerFallbacks,
            int frameIndex,
            float adaptiveMipBias)
        {
            if (properties == null || !binding.IsValid)
                return;

            Array.Clear(spaceParams, 0, spaceParams.Length);
            Array.Clear(mipOffsets, 0, mipOffsets.Length);
            Array.Clear(layerFallbacks, 0, layerFallbacks.Length);
            binding.ShaderParams.CopyTo(spaceParams);
            CopyMipOffsets(binding.MipOffsets, mipOffsets);
            CopyLayerFallbacks(binding.LayerFallbacks, layerFallbacks);

            properties.SetBuffer(VirtualTextureShaderIDs._VTPageTable, binding.PageTableBuffer);
            BindPhysicalCaches(properties, binding);
            properties.SetFloatArray(VirtualTextureShaderIDs._VTSpaceParams, spaceParams);
            properties.SetFloatArray(VirtualTextureShaderIDs._VTMipOffsets, mipOffsets);
            properties.SetVectorArray(VirtualTextureShaderIDs._VTLayerFallbacks, layerFallbacks);
            properties.SetInt(VirtualTextureShaderIDs._VTFeedbackEnabled, 0);
            properties.SetInt(VirtualTextureShaderIDs._VTFeedbackFrameIndex, frameIndex);
            properties.SetInt(VirtualTextureShaderIDs._VTFeedbackSampleRate, 1);
            properties.SetInt(VirtualTextureShaderIDs._VTFeedbackRequestCapacity, 0);
            properties.SetInt(VirtualTextureShaderIDs._VTFeedbackResidentHashCapacity, 0);
            properties.SetVector(VirtualTextureShaderIDs._VTFeedbackViewParams, Vector4.zero);
            properties.SetFloat(
                VirtualTextureShaderIDs._VTAdaptiveMipBias,
                VirtualTextureSystem.ResolveAdaptiveMipBias(
                    binding.SpaceId,
                    adaptiveMipBias));
            properties.SetInt(VirtualTextureShaderIDs._VTDebugMode, (int) VirtualTextureDebugMode.None);
        }

        private static void BindPhysicalCaches(
            MaterialPropertyBlock properties,
            in VirtualTextureSpaceBinding binding)
        {
            Texture2D fallback = binding.PhysicalCache;
            var physicalCaches = binding.PhysicalCaches;
            int[] shaderIds = VirtualTextureShaderIDs.PhysicalCaches;
            for (int physicalGroup = 0; physicalGroup < shaderIds.Length; physicalGroup++)
            {
                Texture2D cache = physicalCaches != null && physicalGroup < physicalCaches.Count
                    ? physicalCaches[physicalGroup]
                    : null;
                properties.SetTexture(shaderIds[physicalGroup], cache != null ? cache : fallback);
            }
        }

        private static void CopyMipOffsets(int[] source, float[] destination)
        {
            if (source == null || destination == null)
                return;

            int length = Mathf.Min(source.Length, destination.Length);
            for (int index = 0; index < length; index++)
                destination[index] = source[index];
        }

        private static void CopyLayerFallbacks(Vector4[] source, Vector4[] destination)
        {
            if (source == null || destination == null)
                return;

            Array.Copy(source, destination, Mathf.Min(source.Length, destination.Length));
        }
    }
}
