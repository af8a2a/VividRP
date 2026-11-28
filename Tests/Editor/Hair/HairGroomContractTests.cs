using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class HairGroomContractTests
    {
        [Test]
        public void GpuStructs_MatchExactHlslStrideAndFieldOffsets()
        {
            Assert.That(Marshal.SizeOf<HairGroomStrandDesc>(), Is.EqualTo(HairGroomStrandDesc.Stride));
            Assert.That(Marshal.OffsetOf<HairGroomStrandDesc>(nameof(HairGroomStrandDesc.FirstVertex)).ToInt32(), Is.EqualTo(0));
            Assert.That(Marshal.OffsetOf<HairGroomStrandDesc>(nameof(HairGroomStrandDesc.VertexCount)).ToInt32(), Is.EqualTo(4));
            Assert.That(Marshal.OffsetOf<HairGroomStrandDesc>(nameof(HairGroomStrandDesc.RootUVPacked)).ToInt32(), Is.EqualTo(8));
            Assert.That(Marshal.OffsetOf<HairGroomStrandDesc>(nameof(HairGroomStrandDesc.ClumpAndSeed)).ToInt32(), Is.EqualTo(12));

            Assert.That(Marshal.SizeOf<HairGroomRestVertex>(), Is.EqualTo(HairGroomRestVertex.Stride));
            Assert.That(Marshal.OffsetOf<HairGroomRestVertex>(nameof(HairGroomRestVertex.PositionOS)).ToInt32(), Is.EqualTo(0));
            Assert.That(Marshal.OffsetOf<HairGroomRestVertex>(nameof(HairGroomRestVertex.Radius)).ToInt32(), Is.EqualTo(12));
            Assert.That(Marshal.OffsetOf<HairGroomRestVertex>(nameof(HairGroomRestVertex.TangentOS)).ToInt32(), Is.EqualTo(16));
            Assert.That(Marshal.OffsetOf<HairGroomRestVertex>(nameof(HairGroomRestVertex.PackedAttrs)).ToInt32(), Is.EqualTo(28));

            Assert.That(Marshal.SizeOf<HairGroomSegmentIndex>(), Is.EqualTo(HairGroomSegmentIndex.Stride));
            Assert.That(Marshal.OffsetOf<HairGroomSegmentIndex>(nameof(HairGroomSegmentIndex.VertexIndex0)).ToInt32(), Is.EqualTo(0));
            Assert.That(Marshal.OffsetOf<HairGroomSegmentIndex>(nameof(HairGroomSegmentIndex.StrandAndLocalIndex)).ToInt32(), Is.EqualTo(4));

            Assert.That(Marshal.SizeOf<HairGroomVisibilitySH>(), Is.EqualTo(HairGroomVisibilitySH.Stride));
            Assert.That(Marshal.OffsetOf<HairGroomVisibilitySH>(nameof(HairGroomVisibilitySH.Coefficients)).ToInt32(), Is.EqualTo(0));

            Assert.That(Marshal.SizeOf<HairGroomBinaryHeader>(), Is.EqualTo(HairGroomBinaryHeader.Stride));
            Assert.That(Marshal.OffsetOf<HairGroomBinaryHeader>(nameof(HairGroomBinaryHeader.Magic)).ToInt32(), Is.EqualTo(0));
            Assert.That(Marshal.OffsetOf<HairGroomBinaryHeader>(nameof(HairGroomBinaryHeader.BoundsMin)).ToInt32(), Is.EqualTo(24));
            Assert.That(Marshal.OffsetOf<HairGroomBinaryHeader>(nameof(HairGroomBinaryHeader.BoundsMax)).ToInt32(), Is.EqualTo(36));
            Assert.That(Marshal.OffsetOf<HairGroomBinaryHeader>(nameof(HairGroomBinaryHeader.DefaultRadius)).ToInt32(), Is.EqualTo(48));
        }

        [Test]
        public void PackingHelpers_RoundTripWithinExpectedPrecision()
        {
            Vector2 uv = new Vector2(0.25f, 0.75f);
            uint packedUv = HairGroomPacking.PackHalf2(uv);
            Vector2 unpackedUv = HairGroomPacking.UnpackHalf2(packedUv);
            Assert.That(unpackedUv.x, Is.EqualTo(uv.x).Within(1e-3f));
            Assert.That(unpackedUv.y, Is.EqualTo(uv.y).Within(1e-3f));

            uint segPacked = HairGroomPacking.PackSegmentStrandLocal(123456u, 15u);
            Assert.That(HairGroomPacking.UnpackSegmentStrandIndex(segPacked), Is.EqualTo(123456u));
            Assert.That(HairGroomPacking.UnpackSegmentLocalIndex(segPacked), Is.EqualTo(15u));

            uint visPayload = HairGroomPacking.PackVisibilityPayload(0.375f, 987654u);
            Assert.That(HairGroomPacking.UnpackVisibilitySegmentId20(visPayload), Is.EqualTo(987654u));
            Assert.That(HairGroomPacking.UnpackVisibilityDepth12(visPayload), Is.EqualTo(0.375f).Within(1.0f / 4095.0f));
        }

        [Test]
        public void BinaryCodec_RoundTripsWithAndWithoutVisibilitySH()
        {
            byte[] bytesWithoutSH = CreateSampleBinary(strandCount: 4, vertsPerStrand: 5, includeSH: false);
            var parsed = HairGroomBinaryCodec.Read(bytesWithoutSH);
            Assert.That(parsed.Header.StrandCount, Is.EqualTo(4u));
            Assert.That(parsed.Header.VertexCount, Is.EqualTo(20u));
            Assert.That(parsed.Header.SegmentCount, Is.EqualTo(16u));
            Assert.That(parsed.VisibilitySH, Is.Null);

            byte[] bytesWithSH = CreateSampleBinary(strandCount: 3, vertsPerStrand: 4, includeSH: true);
            var parsedSH = HairGroomBinaryCodec.Read(bytesWithSH);
            Assert.That(parsedSH.VisibilitySH, Is.Not.Null);
            Assert.That(parsedSH.VisibilitySH.Length, Is.EqualTo(12));
            Assert.That(parsedSH.VisibilitySH[5].Coefficients, Is.EqualTo(new Vector4(0.5f, -0.25f, 0.75f, 0.125f)));
        }

        [Test]
        public void BinaryCodec_RejectsCorruptMagicAndTruncation()
        {
            byte[] bytes = CreateSampleBinary(strandCount: 2, vertsPerStrand: 3, includeSH: false);

            byte[] badMagic = (byte[])bytes.Clone();
            badMagic[0] ^= 0xFF;
            Assert.Throws<InvalidDataException>(() => HairGroomBinaryCodec.Read(badMagic));

            byte[] truncated = new byte[bytes.Length - 8];
            Buffer.BlockCopy(bytes, 0, truncated, 0, truncated.Length);
            Assert.Throws<InvalidDataException>(() => HairGroomBinaryCodec.Read(truncated));
        }

        [Test]
        public void HairGroomAsset_BuildsValid48ByteGpuStrandSegments()
        {
            var asset = ScriptableObject.CreateInstance<HairGroomAsset>();
            try
            {
                asset.ImportFromBinary(CreateSampleBinary(strandCount: 4, vertsPerStrand: 5, includeSH: false));
                Assert.That(asset.StrandCount, Is.EqualTo(4));
                Assert.That(asset.VertexCount, Is.EqualTo(20));
                Assert.That(asset.SegmentCount, Is.EqualTo(16));

                var gpuSegments = new HairGpuStrandSegment[asset.SegmentCount];
                int written = asset.BuildGpuStrandSegments(gpuSegments, radiusScale: 2.0f);
                Assert.That(written, Is.EqualTo(16));
                Assert.That(gpuSegments[0].StartPositionRadius.w, Is.GreaterThan(0.0f));
                Assert.That(gpuSegments[0].EndPositionRadius.w, Is.GreaterThan(0.0f));
                Assert.That(gpuSegments[0].StartEndUV.x, Is.EqualTo(0.0f).Within(1e-4f));
                Assert.That(gpuSegments[0].StartEndUV.z, Is.EqualTo(0.25f).Within(1e-4f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void HairGroomSystem_CollectActiveEntries_AllocatesZeroManagedMemory()
        {
            var asset = ScriptableObject.CreateInstance<HairGroomAsset>();
            var go = new GameObject("HairGroomZeroAllocTest");
            try
            {
                asset.ImportFromBinary(CreateSampleBinary(strandCount: 2, vertsPerStrand: 3, includeSH: false));
                var renderer = go.AddComponent<HairGroomRenderer>();
                renderer.EnablePathTracingDotsProxy = false;
                renderer.GroomAsset = asset;

                var entries = new List<HairGroomRenderEntry>(Math.Max(8, HairGroomSystem.ActiveCount + 4));
                // Warm up list, static registry, and GC measurement API before measuring.
                int warmCount = HairGroomSystem.CollectActiveEntries(entries);
                HairGroomSystem.CollectActiveEntries(entries);
                _ = GC.GetAllocatedBytesForCurrentThread();

                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 64; i++)
                {
                    HairGroomSystem.CollectActiveEntries(entries);
                }
                long after = GC.GetAllocatedBytesForCurrentThread();

                Assert.That(warmCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(after - before, Is.EqualTo(0L),
                    "HairGroomSystem.CollectActiveEntries must allocate zero managed bytes on steady-state frames.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        private static byte[] CreateSampleBinary(int strandCount, int vertsPerStrand, bool includeSH)
        {
            int vertCount = strandCount * vertsPerStrand;
            int segCount = strandCount * (vertsPerStrand - 1);

            var strands = new HairGroomStrandDesc[strandCount];
            var verts = new HairGroomRestVertex[vertCount];
            var segs = new HairGroomSegmentIndex[segCount];
            var sh = includeSH ? new HairGroomVisibilitySH[vertCount] : null;

            int vCursor = 0;
            int sCursor = 0;
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool boundsInit = false;

            for (int s = 0; s < strandCount; s++)
            {
                strands[s] = new HairGroomStrandDesc(
                    (uint)vCursor,
                    (uint)vertsPerStrand,
                    new Vector2(s / (float)strandCount, 0.5f),
                    (ushort)(s & 3),
                    (s + 0.5f) / strandCount);

                for (int v = 0; v < vertsPerStrand; v++)
                {
                    float u = v / (float)(vertsPerStrand - 1);
                    Vector3 pos = new Vector3(s * 0.02f, u * 0.15f, 0.0f);
                    float radius = Mathf.Lerp(0.0008f, 0.0002f, u);
                    verts[vCursor + v] = new HairGroomRestVertex(
                        pos,
                        radius,
                        Vector3.up,
                        u,
                        (s + 0.5f) / strandCount);

                    if (includeSH)
                        sh[vCursor + v] = new HairGroomVisibilitySH(new Vector4(0.5f, -0.25f, 0.75f, 0.125f));

                    if (!boundsInit)
                    {
                        bounds = new Bounds(pos, Vector3.zero);
                        boundsInit = true;
                    }
                    else
                    {
                        bounds.Encapsulate(pos);
                    }
                }

                for (int k = 0; k < vertsPerStrand - 1; k++)
                {
                    segs[sCursor++] = new HairGroomSegmentIndex(
                        (uint)(vCursor + k),
                        (uint)s,
                        (uint)k);
                }

                vCursor += vertsPerStrand;
            }

            return HairGroomBinaryCodec.WriteToBytes(
                strands,
                verts,
                segs,
                bounds,
                0.0006f,
                HairGroomFlags.StrandsRandomized | HairGroomFlags.HasClumpData,
                sh);
        }
    }
}
