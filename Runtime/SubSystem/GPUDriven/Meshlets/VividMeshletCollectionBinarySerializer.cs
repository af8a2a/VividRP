using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Unity.Mathematics;

namespace VividRP.Runtime.GPUDriven.Meshlets
{
    internal static class VividMeshletCollectionBinarySerializer
    {
        public const uint CurrentVersion = 6u;

        private const uint Magic = 0x564D4342u;
        private const uint LegacyGZipVersion = 1u;
        private const uint LegacyLZ4Version = 2u;
        private const uint PackedVertexLegacyMetadataVersion = 3u;
        private const uint UnquantizedVertexVersion = 4u;

        private const uint RecenteredParentVersion = 5u;

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
            byte[] payload = null;
            int payloadLength = 0;
            try
            {
                payload = version switch
                {
                    LegacyGZipVersion => ReadLegacyGZipPayload(inputStream, headerReader),
                    LegacyLZ4Version => ReadLZ4Payload(serializedData, inputStream, headerReader, out payloadLength),
                    PackedVertexLegacyMetadataVersion => ReadLZ4Payload(serializedData, inputStream, headerReader, out payloadLength),
                    UnquantizedVertexVersion => ReadLZ4Payload(serializedData, inputStream, headerReader, out payloadLength),
                    RecenteredParentVersion => ReadLZ4Payload(serializedData, inputStream, headerReader, out payloadLength),
                    CurrentVersion => ReadLZ4Payload(serializedData, inputStream, headerReader, out payloadLength),
                    _ => throw new InvalidDataException(
                        $"Unsupported meshlet blob version {version}. Expected versions " +
                        $"{LegacyGZipVersion}, {LegacyLZ4Version}, " +
                        $"{PackedVertexLegacyMetadataVersion}, {UnquantizedVertexVersion}, {RecenteredParentVersion}, or {CurrentVersion}."
                    ),
                };
                bool usesLegacyVertexLayout = version <= LegacyLZ4Version;
                bool usesLegacyMetadataLayout = version <= PackedVertexLegacyMetadataVersion;

                if (version == LegacyGZipVersion)
                    payloadLength = payload.Length;
                using var payloadStream = new MemoryStream(payload, 0, payloadLength, writable: false);
                using var payloadReader = new BinaryReader(payloadStream, Encoding.UTF8, leaveOpen: true);
                meshLODLevelNodeCounts = ReadIntArray(payloadReader);
                meshLODNodes = usesLegacyMetadataLayout
                    ? ConvertLegacyMeshLODNodes(ReadStructArray<VividMeshLODNodeLegacy64>(payloadReader))
                    : version <= RecenteredParentVersion
                        ? ConvertRecenteredMeshLODNodes(ReadStructArray<VividMeshLODNodeLegacy32>(payloadReader), out meshLODLevelNodeCounts)
                        : ReadStructArray<VividMeshLODNode>(payloadReader);
                meshlets = usesLegacyMetadataLayout
                    ? ConvertLegacyMeshlets(ReadStructArray<VividMeshletLegacy64>(payloadReader))
                    : ReadStructArray<VividMeshlet>(payloadReader);
                float quantizationError = 0f;
                vertexBuffer = usesLegacyVertexLayout
                    ? ConvertLegacyVertices(ReadStructArray<VividMeshletVertexLegacy64>(payloadReader), out quantizationError)
                    : version <= UnquantizedVertexVersion
                        ? ConvertLegacyVertices(ReadStructArray<VividMeshletVertexLegacy32>(payloadReader), out quantizationError)
                        : ReadStructArray<VividMeshletVertex>(payloadReader);
                ExpandLegacyBounds(meshLODNodes, meshlets, quantizationError);
                indexBuffer = ReadByteArray(payloadReader);
            }
            finally
            {
                if (payload != null && version != LegacyGZipVersion)
                    ArrayPool<byte>.Shared.Return(payload);
            }
        }

        internal static VividMeshletVertex[] ConvertLegacyVertices(VividMeshletVertexLegacy64[] legacyVertices)
            => ConvertLegacyVertices(legacyVertices, out _);

