using System;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;
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
            var random = new Random(12345);
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
            byte[] blob = VividMeshletCollectionBinarySerializer.Serialize(
                levelCounts,
                Array.Empty<VividMeshLODNode>(),
                Array.Empty<VividMeshlet>(),
                Array.Empty<VividMeshletVertex>(),
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
            Assert.That(deserializedVertices, Is.Empty);
            CollectionAssert.AreEqual(indices, deserializedIndices);
        }

        [Test]
        public void Deserialize_ReadsLegacyVersionOneGZipBlob()
        {
            byte[] legacyPayload;
            using (var payloadStream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(payloadStream, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    for (int arrayIndex = 0; arrayIndex < 5; arrayIndex++)
                    {
                        writer.Write(0);
                    }
                }

                legacyPayload = payloadStream.ToArray();
            }

            byte[] legacyBlob;
            using (var outputStream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(outputStream, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write(MeshletBlobMagic);
                    writer.Write(1u);
                    writer.Write(legacyPayload.Length);
                }

                using (var gzipStream = new GZipStream(outputStream, CompressionLevel.Optimal, leaveOpen: true))
                {
                    gzipStream.Write(legacyPayload, 0, legacyPayload.Length);
                }

                legacyBlob = outputStream.ToArray();
            }

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
            Assert.That(vertices, Is.Empty);
            Assert.That(indices, Is.Empty);
        }
    }
}
