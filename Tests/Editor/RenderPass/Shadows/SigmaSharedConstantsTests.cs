using System;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime.RenderPass.Core.Sigma;

namespace VividRP.Editor.Tests
{
    public sealed class SigmaSharedConstantsTests
    {
        [Test]
        public void Compute_ConvertsLoggedRightHandedInputs_ToLeftHandedSpace()
        {
            Matrix4x4 worldToView = new Matrix4x4
            {
                m00 = 0.951824f,
                m01 = -0.00391992f,
                m02 = -0.306619f,
                m03 = 0.0f,
                m10 = 0.167563f,
                m11 = 0.844078f,
                m12 = 0.509367f,
                m13 = 0.0f,
                m20 = -0.256814f,
                m21 = 0.536206f,
                m22 = -0.804071f,
                m23 = 0.0f,
                m30 = 0.0f,
                m31 = 0.0f,
                m32 = 0.0f,
                m33 = 1.0f
            };

            Matrix4x4 viewToClip = new Matrix4x4
            {
                m00 = 0.892692f,
                m01 = 0.0f,
                m02 = 0.0f,
                m03 = 0.0f,
                m10 = 0.0f,
                m11 = -2.24604f,
                m12 = 0.0f,
                m13 = 0.0f,
                m20 = 0.0f,
                m21 = 0.0f,
                m22 = 3.00407e-05f,
                m23 = 0.0300009f,
                m30 = 0.0f,
                m31 = 0.0f,
                m32 = -1.0f,
                m33 = 0.0f
            };

            SigmaSharedConstants constants = SigmaSharedConstants.Compute(
                worldToView,
                viewToClip,
                worldToView,
                viewToClip,
                Vector3.zero,
                Vector3.zero,
                Vector3.down,
                1920,
                1080,
                1920,
                1080,
                1u,
                1.0f,
                0.02f,
                0.0f,
                false);

            Assert.That(constants.gViewToClip.m22, Is.EqualTo(-3.00407e-05f).Within(1e-8f));
            Assert.That(constants.gViewToClip.m32, Is.EqualTo(1.0f).Within(1e-6f));
            Assert.That(constants.gWorldToView.m20, Is.EqualTo(0.256814f).Within(1e-6f));
            Assert.That(constants.gWorldToView.m21, Is.EqualTo(-0.536206f).Within(1e-6f));
            Assert.That(constants.gWorldToView.m22, Is.EqualTo(0.804071f).Within(1e-6f));
            Assert.That(constants.gFrustum.x, Is.LessThan(0.0f));
            Assert.That(constants.gFrustum.z, Is.GreaterThan(0.0f));
        }

        [Test]
        public void Compute_DoesNotAllocate_WhenResolvingProjectionPlanes()
        {
            var worldToView = Matrix4x4.identity;
            var viewToClip = Matrix4x4.Perspective(60.0f, 16.0f / 9.0f, 0.1f, 1000.0f);

            SigmaSharedConstants.Compute(
                worldToView,
                viewToClip,
                worldToView,
                viewToClip,
                Vector3.zero,
                Vector3.zero,
                Vector3.down,
                1920,
                1080,
                1920,
                1080,
                1u,
                1.0f,
                0.02f,
                0.0f,
                false);
            GC.Collect();

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            SigmaSharedConstants.Compute(
                worldToView,
                viewToClip,
                worldToView,
                viewToClip,
                Vector3.zero,
                Vector3.zero,
                Vector3.down,
                1920,
                1080,
                1920,
                1080,
                1u,
                1.0f,
                0.02f,
                0.0f,
                false);
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Assert.That(allocatedBytes, Is.EqualTo(0));
        }

        [Test]
        public void Bayer4x4_UsesReverseBitPattern_ByDefault()
        {
            Assert.That(SequenceHelpers.Bayer4x4(0u), Is.EqualTo(0.0f).Within(1e-6f));
            Assert.That(SequenceHelpers.Bayer4x4(1u), Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(SequenceHelpers.Bayer4x4(2u), Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(SequenceHelpers.Bayer4x4(15u), Is.EqualTo(0.9375f).Within(1e-6f));
            Assert.That(SequenceHelpers.Bayer4x4(16u), Is.EqualTo(0.0f).Within(1e-6f));
        }

        [Test]
        public void Weyl1D_UsesInt32OverflowSemantics()
        {
            Assert.That(SequenceHelpers.Weyl1D(0.0f, 500), Is.EqualTo(0.0169727802f).Within(1e-7f));
            Assert.That(SequenceHelpers.Weyl1D(0.0f, 12345), Is.EqualTo(0.629057944f).Within(1e-6f));
        }
    }
}
