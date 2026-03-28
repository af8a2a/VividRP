using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime.RenderPass.Core.Sigma;

namespace VividRP.Editor.Tests
{
    public sealed class SigmaSharedConstantsComparerTests
    {
        [Test]
        public void Compare_ReturnsMatchSignature_WhenConstantsAreEqual()
        {
            var constants = new SigmaSharedConstants
            {
                gWorldToView = Matrix4x4.identity,
                gViewToClip = Matrix4x4.identity,
                gWorldToClipPrev = Matrix4x4.identity,
                gWorldToViewPrev = Matrix4x4.identity,
                gRectSize = new Vector2(1920.0f, 1080.0f),
                gUnproject = 1.0f,
                gFrameIndex = 1u
            };

            SigmaSharedConstantsComparison comparison = SigmaSharedConstantsComparer.Compare(constants, constants);

            Assert.That(comparison.HasDifferences, Is.False);
            Assert.That(comparison.FieldSignature, Is.EqualTo("match"));
        }

        [Test]
        public void Compare_ReportsChangedField_WhenScalarDiffers()
        {
            var manual = new SigmaSharedConstants
            {
                gUnproject = 1.0f
            };
            var native = new SigmaSharedConstants
            {
                gUnproject = 2.0f
            };

            SigmaSharedConstantsComparison comparison = SigmaSharedConstantsComparer.Compare(manual, native, 1e-6f);

            Assert.That(comparison.HasDifferences, Is.True);
            Assert.That(comparison.DifferentFieldCount, Is.EqualTo(1));
            Assert.That(comparison.FieldSignature, Does.Contain("gUnproject"));
            Assert.That(comparison.Summary, Does.Contain("gUnproject"));
            Assert.That(comparison.Summary, Does.Contain("manual=1"));
            Assert.That(comparison.Summary, Does.Contain("native=2"));
        }
    }
}
