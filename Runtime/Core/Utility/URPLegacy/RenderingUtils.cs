using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;
using Debug = System.Diagnostics.Debug;

namespace VividRP.Runtime
{
    /// <summary>
    /// Contains properties and helper functions that you can use when rendering.
    /// </summary>
    public static class RenderingUtils
    {
        internal static RTHandleResourcePool s_RTHandlePool= new RTHandleResourcePool();

        /// <summary>
        /// Add stale rtHandle to pool so that it could be reused in the future.
        /// For stale rtHandle failed to add to pool(could happen when pool is reaching its max stale resource capacity), the stale resource will be released.
        /// </summary>
        internal static void AddStaleResourceToPoolOrRelease(TextureDesc desc, RTHandle handle)
        {
            if (!s_RTHandlePool.AddResourceToPool(desc, handle, Time.frameCount))
                RTHandles.Release(handle);
        }

        /// <summary>
        /// Return true if handle does not match descriptor
        /// </summary>
        /// <param name="handle">RTHandle to check (can be null)</param>
        /// <param name="descriptor">Descriptor for the RTHandle to match</param>
        /// <param name="scaled">Check if the RTHandle has auto scaling enabled if not, check the widths and heights</param>
        /// <returns></returns>
        internal static bool RTHandleNeedsReAlloc(
            RTHandle handle,
            in TextureDesc descriptor,
            bool scaled)
        {
            if (handle == null || handle.rt == null)
                return true;
            if (handle.useScaling != scaled)
                return true;
            if (!scaled && (handle.rt.width != descriptor.width || handle.rt.height != descriptor.height))
                return true;
            if (handle.rt.enableShadingRate && handle.rt.graphicsFormat != descriptor.colorFormat)
                return true;

            //We should always prefer to cache data from Native to prevent duplicate copy operations when re-fetching
            var rtDescriptor = handle.rt.descriptor;
            var rtHandleFormat = (rtDescriptor.depthStencilFormat != GraphicsFormat.None)
                ? rtDescriptor.depthStencilFormat
                : rtDescriptor.graphicsFormat;
            var isShadowMap = rtDescriptor.shadowSamplingMode != ShadowSamplingMode.None;

            return
                rtHandleFormat != descriptor.format ||
                rtDescriptor.dimension != descriptor.dimension ||
                rtDescriptor.volumeDepth != descriptor.slices ||
                rtDescriptor.enableRandomWrite != descriptor.enableRandomWrite ||
                rtDescriptor.enableShadingRate != descriptor.enableShadingRate ||
                rtDescriptor.useMipMap != descriptor.useMipMap ||
                rtDescriptor.autoGenerateMips != descriptor.autoGenerateMips ||
                isShadowMap != descriptor.isShadowMap ||
                (MSAASamples)rtDescriptor.msaaSamples != descriptor.msaaSamples ||
                rtDescriptor.bindMS != descriptor.bindTextureMS ||
                rtDescriptor.useDynamicScale != descriptor.useDynamicScale ||
                rtDescriptor.useDynamicScaleExplicit != descriptor.useDynamicScaleExplicit ||
                rtDescriptor.memoryless != descriptor.memoryless ||
                handle.rt.filterMode != descriptor.filterMode ||
                handle.rt.wrapMode != descriptor.wrapMode ||
                handle.rt.anisoLevel != descriptor.anisoLevel ||
                Mathf.Abs(handle.rt.mipMapBias - descriptor.mipMapBias) > Mathf.Epsilon ||
                handle.name != descriptor.name;
        }


