using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace VividRP.Runtime
{
    internal sealed class VTConstantEncodedPageFinalizer : IVTEncodedPageFinalizer
    {
        private readonly byte[][] m_EncodedLayers;

        internal VTConstantEncodedPageFinalizer(in VTStackDesc stackDesc)
        {
            m_EncodedLayers = new byte[stackDesc.LayerCount][];
            for (int layerIndex = 0; layerIndex < stackDesc.LayerCount; layerIndex++)
            {
                VTLayerDesc layer = stackDesc.GetLayer(layerIndex);
                m_EncodedLayers[layerIndex] = EncodeConstantPage(
                    layer.GraphicsFormat,
                    layer.FallbackColor,
                    stackDesc.PhysicalPageSize);
            }
        }

        public int LayerCount => m_EncodedLayers.Length;

        public void FinalizeEncodedUploadLayer(Texture2DArray stagingTexture, int slice, int layerIndex)
        {
            if (stagingTexture == null)
                throw new ArgumentNullException(nameof(stagingTexture));
            if (layerIndex < 0 || layerIndex >= m_EncodedLayers.Length)
                throw new ArgumentOutOfRangeException(nameof(layerIndex));

            stagingTexture.SetPixelData(m_EncodedLayers[layerIndex], 0, slice, 0);
        }

        public void Dispose()
        {
        }

        private static byte[] EncodeConstantPage(GraphicsFormat format, Color32 color, int physicalPageSize)
        {
            if (physicalPageSize <= 0 || (physicalPageSize & 3) != 0)
                throw new ArgumentOutOfRangeException(nameof(physicalPageSize));

            byte[] block = format switch
            {
                GraphicsFormat.R_BC4_UNorm => EncodeBc4Block(color.r),
                GraphicsFormat.RG_BC5_UNorm => EncodeBc5Block(color.r, color.g),
                GraphicsFormat.RGBA_BC7_UNorm => EncodeBc7Mode6Block(color),
                GraphicsFormat.RGBA_BC7_SRGB => EncodeBc7Mode6Block(color),
                _ => throw new InvalidOperationException(
                    $"Compressed VT fallback pages do not support {format}."),
            };

            int blockCount = checked((physicalPageSize / 4) * (physicalPageSize / 4));
            var page = new byte[checked(block.Length * blockCount)];
            for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
                Buffer.BlockCopy(block, 0, page, blockIndex * block.Length, block.Length);
            return page;
        }

        internal static byte[] EncodeBc4Block(byte value)
        {
            return new[]
            {
                value, value,
                (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0,
            };
        }

        internal static byte[] EncodeBc5Block(byte red, byte green)
        {
            byte[] redBlock = EncodeBc4Block(red);
            byte[] greenBlock = EncodeBc4Block(green);
            var block = new byte[16];
            Buffer.BlockCopy(redBlock, 0, block, 0, redBlock.Length);
            Buffer.BlockCopy(greenBlock, 0, block, redBlock.Length, greenBlock.Length);
            return block;
        }

        internal static byte[] EncodeBc7Mode6Block(Color32 color)
        {
            var block = new byte[16];
            int bitPosition = 0;
            WriteBits(block, ref bitPosition, 1u << 6, 7);

            byte[] channels = { color.r, color.g, color.b, color.a };
            int pBit = ChooseEndpointPBit(channels);
            for (int channelIndex = 0; channelIndex < channels.Length; channelIndex++)
            {
                uint quantized = QuantizeSevenBits(channels[channelIndex], pBit);
                WriteBits(block, ref bitPosition, quantized, 7);
                WriteBits(block, ref bitPosition, quantized, 7);
            }

            WriteBits(block, ref bitPosition, (uint)pBit, 1);
            WriteBits(block, ref bitPosition, (uint)pBit, 1);
            // Mode 6 has 63 index bits. They remain zero, selecting endpoint zero.
            return block;
        }

        private static int ChooseEndpointPBit(byte[] channels)
        {
            int zeroError = 0;
            int oneError = 0;
            for (int channelIndex = 0; channelIndex < channels.Length; channelIndex++)
            {
                int value = channels[channelIndex];
                int zeroDecoded = (int)QuantizeSevenBits(value, 0) * 2;
                int oneDecoded = (int)QuantizeSevenBits(value, 1) * 2 + 1;
                zeroError += (zeroDecoded - value) * (zeroDecoded - value);
                oneError += (oneDecoded - value) * (oneDecoded - value);
            }

            return oneError < zeroError ? 1 : 0;
        }

        private static uint QuantizeSevenBits(int value, int pBit)
        {
            return (uint)Mathf.Clamp(Mathf.RoundToInt((value - pBit) * 0.5f), 0, 127);
        }

        private static void WriteBits(byte[] destination, ref int bitPosition, uint value, int bitCount)
        {
            for (int bitIndex = 0; bitIndex < bitCount; bitIndex++, bitPosition++)
            {
                if (((value >> bitIndex) & 1u) != 0)
                    destination[bitPosition >> 3] |= (byte)(1 << (bitPosition & 7));
            }
        }
    }
}
