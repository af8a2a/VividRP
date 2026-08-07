using System;
using System.Runtime.InteropServices;

namespace VividRP.Runtime
{
    internal interface IVTStreamCodec
    {
        VividVirtualTextureStreamCompression Compression { get; }

        bool IsAvailable { get; }

        bool TryEncode(byte[] decodedData, int level, out byte[] storedData, out string error);

        bool TryDecode(byte[] storedData, int decodedByteSize, out byte[] decodedData, out string error);
    }

    internal static class VTStreamCodecRegistry
    {
        private static readonly IVTStreamCodec s_None = new VTNoneStreamCodec();
        private static readonly IVTStreamCodec s_Zstd = new VTZstdStreamCodec();

        internal static IVTStreamCodec Get(VividVirtualTextureStreamCompression compression)
        {
            return compression switch
            {
                VividVirtualTextureStreamCompression.None => s_None,
                VividVirtualTextureStreamCompression.Zstd => s_Zstd,
                _ => null,
            };
        }
    }

    internal sealed class VTNoneStreamCodec : IVTStreamCodec
    {
        public VividVirtualTextureStreamCompression Compression => VividVirtualTextureStreamCompression.None;

        public bool IsAvailable => true;

        public bool TryEncode(byte[] decodedData, int level, out byte[] storedData, out string error)
        {
            storedData = decodedData != null ? (byte[])decodedData.Clone() : Array.Empty<byte>();
            error = null;
            return true;
        }

        public bool TryDecode(byte[] storedData, int decodedByteSize, out byte[] decodedData, out string error)
        {
            decodedData = null;
            if (storedData == null || decodedByteSize < 0 || storedData.Length != decodedByteSize)
            {
                error = "Raw VT chunk stored and decoded sizes differ.";
                return false;
            }

            decodedData = (byte[])storedData.Clone();
            error = null;
            return true;
        }
    }

    internal sealed class VTZstdStreamCodec : IVTStreamCodec
    {
        private const string NativeLibrary = "VividVTStreamingNative";
        private const uint RequiredVersionNumber = 10507;

        private readonly bool m_IsAvailable;

        internal VTZstdStreamCodec()
        {
            try
            {
                m_IsAvailable = VividVT_ZstdVersionNumber() == RequiredVersionNumber;
            }
            catch (DllNotFoundException)
            {
                m_IsAvailable = false;
            }
            catch (EntryPointNotFoundException)
            {
                m_IsAvailable = false;
            }
            catch (BadImageFormatException)
            {
                m_IsAvailable = false;
            }
        }

        public VividVirtualTextureStreamCompression Compression => VividVirtualTextureStreamCompression.Zstd;

        public bool IsAvailable => m_IsAvailable;

        public bool TryEncode(byte[] decodedData, int level, out byte[] storedData, out string error)
        {
            storedData = null;
            if (!m_IsAvailable)
            {
                error = "VividVTStreamingNative with Zstd 1.5.7 is unavailable.";
                return false;
            }

            if (decodedData == null)
            {
                error = "Decoded VT chunk data is null.";
                return false;
            }

            level = level < 1 ? 1 : level > 3 ? 3 : level;
            UIntPtr bound = VividVT_ZstdCompressBound((UIntPtr)(uint)decodedData.Length);
            ulong boundValue = bound.ToUInt64();
            if (boundValue > int.MaxValue)
            {
                error = "Compressed VT chunk bound exceeds the managed buffer limit.";
                return false;
            }

            var destination = new byte[(int)boundValue];
            UIntPtr result = VividVT_ZstdCompress(
                destination,
                (UIntPtr)(uint)destination.Length,
                decodedData,
                (UIntPtr)(uint)decodedData.Length,
                level);
            if (VividVT_ZstdIsError(result) != 0)
            {
                error = Marshal.PtrToStringAnsi(VividVT_ZstdGetErrorName(result)) ?? "Zstd compression failed.";
                return false;
            }

            int storedSize = checked((int)result.ToUInt64());
            storedData = new byte[storedSize];
            Buffer.BlockCopy(destination, 0, storedData, 0, storedSize);
            error = null;
            return true;
        }

        public bool TryDecode(byte[] storedData, int decodedByteSize, out byte[] decodedData, out string error)
        {
            decodedData = null;
            if (!m_IsAvailable)
            {
                error = "VividVTStreamingNative with Zstd 1.5.7 is unavailable.";
                return false;
            }

            if (storedData == null || decodedByteSize < 0)
            {
                error = "Invalid Zstd VT chunk input.";
                return false;
            }

            var destination = new byte[decodedByteSize];
            UIntPtr result = VividVT_ZstdDecompress(
                destination,
                (UIntPtr)(uint)destination.Length,
                storedData,
                (UIntPtr)(uint)storedData.Length);
            if (VividVT_ZstdIsError(result) != 0)
            {
                error = Marshal.PtrToStringAnsi(VividVT_ZstdGetErrorName(result)) ?? "Zstd decompression failed.";
                return false;
            }

            if (result.ToUInt64() != (ulong)decodedByteSize)
            {
                error = $"Zstd VT chunk decoded to {result.ToUInt64()} bytes, expected {decodedByteSize}.";
                return false;
            }

            decodedData = destination;
            error = null;
            return true;
        }

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint VividVT_ZstdVersionNumber();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern UIntPtr VividVT_ZstdCompressBound(UIntPtr sourceSize);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern UIntPtr VividVT_ZstdCompress(
            [Out] byte[] destination,
            UIntPtr destinationCapacity,
            byte[] source,
            UIntPtr sourceSize,
            int compressionLevel);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern UIntPtr VividVT_ZstdDecompress(
            [Out] byte[] destination,
            UIntPtr destinationCapacity,
            byte[] source,
            UIntPtr sourceSize);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern int VividVT_ZstdIsError(UIntPtr code);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr VividVT_ZstdGetErrorName(UIntPtr code);
    }

    internal static class VTDecodedPayloadCRC
    {
        private static readonly uint[] s_Table = CreateTable();

        internal static uint Compute(byte[] data)
        {
            if (data == null)
                return 0;

            uint crc = uint.MaxValue;
            for (int index = 0; index < data.Length; index++)
                crc = s_Table[(crc ^ data[index]) & 0xff] ^ (crc >> 8);

            return ~crc;
        }

        private static uint[] CreateTable()
        {
            var table = new uint[256];
            for (uint value = 0; value < table.Length; value++)
            {
                uint current = value;
                for (int bit = 0; bit < 8; bit++)
                    current = (current & 1) != 0 ? 0xedb88320u ^ (current >> 1) : current >> 1;

                table[value] = current;
            }

            return table;
        }
    }
}
