using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class VividShadowDataTests
    {
        [Test]
        public void ComputeAtlasLayout_UsesExactCascadeToAtlasScale()
        {
            var shadowData = new VividShadowData
            {
                cascadeCount = 4,
                atlasResolution = 4097,
                cascadeResolution = 2048
            };

            shadowData.ComputeAtlasLayout();

            float scale = 2048f / 4097f;
            AssertVectorApproximately(shadowData.cascadeAtlasScaleOffsets[0], new Vector4(scale, scale, 0f, 0f));
            AssertVectorApproximately(shadowData.cascadeAtlasScaleOffsets[1], new Vector4(scale, scale, scale, 0f));
            AssertVectorApproximately(shadowData.cascadeAtlasScaleOffsets[2], new Vector4(scale, scale, 0f, scale));
            AssertVectorApproximately(shadowData.cascadeAtlasScaleOffsets[3], new Vector4(scale, scale, scale, scale));
        }

        private static void AssertVectorApproximately(Vector4 actual, Vector4 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(1e-6f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(1e-6f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(1e-6f));
            Assert.That(actual.w, Is.EqualTo(expected.w).Within(1e-6f));
        }
    }
}
