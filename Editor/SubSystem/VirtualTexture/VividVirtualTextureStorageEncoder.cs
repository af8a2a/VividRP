using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor
{
    internal interface IVTGpuStorageEncoder
    {
        string Version { get; }

        bool TryEncodePages(
            GraphicsFormat destinationFormat,
            int physicalPageSize,
            IReadOnlyList<Color32[]> pages,
            VividVirtualTextureBCQuality quality,
            out byte[][] encodedPages,
            out string error);
    }

    internal sealed class VTUnityBCnStorageEncoder : IVTGpuStorageEncoder
    {
        internal const string EncoderVersion = "UnityEditor-BCn-v1";

        public string Version => $"{EncoderVersion}-{Application.unityVersion}";

        public bool TryEncodePages(
            GraphicsFormat destinationFormat,
            int physicalPageSize,
            IReadOnlyList<Color32[]> pages,
            VividVirtualTextureBCQuality quality,
            out byte[][] encodedPages,
            out string error)
        {
            encodedPages = null;
            if (pages == null || pages.Count == 0)
            {
                error = "BCn page batch is empty.";
                return false;
            }

            if (physicalPageSize <= 0 || (physicalPageSize & 3) != 0)
            {
                error = $"BCn physical page size {physicalPageSize} is not 4x4 block aligned.";
                return false;
            }

            if (!TryGetTextureFormat(destinationFormat, out TextureFormat textureFormat))
            {
                error = $"Unsupported BCn destination format {destinationFormat}.";
                return false;
            }

            int pagePixelCount = checked(physicalPageSize * physicalPageSize);
            int pageByteSize = GetPageByteSize(destinationFormat, physicalPageSize);
            int stripCapacity = Mathf.Clamp(SystemInfo.maxTextureSize / physicalPageSize, 1, 64);
            encodedPages = new byte[pages.Count][];

            for (int firstPage = 0; firstPage < pages.Count; firstPage += stripCapacity)
            {
                int pageCount = Mathf.Min(stripCapacity, pages.Count - firstPage);
                var stripPixels = new Color32[checked(pagePixelCount * pageCount)];
                for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
                {
                    Color32[] page = pages[firstPage + pageIndex];
                    if (page == null || page.Length != pagePixelCount)
                    {
                        error = $"BCn source page {firstPage + pageIndex} has an invalid pixel count.";
                        encodedPages = null;
                        return false;
                    }

                    Array.Copy(page, 0, stripPixels, pageIndex * pagePixelCount, pagePixelCount);
                }

                var strip = new Texture2D(
                    physicalPageSize,
                    checked(physicalPageSize * pageCount),
                    TextureFormat.RGBA32,
                    mipChain: false,
                    linear: true)
                {
                    name = "VividVT_BCnPageStrip",
                    hideFlags = HideFlags.HideAndDontSave,
                };

                try
                {
                    strip.SetPixels32(stripPixels, 0);
                    strip.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                    EditorUtility.CompressTexture(strip, textureFormat, ToUnityQuality(quality));
                    NativeArray<byte> encodedStrip = strip.GetRawTextureData<byte>();
                    int expectedByteSize = checked(pageByteSize * pageCount);
                    if (encodedStrip.Length != expectedByteSize)
                    {
                        error = $"Unity BCn encoder returned {encodedStrip.Length} bytes, expected {expectedByteSize}.";
                        encodedPages = null;
                        return false;
                    }

                    for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
                    {
                        var encodedPage = new byte[pageByteSize];
                        NativeArray<byte>.Copy(encodedStrip, pageIndex * pageByteSize, encodedPage, 0, pageByteSize);
                        encodedPages[firstPage + pageIndex] = encodedPage;
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(strip);
                }
            }

            error = null;
            return true;
        }

        internal static int GetPageByteSize(GraphicsFormat format, int physicalPageSize)
        {
            int blockWidth = checked((int)Math.Max(1u, GraphicsFormatUtility.GetBlockWidth(format)));
            int blockHeight = checked((int)Math.Max(1u, GraphicsFormatUtility.GetBlockHeight(format)));
            int blockSize = checked((int)Math.Max(1u, GraphicsFormatUtility.GetBlockSize(format)));
            int blocksX = (physicalPageSize + blockWidth - 1) / blockWidth;
            int blocksY = (physicalPageSize + blockHeight - 1) / blockHeight;
            return checked(blocksX * blocksY * blockSize);
        }

        private static bool TryGetTextureFormat(GraphicsFormat format, out TextureFormat textureFormat)
        {
            textureFormat = format switch
            {
                GraphicsFormat.R_BC4_UNorm => TextureFormat.BC4,
                GraphicsFormat.RG_BC5_UNorm => TextureFormat.BC5,
                GraphicsFormat.RGBA_BC7_UNorm => TextureFormat.BC7,
                GraphicsFormat.RGBA_BC7_SRGB => TextureFormat.BC7,
                _ => TextureFormat.RGBA32,
            };
            return textureFormat != TextureFormat.RGBA32;
        }

        private static TextureCompressionQuality ToUnityQuality(VividVirtualTextureBCQuality quality)
        {
            return quality switch
            {
                VividVirtualTextureBCQuality.Fast => TextureCompressionQuality.Fast,
                VividVirtualTextureBCQuality.High => TextureCompressionQuality.Best,
                _ => TextureCompressionQuality.Normal,
            };
        }
    }
}
