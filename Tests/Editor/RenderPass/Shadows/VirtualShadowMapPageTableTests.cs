using System;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime.VirtualShadowMap;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class VirtualShadowMapPageTableTests
    {
        private const string Production = "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute";
        private const string Sampling = "Packages/com.vivid.render-pipelines/Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.compute";

        [SetUp]
        public void RequireGPU() => Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);

        [TestCase(false)]
        [TestCase(true)]
        public void HZB_RecreatesLostNativeTexture_BeforeRenderGraphImport(bool destroyTexture)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(Production);
            Assert.That(shader, Is.Not.Null);
            try
            {
                Assert.That(VirtualShadowMapPrototypeRuntime.EnsureResources(128, 1, 16), Is.True);
                VirtualShadowMapPrototypeRuntime.Projections.EnsureCapacity(1);
                VirtualShadowMapHZB.EnsureResources(shader);
                var previous = VirtualShadowMapHZB.Texture;
                var texture = previous.rt;
                Assert.That(texture.hideFlags, Is.EqualTo(HideFlags.HideAndDontSave));
                Assert.That(texture.mipmapCount, Is.EqualTo(7));
                Assert.That(texture.volumeDepth, Is.EqualTo(2));

                if (destroyTexture)
                    Object.DestroyImmediate(texture);
                else
                    texture.Release();

                VirtualShadowMapHZB.EnsureResources(shader);
                var recreated = VirtualShadowMapHZB.Texture;
                Assert.That(recreated, Is.Not.SameAs(previous));
                Assert.That(recreated.rt != null && recreated.rt.IsCreated(), Is.True);
                var graph = new UnityEngine.Rendering.RenderGraphModule.RenderGraph("HZBImportRegression");
                try
                {
                    Assert.That(graph.ImportTexture(recreated).IsValid(), Is.True);
                }
                finally
                {
                    graph.Cleanup();
                }
            }
            finally
            {
                VirtualShadowMapPrototypeRuntime.ReleaseResources();
            }
        }

        [Test]
        public void HZB_StableResourcesReuseTexture_WithoutManagedAllocations()
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(Production);
            Assert.That(shader, Is.Not.Null);
            try
            {
                Assert.That(VirtualShadowMapPrototypeRuntime.EnsureResources(128, 1, 16), Is.True);
                VirtualShadowMapPrototypeRuntime.Projections.EnsureCapacity(1);
                for (int i = 0; i < 32; ++i)
                    VirtualShadowMapHZB.EnsureResources(shader);
                var texture = VirtualShadowMapHZB.Texture;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 256; ++i)
                    VirtualShadowMapHZB.EnsureResources(shader);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.Zero);
                Assert.That(VirtualShadowMapHZB.Texture, Is.SameAs(texture));
            }
            finally
            {
                VirtualShadowMapPrototypeRuntime.ReleaseResources();
            }
        }

        [Test]
        public void PackedEntry_MatchesUEBitsIncludingPhysicalZeroAndMaximumFields()
        {
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(Sampling));
            var values = new[] { new uint4(0, 0, 0, 0), new uint4(0, 0, 0, 1),
                new uint4(1023, 1023, 63, 0), new uint4(31, 17, 0, 1),
                new uint4(0, 1023, 24, 0), new uint4(0, 0, 0, 2) };
            using var input = new GraphicsBuffer(GraphicsBuffer.Target.Structured, values.Length, 16);
            using var output = new GraphicsBuffer(GraphicsBuffer.Target.Structured, values.Length, 16);
            try
            {
                input.SetData(values);
                int kernel = shader.FindKernel("InspectPackedPageTable");
                shader.SetInt("_PackedPageTableCount", values.Length);
                shader.SetBuffer(kernel, "_PackedPageTableInputs", input);
                shader.SetBuffer(kernel, "_PackedPageTableResults", output);
                shader.Dispatch(kernel, 1, 1, 1);
                var actual = new uint4[values.Length]; output.GetData(actual);
                for (int i = 0; i < values.Length; i++)
                {
                    uint4 v = values[i];
                    uint expected = v.w == 2 ? 0 : 0x80000000u | (v.z << 20) | (v.y << 10) | v.x
                        | (v.z == 0 && v.w == 1 ? 0x40000000u : 0u);
                    uint flags = v.w == 2 ? 0 : 4u | (v.z == 0 ? 1u : 0u) | (v.z == 0 && v.w == 1 ? 2u : 0u);
                    Assert.That(actual[i], Is.EqualTo(new uint4(expected, flags, v.y * 1024 + v.x, v.z)));
                }
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [TestCase(6, 8)]
        [TestCase(24, 4)]
        public void PackedEntry_InPlacePropagationAndSamplingMatchNativeOnlyOracle(int levels, int axis)
        {
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(Production));
            var sampler = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(Sampling));
            int perLevel = axis * axis, count = levels * perLevel;
            using var table = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 4);
            using var offsets = new GraphicsBuffer(GraphicsBuffer.Target.Structured, levels * levels, 8);
            using var inputs = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16);
            using var results = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16);
            try
            {
                var delta = new int2[levels * levels];
                for (int source = 0; source < levels; source++) for (int target = source; target < levels; target++)
                    delta[source * levels + target] = (new int2(target % 3 - 1, target % 2) << (target - source))
                        - new int2(source % 3 - 1, source % 2);
                offsets.SetData(delta);
                int propagate = shader.FindKernel("VSMPropagateMappedClipmaps");
                shader.SetInt("_VSMPrototypePageTableEntryCount", count);
                shader.SetInt("_VSMPrototypePagesPerAxis", axis); shader.SetInt("_VSMProjectionCount", levels);
                shader.SetBuffer(propagate, "_VSMPrototypeWritablePageTable", table);
                shader.SetBuffer(propagate, "_VSMClipmapPageOffsets", offsets);
                int sample = sampler.FindKernel("InspectUEMappedTexel");
                sampler.SetInt("_SamplingCount", count); sampler.SetInt("_VSMPrototypeEnabled", 1);
                sampler.SetInt("_VSMPrototypePageSize", 128); sampler.SetInt("_VSMPrototypePagesPerAxis", axis);
                sampler.SetInt("_VSMPrototypeVirtualResolution", axis * 128); sampler.SetInt("_VSMProjectionCount", levels);
                sampler.SetBuffer(sample, "_VSMPrototypePageTable", table); sampler.SetBuffer(sample, "_VSMClipmapPageOffsets", offsets);
                sampler.SetBuffer(sample, "_SamplingInputs", inputs); sampler.SetBuffer(sample, "_UESMRTResults", results);
                var points = new float4[count];
                for (int i = 0; i < count; i++) points[i] = new float4((i % axis + .5f) / axis, (i % perLevel / axis + .5f) / axis, i / perLevel, 0);
                inputs.SetData(points);
                var random = new System.Random(715);
                for (int frame = 0; frame < 5; frame++)
                {
                    var entries = new uint[count];
                    for (int i = 0; i < count; i++) entries[i] = random.Next(4) == 0
                        ? VirtualShadowMapPageTableTestData.EncodeSlot((uint)random.Next(1, 17), renderable: (i & 1) != 0)
                        : (i % 3 == 0 ? 0x8010000fu : 0u); // Stale aliases must be overwritten, never used as sources.
                    var expected = new uint[count]; var targetPage = new int2[count];
                    for (int i = 0; i < count; i++)
                    {
                        int source = i / perLevel;
                        var page = new int2(i % axis, i % perLevel / axis);
                        for (int level = source; level < levels; level++)
                        {
                            int offset = level - source;
                            int2 parent = (page + delta[source * levels + level]) >> offset;
                            if (math.any(parent < 0) || math.any(parent >= axis)) continue;
                            uint native = entries[level * perLevel + parent.y * axis + parent.x];
                            if ((native & 0x83f00000u) != 0x80000000u) continue;
                            expected[i] = offset == 0 ? native : 0x80000000u | ((uint)offset << 20) | (native & 0xfffffu);
                            targetPage[i] = parent;
                            break;
                        }
                    }
                    table.SetData(entries);
                    shader.Dispatch(propagate, (count + 63) / 64, 1, 1);
                    var actual = new uint[count]; table.GetData(actual);
                    Assert.That(actual, Is.EqualTo(expected), "Native-only propagation, frame " + frame);
                    // No metadata buffer is bound to either kernel.
                    sampler.Dispatch(sample, (count + 63) / 64, 1, 1);
                    var samples = new float4[count]; results.GetData(samples);
                    for (int i = 0; i < count; i++)
                    {
                        Assert.That(samples[i].x, Is.EqualTo(expected[i] == 0 ? 0 : 1));
                        if (expected[i] == 0) continue;
                        int offset = (int)(expected[i] >> 20) & 63, source = i / perLevel;
                        float scale = 1f / (1 << offset);
                        float2 uv = points[i].xy * scale + (float2)delta[source * levels + source + offset] * (scale / axis);
                        int2 texel = math.clamp((int2)(uv * (axis * 128)), targetPage[i] * 128, targetPage[i] * 128 + 127);
                        int2 physical = new int2((int)(expected[i] & 1023u), (int)((expected[i] >> 10) & 1023u)) * 128 + texel % 128;
                        Assert.That(samples[i].yzw, Is.EqualTo(new float3(source + offset, physical.x, physical.y)), "Sample " + i);
                    }
                }
            }
            finally { Object.DestroyImmediate(shader); Object.DestroyImmediate(sampler); }
        }

        [Test]
        public void PackedEntry_WorkListsRestoreRenderValidityWhenCachedPagesBecomeDirty()
        {
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(Production));
            using var table = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 4);
            using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 16);
            using var owners = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 4);
            using var requests = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 4);
            using var work = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, 4);
            using var args = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments, VirtualShadowMapPrototypeRuntime.PageWorkArgsWordCount, 4);
            try
            {
                var state = new[] { new uint4(10, 1, 2, 8), new uint4(6, 2, 2, 4), new uint4(6, 3, 2, 4), uint4.zero };
                metadata.SetData(state); owners.SetData(new uint[] { 1, 2, 3, 0 });
                requests.SetData(new uint[] { 1, 1, 0, 0 });
                table.SetData(new uint[] { 0x80000000, 0x80000001, 0x80000002, 0 });
                int reset = shader.FindKernel("VSMResetPageWorkListsUE"), build = shader.FindKernel("VSMBuildPageWorkListsUE");
                shader.SetInt("_VSMPrototypePhysicalPageCapacity", 4); shader.SetInt("_VSMPrototypePhysicalPagesPerRow", 4);
                shader.SetBuffer(reset, "_VSMPageWorkDispatchArgsRW", args);
                shader.SetBuffer(build, "_VSMPrototypeWritablePageTable", table);
                shader.SetBuffer(build, "_VSMPrototypePageMetadata", metadata);
                shader.SetBuffer(build, "_VSMPrototypePhysicalPageOwners", owners);
                shader.SetBuffer(build, "_VSMPageRequestFlags", requests);
                shader.SetBuffer(build, "_VSMPageWorkListRW", work);
                shader.SetBuffer(build, "_VSMPageWorkDispatchArgsRW", args);
                var actual = new uint[4];
                for (int frame = 0; frame < 2; frame++)
                {
                    shader.Dispatch(reset, 1, 1, 1); shader.Dispatch(build, 1, 1, 1);
                    table.GetData(actual);
                    Assert.That(actual, Is.EqualTo(new uint[] { frame == 0 ? 0x80000000u : 0xc0000000u, 0xc0000001, 0, 0 }));
                    metadata.GetData(state);
                    Assert.That(state[2].y, Is.EqualTo(3u), "Deferred ownership remains resident");
                    Assert.That(state[2].x & 131072u, Is.Not.Zero);
                    // Invalidate an existing cached page without allocating/remapping it.
                    state[0].x = 6; metadata.SetData(state);
                }
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(63)]
        [TestCase(64)]
        [TestCase(65)]
        [TestCase(1025)]
        public void SparseFinalize_UsesGPUWorkCountAndPreservesUntouchedVirtualPages(int dirtyCount)
        {
            const int pages = 17 * 64 * 64;
            int capacity = dirtyCount + 2;
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(Production));
            using var masks = new VirtualShadowMapReceiverMaskTestBuffers(shader, pages, capacity);
            using var table = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pages, 4);
            using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pages, 16);
            using var owners = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 4);
            using var requests = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pages, 4);
            using var work = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity * 2, 4);
            using var args = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments,
                VirtualShadowMapPrototypeRuntime.PageWorkArgsWordCount, 4);
            try
            {
                var state = new uint4[pages]; var entries = new uint[pages]; var demand = new uint[pages];
                var owner = new uint[capacity];
                for (int page = 0; page < pages; page++)
                {
                    state[page] = new uint4(0, 0, (uint)page, 64);
                    entries[page] = 0x80100007; // Alias cleanup belongs to propagation.
                }
                for (int slot = 0; slot < capacity; slot++)
                {
                    int page = pages - 1 - slot * 7;
                    owner[slot] = (uint)page + 1u;
                    // One clean cached owner and one dirty, unrequested owner.
                    state[page] = new uint4(slot == dirtyCount ? 10u : 6u, (uint)slot + 1u, 3, 0);
                    demand[page] = slot < dirtyCount ? 1u : 0u;
                    entries[page] = VirtualShadowMapPageTableTestData.EncodeSlot((uint)slot + 1u, 128, false);
                }
                table.SetData(entries); metadata.SetData(state); owners.SetData(owner); requests.SetData(demand);
                int reset = shader.FindKernel("VSMResetPageWorkListsUE"), build = shader.FindKernel("VSMBuildPageWorkListsUE");
                int finalize = shader.FindKernel("VSMPrototypeFinalizeDirtyPages");
                shader.SetInt("_VSMPrototypePhysicalPageCapacity", capacity);
                shader.SetInt("_VSMPrototypePageTableEntryCount", pages);
                shader.SetInt("_VSMPrototypePhysicalPagesPerRow", 128); shader.SetInt("_VSMPrototypePageSize", 128);
                shader.SetBuffer(reset, "_VSMPageWorkDispatchArgsRW", args);
                shader.SetBuffer(build, "_VSMPageWorkDispatchArgsRW", args);
                shader.SetBuffer(build, "_VSMPageWorkListRW", work);
                shader.SetBuffer(build, "_VSMPrototypeWritablePageTable", table);
                shader.SetBuffer(build, "_VSMPrototypePageMetadata", metadata);
                shader.SetBuffer(build, "_VSMPrototypePhysicalPageOwners", owners);
                shader.SetBuffer(build, "_VSMPageRequestFlags", requests);
                shader.SetBuffer(finalize, "_VSMPrototypeWritablePageTable", table);
                shader.SetBuffer(finalize, "_VSMPrototypePageMetadata", metadata);
                var dispatch = new uint[VirtualShadowMapPrototypeRuntime.PageWorkArgsWordCount];
                // The second frame reuses completed pages and must issue zero groups.
                for (int frame = 0; frame < 2; frame++)
                {
                    shader.Dispatch(reset, 1, 1, 1); shader.Dispatch(build, (capacity + 63) / 64, 1, 1);
                    args.GetData(dispatch); metadata.GetData(state); table.GetData(entries);
                    int completed = frame == 0 ? dirtyCount : 0;
                    Assert.That(dispatch[2], Is.EqualTo(completed));
                    Assert.That(dispatch[6], Is.EqualTo((completed + 63) / 64));
                    Assert.That(dispatch[7], Is.EqualTo(1u)); Assert.That(dispatch[8], Is.EqualTo(1u));
                    var expectedState = (uint4[])state.Clone(); var expectedEntries = (uint[])entries.Clone();
                    for (int slot = 0; slot < completed; slot++)
                    {
                        int page = (int)owner[slot] - 1;
                        expectedState[page].x = 10u;
                        expectedState[page].w = 4u | 32768u;
                        expectedEntries[page] = VirtualShadowMapPageTableTestData.EncodeSlot((uint)slot + 1u, 128, false);
                    }
                    VirtualShadowMapPageTableTestData.DispatchFinalize(shader, finalize, work, args, owners);
                    metadata.GetData(state); table.GetData(entries);
                    Assert.That(state, Is.EqualTo(expectedState), "Only completed owners may change cache state");
                    Assert.That(entries, Is.EqualTo(expectedEntries), "No finalization writes to other virtual entries");
                }
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [Test]
        public void PackedEntry_FinalizeTouchesOnlyWorkAndPropagationReplacesStaleAliases()
        {
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(Production));
            using var masks = new VirtualShadowMapReceiverMaskTestBuffers(shader, 4, 4);
            using var table = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 4);
            using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 16);
            try
            {
                var state = new[] { new uint4(10, 1, 2, 8), new uint4(6, 2, 2, 4),
                    new uint4(2u | 4u | 131072u, 3, 2, 4), uint4.zero };
                metadata.SetData(state); table.SetData(new uint[] { 0x80000000, 0xc0000001, 0x8010000f, 0x8010000f });
                int kernel = shader.FindKernel("VSMPrototypeFinalizeDirtyPages");
                shader.SetInt("_VSMPrototypePageTableEntryCount", 4); shader.SetInt("_VSMPrototypePhysicalPagesPerRow", 4);
                shader.SetBuffer(kernel, "_VSMPrototypePageMetadata", metadata); shader.SetBuffer(kernel, "_VSMPrototypeWritablePageTable", table);
                VirtualShadowMapPageTableTestData.FinalizeResidentFixture(shader, kernel, metadata, 4);
                var actual = new uint[4]; table.GetData(actual);
                Assert.That(actual, Is.EqualTo(new uint[] { 0x80000000, 0x80000001, 0x8010000f, 0x8010000f }));
                using var offsets = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 8);
                offsets.SetData(new[] { int2.zero });
                int propagate = shader.FindKernel("VSMPropagateMappedClipmaps");
                shader.SetInt("_VSMPrototypePagesPerAxis", 2); shader.SetInt("_VSMProjectionCount", 1);
                shader.SetBuffer(propagate, "_VSMClipmapPageOffsets", offsets);
                shader.SetBuffer(propagate, "_VSMPrototypeWritablePageTable", table);
                shader.Dispatch(propagate, 1, 1, 1); table.GetData(actual);
                Assert.That(actual, Is.EqualTo(new uint[] { 0x80000000, 0x80000001, 0, 0 }));
                var after = new uint4[4]; metadata.GetData(after);
                Assert.That(after[2], Is.EqualTo(state[2]), "Unproduced page remains resident and dirty");
                Assert.That(after[1].x, Is.EqualTo(10u));
            }
            finally { Object.DestroyImmediate(shader); }
        }
    }
}
