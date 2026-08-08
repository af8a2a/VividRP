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
        public void Deserialize_ReadsAndConvertsLegacyVertexAndMetadataLayouts(uint version)
        {
            var legacyNode = new VividMeshLODNodeLegacy64
            {
                Bounds = new float4(10.0f, 20.0f, 30.0f, 2.0f),
                ParentBounds = new float4(13.0f, 24.0f, 30.0f, 8.0f),
                ParentError = 0.03125f,
                Error = 0.0078125f,
                MeshletStartIndex = 123456u,
                MeshletCount = 4u,
                LevelIndex = 7u,
            };
            var legacyMeshlet = new VividMeshletLegacy64
            {
                VertexOffset = 123u,
                TriangleOffset = 456u,
                VertexCount = 128u,
                TriangleCount = 96u,
                BoundingSphere = new float4(1.0f, 2.0f, 3.0f, 4.0f),
                ConeApexCutoff = new float4(9.0f, 8.0f, 7.0f, 0.25f),
                ConeAxis = new float4(math.normalize(new float3(1.0f, 2.0f, 3.0f)), 0.0f),
            };
            var legacyVertex = new VividMeshletVertexLegacy64
            {
                Position = new float4(3.0f, 4.0f, 5.0f, 1.0f),
                Normal = new float4(math.normalize(new float3(1.0f, 2.0f, 3.0f)), 0.0f),
                Tangent = new float4(math.normalize(new float3(-2.0f, 1.0f, 0.5f)), -1.0f),
                UV = new float4(0.25f, 0.75f, 0.0f, 0.0f),
            };
            byte[] legacyBlob = CreateLegacyBlob(
                version,
                new[] { legacyNode },
                new[] { legacyMeshlet },
                new[] { legacyVertex },
                Array.Empty<VividMeshletVertex>(),
                new byte[] { 0, 1, 2 });

            VividMeshletCollectionBinarySerializer.Deserialize(
                legacyBlob,
                out int[] levelCounts,
                out VividMeshLODNode[] nodes,
                out VividMeshlet[] meshlets,
                out VividMeshletVertex[] vertices,
                out byte[] indices
            );

            Assert.That(levelCounts, Is.Empty);
            Assert.That(nodes, Has.Length.EqualTo(1));
            Assert.That(nodes[0].Bounds, Is.EqualTo(legacyNode.Bounds));
            Assert.That(nodes[0].ParentBounds.xyz, Is.EqualTo(legacyNode.Bounds.xyz));
            Assert.That(
                nodes[0].ParentBounds.w,
                Is.GreaterThanOrEqualTo(legacyNode.ParentBounds.w + math.distance(
                    legacyNode.Bounds.xyz,
                    legacyNode.ParentBounds.xyz)));
            Assert.That(nodes[0].ParentError, Is.GreaterThanOrEqualTo(legacyNode.ParentError));
            Assert.That(nodes[0].Error, Is.EqualTo(legacyNode.Error));
            Assert.That(nodes[0].MeshletStartIndex, Is.EqualTo(legacyNode.MeshletStartIndex));
            Assert.That(nodes[0].MeshletCount, Is.EqualTo(legacyNode.MeshletCount));
            Assert.That(nodes[0].LevelIndex, Is.EqualTo(legacyNode.LevelIndex));
            Assert.That(meshlets, Has.Length.EqualTo(1));
            Assert.That(meshlets[0].VertexOffset, Is.EqualTo(legacyMeshlet.VertexOffset));
            Assert.That(meshlets[0].TriangleOffset, Is.EqualTo(legacyMeshlet.TriangleOffset));
            Assert.That(meshlets[0].VertexCount, Is.EqualTo(legacyMeshlet.VertexCount));
            Assert.That(meshlets[0].TriangleCount, Is.EqualTo(legacyMeshlet.TriangleCount));
            Assert.That(meshlets[0].BoundingSphere, Is.EqualTo(legacyMeshlet.BoundingSphere));
            Assert.That(VividMeshletMetadataPacking.IsConeValid(meshlets[0].PackedCone), Is.True);
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
        public void Deserialize_ReadsVersion3PackedVerticesAndConvertsLegacyMetadata()
        {
            var legacyNode = new VividMeshLODNodeLegacy64
            {
                Bounds = new float4(4.0f, 5.0f, 6.0f, 2.0f),
                ParentBounds = new float4(4.0f, 5.0f, 6.0f, 9.0f),
                ParentError = 0.5f,
                Error = 0.25f,
                MeshletStartIndex = 77u,
                MeshletCount = 1u,
                LevelIndex = 3u,
            };
            var legacyMeshlet = new VividMeshletLegacy64
            {
                VertexOffset = 11u,
                TriangleOffset = 22u,
                VertexCount = 33u,
                TriangleCount = 44u,
                BoundingSphere = new float4(1.0f, 2.0f, 3.0f, 8.0f),
                ConeApexCutoff = new float4(0.0f, 0.0f, 0.0f, 0.75f),
                ConeAxis = new float4(0.0f, 0.0f, 1.0f, 0.0f),
            };
            VividMeshletVertex packedVertex = VividMeshletVertexPacking.Pack(
                new float3(1.0f, 2.0f, 3.0f),
                new float3(0.0f, 1.0f, 0.0f),
                new float4(1.0f, 0.0f, 0.0f, -1.0f),
                new float2(0.25f, 0.75f));
            byte[] blob = CreateLegacyBlob(
                3u,
                new[] { legacyNode },
                new[] { legacyMeshlet },
                Array.Empty<VividMeshletVertexLegacy64>(),
                new[] { packedVertex },
                new byte[] { 0, 1, 2 });

            VividMeshletCollectionBinarySerializer.Deserialize(
                blob,
                out _,
                out VividMeshLODNode[] nodes,
                out VividMeshlet[] meshlets,
                out VividMeshletVertex[] vertices,
                out byte[] indices);

            Assert.That(nodes, Has.Length.EqualTo(1));
            Assert.That(nodes[0].MeshletStartIndex, Is.EqualTo(77u));
            Assert.That(meshlets, Has.Length.EqualTo(1));
            Assert.That(meshlets[0].VertexCount, Is.EqualTo(33u));
            CollectionAssert.AreEqual(new[] { packedVertex }, vertices);
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

            byte[] legacyBlob = CreateLegacyBlob(
                2u,
                Array.Empty<VividMeshLODNodeLegacy64>(),
                Array.Empty<VividMeshletLegacy64>(),
                legacyVertices,
                Array.Empty<VividMeshletVertex>(),
                Array.Empty<byte>());
            byte[] packedBlob = VividMeshletCollectionBinarySerializer.Serialize(
                Array.Empty<int>(),
                Array.Empty<VividMeshLODNode>(),
                Array.Empty<VividMeshlet>(),
                packedVertices,
                Array.Empty<byte>()
            );

            Assert.That(packedBlob.Length, Is.LessThan(legacyBlob.Length));
        }

        [Test]
        public void Serialize_PackedMetadataBlobIsSmallerThanVersion3MetadataBlob()
        {
            const int metadataCount = 2048;
            var legacyNodes = new VividMeshLODNodeLegacy64[metadataCount];
            var legacyMeshlets = new VividMeshletLegacy64[metadataCount];
            for (int index = 0; index < metadataCount; index++)
            {
                float value = index * 0.03125f;
                float3 center = new(value, math.sin(value) * 100.0f, math.cos(value) * 100.0f);
                legacyNodes[index] = new VividMeshLODNodeLegacy64
                {
                    Bounds = new float4(center, 2.0f + value * 0.01f),
                    ParentBounds = new float4(center + new float3(1.0f, 2.0f, 3.0f), 8.0f + value * 0.02f),
                    ParentError = 0.01f + value * 0.0001f,
                    Error = 0.005f + value * 0.00005f,
                    MeshletStartIndex = (uint) index,
                    MeshletCount = 1u,
                    LevelIndex = (uint) (index % 8),
                };
                legacyMeshlets[index] = new VividMeshletLegacy64
                {
                    VertexOffset = (uint) (index * 64),
                    TriangleOffset = (uint) (index * 192),
                    VertexCount = 64u,
                    TriangleCount = 64u,
                    BoundingSphere = new float4(center, 4.0f + value * 0.01f),
                    ConeApexCutoff = new float4(center + 0.5f, 0.25f),
                    ConeAxis = new float4(math.normalize(new float3(1.0f, value + 1.0f, 2.0f)), 0.0f),
                };
            }

            byte[] version3Blob = CreateLegacyBlob(
                3u,
                legacyNodes,
                legacyMeshlets,
                Array.Empty<VividMeshletVertexLegacy64>(),
                Array.Empty<VividMeshletVertex>(),
                Array.Empty<byte>());
            byte[] version4Blob = VividMeshletCollectionBinarySerializer.Serialize(
                Array.Empty<int>(),
                VividMeshletCollectionBinarySerializer.ConvertLegacyMeshLODNodes(legacyNodes),
                VividMeshletCollectionBinarySerializer.ConvertLegacyMeshlets(legacyMeshlets),
                Array.Empty<VividMeshletVertex>(),
                Array.Empty<byte>());

            Assert.That(version4Blob.Length, Is.LessThan(version3Blob.Length));
        }

        private static byte[] CreateLegacyBlob(
            uint version,
            VividMeshLODNodeLegacy64[] nodes,
            VividMeshletLegacy64[] meshlets,
            VividMeshletVertexLegacy64[] legacyVertices,
            VividMeshletVertex[] packedVertices,
            byte[] indices)
        {
            byte[] payload;
            using (var payloadStream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(payloadStream, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write(0);
                    writer.Write(nodes.Length);
                    foreach (VividMeshLODNodeLegacy64 node in nodes)
                    {
                        WriteFloat4(writer, node.Bounds);
                        WriteFloat4(writer, node.ParentBounds);
                        writer.Write(node.ParentError);
                        writer.Write(node.Error);
                        writer.Write(node.MeshletStartIndex);
                        writer.Write(node.MeshletCount);
                        writer.Write(node.LevelIndex);
                        writer.Write(node.Padding0);
                        writer.Write(node.Padding1);
                        writer.Write(node.Padding2);
                    }

                    writer.Write(meshlets.Length);
                    foreach (VividMeshletLegacy64 meshlet in meshlets)
                    {
                        writer.Write(meshlet.VertexOffset);
                        writer.Write(meshlet.TriangleOffset);
                        writer.Write(meshlet.VertexCount);
                        writer.Write(meshlet.TriangleCount);
                        WriteFloat4(writer, meshlet.BoundingSphere);
                        WriteFloat4(writer, meshlet.ConeApexCutoff);
                        WriteFloat4(writer, meshlet.ConeAxis);
                    }

                    if (version <= 2u)
                    {
                        writer.Write(legacyVertices.Length);
                        foreach (VividMeshletVertexLegacy64 vertex in legacyVertices)
                        {
                            WriteFloat4(writer, vertex.Position);
                            WriteFloat4(writer, vertex.Normal);
                            WriteFloat4(writer, vertex.Tangent);
                            WriteFloat4(writer, vertex.UV);
                        }
                    }
                    else
                    {
                        writer.Write(packedVertices.Length);
                        foreach (VividMeshletVertex vertex in packedVertices)
                        {
                            writer.Write(vertex.PositionX);
                            writer.Write(vertex.PositionY);
                            writer.Write(vertex.PositionZ);
                            writer.Write(vertex.PackedNormal);
                            writer.Write(vertex.PackedTangent);
                            writer.Write(vertex.UV.x);
                            writer.Write(vertex.UV.y);
                            writer.Write(vertex.Reserved);
                        }
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