        private static VividMeshletVertex[] ConvertLegacyVertices(VividMeshletVertexLegacy64[] legacyVertices, out float error)
        {
            error = 0f;
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
                error = math.max(error, math.distance(legacyVertex.Position.xyz, vertices[index].Position));
            }

            return vertices;
        }

        private static VividMeshletVertex[] ConvertLegacyVertices(VividMeshletVertexLegacy32[] source, out float error)
        {
            error = 0f;
            var result = new VividMeshletVertex[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                result[i] = VividMeshletVertexPacking.Pack(source[i].Position,
                    VividMeshletVertexPacking.UnpackNormal(source[i].PackedNormal),
                    VividMeshletVertexPacking.UnpackTangent(source[i].PackedTangent), source[i].UV);
                error = math.max(error, math.distance(source[i].Position, result[i].Position));
            }
            return result;
        }

        private static void ExpandLegacyBounds(VividMeshLODNode[] nodes, VividMeshlet[] meshlets, float error)
        {
            if (error <= 0f) return;
            for (int i = 0; i < meshlets.Length; i++)
            {
                meshlets[i].BoundingSphere.w += error;
                // Position quantization can rotate normals of arbitrarily thin triangles.
                meshlets[i].PackedCone = 0;
            }
            for (int i = 0; i < nodes.Length; i++)
            {
                float4 parent = nodes[i].ParentBounds;
                float parentError = nodes[i].ParentError;
                nodes[i].Bounds.w += error;
                if (nodes[i].Error > 0f) nodes[i].Error += error;
                if (parentError >= 0)
                {
                    parent.w += error;
                    nodes[i].ParentBounds = parent;
                    nodes[i].ParentError = parentError + error;
                }
            }
        }

        private static VividMeshLODNode[] ConvertRecenteredMeshLODNodes(
            VividMeshLODNodeLegacy32[] source, out int[] levelCounts)
        {
            // v4/v5 discarded the original parent center and rounded its error.
            // Neither the shared transition nor group membership can be recovered.
            // Render the complete finest cut until the source is rebuilt; never
            // guess parent links or retain the lossy overlapping automatic cut.
            uint finestLevel = 0;
            foreach (var node in source) finestLevel = math.max(finestLevel, node.PackedMeshletCountLevel >> 16);
            int count = 0;
            foreach (var node in source) if (node.PackedMeshletCountLevel >> 16 == finestLevel) count++;
            var result = new VividMeshLODNode[count];
            int index = 0;
            foreach (var node in source)
            {
                if (node.PackedMeshletCountLevel >> 16 != finestLevel) continue;
                result[index++] = VividMeshletMetadataPacking.PackMeshLODNode(node.Bounds, default,
                    -1f, 0f, node.MeshletStartIndex, node.PackedMeshletCountLevel & 0xffffu, 0u);
            }
            levelCounts = count == 0 ? Array.Empty<int>() : new[] { count };
            return result;
        }

        internal static VividMeshLODNode[] ConvertLegacyMeshLODNodes(
            VividMeshLODNodeLegacy64[] legacyNodes)
        {
            if (legacyNodes == null || legacyNodes.Length == 0)
            {
                return Array.Empty<VividMeshLODNode>();
            }

            var nodes = new VividMeshLODNode[legacyNodes.Length];
            for (int index = 0; index < legacyNodes.Length; index++)
            {
                VividMeshLODNodeLegacy64 legacyNode = legacyNodes[index];
                nodes[index] = VividMeshletMetadataPacking.PackMeshLODNode(
                    legacyNode.Bounds,
                    legacyNode.ParentBounds,
                    legacyNode.ParentError,
                    legacyNode.Error,
                    legacyNode.MeshletStartIndex,
                    legacyNode.MeshletCount,
                    legacyNode.LevelIndex);
            }

            return nodes;
        }

        internal static VividMeshlet[] ConvertLegacyMeshlets(VividMeshletLegacy64[] legacyMeshlets)
        {
            if (legacyMeshlets == null || legacyMeshlets.Length == 0)
            {
                return Array.Empty<VividMeshlet>();
            }

            var meshlets = new VividMeshlet[legacyMeshlets.Length];
            for (int index = 0; index < legacyMeshlets.Length; index++)
            {
                VividMeshletLegacy64 legacyMeshlet = legacyMeshlets[index];
                meshlets[index] = VividMeshletMetadataPacking.PackMeshlet(
                    legacyMeshlet.VertexOffset,
                    legacyMeshlet.TriangleOffset,
                    legacyMeshlet.VertexCount,
                    legacyMeshlet.TriangleCount,
                    legacyMeshlet.BoundingSphere,
                    legacyMeshlet.ConeAxis.xyz,
                    legacyMeshlet.ConeApexCutoff.w);
            }

            return meshlets;
        }

        private static byte[] ReadLZ4Payload(byte[] serializedData, MemoryStream inputStream, BinaryReader headerReader, out int payloadLength)
        {
            uint compressionCodec = headerReader.ReadUInt32();
            if (compressionCodec != LZ4CompressionCodec)
            {
                throw new InvalidDataException($"Unsupported meshlet compression codec {compressionCodec}.");
            }

            payloadLength = ReadPayloadLength(headerReader);
            int compressedLength = headerReader.ReadInt32();
            if (compressedLength < 0 || compressedLength != inputStream.Length - inputStream.Position)
            {
                throw new InvalidDataException(
                    $"Invalid LZ4 meshlet payload length {compressedLength}; " +
                    $"{inputStream.Length - inputStream.Position} bytes remain."
                );
            }

            byte[] payload = ArrayPool<byte>.Shared.Rent(Math.Max(1, payloadLength));
            try
            {
                VividLZ4Codec.Decompress(
                    serializedData.AsSpan((int)inputStream.Position, compressedLength),
                    payload.AsSpan(0, payloadLength));
                return payload;
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(payload);
                throw;
            }
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
            return ReadStructArray<int>(reader);
        }

        private static unsafe void WriteStructArray<T>(BinaryWriter writer, T[] values)
            where T : unmanaged
        {
            writer.Write(values.Length);
            if (values.Length == 0)
            {
                return;
            }

            writer.Write(MemoryMarshal.AsBytes(values.AsSpan()));
        }

        private static unsafe T[] ReadStructArray<T>(BinaryReader reader)
            where T : unmanaged
        {
            int count = reader.ReadInt32();
            if (count <= 0)
            {
                return Array.Empty<T>();
            }

            int byteCount = checked(count * sizeof(T));
            if (byteCount > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new EndOfStreamException($"Expected {byteCount} bytes for {typeof(T).Name} array.");

            var result = new T[count];
            Span<byte> destination = MemoryMarshal.AsBytes(result.AsSpan());
            int bytesRead = 0;
            while (bytesRead < byteCount)
            {
                int read = reader.Read(destination.Slice(bytesRead));
                if (read == 0)
                    throw new EndOfStreamException($"Expected {byteCount} bytes for {typeof(T).Name} array.");
                bytesRead += read;
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

    [StructLayout(LayoutKind.Sequential)]
    internal struct VividMeshletVertexLegacy32
    {
        public float3 Position;
        public uint PackedNormal;
        public uint PackedTangent;
        public float2 UV;
        public uint Reserved;
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

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    internal struct VividMeshletLegacy64
    {
        public uint VertexOffset;
        public uint TriangleOffset;
        public uint VertexCount;
        public uint TriangleCount;
        public float4 BoundingSphere;
        public float4 ConeApexCutoff;
        public float4 ConeAxis;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VividMeshLODNodeLegacy32
    {
        public float4 Bounds;
        public float Error;
        public uint PackedParentErrorRadius;
        public uint MeshletStartIndex;
        public uint PackedMeshletCountLevel;
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    internal struct VividMeshLODNodeLegacy64
    {
        public float4 Bounds;
        public float4 ParentBounds;
        public float ParentError;
        public float Error;
        public uint MeshletStartIndex;
        public uint MeshletCount;
        public uint LevelIndex;
        public uint Padding0;
        public uint Padding1;
        public uint Padding2;
    }
}
