using System;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;
using Unity.Mathematics;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Editor.Tests
{
    public sealed class VividMeshletCollectionBinarySerializerTests
    {
        private const uint MeshletBlobMagic = 0x564D4342u;

        [Test]
        public void LZ4Codec_RoundTripsLiteralRepeatedAndRandomPayloads()
        {
            var random = new System.Random(12345);
            var randomPayload = new byte[65537];
            random.NextBytes(randomPayload);
            var repeatedPayload = new byte[128 * 1024];
            for (int index = 0; index < repeatedPayload.Length; index++)
            {
                repeatedPayload[index] = (byte) (index % 19);
            }

            byte[][] payloads =
            {
                Array.Empty<byte>(),
                new byte[] { 1, 2, 3 },
                new byte[] { 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4 },
                repeatedPayload,
                randomPayload,
            };

            foreach (byte[] payload in payloads)
            {
                byte[] compressed = VividLZ4Codec.Compress(payload);
                byte[] decompressed = VividLZ4Codec.Decompress(compressed, payload.Length);
                CollectionAssert.AreEqual(payload, decompressed);
            }

            Assert.That(VividLZ4Codec.Compress(repeatedPayload).Length, Is.LessThan(repeatedPayload.Length));
        }

        [Test]
        public void Serialize_WritesVersionedLZ4BlobAndRoundTripsPayload()
        {
            int[] levelCounts = { 1, 2, 3 };
            byte[] indices = { 0, 1, 2, 2, 1, 3 };
            VividMeshletVertex vertex = VividMeshletVertexPacking.Pack(
                new float3(12.5f, -7.0f, 0.25f),
                math.normalize(new float3(1.0f, 2.0f, 3.0f)),
                new float4(math.normalize(new float3(-2.0f, 1.0f, 0.5f)), -1.0f),
                new float2(4.0f, -8.0f)
            );
            byte[] blob = VividMeshletCollectionBinarySerializer.Serialize(
                levelCounts,
                Array.Empty<VividMeshLODNode>(),
                Array.Empty<VividMeshlet>(),
                new[] { vertex },
                indices
            );

            Assert.That(BitConverter.ToUInt32(blob, 0), Is.EqualTo(MeshletBlobMagic));
            Assert.That(BitConverter.ToUInt32(blob, 4), Is.EqualTo(VividMeshletCollectionBinarySerializer.CurrentVersion));
            Assert.That(BitConverter.ToUInt32(blob, 8), Is.EqualTo(VividMeshletCollectionBinarySerializer.LZ4CompressionCodec));
            Assert.That(BitConverter.ToInt32(blob, 16), Is.EqualTo(blob.Length - 20));

            VividMeshletCollectionBinarySerializer.Deserialize(
                blob,
                out int[] deserializedLevelCounts,
                out VividMeshLODNode[] deserializedNodes,
                out VividMeshlet[] deserializedMeshlets,
                out VividMeshletVertex[] deserializedVertices,
                out byte[] deserializedIndices
            );

            CollectionAssert.AreEqual(levelCounts, deserializedLevelCounts);
            Assert.That(deserializedNodes, Is.Empty);
            Assert.That(deserializedMeshlets, Is.Empty);
            CollectionAssert.AreEqual(new[] { vertex }, deserializedVertices);
            CollectionAssert.AreEqual(indices, deserializedIndices);
        }

        [TestCase(1u)]
        [TestCase(2u)]
        public void Deserialize_ReadsAndConvertsLegacyVertexLayout(uint version)
        {
            var legacyVertex = new VividMeshletVertexLegacy64
            {
                Position = new float4(3.0f, 4.0f, 5.0f, 1.0f),
                Normal = new float4(math.normalize(new float3(1.0f, 2.0f, 3.0f)), 0.0f),
                Tangent = new float4(math.normalize(new float3(-2.0f, 1.0f, 0.5f)), -1.0f),
                UV = new float4(0.25f, 0.75f, 0.0f, 0.0f),
            };
            byte[] legacyBlob = CreateLegacyBlob(version, new[] { legacyVertex }, new byte[] { 0, 1, 2 });

            VividMeshletCollectionBinarySerializer.Deserialize(
                legacyBlob,
                out int[] levelCounts,
                out VividMeshLODNode[] nodes,
                out VividMeshlet[] meshlets,
                out VividMeshletVertex[] vertices,
                out byte[] indices
            );

            Assert.That(levelCounts, Is.Empty);
            Assert.That(nodes, Is.Empty);
            Assert.That(meshlets, Is.Empty);
            Assert.That(vertices, Has.Length.EqualTo(1));
            Assert.That(vertices[0].Position, Is.EqualTo(legacyVertex.Position.xyz));
            Assert.That(vertices[0].UV, Is.EqualTo(legacyVertex.UV.xy));
            Assert.That(
                DirectionErrorDegrees(legacyVertex.Normal.xyz, VividMeshletVertexPacking.UnpackNormal(vertices[0].PackedNormal)),
                Is.LessThanOrEqualTo(0.02f)
            );
            float4 tangent = VividMeshletVertexPacking.UnpackTangent(vertices[0].PackedTangent);
            Assert.That(DirectionErrorDegrees(legacyVertex.Tangent.xyz, tangent.xyz), Is.LessThanOrEqualTo(0.02f));
            Assert.That(tangent.w, Is.EqualTo(-1.0f));
            Assert.That(vertices[0].Reserved, Is.Zero);
            CollectionAssert.AreEqual(new byte[] { 0, 1, 2 }, indices);
        }

        [Test]
        public void Serialize_PackedVertexBlobIsSmallerThanLegacyVertexBlob()
        {
            const int vertexCount = 2048;
            var legacyVertices = new VividMeshletVertexLegacy64[vertexCount];
            var packedVertices = new VividMeshletVertex[vertexCount];
            for (int index = 0; index < vertexCount; index++)
            {
                float value = index * 0.03125f;
                float3 position = new(value, math.sin(value) * 37.0f, math.cos(value * 0.73f) * 19.0f);
                float3 normal = math.normalize(new float3(
                    math.sin(value * 0.37f),
                    math.cos(value * 0.53f),
                    1.0f));
                float4 tangent = new(
                    math.normalize(new float3(math.cos(value), 0.5f, math.sin(value))),
                    (index & 1) == 0 ? 1.0f : -1.0f);
                float2 uv = new(value * 0.1f, value * -0.17f);
                legacyVertices[index] = new VividMeshletVertexLegacy64
                {
                    Position = new float4(position, 1.0f),
                    Normal = new float4(normal, 0.0f),
                    Tangent = tangent,
                    UV = new float4(uv, 0.0f, 0.0f),
                };
                packedVertices[index] = VividMeshletVertexPacking.Pack(position, normal, tangent, uv);
            }

            byte[] legacyBlob = CreateLegacyBlob(2u, legacyVertices, Array.Empty<byte>());
            byte[] packedBlob = VividMeshletCollectionBinarySerializer.Serialize(
                Array.Empty<int>(),
                Array.Empty<VividMeshLODNode>(),
                Array.Empty<VividMeshlet>(),
                packedVertices,
                Array.Empty<byte>()
            );

            Assert.That(packedBlob.Length, Is.LessThan(legacyBlob.Length));
        }

        private static byte[] CreateLegacyBlob(
            uint version,
            VividMeshletVertexLegacy64[] vertices,
            byte[] indices)
        {
            byte[] payload;
            using (var payloadStream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(payloadStream, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(vertices.Length);
                    foreach (VividMeshletVertexLegacy64 vertex in vertices)
                    {
                        WriteFloat4(writer, vertex.Position);
                        WriteFloat4(writer, vertex.Normal);
                        WriteFloat4(writer, vertex.Tangent);
                        WriteFloat4(writer, vertex.UV);
                    }

                    writer.Write(indices.Length);
                    writer.Write(indices);
                }

                payload = payloadStream.ToArray();
            }

            using var outputStream = new MemoryStream();
            using (var writer = new BinaryWriter(outputStream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(MeshletBlobMagic);
                writer.Write(version);
                if (version == 1u)
                {
                    writer.Write(payload.Length);
                }
                else
                {
                    byte[] compressed = VividLZ4Codec.Compress(payload);
                    writer.Write(VividMeshletCollectionBinarySerializer.LZ4CompressionCodec);
                    writer.Write(payload.Length);
                    writer.Write(compressed.Length);
                    writer.Write(compressed);
                    return outputStream.ToArray();
                }
            }

            using (var gzipStream = new GZipStream(outputStream, CompressionLevel.Optimal, leaveOpen: true))
            {
                gzipStream.Write(payload, 0, payload.Length);
            }

            return outputStream.ToArray();
        }

        private static void WriteFloat4(BinaryWriter writer, float4 value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
            writer.Write(value.w);
        }

        private static float DirectionErrorDegrees(float3 expected, float3 actual)
        {
            float chordLength = math.length(math.normalize(expected) - math.normalize(actual));
            return math.degrees(2.0f * math.asin(math.saturate(chordLength * 0.5f)));
        }
    }
}
