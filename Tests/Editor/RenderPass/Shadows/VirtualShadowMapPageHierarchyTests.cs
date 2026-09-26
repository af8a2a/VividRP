using System;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.VirtualShadowMap;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualShadowMapPageHierarchyTests
    {
        [TestCase(1)]
        [TestCase(3)]
        [TestCase(10)]
        [TestCase(16)]
        public void CompactViews_PreservesProjectionIdsAndOtherLayer_AndClearsEmptyDispatch(int levels)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GPUDriven/GPUInstanceCulling.compute"));
            using var bounds = new GraphicsBuffer(GraphicsBuffer.Target.Structured, levels * 2, 16);
            using var views = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (levels + 1) * 2, 4);
            using var args = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments, 6, 4);
            using var listArgs = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments, 3, 4);
            using var cmd = new CommandBuffer();
            try
            {
                int kernel = shader.FindKernel("CSCompactVSMViews");
                var rects = new uint4[levels * 2]; var result = new uint[(levels + 1) * 2];
                var initial = new uint[result.Length]; Array.Fill(initial, 0xeeeeeeeeu);
                var indirect = new uint[6]; var initialArgs = new uint[6]; Array.Fill(initialArgs, 0xeeeeeeeeu);
                var list = new uint[3];
                foreach (int layer in new[] { 0, 1 })
                foreach (int pattern in new[] { 1, 2, 0, 3 }) // sparse -> other sparse -> empty -> full
                foreach (int instances in new[] { 0, 1, 32, 33, 1025 })
                {
                    uint expected = 0;
                    for (int i = 0; i < levels; i++)
                        for (int l = 0; l < 2; l++)
                            rects[i * 2 + l] = pattern == 3 || (pattern != 0 && (i + l) % 3 == pattern - 1)
                                ? new uint4(0) : new uint4(1, 1, 0, 0);
                    bounds.SetData(rects); views.SetData(initial); args.SetData(initialArgs);
                    var parameters = new VirtualShadowMapCullingParameters(null, null, bounds, 1, 128, 128, layer, true, views, args);
                    cmd.Clear(); parameters.CompactViews(cmd, shader, kernel, instances, levels, listArgs);
                    Graphics.ExecuteCommandBuffer(cmd); views.GetData(result); args.GetData(indirect); listArgs.GetData(list);
                    int offset = layer * (levels + 1);
                    for (int i = 0; i < levels; i++)
                        if (rects[i * 2 + layer].x <= rects[i * 2 + layer].z)
                            Assert.That(result[offset + 1 + expected++], Is.EqualTo((uint)i));
                    Assert.That(result[offset], Is.EqualTo(expected));
                    for (uint i = expected; i < levels; i++) Assert.That(result[offset + 1 + i], Is.EqualTo(uint.MaxValue));
                    int other = (1 - layer) * (levels + 1);
                    for (int i = 0; i <= levels; i++) Assert.That(result[other + i], Is.EqualTo(0xeeeeeeeeu));
                    for (int i = 0; i < 3; i++) Assert.That(indirect[(1 - layer) * 3 + i], Is.EqualTo(0xeeeeeeeeu));
                    Assert.That(indirect[layer * 3], Is.EqualTo(expected == 0 ? 0u : (uint)(instances + 31) / 32));
                    Assert.That(indirect[layer * 3 + 1], Is.EqualTo(expected));
                    Assert.That(indirect[layer * 3 + 2], Is.EqualTo(1u));
                    Assert.That(list, Is.EqualTo(new[] { 0u, expected, 1u }));
                }
                var stable = new VirtualShadowMapCullingParameters(null, null, bounds, 1, 128, 128, 0, true, views, args);
                for (int i = 0; i < 16; i++) { cmd.Clear(); stable.CompactViews(cmd, shader, kernel, 33, levels, listArgs); }
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 256; i++) { cmd.Clear(); stable.CompactViews(cmd, shader, kernel, 33, levels, listArgs); }
                Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [TestCase(3)]
        [TestCase(16)]
        [TestCase(33)]
        public void PageHierarchy_PropagatesNewMaskWithExistingFlags_AndNewFlagsWithExistingMask(int axis)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute"));
            try
            {
                const int levels = 2;
                int pages = axis * axis * levels, capacity = Math.Min(pages, 1024);
                int padded = Mathf.NextPowerOfTwo(axis), nodes = VirtualShadowMapPrototypeRuntime.CalculateHierarchyNodesPerLevel(axis);
                using var masks = new VirtualShadowMapReceiverMaskTestBuffers(shader, pages, capacity, true);
                using var table = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pages, 4);
                using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pages, 16);
                using var owners = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 4);
                using var hierarchy = new GraphicsBuffer(GraphicsBuffer.Target.Structured, nodes * levels, 12);
                var map = new uint[pages]; var meta = new uint4[pages]; var own = new uint[capacity]; var mask = new uint2[pages];
                var expected = new uint3[nodes * levels]; var actual = new uint3[expected.Length];
                var expectedBounds = new uint4[levels * 2]; var actualBounds = new uint4[levels * 2];
                for (int i = 0; i < expectedBounds.Length; i++) expectedBounds[i] = new uint4((uint)axis, (uint)axis, 0, 0);
                for (int slot = 0; slot < capacity; slot++)
                {
                    int p = slot * 257 % pages;
                    map[p] = (uint)slot + 1; own[slot] = (uint)p + 1;
                }
                table.SetData(map); owners.SetData(own);
                int clear = shader.FindKernel("VSMClearPageCullHierarchy"), build = shader.FindKernel("VSMBuildPageCullHierarchy");
                shader.SetInt("_VSMProjectionCount", levels); shader.SetInt("_VSMPrototypePagesPerAxis", axis);
                shader.SetInt("_VSMPrototypePageTableEntryCount", pages); shader.SetInt("_VSMPrototypePhysicalPageCapacity", capacity);
                shader.SetBuffer(clear, "_VSMPageCullHierarchyRW", hierarchy);
                shader.SetBuffer(build, "_VSMPageCullHierarchyRW", hierarchy);
                shader.SetBuffer(build, "_VSMPrototypePhysicalPageOwners", owners);
                shader.SetBuffer(build, "_VSMPrototypePageTable", table); shader.SetBuffer(build, "_VSMPrototypePageMetadata", metadata);
                shader.Dispatch(clear, (expected.Length + 63) / 64, 1, 1);
                // Add coverage after all flags already exist, then add a flag
                // after all mask bits exist. Finally repeat identical inputs.
                for (int phase = 0; phase < 4; phase++)
                {
                    uint flags = 2u | 32768u | (phase >= 2 ? 4u : 0u);
                    for (int slot = 0; slot < capacity; slot++)
                    {
                        int p = (int)own[slot] - 1, px = p % axis, py = p % (axis * axis) / axis, level = p / (axis * axis);
                        meta[p] = new uint4(flags, (uint)slot + 1, 0, 0);
                        mask[p] = phase == 0 ? new uint2(1u << (slot & 31), 0) : new uint2(0, 1u << (31 - (slot & 31)));
                        ulong bits = mask[p].x | ((ulong)mask[p].y << 32);
                        int offset = level * nodes;
                        for (int mip = 0, n = padded; n > 0; mip++, offset += n * n, n >>= 1)
                        {
                            int index = offset + (py >> mip) * n + (px >> mip);
                            expected[index].x |= flags;
                            // Independent cell-coordinate projection, not the shader's bit reduction.
                            for (int bit = 0; bit < 64; bit++)
                                if ((bits & (1UL << bit)) != 0)
                                {
                                    int x = ((px * 8 + bit % 8) >> mip) & 7, y = ((py * 8 + bit / 8) >> mip) & 7;
                                    if (y < 4) expected[index].y |= 1u << (y * 8 + x);
                                    else expected[index].z |= 1u << ((y - 4) * 8 + x);
                                }
                        }
                        for (int layer = 0; layer < 2; layer++)
                            if ((flags & (layer == 0 ? 4u : 32768u)) != 0)
                            {
                                int b = level * 2 + layer; var coord = new uint2((uint)px, (uint)py);
                                expectedBounds[b] = new uint4(math.min(expectedBounds[b].xy, coord), math.max(expectedBounds[b].zw, coord));
                            }
                    }
                    metadata.SetData(meta); masks.Requests.SetData(mask);
                    shader.Dispatch(build, (capacity + 63) / 64, 1, 1); hierarchy.GetData(actual);
                    masks.UncachedBounds.GetData(actualBounds, 0, 0, actualBounds.Length);
                    Assert.That(actual, Is.EqualTo(expected), "Flags and receiver coverage must propagate independently.");
                    Assert.That(actualBounds, Is.EqualTo(expectedBounds), "Bounds must be reduced before hierarchy early exit.");
                }
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [TestCase(1)]
        [TestCase(32)]
        [TestCase(33)]
        [TestCase(65)]
        [TestCase(1025)]
        public void LodHierarchy_PreservesRangesAndBounds_AndReusesStableGeometry(int count)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var source = new System.Collections.Generic.List<VividMeshLODNode>(count + 3);
            for (int i = 0; i < count + 3; i++) source.Add(new VividMeshLODNode
                { Bounds = new float4(i, i % 7, -i, .25f), LevelIndex = (uint)(i % 3) });
            var instances = new System.Collections.Generic.List<VividInstanceData>
                { new() { TopMeshLODStartIndex = 3, TotalMeshLODCount = (uint)count, MeshLODLevelCount = 3 } };
            using var hierarchy = new VirtualShadowMapLODHierarchy();
            hierarchy.Update(source, instances, true);
            var roots = new uint[source.Count]; hierarchy.Roots.GetData(roots);
            int root = (int)roots[3], end = (int)hierarchy.CpuNodes[root].Range.z;
            var covered = new int[count];
            for (int index = root; index < end; index++)
            {
                var node = hierarchy.CpuNodes[index];
                Assert.That(node.Range.z, Is.GreaterThan((uint)index).And.LessThanOrEqualTo((uint)end));
                for (uint n = node.Range.x; n < node.Range.x + node.Range.y; n++)
                {
                    var child = source[(int)n];
                    Assert.That(math.all(node.Min.xyz <= child.Bounds.xyz - child.Bounds.w), Is.True);
                    Assert.That(math.all(node.Max.xyz >= child.Bounds.xyz + child.Bounds.w), Is.True);
                    Assert.That(child.LevelIndex >= node.Min.w && child.LevelIndex <= node.Max.w, Is.True);
                }
                if (node.Range.w == 0) continue;
                Assert.That(node.Range.y, Is.InRange(1u, 32u));
                Assert.That((node.Range.x - 3u) % 32u, Is.Zero);
                for (uint n = node.Range.x; n < node.Range.x + node.Range.y; n++) covered[n - 3]++;
            }
            foreach (int visits in covered) Assert.That(visits, Is.EqualTo(1));
            var nodeBuffer = hierarchy.Nodes; var rootBuffer = hierarchy.Roots;
            for (int i = 0; i < 32; i++) hierarchy.Update(source, instances, false);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1024; i++) hierarchy.Update(source, instances, false);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero); Assert.That(hierarchy.Nodes, Is.SameAs(nodeBuffer));
            Assert.That(hierarchy.Roots, Is.SameAs(rootBuffer));
            // Adding a previously unused range must work without a geometry upload.
            instances.Add(new VividInstanceData { TopMeshLODStartIndex = 0, TotalMeshLODCount = 3, MeshLODLevelCount = 3 });
            hierarchy.Update(source, instances, false); hierarchy.Roots.GetData(roots);
            Assert.That(roots[0], Is.Not.EqualTo(uint.MaxValue));
            var changed = source[3]; changed.Bounds = new float4(-1000, 0, 0, 1); source[3] = changed;
            hierarchy.Update(source, instances, true); hierarchy.Roots.GetData(roots);
            Assert.That(hierarchy.CpuNodes[(int)roots[3]].Min.x, Is.LessThanOrEqualTo(-1001));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AffineProjection_ContainsTransformedBoundSamples(bool sphere)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.compute"));
            try
            {
                using var inputs = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 16);
                using var extents = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 16);
                using var projection = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 160);
                using var rects = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 16);
                using var results = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 8);
                Vector3 center = new(.1f, -.2f, .05f), extent = new(.08f, .025f, .04f);
                Matrix4x4 matrix = Matrix4x4.TRS(new Vector3(.5f, .5f, 0), Quaternion.Euler(15, 25, 35), new Vector3(-2, .4f, 1.7f));
                matrix.m01 += .7f; // Shear must not use a max-axis world sphere.
                inputs.SetData(new[] { new Vector4(center.x, center.y, center.z, .08f) });
                extents.SetData(new[] { new Vector4(extent.x, extent.y, extent.z, sphere ? 1 : 0) });
                projection.SetData(new[] { new VirtualShadowMapProjection { WorldToShadow = matrix } });
                int k = shader.FindKernel("InspectVSMAffineBounds");
                shader.SetInt("_SamplingCount", 1); shader.SetInt("_VSMPrototypeVirtualResolution", 4096);
                shader.SetBuffer(k, "_SamplingInputs", inputs); shader.SetBuffer(k, "_SamplingNormals", extents);
                shader.SetBuffer(k, "_VSMProjections", projection); shader.SetBuffer(k, "_SamplingClippedRects", rects);
                shader.SetBuffer(k, "_SamplingResults", results); shader.Dispatch(k, 1, 1, 1);
                var actual = new uint4[1]; var valid = new Vector2[1]; rects.GetData(actual); results.GetData(valid);
                Assert.That(valid[0].x, Is.EqualTo(1));
                for (int i = 0; i < (sphere ? 256 : 8); i++)
                {
                    Vector3 point;
                    if (sphere)
                    {
                        double z = 1 - 2 * (i + .5) / 256, angle = i * 2.399963229728653;
                        double radius = Math.Sqrt(1 - z * z);
                        point = center + .08f * new Vector3((float)(radius * Math.Cos(angle)), (float)(radius * Math.Sin(angle)), (float)z);
                    }
                    else point = center + Vector3.Scale(extent, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 uv = matrix.MultiplyPoint3x4(point);
                    if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) continue;
                    uint x = (uint)Math.Min(4095, Math.Floor(uv.x * 4096)), y = (uint)Math.Min(4095, Math.Floor(uv.y * 4096));
                    Assert.That(x, Is.InRange(actual[0].x, actual[0].z)); Assert.That(y, Is.InRange(actual[0].y, actual[0].w));
                }
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [TestCase(0, false, true)]
        [TestCase(0, true, true)]
        [TestCase(1, false, false)]
        [TestCase(1, true, true)]
        public void EarlyInstanceCulling_UsesSelectedLayerAndMask_BeforeCreatingJobs(int layer, bool fullMask, bool expected)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GPUDriven/GPUInstanceCulling.compute"));
            using var projections = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 160);
            using var hierarchy = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 12);
            using var bounds = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, 16);
            using var instances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1,
                System.Runtime.InteropServices.Marshal.SizeOf<VividInstanceData>());
            using var contexts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 288);
            using var indices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4);
            using var jobs = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 16);
            using var counts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4);
            using var args = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, 4);
            using var cmd = new CommandBuffer();
            try
            {
                projections.SetData(new[] { new VirtualShadowMapProjection { WorldToShadow = Matrix4x4.identity } });
                hierarchy.SetData(new[] { new uint3(4u | 32768u, fullMask ? uint.MaxValue : 0u, fullMask ? uint.MaxValue : 0u) });
                bounds.SetData(new uint4[2]);
                instances.SetData(new[] { new VividInstanceData { ObjectToWorldMatrix = float4x4.identity,
                    AABBMin = new float4(.4f, .4f, 0, 0), AABBMax = new float4(.6f, .6f, 0, 0),
                    PassMask = VividInstancePassMask.Shadows, TotalMeshLODCount = 1 } });
                // Shader ABI: two matrices, camera, six planes, light sphere, eight scalar words.
                var context = new uint[72]; context[64] = (uint)VividInstancePassMask.Shadows; contexts.SetData(context);
                indices.SetData(new uint[1]);
                int kernel = shader.FindKernel("CSVSM");
                shader.SetInt("_InstanceDataCount", 1); shader.SetInt("_CullingContextCount", 1);
                shader.SetInt("_VividPrimitiveDrawSetEnabled", 0);
                shader.SetBuffer(kernel, "_CullingContexts", contexts); shader.SetBuffer(kernel, "_InstanceData", instances);
                shader.SetBuffer(kernel, "_VividPrimitiveDrawSetInstanceIndices", indices);
                shader.SetBuffer(kernel, "_MeshletListBuildJobs", jobs); shader.SetBuffer(kernel, "_MeshletListBuildJobCounter", counts);
                shader.SetBuffer(kernel, "_MeshletListBuildIndirectArgs", args);
                var parameters = new VirtualShadowMapCullingParameters(projections, hierarchy, bounds, 1, 128, 128, layer, true);
                var result = new uint[1];
                for (int empty = 0; empty < 2; empty++)
                {
                    counts.SetData(new uint[1]); args.SetData(new uint[3]);
                    if (empty != 0) bounds.SetData(new[] { new uint4(1, 1, 0, 0), new uint4(1, 1, 0, 0) });
                    cmd.Clear(); parameters.Bind(cmd, shader, kernel); cmd.DispatchCompute(shader, kernel, 1, 1, 1);
                    Graphics.ExecuteCommandBuffer(cmd); counts.GetData(result);
                    Assert.That(result[0], Is.EqualTo(empty == 0 && expected ? 1u : 0u));
                }
                // Stable command recording must neither allocate nor replace the borrowed resources.
                for (int i = 0; i < 16; i++) { cmd.Clear(); parameters.Bind(cmd, shader, kernel); }
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 256; i++) { cmd.Clear(); parameters.Bind(cmd, shader, kernel); }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.Zero);
                Assert.That(default(VirtualShadowMapCullingParameters).IsEnabled, Is.False);
            }
            finally { Object.DestroyImmediate(shader); }
        }

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
                var expectedBounds = new uint4[4];
                for (int i = 0; i < expectedBounds.Length; i++) expectedBounds[i] = new uint4((uint)axis, (uint)axis, 0, 0);
                for (int slot = 0; slot < capacity; slot++)
                {
                    int p = (int)own[slot] - 1, px = p % axis, py = p % (axis * axis) / axis, level = p / (axis * axis);
                    uint flags = 2u | ((meta[p].x & 131072u) == 0 ? meta[p].x & (4u | 32768u) : 0u);
                    for (int layer = 0; layer < 2; layer++)
                        if ((flags & (layer == 0 ? 4u : 32768u)) != 0)
                        {
                            int b = level * 2 + layer;
                            expectedBounds[b] = new uint4(math.min(expectedBounds[b].xy, new uint2((uint)px, (uint)py)),
                                math.max(expectedBounds[b].zw, new uint2((uint)px, (uint)py)));
                        }
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
                var actualBounds = new uint4[4]; masks.UncachedBounds.GetData(actualBounds, 0, 0, 4);
                Assert.That(actualBounds, Is.EqualTo(expectedBounds), "Only selected work belongs in uncached bounds.");
                var rects = new Vector4[128]; var clipmaps = new Vector4[128]; var result = new Vector2[128];
                for (int i = 0; i < rects.Length; i++)
                {
                    int x = random.Next(axis * 128), y = random.Next(axis * 128);
                    rects[i] = new Vector4(x, y, Math.Min(axis * 128 - 1, x + random.Next(256)), Math.Min(axis * 128 - 1, y + random.Next(256)));
                    clipmaps[i].x = i & 1;
                }
                rects[0] = new Vector4(0, 0, axis * 128 - 1, axis * 128 - 1);
                input.SetData(rects); levels.SetData(clipmaps);
                using var clipped = new GraphicsBuffer(GraphicsBuffer.Target.Structured, rects.Length * 2, 16);
                var clippedData = new uint4[rects.Length * 2];
                int clip = shader.FindKernel("InspectVSMUncachedRect");
                shader.SetInt("_VSMUncachedPageRectBoundsEnabled", 1);
                shader.SetBuffer(clip, "_VSMUncachedPageRectBounds", masks.UncachedBounds);
                shader.SetBuffer(clip, "_SamplingInputs", input); shader.SetBuffer(clip, "_SamplingNormals", levels);
                shader.SetBuffer(clip, "_SamplingResults", results); shader.SetBuffer(clip, "_SamplingClippedRects", clipped);
                shader.SetBuffer(inspect, "_VSMPageCullHierarchy", hierarchy);
                shader.SetBuffer(inspect, "_SamplingInputs", input); shader.SetBuffer(inspect, "_SamplingNormals", levels);
                shader.SetBuffer(inspect, "_SamplingResults", results); shader.SetInt("_SamplingCount", 128);
                for (int layer = 0; layer < 2; layer++)
                {
                    shader.SetInt("_VSMPrototypeCasterLayer", layer); shader.Dispatch(inspect, 2, 1, 1); results.GetData(result);
                    foreach (var pair in result) Assert.That(pair.x >= pair.y, Is.True, "Hierarchy rejected required work.");
                    shader.Dispatch(clip, 2, 1, 1); results.GetData(result); clipped.GetData(clippedData);
                    for (int i = 0; i < rects.Length; i++)
                    {
                        var b = expectedBounds[(i & 1) * 2 + layer];
                        uint x0 = Math.Max((uint)rects[i].x, b.x * 128u), y0 = Math.Max((uint)rects[i].y, b.y * 128u);
                        uint x1 = Math.Min((uint)rects[i].z, (b.z + 1u) * 128u - 1u);
                        uint y1 = Math.Min((uint)rects[i].w, (b.w + 1u) * 128u - 1u);
                        bool overlaps = x0 <= x1 && y0 <= y1;
                        Assert.That(result[i].x, Is.EqualTo(overlaps ? 1f : 0f));
                        if (!overlaps) continue;
                        Assert.That(clippedData[i * 2 + 1], Is.EqualTo(new uint4(x0, y0, x1, y1)));
                        Assert.That(clippedData[i * 2], Is.EqualTo(new uint4(x0 / 128u, y0 / 128u, x1 / 128u, y1 / 128u)));
                    }
                }
                // The next frame must not retain any prior mask or flags, including padding/root.
                owners.SetData(new uint[capacity]);
                shader.Dispatch(clear, (nodes * 2 + 63) / 64, 1, 1);
                shader.Dispatch(build, (capacity + 63) / 64, 1, 1); hierarchy.GetData(actual);
                Assert.That(actual, Is.EqualTo(new uint3[nodes * 2]));
                masks.UncachedBounds.GetData(actualBounds, 0, 0, 4);
                foreach (var bounds in actualBounds) Assert.That(bounds, Is.EqualTo(new uint4((uint)axis, (uint)axis, 0, 0)));
            }
            finally { Object.DestroyImmediate(shader); }
        }
    }
}
