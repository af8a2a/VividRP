using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class StrandHairVisibilityAtlasTests
    {
        [Test]
        public void AtlasAllocator_SortsBackToFrontAndAssignsDisjointTiles()
        {
            var asset = CreateTestGroomAsset(strandCount: 8, vertsPerStrand: 4);
            try
            {
                var entries = new List<HairGroomRenderEntry>
                {
                    CreateEntry(asset, new Vector3(-0.3f, 0.0f, 1.5f)), // nearer
                    CreateEntry(asset, new Vector3( 0.3f, 0.0f, 3.5f)), // farther
                };

                Matrix4x4 view = Matrix4x4.LookAt(Vector3.zero, Vector3.forward, Vector3.up).inverse;
                // Unity camera view matrix looks down -Z
                view = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1, 1, -1)).inverse;
                Matrix4x4 proj = Matrix4x4.Perspective(45.0f, 16.0f / 9.0f, 0.1f, 50.0f);
                Matrix4x4 vp = GL.GetGPUProjectionMatrix(proj, true) * view;

                var allocs = new List<StrandHairModelAtlasAllocation>(4);
                int count = StrandHairAtlasAllocator.AllocateModels(
                    entries,
                    view,
                    vp,
                    Vector3.zero,
                    0.1f,
                    1280,
                    720,
                    StrandHairAtlasContract.DefaultMaxAtlasTiles,
                    allocs,
                    out int totalTiles,
                    out int downscale);

                Assert.That(count, Is.EqualTo(2));
                Assert.That(downscale, Is.EqualTo(1));
                Assert.That(allocs[0].EntryIndex, Is.EqualTo(1), "Farther model must be sorted first (back-to-front).");
                Assert.That(allocs[1].EntryIndex, Is.EqualTo(0), "Nearer model must be sorted second.");
                Assert.That(allocs[0].FirstAtlasTile, Is.EqualTo(0));
                Assert.That(allocs[1].FirstAtlasTile, Is.EqualTo(allocs[0].TileCount));
                Assert.That(totalTiles, Is.EqualTo(allocs[0].TileCount + allocs[1].TileCount));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void AtlasAllocator_DownscalesWhenOverlappingModelsExceedTileBudget()
        {
            var asset = CreateTestGroomAsset(strandCount: 8, vertsPerStrand: 4);
            try
            {
                var entries = new List<HairGroomRenderEntry>
                {
                    CreateEntry(asset, new Vector3(0.0f, 0.0f, 0.6f), scale: 2.5f),
                    CreateEntry(asset, new Vector3(0.0f, 0.0f, 0.8f), scale: 2.5f),
                };

                Matrix4x4 view = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1, 1, -1)).inverse;
                Matrix4x4 proj = Matrix4x4.Perspective(45.0f, 16.0f / 9.0f, 0.1f, 50.0f);
                Matrix4x4 vp = GL.GetGPUProjectionMatrix(proj, true) * view;

                // Deliberately tight budget (120 tiles) so 1280x720 fullscreen x2 models (~480 tiles) must downscale
                var allocs = new List<StrandHairModelAtlasAllocation>(4);
                int count = StrandHairAtlasAllocator.AllocateModels(
                    entries,
                    view,
                    vp,
                    Vector3.zero,
                    0.1f,
                    1280,
                    720,
                    maxAtlasTiles: 120,
                    allocs,
                    out int totalTiles,
                    out int downscale);

                Assert.That(count, Is.EqualTo(2));
                Assert.That(downscale, Is.GreaterThanOrEqualTo(2));
                Assert.That(totalTiles, Is.LessThanOrEqualTo(120));
                Assert.That(allocs[0].IsVisible, Is.True);
                Assert.That(allocs[1].IsVisible, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void AtlasAllocator_CullsBehindCameraAndAllocatesZeroManagedBytes()
        {
            var asset = CreateTestGroomAsset(strandCount: 4, vertsPerStrand: 3);
            try
            {
                var entries = new List<HairGroomRenderEntry>
                {
                    CreateEntry(asset, new Vector3(0.0f, 0.0f, -3.0f)), // behind camera (+Z forward)
                    CreateEntry(asset, new Vector3(0.0f, 0.0f,  2.0f)), // visible
                };

                Matrix4x4 view = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1, 1, -1)).inverse;
                Matrix4x4 proj = Matrix4x4.Perspective(45.0f, 16.0f / 9.0f, 0.1f, 50.0f);
                Matrix4x4 vp = GL.GetGPUProjectionMatrix(proj, true) * view;

                var allocs = new List<StrandHairModelAtlasAllocation>(8);
                int warm = StrandHairAtlasAllocator.AllocateModels(
                    entries, view, vp, Vector3.zero, 0.1f, 1280, 720, 512, allocs, out _, out _);
                StrandHairAtlasAllocator.AllocateModels(
                    entries, view, vp, Vector3.zero, 0.1f, 1280, 720, 512, allocs, out _, out _);
                _ = GC.GetAllocatedBytesForCurrentThread();

                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 64; i++)
                {
                    StrandHairAtlasAllocator.AllocateModels(
                        entries, view, vp, Vector3.zero, 0.1f, 1280, 720, 512, allocs, out _, out _);
                }
                long after = GC.GetAllocatedBytesForCurrentThread();

                Assert.That(warm, Is.EqualTo(1), "Behind-camera model must be culled.");
                Assert.That(after - before, Is.EqualTo(0L), "StrandHairAtlasAllocator must allocate 0 managed bytes.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        private static HairGroomRenderEntry CreateEntry(HairGroomAsset asset, Vector3 position, float scale = 1.0f)
        {
            Matrix4x4 trs = Matrix4x4.TRS(position, Quaternion.identity, Vector3.one * scale);
            Bounds worldBounds = HairGroomSystem.TransformBounds(asset.Bounds, trs);
            return new HairGroomRenderEntry(
                null,
                asset,
                trs,
                worldBounds,
                1.0f,
                0,
                HairGroomShadingParameters.CreateDefault());
        }

        private static HairGroomAsset CreateTestGroomAsset(int strandCount, int vertsPerStrand)
        {
            var strands = new HairGroomStrandDesc[strandCount];
            var verts = new HairGroomRestVertex[strandCount * vertsPerStrand];
            var segs = new HairGroomSegmentIndex[strandCount * (vertsPerStrand - 1)];
            int vCur = 0;
            int sCur = 0;
            for (int s = 0; s < strandCount; s++)
            {
                strands[s] = new HairGroomStrandDesc((uint)vCur, (uint)vertsPerStrand, Vector2.zero, 0, 0.5f);
                for (int v = 0; v < vertsPerStrand; v++)
                {
                    float u = v / (float)(vertsPerStrand - 1);
                    verts[vCur + v] = new HairGroomRestVertex(
                        new Vector3((s - strandCount * 0.5f) * 0.02f, u * 0.2f - 0.1f, 0.0f),
                        0.0005f,
                        Vector3.up,
                        u,
                        0.5f);
                }
                for (int k = 0; k < vertsPerStrand - 1; k++)
                    segs[sCur++] = new HairGroomSegmentIndex((uint)(vCur + k), (uint)s, (uint)k);
                vCur += vertsPerStrand;
            }

            byte[] bin = HairGroomBinaryCodec.WriteToBytes(
                strands,
                verts,
                segs,
                new Bounds(Vector3.zero, new Vector3(0.32f, 0.32f, 0.32f)),
                0.0005f,
                HairGroomFlags.StrandsRandomized);

            var asset = ScriptableObject.CreateInstance<HairGroomAsset>();
            asset.ImportFromBinary(bin);
            return asset;
        }
    }
}
