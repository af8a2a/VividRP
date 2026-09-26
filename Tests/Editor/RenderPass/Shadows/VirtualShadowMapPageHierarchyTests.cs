using System;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime.VirtualShadowMap;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualShadowMapPageHierarchyTests
    {
        [TestCase(1)]
        [TestCase(3)]
        [TestCase(16)]
        public void HierarchyMip_IsFinestCoveringAtMostTwoByTwoNodes(int axis)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.compute"));
            try
            {
                int intervals = axis * (axis + 1) / 2;
                var rects = new Vector4[intervals * intervals];
                int index = 0;
                for (int x0 = 0; x0 < axis; x0++)
                    for (int x1 = x0; x1 < axis; x1++)
                        for (int y0 = 0; y0 < axis; y0++)
                            for (int y1 = y0; y1 < axis; y1++)
                                rects[index++] = new Vector4(x0, y0, x1, y1);
                using var input = new GraphicsBuffer(GraphicsBuffer.Target.Structured, rects.Length, 16);
                using var output = new GraphicsBuffer(GraphicsBuffer.Target.Structured, rects.Length, 8);
                input.SetData(rects);
                int kernel = shader.FindKernel("InspectVSMHierarchyMip");
                shader.SetBuffer(kernel, "_SamplingInputs", input);
                shader.SetBuffer(kernel, "_SamplingResults", output);
                shader.SetInt("_SamplingCount", rects.Length);
                shader.Dispatch(kernel, (rects.Length + 63) / 64, 1, 1);
                var result = new Vector2[rects.Length]; output.GetData(result);
                for (int i = 0; i < rects.Length; i++)
                {
                    Vector4 r = rects[i];
                    // Independent oracle: search from leaves until the shifted
                    // rectangle fits, rather than repeating firstbithigh math.
                    int expected = 0;
                    while (((int)r.z >> expected) - ((int)r.x >> expected) > 1
                        || ((int)r.w >> expected) - ((int)r.y >> expected) > 1) expected++;
                    Assert.That(result[i].x, Is.EqualTo(expected));
                    Assert.That(result[i].y, Is.InRange(1f, 4f));
                }
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [TestCase(0, false)]
        [TestCase(5, true)]
        [TestCase(10, true)]
        [TestCase(15, false)]
        public void PageHierarchy_TwoByTwoPageRect_RejectsAdjacentDirtyPages(int dirtyPage, bool overlaps)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.compute"));
            try
            {
                using var masks = new VirtualShadowMapReceiverMaskTestBuffers(shader, 16, 1, true);
                using var table = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 16, 4);
                using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 16, 16);
                using var owners = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4);
                using var hierarchy = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 21, 12);
                using var input = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 16);
                using var levels = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 16);
                using var output = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 8);
                var map = new uint[16]; map[dirtyPage] = 1; table.SetData(map);
                var meta = new uint4[16]; meta[dirtyPage] = new uint4(2u | 4u | 32768u, 1, 0, 0); metadata.SetData(meta);
                var mask = new uint2[16]; mask[dirtyPage] = new uint2(uint.MaxValue); masks.Requests.SetData(mask);
                owners.SetData(new[] { (uint)dirtyPage + 1u });
                input.SetData(new[] { new Vector4(128, 128, 383, 383) }); // Pages [1,1]..[2,2].
                levels.SetData(new[] { Vector4.zero });
                shader.SetInt("_VSMProjectionCount", 1); shader.SetInt("_VSMPrototypePagesPerAxis", 4);
                shader.SetInt("_VSMPrototypePageSize", 128); shader.SetInt("_VSMPrototypePageTableEntryCount", 16);
                shader.SetInt("_VSMPrototypePhysicalPageCapacity", 1); shader.SetInt("_VSMPageCullHierarchyEnabled", 1);
                int clear = shader.FindKernel("VSMClearPageCullHierarchy"), build = shader.FindKernel("VSMBuildPageCullHierarchy");
                int inspect = shader.FindKernel("InspectVSMPageHierarchy");
                foreach (int kernel in new[] { build, inspect })
                {
                    shader.SetBuffer(kernel, "_VSMPrototypePageTable", table);
                    shader.SetBuffer(kernel, "_VSMPrototypePageMetadata", metadata);
                    shader.SetBuffer(kernel, "_VSMPageReceiverMasks", masks.Requests);
                }
                shader.SetBuffer(clear, "_VSMPageCullHierarchyRW", hierarchy);
                shader.SetBuffer(build, "_VSMPageCullHierarchyRW", hierarchy);
                shader.SetBuffer(build, "_VSMPrototypePhysicalPageOwners", owners);
                shader.Dispatch(clear, 1, 1, 1); shader.Dispatch(build, 1, 1, 1);
                shader.SetBuffer(inspect, "_VSMPageCullHierarchy", hierarchy);
                shader.SetBuffer(inspect, "_SamplingInputs", input); shader.SetBuffer(inspect, "_SamplingNormals", levels);
                shader.SetBuffer(inspect, "_SamplingResults", output); shader.SetInt("_SamplingCount", 1);
                var result = new Vector2[1];
                for (int layer = 0; layer < 2; layer++)
                {
                    shader.SetInt("_VSMPrototypeCasterLayer", layer); shader.Dispatch(inspect, 1, 1, 1); output.GetData(result);
                    Assert.That(result[0], Is.EqualTo(new Vector2(overlaps ? 1 : 0, overlaps ? 1 : 0)));
                }
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [TestCase(0.5f, 0.5f)]
        [TestCase(0.625f, 0.375f)]
        [TestCase(0.01f, 0.99f)]
        public void CoarsePages_MarkOriginWithoutReceivers_AndPreserveRequests(float x, float y)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute"));
            try
            {
                using var masks = new VirtualShadowMapReceiverMaskTestBuffers(shader, 32, 1, true);
                using var requests = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 32, 4);
                using var projections = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, 160);
                var data = new uint[32];
                for (int i = 0; i < data.Length; i++) data[i] = 1u | 512u;
                requests.SetData(data);
                var projection = new VirtualShadowMapProjection { WorldToShadow = Matrix4x4.identity,
                    SelectionSphere = new Vector4(x, y, 0, -1) };
                projections.SetData(new[] { projection, projection });
                int kernel = shader.FindKernel("VSMMarkCoarsePages");
                shader.SetInt("_VSMProjectionCount", 2);
                shader.SetInt("_VSMPrototypePagesPerAxis", 4);
                shader.SetInt("_VSMPrototypeRequestEnabled", 1);
                shader.SetBuffer(kernel, "_VSMProjections", projections);
                shader.SetBuffer(kernel, "_VSMPageRequestFlags", requests);
                shader.Dispatch(kernel, 1, 1, 1);
                shader.Dispatch(kernel, 1, 1, 1); // Idempotent OR, no input textures.
                requests.GetData(data);
                var maskData = new uint2[32]; masks.Requests.GetData(maskData);
                for (int p = 0; p < data.Length; p++)
                {
                    int px = p % 4, py = p % 16 / 4;
                    bool coarse = p >= 16 && px >= Mathf.FloorToInt(x * 4 - .5f) && px <= Mathf.CeilToInt(x * 4 - .5f)
                        && py >= Mathf.FloorToInt(y * 4 - .5f) && py <= Mathf.CeilToInt(y * 4 - .5f);
                    Assert.That(data[p], Is.EqualTo(513u | (coarse ? 256u : 0u)));
                    Assert.That(maskData[p], Is.EqualTo(coarse ? new uint2(uint.MaxValue) : new uint2(0)));
                }
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [TestCase(1)]
        [TestCase(3)]
        [TestCase(16)]
        [TestCase(33)]
        [TestCase(128)]
        public void PageHierarchy_MatchesIndependentCellReduction_AndNeverRejectsRequiredPages(int axis)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.compute"));
            try
            {
                int pages = axis * axis * 2, capacity = Math.Min(pages, 512);
                int padded = Mathf.NextPowerOfTwo(axis), nodes = VirtualShadowMapPrototypeRuntime.CalculateHierarchyNodesPerLevel(axis);
                using var masks = new VirtualShadowMapReceiverMaskTestBuffers(shader, pages, capacity, true);
                using var table = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pages, 4);
                using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pages, 16);
                using var owners = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 4);
                using var hierarchy = new GraphicsBuffer(GraphicsBuffer.Target.Structured, nodes * 2, 12);
                using var input = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 128, 16);
                using var levels = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 128, 16);
                using var results = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 128, 8);
                var map = new uint[pages]; var meta = new uint4[pages]; var own = new uint[capacity]; var mask = new uint2[pages];
                var random = new System.Random(7621 + axis);
                for (int slot = 0; slot < capacity; slot++)
                {
                    int page = slot * 257 % pages;
                    map[page] = (uint)slot + 1; own[slot] = (uint)page + 1;
                    uint flags = 2u | (slot % 3 != 0 ? 4u : 0u) | (slot % 4 != 0 ? 32768u : 0u)
                        | (slot % 7 == 0 ? 131072u : 0u);
                    meta[page] = new uint4(flags, (uint)slot + 1, 0, 0);
                    mask[page] = new uint2((uint)random.Next(), (uint)random.Next());
                }
                table.SetData(map); metadata.SetData(meta); owners.SetData(own); masks.Requests.SetData(mask);
                shader.SetInt("_VSMProjectionCount", 2); shader.SetInt("_VSMPrototypePagesPerAxis", axis);
                shader.SetInt("_VSMPrototypePageSize", 128); shader.SetInt("_VSMPrototypePageTableEntryCount", pages);
                shader.SetInt("_VSMPrototypePhysicalPageCapacity", capacity); shader.SetInt("_VSMPageCullHierarchyEnabled", 1);
                int clear = shader.FindKernel("VSMClearPageCullHierarchy"), build = shader.FindKernel("VSMBuildPageCullHierarchy");
                int inspect = shader.FindKernel("InspectVSMPageHierarchy");
                foreach (int kernel in new[] { build, inspect })
                {
                    shader.SetBuffer(kernel, "_VSMPrototypePageTable", table);
                    shader.SetBuffer(kernel, "_VSMPrototypePageMetadata", metadata);
                    shader.SetBuffer(kernel, "_VSMPageReceiverMasks", masks.Requests);
                }
                shader.SetBuffer(clear, "_VSMPageCullHierarchyRW", hierarchy);
                shader.SetBuffer(build, "_VSMPageCullHierarchyRW", hierarchy);
                shader.SetBuffer(build, "_VSMPrototypePhysicalPageOwners", owners);
                shader.Dispatch(clear, (nodes * 2 + 63) / 64, 1, 1);
                shader.Dispatch(build, (capacity + 63) / 64, 1, 1);
                var actual = new uint3[nodes * 2]; hierarchy.GetData(actual);
                var expected = new uint3[nodes * 2];
                for (int slot = 0; slot < capacity; slot++)
                {
                    int p = (int)own[slot] - 1, px = p % axis, py = p % (axis * axis) / axis, level = p / (axis * axis);
                    uint flags = 2u | ((meta[p].x & 131072u) == 0 ? meta[p].x & (4u | 32768u) : 0u);
                    ulong bits = (flags & 32768u) != 0 ? mask[p].x | ((ulong)mask[p].y << 32) : 0;
                    int offset = level * nodes;
                    for (int mip = 0, n = padded; n > 0; mip++, offset += n * n, n >>= 1)
                    {
                        int index = offset + (py >> mip) * n + (px >> mip);
                        expected[index].x |= flags;
                        for (int bit = 0; bit < 64; bit++)
                            if ((bits & (1UL << bit)) != 0)
                            {
                                int cx = ((px * 8 + bit % 8) >> mip) & 7;
                                int cy = ((py * 8 + bit / 8) >> mip) & 7;
                                if (cy < 4) expected[index].y |= 1u << (cy * 8 + cx);
                                else expected[index].z |= 1u << ((cy - 4) * 8 + cx);
                            }
                    }
                }
                Assert.That(actual, Is.EqualTo(expected));
                var rects = new Vector4[128]; var clipmaps = new Vector4[128]; var result = new Vector2[128];
                for (int i = 0; i < rects.Length; i++)
                {
                    int x = random.Next(axis * 128), y = random.Next(axis * 128);
                    rects[i] = new Vector4(x, y, Math.Min(axis * 128 - 1, x + random.Next(256)), Math.Min(axis * 128 - 1, y + random.Next(256)));
                    clipmaps[i].x = i & 1;
                }
                input.SetData(rects); levels.SetData(clipmaps);
                shader.SetBuffer(inspect, "_VSMPageCullHierarchy", hierarchy);
                shader.SetBuffer(inspect, "_SamplingInputs", input); shader.SetBuffer(inspect, "_SamplingNormals", levels);
                shader.SetBuffer(inspect, "_SamplingResults", results); shader.SetInt("_SamplingCount", 128);
                for (int layer = 0; layer < 2; layer++)
                {
                    shader.SetInt("_VSMPrototypeCasterLayer", layer); shader.Dispatch(inspect, 2, 1, 1); results.GetData(result);
                    foreach (var pair in result) Assert.That(pair.x >= pair.y, Is.True, "Hierarchy rejected required work.");
                }
                // The next frame must not retain any prior mask or flags, including padding/root.
                owners.SetData(new uint[capacity]);
                shader.Dispatch(clear, (nodes * 2 + 63) / 64, 1, 1);
                shader.Dispatch(build, (capacity + 63) / 64, 1, 1); hierarchy.GetData(actual);
                Assert.That(actual, Is.EqualTo(new uint3[nodes * 2]));
            }
            finally { Object.DestroyImmediate(shader); }
        }
    }
}