        /// <summary>
        /// Re-allocate fixed-size RTHandle if it is not allocated or doesn't match the descriptor
        /// </summary>
        /// <param name="handle">RTHandle to check (can be null)</param>
        /// <param name="descriptor">Descriptor for the RTHandle to match</param>
        /// <param name="filterMode">Filtering mode of the RTHandle.</param>
        /// <param name="wrapMode">Addressing mode of the RTHandle.</param>
        /// <param name="anisoLevel">Anisotropic filtering level.</param>
        /// <param name="mipMapBias">Bias applied to mipmaps during filtering.</param>
        /// <param name="name">Name of the RTHandle.</param>
        /// <returns>If an allocation was done.</returns>
        public static bool ReAllocateHandleIfNeeded(
            ref RTHandle handle,
            in RenderTextureDescriptor descriptor,
            FilterMode filterMode = FilterMode.Point,
            TextureWrapMode wrapMode = TextureWrapMode.Repeat,
            int anisoLevel = 1,
            float mipMapBias = 0,
            string name = "")
        {
            Assert.IsTrue(descriptor.graphicsFormat == GraphicsFormat.None ^
                          descriptor.depthStencilFormat == GraphicsFormat.None);

            TextureDesc requestRTDesc = RTHandleResourcePool.CreateTextureDesc(descriptor, TextureSizeMode.Explicit,
                anisoLevel, 0, filterMode, wrapMode, name);
            if (RTHandleNeedsReAlloc(handle, requestRTDesc, false))
            {
                if (handle != null && handle.rt != null)
                {
                    TextureDesc currentRTDesc = RTHandleResourcePool.CreateTextureDesc(handle.rt.descriptor,
                        TextureSizeMode.Explicit, handle.rt.anisoLevel, handle.rt.mipMapBias, handle.rt.filterMode,
                        handle.rt.wrapMode, handle.name);
                    AddStaleResourceToPoolOrRelease(currentRTDesc, handle);
                }

                if (s_RTHandlePool.TryGetResource(requestRTDesc, out handle))
                {
                    return true;
                }

                var allocInfo = CreateRTHandleAllocInfo(descriptor, filterMode, wrapMode, anisoLevel, mipMapBias, name);
                handle = RTHandles.Alloc(descriptor.width, descriptor.height, allocInfo);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Re-allocate fixed-size RTHandle if it is not allocated or doesn't match the descriptor
        /// </summary>
        /// <param name="handle">RTHandle to check (can be null)</param>
        /// <param name="descriptor">TextureDesc for the RTHandle to match</param>
        /// <param name="name">Name of the RTHandle.</param>
        /// <returns>If an allocation was done.</returns>
        public static bool ReAllocateHandleIfNeeded(
            ref RTHandle handle,
            TextureDesc descriptor,
            string name)
        {
            descriptor.name = name;
            descriptor.sizeMode = TextureSizeMode.Explicit;

            if (RTHandleNeedsReAlloc(handle, in descriptor, false))
            {
                if (handle != null && handle.rt != null)
                {
                    TextureDesc currentRTDesc = RTHandleResourcePool.CreateTextureDesc(handle.rt.descriptor,
                        TextureSizeMode.Explicit, handle.rt.anisoLevel, handle.rt.mipMapBias, handle.rt.filterMode,
                        handle.rt.wrapMode, handle.name);
                    AddStaleResourceToPoolOrRelease(currentRTDesc, handle);
                }

                if (s_RTHandlePool.TryGetResource(descriptor, out handle))
                {
                    return true;
                }

                var allocInfo = CreateRTHandleAllocInfo(descriptor, name);
                handle = RTHandles.Alloc(descriptor.width, descriptor.height, allocInfo);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Re-allocate dynamically resized RTHandle if it is not allocated or doesn't match the descriptor
        /// </summary>
        /// <param name="handle">RTHandle to check (can be null)</param>
        /// <param name="scaleFactor">Constant scale for the RTHandle size computation.</param>
        /// <param name="descriptor">Descriptor for the RTHandle to match</param>
        /// <param name="filterMode">Filtering mode of the RTHandle.</param>
        /// <param name="wrapMode">Addressing mode of the RTHandle.</param>
        /// <param name="anisoLevel">Anisotropic filtering level.</param>
        /// <param name="mipMapBias">Bias applied to mipmaps during filtering.</param>
        /// <param name="name">Name of the RTHandle.</param>
        /// <returns>If an allocation was done.</returns>
        public static bool ReAllocateHandleIfNeeded(
            ref RTHandle handle,
            Vector2 scaleFactor,
            in RenderTextureDescriptor descriptor,
            FilterMode filterMode = FilterMode.Point,
            TextureWrapMode wrapMode = TextureWrapMode.Repeat,
            int anisoLevel = 1,
            float mipMapBias = 0,
            string name = "")
        {
            var usingConstantScale = handle != null && handle.useScaling && handle.scaleFactor == scaleFactor;
            TextureDesc requestRTDesc = RTHandleResourcePool.CreateTextureDesc(descriptor, TextureSizeMode.Scale,
                anisoLevel, 0, filterMode, wrapMode);
            if (!usingConstantScale || RTHandleNeedsReAlloc(handle, requestRTDesc, true))
            {
                if (handle != null && handle.rt != null)
                {
                    TextureDesc currentRTDesc = RTHandleResourcePool.CreateTextureDesc(handle.rt.descriptor,
                        TextureSizeMode.Scale, handle.rt.anisoLevel, handle.rt.mipMapBias, handle.rt.filterMode,
                        handle.rt.wrapMode);
                    AddStaleResourceToPoolOrRelease(currentRTDesc, handle);
                }

                if (s_RTHandlePool.TryGetResource(requestRTDesc, out handle))
                {
                    return true;
                }

                var allocInfo = CreateRTHandleAllocInfo(descriptor, filterMode, wrapMode, anisoLevel, mipMapBias, name);
                handle = RTHandles.Alloc(scaleFactor, allocInfo);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Re-allocate dynamically resized RTHandle if it is not allocated or doesn't match the descriptor
        /// </summary>
        /// <param name="handle">RTHandle to check (can be null)</param>
        /// <param name="scaleFunc">Function used for the RTHandle size computation.</param>
        /// <param name="descriptor">Descriptor for the RTHandle to match</param>
        /// <param name="filterMode">Filtering mode of the RTHandle.</param>
        /// <param name="wrapMode">Addressing mode of the RTHandle.</param>
        /// <param name="anisoLevel">Anisotropic filtering level.</param>
        /// <param name="mipMapBias">Bias applied to mipmaps during filtering.</param>
        /// <param name="name">Name of the RTHandle.</param>
        /// <returns>If an allocation was done.</returns>
        public static bool ReAllocateHandleIfNeeded(
            ref RTHandle handle,
            ScaleFunc scaleFunc,
            in RenderTextureDescriptor descriptor,
            FilterMode filterMode = FilterMode.Point,
            TextureWrapMode wrapMode = TextureWrapMode.Repeat,
            int anisoLevel = 1,
            float mipMapBias = 0,
            string name = "")
        {
            var usingScaleFunction = handle != null && handle.useScaling && handle.scaleFactor == Vector2.zero;
            TextureDesc requestRTDesc = RTHandleResourcePool.CreateTextureDesc(descriptor, TextureSizeMode.Functor,
                anisoLevel, 0, filterMode, wrapMode);
            if (!usingScaleFunction || RTHandleNeedsReAlloc(handle, requestRTDesc, true))
            {
                if (handle != null && handle.rt != null)
                {
                    TextureDesc currentRTDesc = RTHandleResourcePool.CreateTextureDesc(handle.rt.descriptor,
                        TextureSizeMode.Functor, handle.rt.anisoLevel, handle.rt.mipMapBias, handle.rt.filterMode,
                        handle.rt.wrapMode);
                    AddStaleResourceToPoolOrRelease(currentRTDesc, handle);
                }

                if (s_RTHandlePool.TryGetResource(requestRTDesc, out handle))
                {
                    return true;
                }

                var allocInfo = CreateRTHandleAllocInfo(descriptor, filterMode, wrapMode, anisoLevel, mipMapBias, name);
                handle = RTHandles.Alloc(scaleFunc, allocInfo);
                return true;
            }

            return false;
        }


        /// <summary>
        /// This is a replace for the old UniversalCameraData.IsHandleYFlipped function to simplify the conversion of code towards
        /// using the TextureUVOrigin to decide if the UV needs to be flipped. The function is a drop-in replacement, with exactly
        /// the same output based on the texture orientation of the TextureHandle passed. For new code, avoid using this function and
        /// directly use the TextureUVOrigin to decide if the UVs need to be flipped for more future proof and understandable code.
        /// </summary>
        /// <param name="renderGraphContext">The RasterGraphContext to use.</param>
        /// <param name="textureHandle">Texture handle representing the texture in the render graph to check.</param>
        /// <returns>If the texture should be rendered flipped.</returns>
        internal static bool IsHandleYFlipped(in RasterGraphContext renderGraphContext, in TextureHandle textureHandle)
        {
            return renderGraphContext.GetTextureUVOrigin(textureHandle) == TextureUVOrigin.BottomLeft;
        }

        internal static Vector4 GetFinalBlitScaleBias(in RasterGraphContext renderGraphContext, in TextureHandle source,
            in TextureHandle destination)
        {
            RTHandle sourceHandle = source;
            return TextureScaleBiasUtility.GetScaleBias(
                sourceHandle,
                renderGraphContext.GetTextureUVOrigin(in source),
                renderGraphContext.GetTextureUVOrigin(in destination));
        }

        /// <summary>
        /// Returns the TextureUVOrigin of the real backbuffer for the current graphics API. In modern graphics APIs like
        /// Vulkan or Metal, this will return TopLeft. For OpenGL, WebGL, GLES, this will return BottomLeft.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static TextureUVOrigin GetRealBackBufferUVOrientation()
        {
            return SystemInfo.graphicsUVStartsAtTop ? TextureUVOrigin.TopLeft : TextureUVOrigin.BottomLeft;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static RTHandleAllocInfo CreateRTHandleAllocInfo(in RenderTextureDescriptor descriptor,
            FilterMode filterMode, TextureWrapMode wrapMode, int anisoLevel, float mipMapBias, string name)
        {
            var actualFormat = descriptor.graphicsFormat != GraphicsFormat.None
                ? descriptor.graphicsFormat
                : descriptor.depthStencilFormat;

            // NOTE: this calls default(RTHandleAllocInfo) not RTHandleAllocInfo(string = "")
            RTHandleAllocInfo allocInfo = new RTHandleAllocInfo();
            allocInfo.slices = descriptor.volumeDepth;
            allocInfo.format = actualFormat;
            allocInfo.filterMode = filterMode;
            allocInfo.wrapModeU = wrapMode;
            allocInfo.wrapModeV = wrapMode;
            allocInfo.wrapModeW = wrapMode;
            allocInfo.dimension = descriptor.dimension;
            allocInfo.enableRandomWrite = descriptor.enableRandomWrite;
            allocInfo.enableShadingRate = descriptor.enableShadingRate;
            allocInfo.useMipMap = descriptor.useMipMap;
            allocInfo.autoGenerateMips = descriptor.autoGenerateMips;
            allocInfo.anisoLevel = anisoLevel;
            allocInfo.mipMapBias = mipMapBias;
            allocInfo.isShadowMap = descriptor.shadowSamplingMode != ShadowSamplingMode.None;
            allocInfo.msaaSamples = (MSAASamples)descriptor.msaaSamples;
            allocInfo.bindTextureMS = descriptor.bindMS;
            allocInfo.useDynamicScale = descriptor.useDynamicScale;
            allocInfo.useDynamicScaleExplicit = descriptor.useDynamicScaleExplicit;
            allocInfo.memoryless = descriptor.memoryless;
            allocInfo.vrUsage = descriptor.vrUsage;
            allocInfo.enableShadingRate = descriptor.enableShadingRate;
            allocInfo.name = name;

            return allocInfo;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static RTHandleAllocInfo CreateRTHandleAllocInfo(in TextureDesc descriptor, string name)
        {
            // NOTE: this calls default(RTHandleAllocInfo) not RTHandleAllocInfo(string = "")
            RTHandleAllocInfo allocInfo = new RTHandleAllocInfo();
            allocInfo.slices = descriptor.slices;
            allocInfo.format = descriptor.format;
            allocInfo.filterMode = descriptor.filterMode;
            allocInfo.wrapModeU = descriptor.wrapMode;
            allocInfo.wrapModeV = descriptor.wrapMode;
            allocInfo.wrapModeW = descriptor.wrapMode;
            allocInfo.dimension = descriptor.dimension;
            allocInfo.enableRandomWrite = descriptor.enableRandomWrite;
            allocInfo.enableShadingRate = descriptor.enableShadingRate;
            allocInfo.useMipMap = descriptor.useMipMap;
            allocInfo.autoGenerateMips = descriptor.autoGenerateMips;
            allocInfo.anisoLevel = descriptor.anisoLevel;
            allocInfo.mipMapBias = descriptor.mipMapBias;
            allocInfo.isShadowMap = descriptor.isShadowMap;
            allocInfo.msaaSamples = (MSAASamples)descriptor.msaaSamples;
            allocInfo.bindTextureMS = descriptor.bindTextureMS;
            allocInfo.useDynamicScale = descriptor.useDynamicScale;
            allocInfo.useDynamicScaleExplicit = descriptor.useDynamicScaleExplicit;
            allocInfo.memoryless = descriptor.memoryless;
            allocInfo.vrUsage = descriptor.vrUsage;
            allocInfo.enableShadingRate = descriptor.enableShadingRate;
            allocInfo.name = name;

            return allocInfo;
        }
    }
}
