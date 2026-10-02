using System;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime.VirtualShadowMap;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    internal static class VirtualShadowMapPageTableTestData
    {
        internal static void DispatchFinalize(ComputeShader shader, int kernel,
            GraphicsBuffer work, GraphicsBuffer args, GraphicsBuffer owners)
        {
            shader.SetBuffer(kernel, "_VSMPageWorkList", work);
            shader.SetBuffer(kernel, "_VSMPageWorkDispatchArgs", args);
            shader.SetBuffer(kernel, "_VSMPrototypePhysicalPageOwners", owners);
            shader.DispatchIndirect(kernel, args, VirtualShadowMapPrototypeRuntime.FinalizeWorkArgsOffset);
        }

        // Metadata-only fixtures have no GPU list producer. Include even clean
        // and deferred owners so they still exercise the finalizer's guards.
        internal static void FinalizeResidentFixture(ComputeShader shader, int kernel, GraphicsBuffer metadata, int capacity)
        {
            var state = new uint4[metadata.count]; metadata.GetData(state);
            int count = 0;
            var ownerData = new uint[capacity]; var workData = new uint[capacity];
            for (int page = 0; page < state.Length; page++)
                if ((state[page].x & 2u) != 0 && state[page].y != 0)
                {
                    uint slot = state[page].y - 1u;
                    Assert.That(slot, Is.LessThan((uint)capacity));
                    ownerData[slot] = (uint)page + 1u; workData[count++] = slot;
                }
            using var owners = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 4);
            using var work = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 4);
            using var args = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments,
                VirtualShadowMapPrototypeRuntime.PageWorkArgsWordCount, 4);
            owners.SetData(ownerData); work.SetData(workData);
            args.SetData(new uint[] { 1, 1, (uint)count, 0, 1, 1, (uint)(count + 63) / 64, 1, 1 });
            shader.SetInt("_VSMPrototypePhysicalPageCapacity", capacity);
            shader.SetInt("_VSMPrototypePageTableEntryCount", metadata.count);
            DispatchFinalize(shader, kernel, work, args, owners);
        }

        internal static uint EncodeSlot(uint slotPlusOne, uint row = 4, bool renderable = true)
        {
            if (slotPlusOne == 0) return 0;
            uint slot = slotPlusOne - 1;
            return (renderable ? 0xc0000000u : 0x80000000u) | ((slot / row) << 10) | slot % row;
        }

        internal static void UploadSlots(ComputeShader shader, GraphicsBuffer table, uint[] slots, uint row = 4)
        {
            var entries = new uint[slots.Length];
            for (int i = 0; i < slots.Length; i++) entries[i] = EncodeSlot(slots[i], row);
            shader.SetInt("_VSMPrototypePhysicalPagesPerRow", (int)row);
            table.SetData(entries);
        }

        internal static void ReadSlots(GraphicsBuffer table, uint[] slots, uint row = 4)
        {
            table.GetData(slots);
            for (int i = 0; i < slots.Length; i++)
                slots[i] = (slots[i] & 0x83f00000u) == 0x80000000u
                    ? ((slots[i] >> 10) & 1023u) * row + (slots[i] & 1023u) + 1u : 0u;
        }
    }

    // Legacy cases explicitly test complete pages. Bind disabled, valid resources
    // rather than inheriting a live Editor compute asset's production settings.
    internal sealed class VirtualShadowMapReceiverMaskTestBuffers : IDisposable
    {
        internal readonly GraphicsBuffer Requests, Completed, UncachedBounds;
        private readonly GraphicsBuffer m_DisabledHierarchy;
        private static readonly string[] s_Kernels =
        {
            "CSMShadowResolve",
            "VSMReceiverDebug",
            "VSMMarkReceiverPages",
            "VSMMarkReceiverPagesGrouped",
            "VSMMarkReceiverPagesUE",
            "MarkUEReceiverInputs",
            "MarkUEPageInputs",
            "VSMMarkCoarsePages",
            "VSMClearPageCullHierarchy",
            "VSMBuildPageCullHierarchy",
            "VSMPrototypeClearReceiverRequests",
            "VSMPrototypeResetReceiverFeedback",
            "VSMPrototypePrepareAllocation",
            "VSMPrototypeAllocatePages",
            "VSMPrepareAllocationCached",
            "VSMAllocatePagesCached",
            "VSMPrototypeFinalizeDirtyPages",
            "VSMPrototypeCullMeshletsToPages",
            "CSMShadowResolveTiles",
            "VSMReceiverCost",
            "VSMShadowResolveAdaptive",
            "TraceSMRTRays",
            "TraceSMRTClipmaps",
            "FilterSMRTFootprints",
            "FilterSMRTFootprintsCached",
            "SampleTaps",
            "ResolveReceivers",
            "MarkFootprints",
            "MarkCoalescedFootprints",
            "FilterFootprints",
            "ResolveScreenReceivers",
            "InspectReceiverDiagnostics",
            "InspectReceiverFootprint",
            "FilterSMRTAdaptive",
            "MarkReceiverInputs",
            "MarkReceiverInputsGrouped",
            "MarkPageWrites",
            "MarkPageWritesGrouped",
            "MarkFootprintPairs",
            "ResolveOnlyReceivers",
        };

        internal VirtualShadowMapReceiverMaskTestBuffers(ComputeShader shader, int pages = 1, int slots = 1, bool enabled = false)
        {
            Requests = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pages, 8);
            Completed = new GraphicsBuffer(GraphicsBuffer.Target.Structured, slots, 8);
            m_DisabledHierarchy = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 12);
            UncachedBounds = new GraphicsBuffer(GraphicsBuffer.Target.Structured, VirtualShadowMapClipmapLayout.MaxLevels * 2, 16);
            shader.SetInt("_VSMUncachedPageRectBoundsEnabled", 0);
            shader.SetInt("_VSMPageCullHierarchyEnabled", 0);
            Requests.SetData(new uint2[pages]); Completed.SetData(new uint2[slots]);
            shader.SetInt("_VSMReceiverMaskEnabled", enabled ? 1 : 0);
            foreach (string name in s_Kernels)
            {
                if (!shader.HasKernel(name)) continue;
                int kernel = shader.FindKernel(name);
                shader.SetBuffer(kernel, "_VSMPageReceiverMasks", Requests);
                shader.SetBuffer(kernel, "_VSMPhysicalReceiverMasks", Completed);
                if (name == "VSMClearPageCullHierarchy" || name == "VSMBuildPageCullHierarchy")
                    shader.SetBuffer(kernel, "_VSMUncachedPageRectBoundsRW", UncachedBounds);
                if (name == "VSMPrototypeCullMeshletsToPages")
                {
                    shader.SetBuffer(kernel, "_VSMUncachedPageRectBounds", UncachedBounds);
                    shader.SetBuffer(kernel, "_VSMPageCullHierarchy", m_DisabledHierarchy);
                }
            }
        }

        public void Dispose() { Requests.Dispose(); Completed.Dispose(); m_DisabledHierarchy.Dispose(); UncachedBounds.Dispose(); }
    }

    public sealed class VirtualShadowMapReceiverMaskTests
    {
        [TestCase(0)]
        [TestCase(1)]
        public void UEReceiverMask_AcceptedCasterWritesOutsideMarkedCell(int layer)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Tests/Editor/RenderPass/Shadows/VirtualShadowMapDepthStorageTests.compute"));
            using var table = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4);
            using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 16);
            using var masks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 8);
            using var inputs = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 64, 16);
            using var results = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 64, 16);
            var pool = new RenderTexture(new RenderTextureDescriptor(8, 8)
            {
                graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_UInt,
                depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None,
                dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
                volumeDepth = 2, enableRandomWrite = true, msaaSamples = 1,
            });
            pool.Create();
            try
            {
                var points = new uint4[64];
                for (int i = 0; i < 64; i++) points[i] = new uint4((uint)i % 8, (uint)i / 8, 0, 0);
                inputs.SetData(points);
                // Only one of 64 cells is marked. This must not trim coverage.
                masks.SetData(new[] { new uint2(1, 0) });
                VirtualShadowMapPageTableTestData.UploadSlots(shader, table, new uint[] { 1 });
                int kernel = shader.FindKernel("ResolveReceiverMaskedCaster");
                shader.SetBuffer(kernel, "_VSMPrototypePageTable", table);
                shader.SetBuffer(kernel, "_VSMPrototypePageMetadata", metadata);
                shader.SetBuffer(kernel, "_VSMPageReceiverMasks", masks);
                shader.SetBuffer(kernel, "_TestDepthInputs", inputs);
                shader.SetBuffer(kernel, "_TestCasterResults", results);
                shader.SetTexture(kernel, "_VSMPrototypePhysicalPage", pool);
                int clear = shader.FindKernel("ClearReceiverMaskedCaster");
                shader.SetTexture(clear, "_VSMPrototypePhysicalPage", pool);
                shader.Dispatch(clear, 1, 1, 1);
                shader.SetInt("_TestDepthInputCount", 64);
                shader.SetInt("_VSMReceiverMaskEnabled", 1);
                shader.SetInt("_VSMPrototypePageSize", 8);
                shader.SetInt("_VSMPrototypeVirtualResolution", 8);
                shader.SetInt("_VSMPrototypePagesPerAxis", 1);
                shader.SetInt("_VSMPrototypePhysicalPagesPerRow", 1);
                shader.SetInt("_VSMPrototypeCasterLayer", layer);
                var actual = new uint4[64];
                uint dirty = layer == 0 ? 4u : 1u << 15;
                foreach (uint flags in new[] { 2u | dirty, 2u, 2u | dirty | (1u << 17) })
                {
                    metadata.SetData(new[] { new uint4(flags, 1, 0, 0) });
                    shader.Dispatch(kernel, 1, 1, 1);
                    results.GetData(actual);
                    uint expected = flags == (2u | dirty) ? 1u : 0u;
                    foreach (var value in actual) Assert.That(value.xyz, Is.EqualTo(new uint3(expected)));
                }
                var readback = UnityEngine.Rendering.AsyncGPUReadback.Request(pool, 0);
                readback.WaitForCompletion();
                Assert.That(readback.hasError, Is.False);
                // Static goes to slice 1, dynamic to final slice 0. Every covered
                // texel must be written, while the other slice stays untouched.
                for (int i = 0; i < 128; i++)
                    Assert.That(readback.GetData<uint>(i / 64)[i % 64],
                        Is.EqualTo(i / 64 == (layer == 0 ? 1 : 0) ? math.asuint(.75f) : 0u));
            }
            finally { pool.Release(); Object.DestroyImmediate(pool); Object.DestroyImmediate(shader); }
        }

        [TestCase(1, 31)]
        [TestCase(2, 64)]
        [TestCase(4, 193)]
        [TestCase(32, 65)]
        public void Marking_GroupedWritesPreservePerPageRolesAndMasks(int keyCount, int count)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.compute"));
            using var masks = new VirtualShadowMapReceiverMaskTestBuffers(shader, 256, 1, true);
            using var flags = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 256, 4);
            using var inputs = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 16);
            try
            {
                var values = new uint4[count]; var expectedFlags = new uint[256]; var expectedMask = new uint2[256];
                for (int i = 0; i < count; i++)
                {
                    uint page = (uint)((i % keyCount) * 3 + 17);
                    uint role = i % 7 == 1 ? 0u : 1u | (i % 4 == 0 ? 256u : i % 4 == 1 ? 512u : i % 4 == 2 ? 1024u : 2048u);
                    uint2 mask = new(i % 3 == 0 ? 0u : 1u << (i % 32), i % 3 == 1 ? 0u : 1u << ((i * 7) % 32));
                    values[i] = new uint4(page, role, mask.x, mask.y);
                    if (role == 0u) continue;
                    expectedFlags[page] |= role;
                    expectedMask[page] |= (role & 256u) != 0u ? new uint2(uint.MaxValue) : mask;
                }
                inputs.SetData(values);
                shader.SetInt("_SamplingCount", count); shader.SetInt("_VSMPrototypePagesPerAxis", 8);
                foreach (string name in new[] { "MarkPageWrites", "MarkPageWritesGrouped" })
                {
                    int kernel = shader.FindKernel(name);
                    flags.SetData(new uint[256]); masks.Requests.SetData(new uint2[256]);
                    shader.SetBuffer(kernel, "_PageWriteInputs", inputs);
                    shader.SetBuffer(kernel, "_VSMPageRequestFlags", flags);
                    shader.Dispatch(kernel, (count + 63) / 64, 1, 1);
                    var actualFlags = new uint[256]; var actualMask = new uint2[256];
                    flags.GetData(actualFlags); masks.Requests.GetData(actualMask);
                    Assert.That(actualFlags, Is.EqualTo(expectedFlags));
                    Assert.That(actualMask, Is.EqualTo(expectedMask));
                }
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [TestCase(0)]
        [TestCase(1)]
        public void Marking_PreservesGapsBetweenFootprints_AndCompletesTerminalMask(int level)
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Tests/Editor/RenderPass/Shadows/VirtualShadowMapSamplingTests.compute"));
            using var masks = new VirtualShadowMapReceiverMaskTestBuffers(shader, 8, 1, true);
            using var flags = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, 4);
            using var inputs = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 128, 16);
            using var normals = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 64, 16);
            try
            {
                var data = new float4[128]; var normalData = new float4[64];
                for (int i = 0; i < 64; i++)
                {
                    data[i * 2] = new float4(17.5f / 256, 17.5f / 256, 512, 0);
                    data[i * 2 + 1] = new float4(113.5f / 256, 113.5f / 256, 2048, 0);
                    normalData[i] = new float4(0, 0, level, 0);
                }
                inputs.SetData(data); normals.SetData(normalData); flags.SetData(new uint[8]);
                shader.SetInt("_VSMPrototypeRequestEnabled", 1);
                shader.SetInt("_VSMPrototypePageSize", 128);
                shader.SetInt("_VSMPrototypeVirtualResolution", 256);
                shader.SetInt("_VSMPrototypePagesPerAxis", 2);
                shader.SetInt("_VSMProjectionCount", 2);
                shader.SetInt("_SamplingCount", 64);
                int kernel = shader.FindKernel("MarkFootprintPairs");
                shader.SetBuffer(kernel, "_SamplingInputs", inputs);
                shader.SetBuffer(kernel, "_SamplingNormals", normals);
                shader.SetBuffer(kernel, "_VSMPageRequestFlags", flags);
                shader.Dispatch(kernel, 1, 1, 1);
                var coverage = new uint2[8]; var roles = new uint[8];
                masks.Requests.GetData(coverage); flags.GetData(roles);
                for (int page = 0; page < 8; page++)
                {
                    uint2 expected = page == level * 4 ? level == 0
                        ? new uint2(1u << 9, 1u << 31) : new uint2(uint.MaxValue) : uint2.zero;
                    Assert.That(coverage[page], Is.EqualTo(expected));
                    Assert.That(roles[page], Is.EqualTo(page == level * 4 ? 1u | 512u | 2048u | (level == 1 ? 256u : 0u) : 0u));
                }
            }
            finally { Object.DestroyImmediate(shader); }
        }

        [Test]
        public void ExpandedCoverage_InvalidatesOnlyDynamic_AndDeferredCannotPublish()
        {
            Assume.That(VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform(), Is.True);
            var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Packages/com.vivid.render-pipelines/Shaders/Core/Private/CSMShadowResolve.compute"));
            using var masks = new VirtualShadowMapReceiverMaskTestBuffers(shader, 2, 2, true);
            using var metadata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, 16);
            using var flags = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, 4);
            using var requests = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4);
            try
            {
                shader.SetInt("_VSMPrototypePageTableEntryCount", 2);
                shader.SetInt("_VSMPrototypePhysicalPageCapacity", 2);
                shader.SetInt("_VSMProjectionCount", 1);
                shader.SetInt("_VSMPrototypeFeedbackFrameIndex", 2);
                int prepare = shader.FindKernel("VSMPrototypePrepareAllocation"), finalize = shader.FindKernel("VSMPrototypeFinalizeDirtyPages");
                shader.SetBuffer(prepare, "_VSMPrototypePageMetadata", metadata);
                shader.SetBuffer(prepare, "_VSMPageRequestFlags", flags);
                shader.SetBuffer(prepare, "_VSMAllocationRequests", requests);
                shader.SetBuffer(finalize, "_VSMPrototypePageMetadata", metadata);
                using var publishedTable = new GraphicsBuffer(GraphicsBuffer.Target.Structured, metadata.count, 4);
                shader.SetInt("_VSMPrototypePhysicalPagesPerRow", 4);
                shader.SetBuffer(finalize, "_VSMPrototypeWritablePageTable", publishedTable);
                flags.SetData(new uint[] { 1, 1 });
                var data = new[] { new uint4(10, 1, 1, 0), new uint4(10, 2, 1, 0) };
                metadata.SetData(data);
                masks.Completed.SetData(new[] { new uint2(3, 0), new uint2(1, 0) });
                masks.Requests.SetData(new[] { new uint2(1, 0), new uint2(2, 0) });
                shader.Dispatch(prepare, 1, 1, 1); metadata.GetData(data);
                Assert.That(data[0].x, Is.EqualTo(10u), "A subset of completed coverage stays cached.");
                Assert.That(data[1].x & (4u | 32768u | 8u), Is.EqualTo(32768u));
                data[1].x |= 131072u; metadata.SetData(data);
                VirtualShadowMapPageTableTestData.FinalizeResidentFixture(shader, finalize, metadata, 2);
                var coverage = new uint2[2]; masks.Completed.GetData(coverage);
                Assert.That(coverage[1], Is.EqualTo(new uint2(1, 0)));
                data[1].x &= ~131072u; metadata.SetData(data);
                VirtualShadowMapPageTableTestData.FinalizeResidentFixture(shader, finalize, metadata, 2); masks.Completed.GetData(coverage);
                Assert.That(coverage[1], Is.EqualTo(new uint2(2, 0)), "Full dynamic clear requires replacement, not union.");
            }
            finally { Object.DestroyImmediate(shader); }
        }
    }
}
