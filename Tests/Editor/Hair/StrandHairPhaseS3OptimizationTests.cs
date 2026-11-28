using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class StrandHairPhaseS3OptimizationTests
    {
        private const string VisibilityComputePath =
            "Packages/com.vivid.render-pipelines/Shaders/Core/Private/Hair/StrandHairVisibility.compute";

        [Test]
        public void ScratchSlot_AllocatesPhaseS3SortAndCompactionBuffersAndReuses()
        {
            using var slot = new StrandHairModelScratchSlot();
            slot.EnsureCapacity(vertexCount: 256, segmentCount: 192);

            Assert.That(slot.SegmentSortBinBuffer, Is.Not.Null);
            Assert.That(slot.SegmentSortBinBuffer.count, Is.EqualTo(192));
            Assert.That(slot.SortedSegmentListBuffer, Is.Not.Null);
            Assert.That(slot.SortedSegmentListBuffer.count, Is.EqualTo(192));
            Assert.That(slot.TileHistogramBuffer, Is.Not.Null);
            Assert.That(slot.TileHistogramBuffer.count, Is.EqualTo(StrandHairAtlasContract.TileHistogramBufferUints));
            Assert.That(slot.VisibleVertexListBuffer, Is.Not.Null);
            Assert.That(slot.VisibleVertexListBuffer.count, Is.EqualTo(256));
            Assert.That(slot.IndirectShadeArgsBuffer, Is.Not.Null);
            Assert.That(slot.IndirectShadeArgsBuffer.count, Is.EqualTo(4));

            var sortBinRef = slot.SegmentSortBinBuffer;
            var sortedRef = slot.SortedSegmentListBuffer;
            var histRef = slot.TileHistogramBuffer;
            var vertListRef = slot.VisibleVertexListBuffer;
            var shadeArgsRef = slot.IndirectShadeArgsBuffer;

            slot.EnsureCapacity(vertexCount: 200, segmentCount: 150);

            Assert.That(slot.SegmentSortBinBuffer, Is.SameAs(sortBinRef));
            Assert.That(slot.SortedSegmentListBuffer, Is.SameAs(sortedRef));
            Assert.That(slot.TileHistogramBuffer, Is.SameAs(histRef));
            Assert.That(slot.VisibleVertexListBuffer, Is.SameAs(vertListRef));
            Assert.That(slot.IndirectShadeArgsBuffer, Is.SameAs(shadeArgsRef));
        }

        [Test]
        public void GpuPrefixSumSortBins_ProducesExactExclusiveOffsetsAcross1024Bins()
        {
            var cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(VisibilityComputePath);
            Assert.That(cs, Is.Not.Null, "StrandHairVisibility.compute must load.");

            int kPrefix = cs.FindKernel("KPrefixSumSortBins");
            Assert.That(kPrefix, Is.GreaterThanOrEqualTo(0));

            const int bins = StrandHairAtlasContract.MaxSortBins;
            var input = new uint[StrandHairAtlasContract.TileHistogramBufferUints];
            uint running = 0;
            var expectedOffsets = new uint[bins];
            for (int i = 0; i < bins; i++)
            {
                uint c = (uint)((i * 7 + 3) % 19);
                input[i] = c;
                expectedOffsets[i] = running;
                running += c;
            }

            using var histBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, input.Length, sizeof(uint));
            histBuf.SetData(input);

            var cmd = new CommandBuffer { name = "Test_PrefixSumSortBins" };
            try
            {
                cmd.SetComputeBufferParam(cs, kPrefix, "_HairTileHistogram", histBuf);
                cmd.DispatchCompute(cs, kPrefix, 1, 1, 1);
                Graphics.ExecuteCommandBuffer(cmd);
            }
            finally
            {
                cmd.Release();
            }

            var output = new uint[input.Length];
            histBuf.GetData(output);
            for (int i = 0; i < bins; i++)
            {
                Assert.That(output[bins + i], Is.EqualTo(expectedOffsets[i]),
                    $"Exclusive prefix sum mismatch at bin {i}.");
            }
        }

        [Test]
        public void GpuCompactVisibleVertices_ProducesStrictlyAscendingNaturalOrderAndArgs()
        {
            var cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(VisibilityComputePath);
            Assert.That(cs, Is.Not.Null);

            int kClear = cs.FindKernel("KClearCullArgs");
            int kCompact = cs.FindKernel("KCompactVisibleVertices");
            Assert.That(kClear, Is.GreaterThanOrEqualTo(0));
            Assert.That(kCompact, Is.GreaterThanOrEqualTo(0));

            const int vertexCount = 200;
            int maskUints = (vertexCount + 31) >> 5;
            var mask = new uint[maskUints];
            var expected = new System.Collections.Generic.List<uint>();
            for (int v = 0; v < vertexCount; v++)
            {
                if ((v % 3 == 0) || (v % 11 == 0))
                {
                    mask[v >> 5] |= 1u << (v & 31);
                    expected.Add((uint)v);
                }
            }

            using var maskBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maskUints, sizeof(uint));
            using var listBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vertexCount, sizeof(uint));
            using var rasterArgsBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments, 8, sizeof(uint));
            using var shadeArgsBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments, 4, sizeof(uint));
            maskBuf.SetData(mask);

            var cmd = new CommandBuffer { name = "Test_CompactVisibleVertices" };
            try
            {
                cmd.SetComputeIntParam(cs, "_HairVertexCount", vertexCount);
                cmd.SetComputeBufferParam(cs, kClear, "_HairIndirectRasterArgs", rasterArgsBuf);
                cmd.SetComputeBufferParam(cs, kClear, "_HairIndirectShadeArgs", shadeArgsBuf);
                cmd.DispatchCompute(cs, kClear, 1, 1, 1);

                cmd.SetComputeBufferParam(cs, kCompact, "_HairVisibleVertexMask", maskBuf);
                cmd.SetComputeBufferParam(cs, kCompact, "_HairVisibleVertexList", listBuf);
                cmd.SetComputeBufferParam(cs, kCompact, "_HairIndirectShadeArgs", shadeArgsBuf);
                cmd.DispatchCompute(cs, kCompact, (maskUints + 63) / 64, 1, 1);
                Graphics.ExecuteCommandBuffer(cmd);
            }
            finally
            {
                cmd.Release();
            }

            var args = new uint[4];
            shadeArgsBuf.GetData(args);
            Assert.That(args[3], Is.EqualTo((uint)expected.Count), "Compacted visible vertex count must match mask.");
            Assert.That(args[0], Is.EqualTo((uint)((expected.Count + 63) / 64)), "Indirect groupX must match ceil(count/64).");

            var actual = new uint[vertexCount];
            listBuf.GetData(actual);
            var actualSlice = new uint[expected.Count];
            Array.Copy(actual, actualSlice, expected.Count);
            Array.Sort(actualSlice);

            for (int i = 0; i < expected.Count; i++)
                Assert.That(actualSlice[i], Is.EqualTo(expected[i]), $"Visible vertex mismatch at index {i}.");
        }
    }
}
