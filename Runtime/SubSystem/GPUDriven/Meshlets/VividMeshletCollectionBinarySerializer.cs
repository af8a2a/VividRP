using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace VividRP.Runtime.GPUDriven.Meshlets
{
    internal static class VividMeshletCollectionBinarySerializer
    {
        public const uint CurrentVersion = 3u;

        private const uint Magic = 0x564D4342u;
        private const uint LegacyGZipVersion = 1u;
        private const uint LegacyLZ4Version = 2u;

        internal const uint LZ4CompressionCodec = 1u;

        public static byte[] Serialize(
            int[] meshLODLevelNodeCounts,
            VividMeshLODNode[] meshLODNodes,
            VividMeshlet[] meshlets,
            VividMeshletVertex[] vertexBuffer,
            byte[] indexBuffer)
        {
            meshLODLevelNodeCounts ??= Array.Empty<int>();
            meshLODNodes ??= Array.Empty<VividMeshLODNode>();
            meshlets ??= Array.Empty<VividMeshlet>();
            vertexBuffer ??= Array.Empty<VividMeshletVertex>();
            indexBuffer ??= Array.Empty<byte>();

            using var payloadStream = new MemoryStream();
            using (var writer = new BinaryWriter(payloadStream, Encoding.UTF8, leaveOpen: true))
            {
                WriteIntArray(writer, meshLODLevelNodeCounts);
                WriteStructArray(writer, meshLODNodes);
                WriteStructArray(writer, meshlets);
                WriteStructArray(writer, vertexBuffer);
                WriteByteArray(writer, indexBuffer);
            }

            byte[] payload = payloadStream.ToArray();
            byte[] compressedPayload = VividLZ4Codec.Compress(payload);

            using var outputStream = new MemoryStream();
            using (var writer = new BinaryWriter(outputStream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Magic);
                writer.Write(CurrentVersion);
                writer.Write(LZ4CompressionCodec);
                writer.Write(payload.Length);
                writer.Write(compressedPayload.Length);
                writer.Write(compressedPayload);
            }

            return outputStream.ToArray();
        }

        public static void Deserialize(
            byte[] serializedData,
            out int[] meshLODLevelNodeCounts,
            out VividMeshLODNode[] meshLODNodes,
            out VividMeshlet[] meshlets,
            out VividMeshletVertex[] vertexBuffer,
            out byte[] indexBuffer)
        {
            if (serializedData == null || serializedData.Length == 0)
            {
                meshLODLevelNodeCounts = Array.Empty<int>();
                meshLODNodes = Array.Empty<VividMeshLODNode>();
                meshlets = Array.Empty<VividMeshlet>();
                vertexBuffer = Array.Empty<VividMeshletVertex>();
                indexBuffer = Array.Empty<byte>();
                return;
            }

            using var inputStream = new MemoryStream(serializedData, writable: false);
            using var headerReader = new BinaryReader(inputStream, Encoding.UTF8, leaveOpen: true);

            uint magic = headerReader.ReadUInt32();
            if (magic != Magic)
            {
                throw new InvalidDataException($"Unexpected meshlet blob magic value: 0x{magic:X8}.");
            }

            uint version = headerReader.ReadUInt32();
            bool usesLegacyVertexLayout;
            byte[] payload = version switch
            {
                LegacyGZipVersion => ReadLegacyGZipPayload(inputStream, headerReader),
                LegacyLZ4Version => ReadLZ4Payload(inputStream, headerReader),
                CurrentVersion => ReadLZ4Payload(inputStream, headerReader),
                _ => throw new InvalidDataException(
                    $"Unsupported meshlet blob version {version}. Expected versions " +
                    $"{LegacyGZipVersion}, {LegacyLZ4Version}, or {CurrentVersion}."
                ),
            };
            usesLegacyVertexLayout = version == LegacyGZipVersion || version == LegacyLZ4Version;

            using var payloadStream = new MemoryStream(payload, writable: false);
            using var payloadReader = new BinaryReader(payloadStream, Encoding.UTF8, leaveOpen: true);
            meshLODLevelNodeCounts = ReadIntArray(payloadReader);
            meshLODNodes = ReadStructArray<VividMeshLODNode>(payloadReader);
            meshlets = ReadStructArray<VividMeshlet>(payloadReader);
            vertexBuffer = usesLegacyVertexLayout
                ? ConvertLegacyVertices(ReadStructArray<VividMeshletVertexLegacy64>(payloadReader))
                : ReadStructArray<VividMeshletVertex>(payloadReader);
            indexBuffer = ReadByteArray(payloadReader);
        }

        internal static VividMeshletVertex[] ConvertLegacyVertices(VividMeshletVertexLegacy64[] legacyVertices)
        {
            if (legacyVertices == null || legacyVertices.Length == 0)
            {
                return Array.Empty<VividMeshletVertex>();
            }

            var vertices = new VividMeshletVertex[legacyVertices.Length];
            for (int index = 0; index < legacyVertices.Length; index++)
            {
                VividMeshletVertexLegacy64 legacyVertex = legacyVertices[index];
                vertices[index] = VividMeshletVertexPacking.Pack(
                    legacyVertex.Position.xyz,
                    legacyVertex.Normal.xyz,
                    legacyVertex.Tangent,
                    legacyVertex.UV.xy
                );
            }

            return vertices;
        }

        private static byte[] ReadLZ4Payload(MemoryStream inputStream, BinaryReader headerReader)
        {
            uint compressionCodec = headerReader.ReadUInt32();
            if (compressionCodec != LZ4CompressionCodec)
            {
                throw new InvalidDataException($"Unsupported meshlet compression codec {compressionCodec}.");
            }

            int payloadLength = ReadPayloadLength(headerReader);
            int compressedLength = headerReader.ReadInt32();
            if (compressedLength < 0 || compressedLength != inputStream.Length - inputStream.Position)
            {
                throw new InvalidDataException(
                    $"Invalid LZ4 meshlet payload length {compressedLength}; " +
                    $"{inputStream.Length - inputStream.Position} bytes remain."
                );
            }

            byte[] compressedPayload = headerReader.ReadBytes(compressedLength);
            return VividLZ4Codec.Decompress(compressedPayload, payloadLength);
        }

        private static byte[] ReadLegacyGZipPayload(MemoryStream inputStream, BinaryReader headerReader)
        {
            int payloadLength = ReadPayloadLength(headerReader);
            using var payloadStream = new MemoryStream(payloadLength);
            using (var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress, leaveOpen: true))
            {
                gzipStream.CopyTo(payloadStream);
            }

            if (payloadStream.Length != payloadLength)
            {
                throw new InvalidDataException(
                    $"Meshlet payload length mismatch. Expected {payloadLength}, got {payloadStream.Length}."
                );
            }

            return payloadStream.ToArray();
        }

        private static int ReadPayloadLength(BinaryReader headerReader)
        {
            int payloadLength = headerReader.ReadInt32();
            if (payloadLength < 0)
            {
                throw new InvalidDataException($"Invalid meshlet payload length {payloadLength}.");
            }

            return payloadLength;
        }

        private static void WriteIntArray(BinaryWriter writer, int[] values)
        {
            writer.Write(values.Length);
            if (values.Length == 0)
            {
                return;
            }

            int byteCount = checked(values.Length * sizeof(int));
            var bytes = new byte[byteCount];
            Buffer.BlockCopy(values, 0, bytes, 0, byteCount);
            writer.Write(bytes);
        }

        private static int[] ReadIntArray(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count <= 0)
            {
                return Array.Empty<int>();
            }

            int byteCount = checked(count * sizeof(int));
            byte[] bytes = reader.ReadBytes(byteCount);
            if (bytes.Length != byteCount)
            {
                throw new EndOfStreamException($"Expected {byteCount} bytes for int array, got {bytes.Length}.");
            }

            var result = new int[count];
            Buffer.BlockCopy(bytes, 0, result, 0, byteCount);
            return result;
        }

        private static unsafe void WriteStructArray<T>(BinaryWriter writer, T[] values)
            where T : unmanaged
        {
            writer.Write(values.Length);
            if (values.Length == 0)
            {
                return;
            }

            int byteCount = checked(values.Length * UnsafeUtility.SizeOf<T>());
            var bytes = new byte[byteCount];

            fixed (T* sourcePtr = values)
            fixed (byte* destinationPtr = bytes)
            {
                UnsafeUtility.MemCpy(destinationPtr, sourcePtr, byteCount);
            }

            writer.Write(bytes);
        }

        private static unsafe T[] ReadStructArray<T>(BinaryReader reader)
            where T : unmanaged
        {
            int count = reader.ReadInt32();
            if (count <= 0)
            {
                return Array.Empty<T>();
            }

            int byteCount = checked(count * UnsafeUtility.SizeOf<T>());
            byte[] bytes = reader.ReadBytes(byteCount);
            if (bytes.Length != byteCount)
            {
                throw new EndOfStreamException($"Expected {byteCount} bytes for {typeof(T).Name} array, got {bytes.Length}.");
            }

            var result = new T[count];
            fixed (byte* sourcePtr = bytes)
            fixed (T* destinationPtr = result)
            {
                UnsafeUtility.MemCpy(destinationPtr, sourcePtr, byteCount);
            }

            return result;
        }

        private static void WriteByteArray(BinaryWriter writer, byte[] values)
        {
            writer.Write(values.Length);
            if (values.Length > 0)
            {
                writer.Write(values);
            }
        }

        private static byte[] ReadByteArray(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count <= 0)
            {
                return Array.Empty<byte>();
            }

            byte[] values = reader.ReadBytes(count);
            if (values.Length != count)
            {
                throw new EndOfStreamException($"Expected {count} bytes for byte array, got {values.Length}.");
            }

            return values;
        }
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    internal struct VividMeshletVertexLegacy64
    {
        public float4 Position;
        public float4 Normal;
        public float4 Tangent;
        public float4 UV;
    }
}
