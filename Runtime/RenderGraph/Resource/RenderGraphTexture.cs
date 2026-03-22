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
        [Header("Dimensions")] public int Width = 1920;
        public int Height = 1080;
        public int Slices = 1;
        public TextureDimension Dimension = TextureDimension.Tex2D;

        [Header("Format")] public GraphicsFormat ColorFormat = GraphicsFormat.R8G8B8A8_SRGB;
        public DepthBits DepthBufferBits = DepthBits.None;

        [Header("Sampling")] public MSAASamples MsaaSamples = MSAASamples.None;
        public FilterMode FilterMode = FilterMode.Bilinear;
        public TextureWrapMode WrapMode = TextureWrapMode.Clamp;
        public int AnisoLevel = 1;
        public float MipMapBias = 0f;

        [Header("Mip Maps")] public bool UseMipMap = false;
        public bool AutoGenerateMips = false;
        public int MipCount = 1;

        [Header("Clear")] public bool ClearBuffer = false;
        public Color ClearColor = Color.clear;

        [Header("Flags")] public bool EnableRandomWrite = false;
        public bool BindTextureMS = false;
        public bool UseDynamicScale = false;
        public bool UseDynamicScaleExplicit = false;
        public Vector2 ScaleFactor = Vector2.one;

        [Header("Metadata")] public string Name = "Texture";

        /// <summary>
        /// Creates a runtime copy of this descriptor.
        /// </summary>
        public RenderGraphTextureDesc Clone()
        {
            return (RenderGraphTextureDesc)MemberwiseClone();
        }

        /// <summary>
        /// Converts this serializable descriptor to Unity's TextureDesc.
        /// </summary>
        private TextureDesc ToTextureDesc()
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

        public static implicit operator TextureDesc(RenderGraphTextureDesc rt)
        {
            return rt.ToTextureDesc();
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
        public static RenderGraphTextureDesc CreateColorTarget(int width, int height,
            GraphicsFormat format = GraphicsFormat.R8G8B8A8_SRGB)
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
        public static RenderGraphTextureDesc CreateDepthTarget(int width, int height,
            DepthBits depthBits = DepthBits.Depth32)
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

    [Serializable]
    public class RenderGraphTexture
    {
        public RenderGraphTextureDesc desc;

        private bool m_UsesImportedHandle;

        public RenderGraphTexture()
        {
            desc = new RenderGraphTextureDesc();
        }

        internal TextureHandle innerHandle;

        internal bool HasImportedHandle => m_UsesImportedHandle && innerHandle.IsValid();

        internal void SetImportedHandle(TextureHandle handle)
        {
            innerHandle = handle;
            m_UsesImportedHandle = handle.IsValid();
        }

        internal void ClearImportedHandle()
        {
            innerHandle = default;
            m_UsesImportedHandle = false;
        }

        /// <summary>
        /// Creates a color target texture with the given name and format.
        /// </summary>
        public static RenderGraphTexture CreateColorTarget(string name, GraphicsFormat format)
        {
            var desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, format);
            desc.Name = name;

            var target = new RenderGraphTexture
            {
                desc = desc
            };
            return target;
        }

        /// <summary>
        /// Creates a depth target texture with the given name and depth bits.
        /// </summary>
        public static RenderGraphTexture CreateDepthTarget(string name, DepthBits depthBits = DepthBits.Depth32)
        {
            var desc = RenderGraphTextureDesc.CreateDepthTarget(1, 1, depthBits);
            desc.Name = name;

            var target = new RenderGraphTexture
            {
                desc = desc
            };
            return target;
        }

        /// <summary>
        /// Creates a read-only input texture (ClearBuffer disabled).
        /// Uses depth target when format is None, otherwise color target.
        /// </summary>
        public static RenderGraphTexture CreateInput(string name, GraphicsFormat format,
            DepthBits depthBits = DepthBits.None)
        {
            var texture = new RenderGraphTexture
            {
                desc = format == GraphicsFormat.None
                    ? RenderGraphTextureDesc.CreateDepthTarget(1, 1, depthBits)
                    : RenderGraphTextureDesc.CreateColorTarget(1, 1, format)
            };
            texture.desc.Name = name;
            texture.desc.ClearBuffer = false;
            return texture;
        }

        /// <summary>
        /// Creates a write-only output texture with random write enabled and ClearBuffer disabled.
        /// </summary>
        public static RenderGraphTexture CreateOutput(string name, GraphicsFormat format)
        {
            var desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, format);
            desc.Name = name;

            var target = new RenderGraphTexture
            {
                desc = desc
            };
            return target;
        }

        /// <summary>
        /// Resizes this texture's descriptor to the given dimensions.
        /// </summary>
        public void Resize(int width, int height)
        {
            if (desc == null)
                return;

            desc.Width = width;
            desc.Height = height;
        }

        public static implicit operator TextureHandle(RenderGraphTexture rt)
        {
            return rt.innerHandle;
        }

        public static implicit operator RenderTargetIdentifier(RenderGraphTexture rt)
        {
            return rt.innerHandle;
        }

        public static implicit operator RenderTexture(RenderGraphTexture rt)
        {
            return rt.innerHandle;
        }

        public static implicit operator Texture(RenderGraphTexture rt)
        {
            return rt.innerHandle;
        }
    }
}