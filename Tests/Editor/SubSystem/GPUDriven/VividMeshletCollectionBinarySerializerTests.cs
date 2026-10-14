using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
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
        public void Deserialize_Version7_RetainsIndicesAndAllocatesOnlyCompactArrays()
        {
            var groups = new[] {
                new VividMeshLODGroup { Bounds = new float4(100, 200, 300, 1), Error = 9 }, // unused
                new VividMeshLODGroup { Bounds = new float4(1, 2, 3, 20), Error = .25f },
                new VividMeshLODGroup { Bounds = new float4(1, 2, 3, 2), Error = 0 },
                new VividMeshLODGroup { Bounds = new float4(1, 2, 3, 2), Error = 0 }, // intentionally distinct ID
            };
            var nodes = new VividMeshLODNodePacked[8192];
            nodes[0] = new VividMeshLODNodePacked { GroupIndex = 1, ParentGroupIndex = uint.MaxValue, MeshletCount = 1 };
            for (int i = 1; i < nodes.Length; i++)
                nodes[i] = new VividMeshLODNodePacked { GroupIndex = 2u + (uint)(i & 1), ParentGroupIndex = 1,
                    MeshletStartIndex = (uint)i, MeshletCount = 1, LevelIndex = 1 };
            int[] levels = { 1, nodes.Length - 1 };
            byte[] blob = VividMeshletCollectionBinarySerializer.Serialize(levels, nodes, groups,
                Array.Empty<VividMeshlet>(), Array.Empty<VividMeshletVertex>(), Array.Empty<byte>());
            VividMeshletCollectionBinarySerializer.Deserialize(blob, out _, out _, out _, out _, out _, out _);
            long before = GC.GetAllocatedBytesForCurrentThread();
            VividMeshletCollectionBinarySerializer.Deserialize(blob, out var loadedLevels, out var loadedNodes,
                out var loadedGroups, out _, out _, out _);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            // Reject both a 48-byte temporary expansion and a rebuilt dictionary.
            Assert.That(allocated, Is.LessThan(nodes.Length * 16L + groups.Length * 20L + 4096));
            CollectionAssert.AreEqual(levels, loadedLevels);
            CollectionAssert.AreEqual(nodes, loadedNodes);
            CollectionAssert.AreEqual(groups, loadedGroups);
            Assert.That(loadedNodes[1].ParentGroupIndex, Is.EqualTo(loadedNodes[0].GroupIndex));
            byte[] roundTrip = VividMeshletCollectionBinarySerializer.Serialize(loadedLevels, loadedNodes, loadedGroups,
                Array.Empty<VividMeshlet>(), Array.Empty<VividMeshletVertex>(), Array.Empty<byte>());
            CollectionAssert.AreEqual(blob, roundTrip);
            // Arrays are owned by each load, never aliases of pooled decode storage.
            loadedNodes[0].GroupIndex = 0;
            loadedGroups[1].Error = 99;
            VividMeshletCollectionBinarySerializer.Deserialize(blob, out _, out var freshNodes, out var freshGroups,
                out _, out _, out _);
            CollectionAssert.AreEqual(nodes, freshNodes);
            CollectionAssert.AreEqual(groups, freshGroups);
        }

        // Legacy-layout assertions use this test-only projection. Production v7
        // loading returns the indexed arrays directly, tested separately below.
        private static void DeserializeExpanded(byte[] blob, out int[] levels, out VividMeshLODNode[] nodes,
            out VividMeshlet[] meshlets, out VividMeshletVertex[] vertices, out byte[] indices)
        {
            VividMeshletCollectionBinarySerializer.Deserialize(blob, out levels, out var packed, out var groups,
                out meshlets, out vertices, out indices);
            nodes = new VividMeshLODNode[packed.Length];
            for (int i = 0; i < packed.Length; i++) nodes[i] = VividGPUDrivenBufferSetTests.ExpandMeshLODNode(packed[i], groups);
        }

        private static byte[] SerializeExpanded(int[] levels, VividMeshLODNode[] nodes,
            VividMeshlet[] meshlets, VividMeshletVertex[] vertices, byte[] indices)
        {
            var table = new VividMeshLODGroupTable(); table.Build(nodes);
            return VividMeshletCollectionBinarySerializer.Serialize(levels, table.Nodes.ToArray(), table.Groups.ToArray(),
                meshlets, vertices, indices);
        }


        [Test]
        public void LZ4Codec_DecodesIntoCallerStorageWithoutAllocating()
        {
            var source = new byte[65537];
            for (int index = 0; index < source.Length; index++)
                source[index] = (byte)(index % 19);
            byte[] compressed = VividLZ4Codec.Compress(source);
            var destination = new byte[source.Length + 2];
            destination[0] = destination[destination.Length - 1] = 123;
            VividLZ4Codec.Decompress(compressed.AsSpan(), destination.AsSpan(1, source.Length));
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < 32; iteration++)
                VividLZ4Codec.Decompress(compressed.AsSpan(), destination.AsSpan(1, source.Length));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
            Assert.That(destination[0], Is.EqualTo(123));
            Assert.That(destination[destination.Length - 1], Is.EqualTo(123));
            for (int index = 0; index < source.Length; index++)
                Assert.That(destination[index + 1], Is.EqualTo(source[index]));
        }

        [Test]
        public void Deserialize_ReusesTemporaryPayloadAndKeepsOutputOwnership()
        {
            var vertices = new VividMeshletVertex[4096];
            vertices[0].Position = new float3(1, 2, 3);
            byte[] blob = SerializeExpanded(
                Array.Empty<int>(), Array.Empty<VividMeshLODNode>(), Array.Empty<VividMeshlet>(), vertices, new byte[] { 0, 1, 2 });
            DeserializeExpanded(blob, out _, out _, out _, out var first, out _);
            long before = GC.GetAllocatedBytesForCurrentThread();
            DeserializeExpanded(blob, out _, out _, out _, out var second, out var indices);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            // Only the owned vertex/index arrays and small reader/stream objects remain.
            Assert.That(allocated, Is.LessThan(vertices.Length * 20L + 4096));
            Assert.That(first, Is.Not.SameAs(second));
            first[0].Position = new float3(4, 5, 6);
            Assert.That(second[0].Position, Is.EqualTo(vertices[0].Position));
            CollectionAssert.AreEqual(new byte[] { 0, 1, 2 }, indices);
        }

        [Test]
        public void LZ4Codec_RejectsTruncatedLiteralsAndInvalidMatchOffsets()
        {
            Assert.Throws<InvalidDataException>(() => VividLZ4Codec.Decompress(new byte[] { 0x20, 1 }, 2));
            Assert.Throws<InvalidDataException>(() => VividLZ4Codec.Decompress(new byte[] { 0, 0, 0 }, 4));
        }

        [Test]
        public void Deserialize_RejectsArrayThatExtendsBeyondPayloadIntoPooledCapacity()
        {
            byte[] compressed = VividLZ4Codec.Compress(BitConverter.GetBytes(1));
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(MeshletBlobMagic);
            writer.Write(VividMeshletCollectionBinarySerializer.CurrentVersion);
            writer.Write(VividMeshletCollectionBinarySerializer.LZ4CompressionCodec);
            writer.Write(sizeof(int)); // A count of one, but no element bytes.
            writer.Write(compressed.Length);
            writer.Write(compressed);
            byte[] blob = stream.ToArray();
            Assert.Throws<EndOfStreamException>(() => DeserializeExpanded(
                blob, out _, out _, out _, out _, out _));
        }

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
            byte[] blob = SerializeExpanded(
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

            DeserializeExpanded(
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

            DeserializeExpanded(
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
            Assert.That(nodes[0].ParentBounds, Is.EqualTo(legacyNode.ParentBounds));
            Assert.That(nodes[0].ParentError, Is.EqualTo(legacyNode.ParentError));
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
                Is.LessThanOrEqualTo(0.5f)
            );
            float4 tangent = VividMeshletVertexPacking.UnpackTangent(vertices[0].PackedTangent);
            Assert.That(DirectionErrorDegrees(math.normalize(legacyVertex.Tangent.xyz - math.normalize(legacyVertex.Normal.xyz) * math.dot(math.normalize(legacyVertex.Normal.xyz), legacyVertex.Tangent.xyz)), tangent.xyz), Is.LessThanOrEqualTo(0.5f));
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

            DeserializeExpanded(
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
            Assert.That(vertices, Has.Length.EqualTo(1));
            Assert.That(vertices[0].Position, Is.EqualTo(packedVertex.Position));
            Assert.That(vertices[0].UV, Is.EqualTo(packedVertex.UV));
            Assert.That(DirectionErrorDegrees(VividMeshletVertexPacking.UnpackNormal(packedVertex),
                VividMeshletVertexPacking.UnpackNormal(vertices[0])), Is.LessThan(0.1f));
            Assert.That(DirectionErrorDegrees(VividMeshletVertexPacking.UnpackTangent(packedVertex).xyz,
                VividMeshletVertexPacking.UnpackTangent(vertices[0]).xyz), Is.LessThan(0.5f));
            Assert.That(VividMeshletVertexPacking.UnpackTangent(vertices[0]).w,
                Is.EqualTo(VividMeshletVertexPacking.UnpackTangent(packedVertex).w));
            CollectionAssert.AreEqual(new byte[] { 0, 1, 2 }, indices);
        }

        [Test]
        public void Deserialize_Version6_PreservesExactTransitionsWithoutRebuild()
        {
            var group = new float4(10.1234f, -20.2345f, 30.3456f, 40.4567f);
            const float error = 0.0036991127f;
            var source = new[] {
                VividMeshletMetadataPacking.PackMeshLODNode(group, default, -1f, error, 0, 1, 0),
                VividMeshletMetadataPacking.PackMeshLODNode(new float4(1, 2, 3, 4), group, error, 0, 1, 1, 1),
            };
            using var payload = new MemoryStream();
            using var writer = new BinaryWriter(payload, System.Text.Encoding.UTF8, true);
            writer.Write(2); writer.Write(1); writer.Write(1);
            writer.Write(source.Length);
            writer.Write(MemoryMarshal.AsBytes(source.AsSpan()));
            writer.Write(0); writer.Write(0); writer.Write(0);
            byte[] blob = CreateVersionedLZ4Blob(6u, payload.ToArray());
            DeserializeExpanded(blob, out var levels, out var nodes, out _, out _, out _);
            CollectionAssert.AreEqual(new[] { 1, 1 }, levels);
            CollectionAssert.AreEqual(source, nodes);
            byte[] converted = SerializeExpanded(levels, nodes,
                Array.Empty<VividMeshlet>(), Array.Empty<VividMeshletVertex>(), Array.Empty<byte>());
            DeserializeExpanded(converted, out _, out var reloaded, out _, out _, out _);
            CollectionAssert.AreEqual(source, reloaded);
        }

        [TestCase(1u, 0xffffffffu)] // missing own group
        [TestCase(0u, 1u)] // missing parent group
        [TestCase(0xffffffffu, 0xffffffffu)] // own ID cannot be the root sentinel
        public void Deserialize_SharedGroups_RejectsInvalidIndices(uint ownIndex, uint parentIndex)
        {
            using var payload = new MemoryStream();
            using var writer = new BinaryWriter(payload, System.Text.Encoding.UTF8, true);
            writer.Write(1); writer.Write(1); // levels
            writer.Write(1); WriteFloat4(writer, new float4(1, 2, 3, 4)); writer.Write(0f);
            writer.Write(1); writer.Write(ownIndex); writer.Write(parentIndex); writer.Write(0u); writer.Write(1u);
            writer.Write(0); writer.Write(0); writer.Write(0);
            byte[] blob = CreateVersionedLZ4Blob(7u, payload.ToArray());
            Assert.Throws<InvalidDataException>(() => DeserializeExpanded(
                blob, out _, out _, out _, out _, out _));
        }

        private static byte[] CreateVersionedLZ4Blob(uint version, byte[] payload)
        {
            byte[] compressed = VividLZ4Codec.Compress(payload);
            using var blob = new MemoryStream();
            using var writer = new BinaryWriter(blob, System.Text.Encoding.UTF8, true);
            writer.Write(MeshletBlobMagic); writer.Write(version);
            writer.Write(VividMeshletCollectionBinarySerializer.LZ4CompressionCodec);
            writer.Write(payload.Length); writer.Write(compressed.Length); writer.Write(compressed);
            return blob.ToArray();
        }

        [Test]
        public void Serialize_SharedGroupTransition_RoundTripsBitExactly()
        {
            var bounds = new float4(12345.123f, -2345.678f, 0.0001234f, 31.464842f);
            const float error = 0.0036991127f;
            var source = new[] {
                VividMeshletMetadataPacking.PackMeshLODNode(bounds, default, -1f, error, 0, 1, 0),
                VividMeshletMetadataPacking.PackMeshLODNode(new float4(2, 3, 4, 5), bounds, error, 0, 1, 1, 1),
                VividMeshletMetadataPacking.PackMeshLODNode(new float4(-5, -3, 4, 2), bounds, error, 0, 2, 1, 1),
            };
            byte[] blob = SerializeExpanded(new[] { 1, 2 }, source,
                Array.Empty<VividMeshlet>(), Array.Empty<VividMeshletVertex>(), Array.Empty<byte>());
            // Inspect the v7 payload itself: both children reference the parent's
            // one shared record, rather than merely decoding to equal copies.
            byte[] payload = VividLZ4Codec.Decompress(blob.AsSpan(20).ToArray(), BitConverter.ToInt32(blob, 12));
            using (var reader = new BinaryReader(new MemoryStream(payload)))
            {
                int levelCount = reader.ReadInt32();
                reader.BaseStream.Position += levelCount * 4;
                int groupCount = reader.ReadInt32();
                Assert.That(groupCount, Is.EqualTo(3));
                reader.BaseStream.Position += groupCount * 20;
                Assert.That(reader.ReadInt32(), Is.EqualTo(3));
                uint parentGroupIndex = reader.ReadUInt32();
                Assert.That(reader.ReadUInt32(), Is.EqualTo(uint.MaxValue));
                reader.ReadUInt32(); reader.ReadUInt32();
                for (int i = 0; i < 2; i++)
                {
                    reader.ReadUInt32();
                    Assert.That(reader.ReadUInt32(), Is.EqualTo(parentGroupIndex));
                    reader.ReadUInt32(); reader.ReadUInt32();
                }
            }
            DeserializeExpanded(blob, out _, out var nodes, out _, out _, out _);
            CollectionAssert.AreEqual(source, nodes);
            for (int i = 1; i < nodes.Length; i++)
            {
                Assert.That(math.asuint(nodes[i].ParentBounds), Is.EqualTo(math.asuint(nodes[0].Bounds)));
                Assert.That(math.asuint(nodes[i].ParentError), Is.EqualTo(math.asuint(nodes[0].Error)));
            }
        }

        [TestCase(4u)]
        [TestCase(5u)]
        public void Deserialize_RecenteredParentFormat_UsesCompleteFinestCut(uint version)
        {
            using var payload = new MemoryStream();
            using var w = new BinaryWriter(payload, System.Text.Encoding.UTF8, true);
            w.Write(3); w.Write(1); w.Write(2); w.Write(4);
            w.Write(7);
            for (int i = 0; i < 7; i++)
            {
                WriteFloat4(w, new float4(i, 2, 3, 4));
                w.Write(0.01f); w.Write(0x42003d00u);
                w.Write((uint)i); w.Write(1u | ((uint)(i == 0 ? 0 : i < 3 ? 1 : 2) << 16));
            }
            w.Write(0); w.Write(0); w.Write(0); // meshlets, vertices, indices
            byte[] bytes = payload.ToArray(), compressed = VividLZ4Codec.Compress(bytes);
            using var blob = new MemoryStream();
            using var header = new BinaryWriter(blob, System.Text.Encoding.UTF8, true);
            header.Write(MeshletBlobMagic); header.Write(version);
            header.Write(VividMeshletCollectionBinarySerializer.LZ4CompressionCodec);
            header.Write(bytes.Length); header.Write(compressed.Length); header.Write(compressed);
            DeserializeExpanded(blob.ToArray(), out var counts, out var nodes,
                out _, out _, out _);
            CollectionAssert.AreEqual(new[] { 4 }, counts);
            Assert.That(nodes.Length, Is.EqualTo(4));
            for (int i = 0; i < nodes.Length; i++)
            {
                Assert.That(nodes[i].MeshletStartIndex, Is.EqualTo((uint)i + 3));
                Assert.That(nodes[i].LevelIndex, Is.Zero);
                Assert.That(nodes[i].Error, Is.Zero);
                Assert.That(nodes[i].ParentError, Is.LessThan(0));
            }
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
            byte[] packedBlob = SerializeExpanded(
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
            byte[] version4Blob = SerializeExpanded(
                Array.Empty<int>(),
                VividMeshletCollectionBinarySerializer.ConvertLegacyMeshLODNodes(legacyNodes),
                VividMeshletCollectionBinarySerializer.ConvertLegacyMeshlets(legacyMeshlets),
                Array.Empty<VividMeshletVertex>(),
                Array.Empty<byte>());

            Assert.That(version4Blob.Length, Is.LessThan(version3Blob.Length));
        }

        [Test]
        public void Deserialize_Version4Quantization_ExpandsBoundsAndDisablesStaleCones()
        {
            var sourcePosition = new float3(12345.123f, -6789.456f, 2345.678f);
            var sourceSphere = new float4(sourcePosition, 1f);
            byte[] payload;
            using (var stream = new MemoryStream())
            {
                using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
                w.Write(1); w.Write(1); // level counts
                w.Write(1); // packed node, 32 bytes
                WriteFloat4(w, sourceSphere);
                w.Write(0f); w.Write(0xBF80u); // leaf error, negative parent sentinel
                w.Write(0u); w.Write(1u); // meshlet start, count/level
                w.Write(1); // packed meshlet, 32 bytes
                w.Write(0u); w.Write(0u); w.Write(3u | (1u << 16));
                w.Write(VividMeshletMetadataPacking.PackMeshlet(0, 0, 3, 1, sourceSphere, new float3(0, 0, 1), 0).PackedCone);
                WriteFloat4(w, sourceSphere);
                w.Write(1); // legacy vertex, 32 bytes
                w.Write(sourcePosition.x); w.Write(sourcePosition.y); w.Write(sourcePosition.z);
                w.Write(VividMeshletVertexPacking.PackNormal(new float3(0, 0, 1)));
                w.Write(VividMeshletVertexPacking.PackTangent(new float4(1, 0, 0, -1)));
                w.Write(0.12345f); w.Write(-0.67891f); w.Write(0u);
                w.Write(0); // indices are irrelevant to this conversion check
                payload = stream.ToArray();
            }
            byte[] compressed = VividLZ4Codec.Compress(payload);
            using var blob = new MemoryStream();
            using var header = new BinaryWriter(blob, System.Text.Encoding.UTF8, true);
            header.Write(MeshletBlobMagic); header.Write(4u);
            header.Write(VividMeshletCollectionBinarySerializer.LZ4CompressionCodec);
            header.Write(payload.Length); header.Write(compressed.Length); header.Write(compressed);
            DeserializeExpanded(blob.ToArray(), out _, out var nodes,
                out var meshlets, out var vertices, out _);
            float error = math.distance(sourcePosition, vertices[0].Position);
            Assert.That(error, Is.GreaterThan(0));
            Assert.That(vertices[0].Position, Is.EqualTo(VividMeshletVertexPacking.QuantizePosition(sourcePosition)));
            Assert.That(nodes[0].Bounds.w, Is.GreaterThanOrEqualTo(1f + error));
            Assert.That(nodes[0].Error, Is.Zero, "The finest cut must remain selectable at a zero budget.");
            Assert.That(nodes[0].ParentError, Is.LessThan(0));
            Assert.That(meshlets[0].BoundingSphere.w, Is.GreaterThanOrEqualTo(1f + error));
            Assert.That(VividMeshletMetadataPacking.IsConeValid(meshlets[0].PackedCone), Is.False);
            byte[] migrated = SerializeExpanded(new[] { 1 }, nodes, meshlets, vertices, Array.Empty<byte>());
            DeserializeExpanded(migrated, out _, out var reloadedNodes,
                out var reloadedMeshlets, out var reloadedVertices, out _);
            CollectionAssert.AreEqual(nodes, reloadedNodes);
            CollectionAssert.AreEqual(meshlets, reloadedMeshlets);
            CollectionAssert.AreEqual(vertices, reloadedVertices);
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
