using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    /// <summary>
    /// Serializable texture descriptor for RenderGraph resources.
    /// Mirrors UnityEngine.Rendering.RenderGraphModule.TextureDesc but can be serialized in assets.
    /// </summary>
    [Serializable]
    public class RenderGraphTextureDesc
    {
        [Header("Dimensions")]
        public int Width = 1920;
        public int Height = 1080;
        public int Slices = 1;
        public TextureDimension Dimension = TextureDimension.Tex2D;

        [Header("Format")]
        public GraphicsFormat ColorFormat = GraphicsFormat.R8G8B8A8_SRGB;
        public DepthBits DepthBufferBits = DepthBits.None;

        [Header("Sampling")]
        public MSAASamples MsaaSamples = MSAASamples.None;
        public FilterMode FilterMode = FilterMode.Bilinear;
        public TextureWrapMode WrapMode = TextureWrapMode.Clamp;
        public int AnisoLevel = 1;
        public float MipMapBias = 0f;

        [Header("Mip Maps")]
        public bool UseMipMap = false;
        public bool AutoGenerateMips = false;
        public int MipCount = 1;

        [Header("Clear")]
        public bool ClearBuffer = true;
        public Color ClearColor = Color.clear;

        [Header("Flags")]
        public bool EnableRandomWrite = false;
        public bool BindTextureMS = false;
        public bool UseDynamicScale = false;
        public bool UseDynamicScaleExplicit = false;
        public Vector2 ScaleFactor = Vector2.one;

        [Header("Metadata")]
        public string Name = "Texture";

        /// <summary>
        /// Converts this serializable descriptor to Unity's TextureDesc.
        /// </summary>
        public TextureDesc ToTextureDesc()
        {
            var desc = new TextureDesc(Width, Height, Slices > 1)
            {
                dimension = Dimension,
                slices = Slices,
                colorFormat = ColorFormat,
                depthBufferBits = DepthBufferBits,
                msaaSamples = MsaaSamples,
                filterMode = FilterMode,
                wrapMode = WrapMode,
                anisoLevel = AnisoLevel,
                mipMapBias = MipMapBias,
                useMipMap = UseMipMap,
                autoGenerateMips = AutoGenerateMips,
                clearBuffer = ClearBuffer,
                clearColor = ClearColor,
                enableRandomWrite = EnableRandomWrite,
                bindTextureMS = BindTextureMS,
                useDynamicScale = UseDynamicScale,
                useDynamicScaleExplicit = UseDynamicScaleExplicit,
                name = Name
            };


            if (UseMipMap && MipCount > 1)
            {
                // MipCount is calculated automatically by RenderGraph based on dimensions
                // but we store it for reference
            }

            return desc;
        }

        /// <summary>
        /// Creates a RenderGraphTextureDesc from Unity's TextureDesc.
        /// </summary>
        public static RenderGraphTextureDesc FromTextureDesc(TextureDesc desc)
        {
            return new RenderGraphTextureDesc
            {
                Width = desc.width,
                Height = desc.height,
                Slices = desc.slices,
                Dimension = desc.dimension,
                ColorFormat = desc.colorFormat,
                DepthBufferBits = desc.depthBufferBits,
                MsaaSamples = desc.msaaSamples,
                FilterMode = desc.filterMode,
                WrapMode = desc.wrapMode,
                AnisoLevel = desc.anisoLevel,
                MipMapBias = desc.mipMapBias,
                UseMipMap = desc.useMipMap,
                AutoGenerateMips = desc.autoGenerateMips,
                ClearBuffer = desc.clearBuffer,
                ClearColor = desc.clearColor,
                EnableRandomWrite = desc.enableRandomWrite,
                BindTextureMS = desc.bindTextureMS,
                UseDynamicScale = desc.useDynamicScale,
                UseDynamicScaleExplicit = desc.useDynamicScaleExplicit,
                Name = desc.name,
            };
        }

        /// <summary>
        /// Creates a default descriptor for a standard color target.
        /// </summary>
        public static RenderGraphTextureDesc CreateColorTarget(int width, int height, GraphicsFormat format = GraphicsFormat.R8G8B8A8_SRGB)
        {
            return new RenderGraphTextureDesc
            {
                Width = width,
                Height = height,
                ColorFormat = format,
                ClearBuffer = true,
                ClearColor = Color.clear,
                Name = "ColorTarget"
            };
        }

        /// <summary>
        /// Creates a default descriptor for a depth target.
        /// </summary>
        public static RenderGraphTextureDesc CreateDepthTarget(int width, int height, DepthBits depthBits = DepthBits.Depth32)
        {
            return new RenderGraphTextureDesc
            {
                Width = width,
                Height = height,
                ColorFormat = GraphicsFormat.None,
                DepthBufferBits = depthBits,
                ClearBuffer = true,
                Name = "DepthTarget"
            };
        }
    }
}
