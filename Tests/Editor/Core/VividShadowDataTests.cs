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

        [Test]
        public void Update_ClearsPreviousCameraData_WhenCurrentCameraHasNoShadowLight()
        {
            var shadowData = new VividShadowData
            {
                isCSMActive = true,
                cascadeCount = VividShadowData.MaxCascadeCount,
                maxShadowDistance = 150.0f,
                atlasResolution = 4096,
                cascadeResolution = 2048,
                normalBias = 1.0f,
                mainLightVisibleIndex = 3,
                slopeScaleDepthBias = 2.0f,
                shadowCasterState = Vector4.one,
            };

            for (int cascadeIndex = 0; cascadeIndex < VividShadowData.MaxCascadeCount; cascadeIndex++)
            {
                shadowData.viewMatrices[cascadeIndex] = Matrix4x4.zero;
                shadowData.projMatrices[cascadeIndex] = Matrix4x4.zero;
                shadowData.viewProjMatrices[cascadeIndex] = Matrix4x4.zero;
                shadowData.cascadeSpheres[cascadeIndex] = Vector4.one;
                shadowData.cascadeAtlasScaleOffsets[cascadeIndex] = Vector4.one;
                shadowData.cascadeWorldTexelSizes[cascadeIndex] = 1.0f;
                shadowData.cascadeBorders[cascadeIndex] = 1.0f;
                shadowData.splitData[cascadeIndex].shadowCascadeBlendCullingFactor = 1.0f;
            }

            shadowData.Update(default, null);

            Assert.That(shadowData.isCSMActive, Is.False);
            Assert.That(shadowData.cascadeCount, Is.Zero);
            Assert.That(shadowData.maxShadowDistance, Is.Zero);
            Assert.That(shadowData.atlasResolution, Is.Zero);
            Assert.That(shadowData.cascadeResolution, Is.Zero);
            Assert.That(shadowData.normalBias, Is.Zero);
            Assert.That(shadowData.mainLightVisibleIndex, Is.EqualTo(-1));
            Assert.That(shadowData.slopeScaleDepthBias, Is.Zero);
            Assert.That(shadowData.shadowCasterState, Is.EqualTo(Vector4.zero));

            for (int cascadeIndex = 0; cascadeIndex < VividShadowData.MaxCascadeCount; cascadeIndex++)
            {
                Assert.That(shadowData.viewMatrices[cascadeIndex], Is.EqualTo(Matrix4x4.identity));
                Assert.That(shadowData.projMatrices[cascadeIndex], Is.EqualTo(Matrix4x4.identity));
                Assert.That(shadowData.viewProjMatrices[cascadeIndex], Is.EqualTo(Matrix4x4.identity));
                Assert.That(shadowData.cascadeSpheres[cascadeIndex], Is.EqualTo(Vector4.zero));
                Assert.That(shadowData.cascadeAtlasScaleOffsets[cascadeIndex], Is.EqualTo(Vector4.zero));
                Assert.That(shadowData.cascadeWorldTexelSizes[cascadeIndex], Is.Zero);
                Assert.That(shadowData.cascadeBorders[cascadeIndex], Is.Zero);
                Assert.That(shadowData.splitData[cascadeIndex].shadowCascadeBlendCullingFactor, Is.Zero);
            }
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
