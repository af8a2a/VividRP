using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests.Fur
{
    public sealed class VividFurShellRendererTests
    {
        [Test]
        public void FillShellHeights_OutsideIn_MonotonicallyDecreasesFromOneToZero()
        {
            float[] heights = new float[VividFurShellRenderer.MaxShellCount];
            VividFurShellRenderer.FillShellHeights(heights, shellCount: 16, heightDistributionPower: 1.0f, outsideInOrder: true);

            Assert.That(heights[0], Is.EqualTo(1.0f).Within(1e-6f));
            Assert.That(heights[15], Is.EqualTo(0.0f).Within(1e-6f));

            for (int i = 1; i < 16; i++)
            {
                Assert.That(heights[i], Is.LessThan(heights[i - 1]),
                    $"Outside-in shell height at index {i} should be strictly less than index {i - 1}.");
            }
        }

        [Test]
        public void FillShellHeights_SingleShell_UsesBaseSkinZeroHeight()
        {
            float[] heights = new float[VividFurShellRenderer.MaxShellCount];
            heights[0] = 0.75f;

            VividFurShellRenderer.FillShellHeights(heights, shellCount: 1, heightDistributionPower: 1.0f, outsideInOrder: true);
            Assert.That(heights[0], Is.EqualTo(0.0f).Within(1e-6f));
        }

        [Test]
        public void FillShellHeights_SparseShadowShells_RespectsMaxHeightAndIncludesBaseSkin()
        {
            float[] heights = new float[VividFurShellRenderer.MaxShellCount];
            const float maxShadowHeight = 0.35f;

            VividFurShellRenderer.FillShellHeights(heights, shellCount: 3, heightDistributionPower: 1.0f, outsideInOrder: true, maxHeight: maxShadowHeight);

            Assert.That(heights[0], Is.EqualTo(maxShadowHeight).Within(1e-6f));
            Assert.That(heights[1], Is.EqualTo(maxShadowHeight * 0.5f).Within(1e-6f));
            Assert.That(heights[2], Is.EqualTo(0.0f).Within(1e-6f));
        }

        [Test]
        public void ComputeLodShellCount_ScalesSmoothlyWithCameraDistance()
        {
            int atNear = VividFurShellRenderer.ComputeLodShellCount(
                baseShellCount: 32, enableDistanceLod: true, distanceToCamera: 2.0f, nearDistance: 4.0f, farDistance: 24.0f, minLodShellCount: 2);
            int atMid = VividFurShellRenderer.ComputeLodShellCount(
                baseShellCount: 32, enableDistanceLod: true, distanceToCamera: 14.0f, nearDistance: 4.0f, farDistance: 24.0f, minLodShellCount: 2);
            int atFar = VividFurShellRenderer.ComputeLodShellCount(
                baseShellCount: 32, enableDistanceLod: true, distanceToCamera: 30.0f, nearDistance: 4.0f, farDistance: 24.0f, minLodShellCount: 2);
            int lodDisabled = VividFurShellRenderer.ComputeLodShellCount(
                baseShellCount: 32, enableDistanceLod: false, distanceToCamera: 100.0f, nearDistance: 4.0f, farDistance: 24.0f, minLodShellCount: 2);

            Assert.That(atNear, Is.EqualTo(32));
            Assert.That(atMid, Is.EqualTo(17));
            Assert.That(atFar, Is.EqualTo(2));
            Assert.That(lodDisabled, Is.EqualTo(32));
        }

        [Test]
        public void ComputeExpandedWorldBounds_IncludesTransformScaleAndFurExtrusion()
        {
            Bounds localBounds = new Bounds(Vector3.zero, Vector3.one);
            Matrix4x4 trs = Matrix4x4.TRS(new Vector3(2.0f, 1.0f, -3.0f), Quaternion.identity, new Vector3(2.0f, 2.0f, 2.0f));
            float extrusion = VividFurShellRenderer.ComputeMaxFurWorldExtrusion(
                furLength: 0.1f,
                gravityWS: Vector3.down,
                gravityStrength: 0.5f,
                windStrength: 0.0f,
                extraPadding: 0.05f);

            // Expected extrusion: 0.1 * (1 + 0.5) + 0.05 = 0.2
            Assert.That(extrusion, Is.EqualTo(0.2f).Within(1e-6f));

            Bounds expanded = VividFurShellRenderer.ComputeExpandedWorldBounds(localBounds, trs, extrusion);
            Assert.That(expanded.center, Is.EqualTo(new Vector3(2.0f, 1.0f, -3.0f)));
            // Local half-extents 0.5 * scale 2.0 = 1.0, plus 0.2 extrusion => 1.2 half-extents (size 2.4)
            Assert.That(expanded.extents.x, Is.EqualTo(1.2f).Within(1e-5f));
            Assert.That(expanded.extents.y, Is.EqualTo(1.2f).Within(1e-5f));
            Assert.That(expanded.extents.z, Is.EqualTo(1.2f).Within(1e-5f));
        }

        [Test]
        public void PrepareFrameData_SteadyStateAndLodTransitions_AllocatesZeroManagedBytes()
        {
            GameObject go = new GameObject("FurShellRendererAllocTest");
            try
            {
                VividFurShellRenderer renderer = go.AddComponent<VividFurShellRenderer>();
                renderer.ShellCount = 24;
                renderer.EnableDistanceLod = true;
                renderer.LodNearDistance = 4.0f;
                renderer.LodFarDistance = 24.0f;
                renderer.MinLodShellCount = 2;
                renderer.ShadowCastingMode = ShadowCastingMode.On;
                renderer.SparseShadowShells = true;
                renderer.ShadowShellCount = 2;
                renderer.ShadowMaxHeight = 0.35f;

                Bounds localBounds = new Bounds(Vector3.zero, Vector3.one);
                Matrix4x4 matrixA = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one);
                Matrix4x4 matrixB = Matrix4x4.TRS(new Vector3(0.1f, 0.0f, 0.0f), Quaternion.identity, Vector3.one);
                Vector3 nearCamera = new Vector3(0.0f, 0.0f, -2.0f);
                Vector3 farCamera = new Vector3(0.0f, 0.0f, -15.0f);

                // Warm up runtime storage and MaterialPropertyBlock uploads outside measurement.
                renderer.PrepareFrameDataForTest(nearCamera, matrixA, localBounds, 0.1f, frameCount: 1, out _, out _, out _);
                renderer.PrepareFrameDataForTest(farCamera, matrixB, localBounds, 0.1f, frameCount: 2, out _, out _, out _);

                int visualCount = 0;
                int shadowCount = 0;
                Bounds worldBounds = default;

                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 64; i++)
                {
                    Vector3 cam = (i & 1) == 0 ? nearCamera : farCamera;
                    Matrix4x4 mat = (i & 1) == 0 ? matrixA : matrixB;
                    renderer.PrepareFrameDataForTest(cam, mat, localBounds, 0.1f, frameCount: 10 + i, out visualCount, out shadowCount, out worldBounds);
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(allocated, Is.EqualTo(0L),
                    $"Steady-state fur shell preparation must allocate 0 managed bytes, observed {allocated} bytes.");
                Assert.That(visualCount, Is.GreaterThanOrEqualTo(2));
                Assert.That(shadowCount, Is.EqualTo(2));
                Assert.That(worldBounds.size.x, Is.GreaterThan(1.0f));

                // Verify previous transform tracking across distinct frame indices
                VividFurShellRenderer.ShellInstanceData instanceData = renderer.GetInstanceDataForTest(0);
                Assert.That(instanceData.objectToWorld, Is.EqualTo(matrixB));
                Assert.That(instanceData.prevObjectToWorld, Is.EqualTo(matrixA));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
