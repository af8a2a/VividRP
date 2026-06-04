using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class VividReflectionProbeTextureCache : IDisposable
    {
        internal const int ConvolutionMipCount = SkyCubemapGGXConvolution.ConvolutionMipCount;

        private const int MaxTexturesInAtlas = 2048;
        private const int MaxFramesTmpUsage = 60;
        private static readonly int InputTexId = Shader.PropertyToID("_InputTex");
        private static readonly int FaceIndexId = Shader.PropertyToID("_FaceIndex");
        private static readonly int LodId = Shader.PropertyToID("_LoD");
        private static readonly ProfilingSampler s_ConvertReflectionProbeSampler = new("VividReflectionProbeTextureCache.Convert");
        private static readonly ProfilingSampler s_BlitTextureToAtlasSampler = new("VividReflectionProbeTextureCache.BlitToAtlas");
        private static readonly ProfilingSampler s_UpdateReflectionProbeAtlasSampler = new("VividReflectionProbeTextureCache.UpdateAtlas");

        private readonly int m_AtlasWidth;
        private readonly int m_AtlasHeight;
        private readonly GraphicsFormat m_AtlasFormat;
        private readonly int m_AtlasMipCount;
        private readonly int m_AtlasSlicesCount = 1;
        private readonly int m_CubeMipPadding;
        private readonly int m_CubeTexelPadding;
        private readonly bool m_DecreaseResToFit;
        private readonly Dictionary<VividTextureId, TextureCacheEntry> m_TextureLRUAndHash = new();
        private readonly List<TextureLRUEntry> m_TextureLRUSorted = new();
        private readonly SkyCubemapGGXConvolution m_BsdfFilter = new();
        private readonly MaterialPropertyBlock m_ConvertTexturePropertyBlock = new();

        private RTHandle m_AtlasTexture;
        private VividTexture2DAtlasDynamic m_Atlas;
        private Material m_ConvertTextureMaterial;
        private uint m_CurrentRender;
        private int m_CubeFrameFetchIndex;
        private bool m_NoMoreSpaceErrorLogged;
        private int m_TempCubeTexturesLastFrameUsed;
        private int m_TmpTextureConvertedSize;
        private int m_TmpTextureConvolvedWidth;
        private int m_TmpTextureConvolvedHeight;
        private GraphicsFormat m_TmpTextureConvertedFormat;
        private GraphicsFormat m_TmpTextureConvolvedFormat;
        private FilterMode m_TmpTextureConvertedFilterMode;
        private FilterMode m_TmpTextureConvolvedFilterMode;
        private RenderTexture m_TempConvertedReflectionProbeTexture;
        private RenderTexture m_TempConvolvedReflectionProbeTexture;
        private Vector4 m_DebugScaleOffset;
        private bool m_HasDebugScaleOffset;

        private readonly struct TextureCacheEntry
        {
            internal readonly uint LastUsedRender;
            internal readonly uint Hash;

            internal TextureCacheEntry(uint lastUsedRender, uint hash)
            {
                LastUsedRender = lastUsedRender;
                Hash = hash;
            }
        }

        private readonly struct TextureLRUEntry
        {
            internal readonly VividTextureId TextureId;
            internal readonly uint LastUsedRender;

            internal TextureLRUEntry(VividTextureId textureId, uint lastUsedRender)
            {
                TextureId = textureId;
                LastUsedRender = lastUsedRender;
            }
        }

        internal VividReflectionProbeTextureCache(
            VividRPCoreResources resources,
            int width,
            int height,
            GraphicsFormat format,
            bool decreaseResToFit,
            int lastValidCubeMip)
        {
            Assert.IsTrue(Mathf.IsPowerOfTwo(width) && Mathf.IsPowerOfTwo(height));
            Assert.IsTrue(width <= (int)VividReflectionProbeAtlasResolution.Resolution16384x16384);
            Assert.IsTrue(height <= (int)VividReflectionProbeAtlasResolution.Resolution16384x16384);
            Assert.IsTrue(
                format == GraphicsFormat.B10G11R11_UFloatPack32 || format == GraphicsFormat.R16G16B16A16_SFloat);

            m_AtlasWidth = width;
            m_AtlasHeight = height;
            m_AtlasFormat = format;
            m_AtlasMipCount = Mathf.FloorToInt(Mathf.Log(Mathf.Max(m_AtlasWidth, m_AtlasHeight), 2.0f)) + 1;
            m_CubeMipPadding = Mathf.Clamp(lastValidCubeMip, 0, ConvolutionMipCount - 1);
            m_CubeTexelPadding = (1 << m_CubeMipPadding) * 2;
            m_DecreaseResToFit = decreaseResToFit;

            m_AtlasTexture = RTHandles.Alloc(
                width: width,
                height: height,
                slices: m_AtlasSlicesCount,
                dimension: TextureDimension.Tex2DArray,
                filterMode: FilterMode.Trilinear,
                colorFormat: format,
                wrapMode: TextureWrapMode.Clamp,
                useMipMap: true,
                autoGenerateMips: false,
                name: "VividReflectionProbeAtlas");
            m_Atlas = new VividTexture2DAtlasDynamic(width, height, MaxTexturesInAtlas, m_AtlasTexture);

            m_BsdfFilter.Build(resources);
            var convertShader = resources?.BlitCubeTextureFaceShader;
#if UNITY_EDITOR
            convertShader ??= Shader.Find("Hidden/VividRP/BlitCubeTextureFace");
#endif
            if (convertShader != null)
                m_ConvertTextureMaterial = CoreUtils.CreateEngineMaterial(convertShader);
        }

        internal bool MatchesSettings(
            int width,
            int height,
            GraphicsFormat format,
            bool decreaseResToFit,
            int lastValidCubeMip)
        {
            return m_AtlasWidth == width
                && m_AtlasHeight == height
                && m_AtlasFormat == format
                && m_DecreaseResToFit == decreaseResToFit
                && m_CubeMipPadding == Mathf.Clamp(lastValidCubeMip, 0, ConvolutionMipCount - 1);
        }

        internal static int GetReflectionProbeSizeInAtlas(int textureSize)
        {
            textureSize = Mathf.Max(textureSize, 32);
            return textureSize < 512 ? textureSize * 4 : textureSize * 2;
        }

        internal static long GetApproxCacheSizeInBytes(int elementsCount, int width, int height, GraphicsFormat format)
        {
            const double mipmapFactorApprox = 1.33;
            return (long)(elementsCount * width * height * mipmapFactorApprox * GraphicsFormatUtility.GetBlockSize(format));
        }

        internal static int GetBSDFFilterSourceSize(int textureSize)
        {
            return Mathf.Max(textureSize, 1 << (ConvolutionMipCount - 1));
        }

        internal static int GetBSDFFilteredSourceMipLevel(int atlasMipLevel)
        {
            return Mathf.Clamp(atlasMipLevel, 0, ConvolutionMipCount - 1);
        }

        internal Vector4 FetchCubeReflectionProbe(CommandBuffer cmd, Texture texture, out int fetchIndex)
        {
            fetchIndex = -1;

            if (cmd == null || !IsValidCubeTexture(texture))
                return Vector4.zero;

            fetchIndex = m_CubeFrameFetchIndex++;
            var scaleOffset = Vector4.zero;
            var textureId = GetTextureIdAndSize(texture, out _);

            if (NeedsUpdate(textureId, GetTextureHash(texture), ref scaleOffset)
                && !UpdateTexture(cmd, texture, ref scaleOffset))
            {
                LogErrorNoMoreSpaceOnce();
            }

            if (scaleOffset.x > 0.0f && scaleOffset.y > 0.0f)
            {
                m_DebugScaleOffset = scaleOffset;
                m_HasDebugScaleOffset = true;
            }

            return scaleOffset;
        }

        internal void ReserveReflectionProbeSlot(Texture texture)
        {
            if (!IsValidCubeTexture(texture))
                return;

            var textureId = GetTextureIdAndSize(texture, out var textureSize);
            if (m_Atlas.IsCached(out _, textureId))
                return;

            var scaleOffset = Vector4.zero;
            if (!TryAllocateTexture(textureId, textureSize, ref scaleOffset) && RelayoutTextureAtlas())
                TryAllocateTexture(textureId, textureSize, ref scaleOffset);
        }

        internal void NewFrame()
        {
            m_CubeFrameFetchIndex = 0;
        }

        internal void NewRender()
        {
            m_NoMoreSpaceErrorLogged = false;
            unchecked
            {
                ++m_CurrentRender;
            }

            m_TextureLRUSorted.Clear();
            foreach (var pair in m_TextureLRUAndHash)
                m_TextureLRUSorted.Add(new TextureLRUEntry(pair.Key, pair.Value.LastUsedRender));

            m_TextureLRUSorted.Sort((left, right) => right.LastUsedRender.CompareTo(left.LastUsedRender));
        }

        internal void GarbageCollectTmpResources()
        {
            if (Mathf.Max((int)m_CurrentRender - m_TempCubeTexturesLastFrameUsed, 0) <= MaxFramesTmpUsage)
                return;

            ReleaseTemporaryReflectionProbeTextures();
        }

        internal void ClearAtlasAllocator()
        {
            m_Atlas?.ResetAllocator();
            m_TextureLRUAndHash.Clear();
            m_TextureLRUSorted.Clear();
            m_DebugScaleOffset = Vector4.zero;
            m_HasDebugScaleOffset = false;
        }

        internal void Clear(CommandBuffer cmd)
        {
            ClearAtlasAllocator();

            if (cmd == null || m_AtlasTexture == null)
                return;

            for (var sliceIndex = 0; sliceIndex < m_AtlasSlicesCount; sliceIndex++)
            {
                for (var mipLevel = 0; mipLevel < m_AtlasMipCount; mipLevel++)
                {
                    cmd.SetRenderTarget(m_AtlasTexture.rt, mipLevel, CubemapFace.Unknown, sliceIndex);
                    Blitter.BlitQuad(
                        cmd,
                        Texture2D.blackTexture,
                        new Vector4(1.0f, 1.0f, 0.0f, 0.0f),
                        new Vector4(1.0f, 1.0f, 0.0f, 0.0f),
                        mipLevel,
                        true);
                }
            }
        }

        internal Texture GetAtlasTexture()
        {
            return m_AtlasTexture?.rt;
        }

        internal Vector4 GetTextureAtlasCubeData()
        {
            return new Vector4(
                (float)m_CubeTexelPadding / m_AtlasWidth,
                (float)m_CubeTexelPadding / m_AtlasHeight,
                m_CubeMipPadding,
                0.0f);
        }

        internal int GetAtlasMipCount()
        {
            return m_AtlasMipCount;
        }

        internal int GetAtlasSamplingMipCount()
        {
            return Mathf.Min(m_AtlasMipCount, ConvolutionMipCount);
        }

        internal int GetEnvSliceSize()
        {
            return m_AtlasSlicesCount;
        }

        internal bool TryGetDebugScaleOffset(out Vector4 scaleOffset)
        {
            scaleOffset = m_DebugScaleOffset;
            return m_HasDebugScaleOffset;
        }

        public void Dispose()
        {
            m_BsdfFilter.Dispose();
            m_Atlas?.Dispose();
            m_Atlas = null;
            m_AtlasTexture?.Release();
            m_AtlasTexture = null;

            if (m_ConvertTextureMaterial != null)
            {
                CoreUtils.Destroy(m_ConvertTextureMaterial);
                m_ConvertTextureMaterial = null;
            }

            ReleaseTemporaryReflectionProbeTextures();
            m_TextureLRUAndHash.Clear();
            m_TextureLRUSorted.Clear();
        }

        private static bool IsValidCubeTexture(Texture texture)
        {
            if (texture == null
                || texture.width <= 0
                || texture.height <= 0
                || texture.width != texture.height
                || texture.dimension != TextureDimension.Cube)
            {
                return false;
            }

            return texture is not RenderTexture renderTexture || renderTexture.IsCreated();
        }

        private static VividTextureId GetTextureIdAndSize(Texture texture, out int textureSize)
        {
            textureSize = GetReflectionProbeSizeInAtlas(texture.width);
            return new VividTextureId(texture.GetEntityId(), textureSize);
        }

        private uint GetTextureHash(Texture texture)
        {
            unchecked
            {
                var hash = (uint)texture.imageContentsHash.GetHashCode();
                if (texture is RenderTexture)
                    hash ^= m_CurrentRender;
                return hash;
            }
        }

        private bool NeedsUpdate(VividTextureId textureId, uint textureHash, ref Vector4 scaleOffset)
        {
            var needsUpdate = false;

            if (!m_Atlas.IsCached(out scaleOffset, textureId))
                needsUpdate = true;
            else if (!m_TextureLRUAndHash.TryGetValue(textureId, out var entry) || entry.Hash != textureHash)
                needsUpdate = true;

            m_TextureLRUAndHash[textureId] = new TextureCacheEntry(m_CurrentRender, textureHash);
            return needsUpdate;
        }

        private bool UpdateTexture(CommandBuffer cmd, Texture texture, ref Vector4 scaleOffset)
        {
            using (new ProfilingScope(cmd, s_UpdateReflectionProbeAtlasSampler))
            {
                var textureId = GetTextureIdAndSize(texture, out var textureSize);
                if (!m_Atlas.IsCached(out scaleOffset, textureId)
                    && !TryAllocateTexture(textureId, textureSize, ref scaleOffset))
                {
                    return false;
                }

                var convertedTexture = PrepareCubeReflectionProbeTexture(cmd, texture, textureSize);
                var sourceTexture = convertedTexture != null ? convertedTexture : texture;
                var filteredTexture = BuildBSDFFilteredCubeMipChain(cmd, sourceTexture);
                BlitTextureCube(cmd, scaleOffset, filteredTexture, 0);
                return true;
            }
        }

        private RenderTexture PrepareCubeReflectionProbeTexture(CommandBuffer cmd, Texture texture, int textureSize)
        {
            var renderTexture = texture as RenderTexture;
            var cubemap = texture as Cubemap;

            Assert.IsTrue(renderTexture != null || cubemap != null);

            using (new ProfilingScope(cmd, s_ConvertReflectionProbeSampler))
            {
                var cubeSize = GetBSDFFilterSourceSize(textureSize);
                var conversionRequired = texture.graphicsFormat != m_AtlasFormat;
                conversionRequired |= cubemap != null && cubemap.mipmapCount == 1;
                conversionRequired |= renderTexture != null && !renderTexture.useMipMap;
                conversionRequired |= texture.width != cubeSize;

                if (conversionRequired)
                {
                    if (m_ConvertTextureMaterial == null)
                        return null;

                    var convertedTexture = GetTempConvertedReflectionProbeTexture(texture, cubeSize);
                    m_ConvertTexturePropertyBlock.Clear();
                    m_ConvertTexturePropertyBlock.SetTexture(InputTexId, texture);
                    m_ConvertTexturePropertyBlock.SetFloat(LodId, 0.0f);

                    for (var faceIndex = 0; faceIndex < 6; faceIndex++)
                    {
                        m_ConvertTexturePropertyBlock.SetFloat(FaceIndexId, faceIndex);
                        CoreUtils.SetRenderTarget(cmd, convertedTexture, ClearFlag.None, Color.black, 0, (CubemapFace)faceIndex);
                        CoreUtils.DrawFullScreen(cmd, m_ConvertTextureMaterial, m_ConvertTexturePropertyBlock);
                    }

                    cmd.GenerateMips(convertedTexture);
                    return convertedTexture;
                }

                if (renderTexture != null && renderTexture.useMipMap && !renderTexture.autoGenerateMips)
                    cmd.GenerateMips(renderTexture);
            }

            return null;
        }

        private RenderTexture GetTempConvertedReflectionProbeTexture(Texture source, int cubeSize)
        {
            if (m_TempConvertedReflectionProbeTexture == null
                || m_TmpTextureConvertedSize != cubeSize
                || m_TmpTextureConvertedFormat != m_AtlasFormat
                || m_TmpTextureConvertedFilterMode != source.filterMode)
            {
                if (m_TempConvertedReflectionProbeTexture != null)
                    RenderTexture.ReleaseTemporary(m_TempConvertedReflectionProbeTexture);

                var convertedTexture = RenderTexture.GetTemporary(cubeSize, cubeSize, 0, m_AtlasFormat);
                convertedTexture.dimension = TextureDimension.Cube;
                convertedTexture.filterMode = source.filterMode;
                convertedTexture.useMipMap = true;
                convertedTexture.autoGenerateMips = false;
                convertedTexture.wrapMode = TextureWrapMode.Clamp;
                convertedTexture.name = "VividConvertedReflectionProbeTemp";
                convertedTexture.Create();
                m_TempConvertedReflectionProbeTexture = convertedTexture;
                m_TmpTextureConvertedSize = cubeSize;
                m_TmpTextureConvertedFormat = m_AtlasFormat;
                m_TmpTextureConvertedFilterMode = source.filterMode;
            }

            m_TempCubeTexturesLastFrameUsed = (int)m_CurrentRender;
            return m_TempConvertedReflectionProbeTexture;
        }

        private Texture BuildBSDFFilteredCubeMipChain(CommandBuffer cmd, Texture texture)
        {
            if (!m_BsdfFilter.IsSupported)
                return texture;

            var filteredTexture = GetTempConvolvedReflectionProbeTexture(texture);
            return m_BsdfFilter.Convolve(cmd, texture, filteredTexture) ? filteredTexture : texture;
        }

        private RenderTexture GetTempConvolvedReflectionProbeTexture(Texture texture)
        {
            if (m_TempConvolvedReflectionProbeTexture == null
                || m_TmpTextureConvolvedWidth != texture.width
                || m_TmpTextureConvolvedHeight != texture.height
                || m_TmpTextureConvolvedFormat != m_AtlasFormat
                || m_TmpTextureConvolvedFilterMode != texture.filterMode)
            {
                if (m_TempConvolvedReflectionProbeTexture != null)
                    RenderTexture.ReleaseTemporary(m_TempConvolvedReflectionProbeTexture);

                var convolvedTexture = RenderTexture.GetTemporary(texture.width, texture.height, 0, m_AtlasFormat);
                convolvedTexture.dimension = TextureDimension.Cube;
                convolvedTexture.filterMode = texture.filterMode;
                convolvedTexture.useMipMap = true;
                convolvedTexture.autoGenerateMips = false;
                convolvedTexture.wrapMode = TextureWrapMode.Clamp;
                convolvedTexture.anisoLevel = 0;
                convolvedTexture.name = "VividConvolvedReflectionProbeTemp";
                convolvedTexture.Create();
                m_TempConvolvedReflectionProbeTexture = convolvedTexture;
                m_TmpTextureConvolvedWidth = texture.width;
                m_TmpTextureConvolvedHeight = texture.height;
                m_TmpTextureConvolvedFormat = m_AtlasFormat;
                m_TmpTextureConvolvedFilterMode = texture.filterMode;
            }

            m_TempCubeTexturesLastFrameUsed = (int)m_CurrentRender;
            return m_TempConvolvedReflectionProbeTexture;
        }

        private void BlitTextureCube(CommandBuffer cmd, Vector4 scaleOffset, Texture texture, int arraySlice)
        {
            Assert.IsTrue(texture.dimension == TextureDimension.Cube);

            using (new ProfilingScope(cmd, s_BlitTextureToAtlasSampler))
            {
                var texelPadding = m_CubeTexelPadding;
                var textureWidthInAtlas = Mathf.CeilToInt(scaleOffset.x * m_AtlasWidth);
                var textureHeightInAtlas = Mathf.CeilToInt(scaleOffset.y * m_AtlasHeight);
                var textureSizeWithoutPadding = GetTextureSizeWithoutPadding(textureWidthInAtlas, textureHeightInAtlas, texelPadding);
                var bilinear = texture.filterMode != FilterMode.Point;

                for (var mipLevel = 0; mipLevel < m_AtlasMipCount; mipLevel++)
                {
                    if (mipLevel > m_CubeMipPadding)
                        texelPadding *= 2;

                    cmd.SetRenderTarget(m_AtlasTexture.rt, mipLevel, CubemapFace.Unknown, arraySlice);
                    // The BSDF filter only builds the fixed HDRP convolution range; lower physical atlas mips reuse the roughest filtered level.
                    var sourceMipLevel = GetBSDFFilteredSourceMipLevel(mipLevel);
                    Blitter.BlitCubeToOctahedral2DQuadWithPadding(
                        cmd,
                        texture,
                        textureSizeWithoutPadding,
                        scaleOffset,
                        sourceMipLevel,
                        bilinear,
                        texelPadding);
                }
            }
        }

        private static Vector2 GetTextureSizeWithoutPadding(int textureWidth, int textureHeight, int texelPadding)
        {
            return new Vector2(
                Mathf.Max(textureWidth - texelPadding, 1),
                Mathf.Max(textureHeight - texelPadding, 1));
        }

        private bool RelayoutTextureAtlas()
        {
            var atlasEntries = new List<(VividTextureId textureId, Vector4 scaleOffset)>(m_TextureLRUAndHash.Count);
            foreach (var pair in m_TextureLRUAndHash)
            {
                if (m_Atlas.IsCached(out var scaleOffset, pair.Key))
                    atlasEntries.Add((pair.Key, scaleOffset));
            }

            atlasEntries.Sort((left, right) => right.scaleOffset.x.CompareTo(left.scaleOffset.x));
            m_Atlas.ResetAllocator();

            var success = true;
            foreach (var entry in atlasEntries)
            {
                var textureWidth = Mathf.CeilToInt(entry.scaleOffset.x * m_AtlasWidth);
                var textureHeight = Mathf.CeilToInt(entry.scaleOffset.y * m_AtlasHeight);

                if (m_Atlas.EnsureTextureSlot(out _, out var scaleOffset, entry.textureId, textureWidth, textureHeight))
                {
                    var texturePos = new Vector2Int(
                        Mathf.FloorToInt(entry.scaleOffset.z * m_AtlasWidth),
                        Mathf.FloorToInt(entry.scaleOffset.w * m_AtlasHeight));
                    var newTexturePos = new Vector2Int(
                        Mathf.FloorToInt(scaleOffset.z * m_AtlasWidth),
                        Mathf.FloorToInt(scaleOffset.w * m_AtlasHeight));

                    if (texturePos != newTexturePos && m_TextureLRUAndHash.TryGetValue(entry.textureId, out var oldEntry))
                        m_TextureLRUAndHash[entry.textureId] = new TextureCacheEntry(oldEntry.LastUsedRender, 0);
                }
                else
                {
                    m_TextureLRUAndHash.Remove(entry.textureId);
                    success = false;
                }
            }

            return success;
        }

        private bool TryAllocateTexture(VividTextureId textureId, int textureSize, ref Vector4 scaleOffset)
        {
            Assert.IsTrue(Mathf.IsPowerOfTwo(textureSize));
            Assert.IsTrue(!m_Atlas.IsCached(out _, textureId));

            if (m_Atlas.EnsureTextureSlot(out _, out scaleOffset, textureId, textureSize, textureSize))
                return true;

            for (var textureIndex = m_TextureLRUSorted.Count - 1; textureIndex >= 0; textureIndex--)
            {
                var textureLRU = m_TextureLRUSorted[textureIndex];
                const int previousRender = 1;

                if (m_CurrentRender - textureLRU.LastUsedRender <= previousRender)
                    break;

                m_Atlas.ReleaseTextureSlot(textureLRU.TextureId);
                m_TextureLRUAndHash.Remove(textureLRU.TextureId);
                m_TextureLRUSorted.RemoveAt(textureIndex);

                if (m_Atlas.EnsureTextureSlot(out _, out scaleOffset, textureId, textureSize, textureSize))
                    return true;
            }

            if (m_DecreaseResToFit
                && m_Atlas.EnsureTextureSlot(out _, out scaleOffset, textureId, textureSize / 2, textureSize / 2))
            {
                return true;
            }

            m_TextureLRUAndHash.Remove(textureId);
            return false;
        }

        private void LogErrorNoMoreSpaceOnce()
        {
            if (m_NoMoreSpaceErrorLogged)
                return;

            m_NoMoreSpaceErrorLogged = true;
            Debug.LogError("[VividRP] No more space in Reflection Probe Atlas. Increase the atlas size in the VividRP asset.");
        }

        private void ReleaseTemporaryReflectionProbeTextures()
        {
            if (m_TempConvertedReflectionProbeTexture != null)
            {
                RenderTexture.ReleaseTemporary(m_TempConvertedReflectionProbeTexture);
                m_TempConvertedReflectionProbeTexture = null;
            }

            if (m_TempConvolvedReflectionProbeTexture != null)
            {
                RenderTexture.ReleaseTemporary(m_TempConvolvedReflectionProbeTexture);
                m_TempConvolvedReflectionProbeTexture = null;
            }
        }
    }
}
