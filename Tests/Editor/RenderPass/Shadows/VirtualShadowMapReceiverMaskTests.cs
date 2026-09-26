using System;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime.VirtualShadowMap;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
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
            "VSMMarkCoarsePages",
            "VSMClearPageCullHierarchy",
            "VSMBuildPageCullHierarchy",
            "VSMPrototypeClearReceiverRequests",
            "VSMPrototypeResetReceiverFeedback",
            "VSMPrototypePrepareAllocation",
            "VSMPrototypeAllocatePages",
            "VSMPrototypeFinalizeDirtyPages",
            "VSMPrototypeCullMeshletsToPages",
            "CSMShadowResolveTiles",
            "VSMReceiverCost",
            "VSMShadowResolveAdaptive",
            "TraceSMRTRays",
            "TraceSMRTClipmaps",
            "FilterSMRTFootprints",
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
                flags.SetData(new uint[] { 1, 1 });
                var data = new[] { new uint4(10, 1, 1, 0), new uint4(10, 2, 1, 0) };
                metadata.SetData(data);
                masks.Completed.SetData(new[] { new uint2(3, 0), new uint2(1, 0) });
                masks.Requests.SetData(new[] { new uint2(1, 0), new uint2(2, 0) });
                shader.Dispatch(prepare, 1, 1, 1); metadata.GetData(data);
                Assert.That(data[0].x, Is.EqualTo(10u), "A subset of completed coverage stays cached.");
                Assert.That(data[1].x & (4u | 32768u | 8u), Is.EqualTo(32768u));
                data[1].x |= 131072u; metadata.SetData(data);
                shader.Dispatch(finalize, 1, 1, 1);
                var coverage = new uint2[2]; masks.Completed.GetData(coverage);
                Assert.That(coverage[1], Is.EqualTo(new uint2(1, 0)));
                data[1].x &= ~131072u; metadata.SetData(data);
                shader.Dispatch(finalize, 1, 1, 1); masks.Completed.GetData(coverage);
                Assert.That(coverage[1], Is.EqualTo(new uint2(2, 0)), "Full dynamic clear requires replacement, not union.");
            }
            finally { Object.DestroyImmediate(shader); }
        }
    }
}
