using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class HairGpuStreamContractTests
    {
        [Test]
        public void GpuSegment_UsesThreeFloat4Layout()
        {
            var segment = new HairStrandSegment(
                new HairStrandPoint(
                    new Vector3(1.0f, 2.0f, 3.0f),
                    0.25f,
                    new Vector2(0.1f, 0.2f)),
                new HairStrandPoint(
                    new Vector3(4.0f, 5.0f, 6.0f),
                    0.125f,
                    new Vector2(0.8f, 0.9f)));

            var gpuSegment = new HairGpuStrandSegment(segment);

            Assert.That(
                Marshal.SizeOf<HairGpuStrandSegment>(),
                Is.EqualTo(HairGpuStrandSegment.Stride));
            Assert.That(
                gpuSegment.StartPositionRadius,
                Is.EqualTo(new Vector4(1.0f, 2.0f, 3.0f, 0.25f)));
            Assert.That(
                gpuSegment.EndPositionRadius,
                Is.EqualTo(new Vector4(4.0f, 5.0f, 6.0f, 0.125f)));
            Assert.That(
                gpuSegment.StartEndUV,
                Is.EqualTo(new Vector4(0.1f, 0.2f, 0.8f, 0.9f)));
        }

        [Test]
        public void HistoryState_ResetsOnFirstFrameGapAndTopologyChange()
        {
            var state = new HairStrandHistoryState();

            Assert.That(
                state.CommitFrame(10, 128, 3),
                Is.EqualTo(HairHistoryResetReason.FirstFrame));
            Assert.That(
                state.CommitFrame(11, 128, 3),
                Is.EqualTo(HairHistoryResetReason.None));
            Assert.That(
                state.CommitFrame(13, 128, 3),
                Is.EqualTo(HairHistoryResetReason.FrameDiscontinuity));
            Assert.That(
                state.CommitFrame(14, 129, 3),
                Is.EqualTo(HairHistoryResetReason.TopologyChanged));
            Assert.That(
                state.CommitFrame(15, 129, 4),
                Is.EqualTo(HairHistoryResetReason.TopologyChanged));
            Assert.That(
                state.CommitFrame(16, 129, 4, false, true),
                Is.EqualTo(HairHistoryResetReason.StorageRecreated));
        }

        [Test]
        public void HistoryState_HandlesExplicitResetAndInvalidation()
        {
            var state = new HairStrandHistoryState();
            state.CommitFrame(20, 64, 0);

            Assert.That(
                state.CommitFrame(21, 64, 0, true),
                Is.EqualTo(HairHistoryResetReason.Explicit));

            state.Invalidate();
            Assert.That(state.IsValid, Is.False);
            Assert.That(
                state.CommitFrame(22, 64, 0),
                Is.EqualTo(HairHistoryResetReason.FirstFrame));
        }

        [Test]
        public void VertexUpdateCompute_ExpandsAndCommitsGpuHistory()
        {
            string source = ReadPackageFile(
                "Shaders",
                "Material",
                "Hair",
                "HairDotsVertexUpdate.compute");

            Assert.That(source, Does.Contain("#pragma kernel ExpandHairDots"));
            Assert.That(source, Does.Contain("#pragma kernel CopyHairHistory"));
            Assert.That(
                source,
                Does.Contain("RWByteAddressBuffer _HairDotsVertexBuffer"));
            Assert.That(source, Does.Contain("kHairVertexStride = 72u"));
            Assert.That(
                source,
                Does.Contain("if (_HairResetHistory == 0u)"));
            Assert.That(
                source,
                Does.Contain("previousRadius0 = -previousRadius0"));
            Assert.That(
                source,
                Does.Contain("_HairHistoryDestination[segmentIndex] ="));
        }

        [Test]
        public void CoreResources_RegistersHairVertexUpdateCompute()
        {
            string source = ReadPackageFile(
                "Runtime",
                "Core",
                "PipelineResource",
                "VividResources.cs");

            Assert.That(
                source,
                Does.Contain(
                    "Shaders/Material/Hair/HairDotsVertexUpdate.compute"));
            Assert.That(
                source,
                Does.Contain("HairDotsVertexUpdateCompute"));
        }

        private static string ReadPackageFile(params string[] parts)
        {
            string customPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "Custom_URP"));
            if (Directory.Exists(customPath))
                return File.ReadAllText(Path.Combine(
                    customPath,
                    Path.Combine(parts)));

            string vividPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "VividRP"));
            if (Directory.Exists(vividPath))
                return File.ReadAllText(Path.Combine(
                    vividPath,
                    Path.Combine(parts)));

            string legacyPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "com.af8a2a.vividrp"));
            return File.ReadAllText(Path.Combine(
                legacyPath,
                Path.Combine(parts)));
        }
    }
}
