using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal static class VirtualTextureFeedbackBindingUtility
    {
        internal const int MaxFeedbackTileShift = 15;

        internal static int ResolveFeedbackTileShift(int feedbackSampleRate)
        {
            int sampleRate = Mathf.Max(1, feedbackSampleRate);
            int shift = 0;
            int tileArea = 1;

            while (tileArea < sampleRate && shift < MaxFeedbackTileShift)
            {
                shift += 1;
                tileArea = 1 << (shift * 2);
            }

            return shift;
        }

        internal static int ResolveFeedbackSampleArea(int feedbackSampleRate)
        {
            int shift = ResolveFeedbackTileShift(feedbackSampleRate);
            return 1 << (shift * 2);
        }

        internal static Vector4 BuildFeedbackViewParams(int feedbackSampleRate, int frameIndex)
        {
            int shift = ResolveFeedbackTileShift(feedbackSampleRate);
            int tileSize = 1 << shift;
            int tileArea = tileSize * tileSize;
            int jitterOffset = tileArea > 1 ? PositiveModulo(frameIndex, tileArea) : 0;

            return new Vector4(tileSize - 1, shift, jitterOffset, 1);
        }

        internal static void BindSpaceGlobals(
            CommandBuffer cmd,
            in VirtualTextureSpaceBinding binding,
            float[] spaceParams,
            float[] mipOffsets,
            Vector4[] layerFallbacks,
            int frameIndex,
            int feedbackSampleRate,
            VirtualTextureDebugMode debugMode)
        {
            if (cmd == null)
                return;

            ClearArray(spaceParams);
            ClearArray(mipOffsets);
            ClearArray(layerFallbacks);

            binding.ShaderParams.CopyTo(spaceParams);
            CopyMipOffsets(binding.MipOffsets, mipOffsets);
            CopyLayerFallbacks(binding.LayerFallbacks, layerFallbacks);

            cmd.SetGlobalBuffer(VirtualTextureShaderIDs._VTPageTable, binding.PageTableBuffer);
            cmd.SetGlobalTexture(VirtualTextureShaderIDs._VTPhysicalCache, binding.PhysicalCache);
            cmd.SetGlobalFloatArray(VirtualTextureShaderIDs._VTSpaceParams, spaceParams);
            cmd.SetGlobalFloatArray(VirtualTextureShaderIDs._VTMipOffsets, mipOffsets);
            cmd.SetGlobalVectorArray(VirtualTextureShaderIDs._VTLayerFallbacks, layerFallbacks);
            cmd.SetGlobalInt(VirtualTextureShaderIDs._VTFeedbackEnabled, binding.HasFeedback ? 1 : 0);
            cmd.SetGlobalVector(
                VirtualTextureShaderIDs._VTFeedbackViewParams,
                BuildFeedbackViewParams(feedbackSampleRate, frameIndex));
            cmd.SetGlobalInt(VirtualTextureShaderIDs._VTFeedbackFrameIndex, frameIndex);
            cmd.SetGlobalInt(
                VirtualTextureShaderIDs._VTFeedbackSampleRate,
                ResolveFeedbackSampleArea(feedbackSampleRate));
            cmd.SetGlobalInt(VirtualTextureShaderIDs._VTDebugMode, (int)debugMode);
        }

        internal static bool BindFeedbackTargets(CommandBuffer cmd, in VirtualTextureSpaceBinding binding)
        {
            if (cmd == null || !binding.HasFeedback)
                return false;

            cmd.SetRandomWriteTarget(1, binding.FeedbackRequests, preserveCounterValue: false);
            cmd.SetRandomWriteTarget(2, binding.FeedbackCounter, preserveCounterValue: true);
            return true;
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

            int length = Mathf.Min(source.Length, destination.Length);
            Array.Copy(source, destination, length);
        }

        private static void ClearArray(float[] values)
        {
            if (values != null)
                Array.Clear(values, 0, values.Length);
        }

        private static void ClearArray(Vector4[] values)
        {
            if (values != null)
                Array.Clear(values, 0, values.Length);
        }

        private static int PositiveModulo(int value, int divisor)
        {
            if (divisor <= 0)
                return 0;

            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }
    }
}
