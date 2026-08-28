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
                hasUnityShadowCasters = true,
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

            shadowData.Update(default, null, null);

            Assert.That(shadowData.isCSMActive, Is.False);
            Assert.That(shadowData.cascadeCount, Is.Zero);
            Assert.That(shadowData.maxShadowDistance, Is.Zero);
            Assert.That(shadowData.atlasResolution, Is.Zero);
            Assert.That(shadowData.cascadeResolution, Is.Zero);
            Assert.That(shadowData.normalBias, Is.Zero);
            Assert.That(shadowData.mainLightVisibleIndex, Is.EqualTo(-1));
            Assert.That(shadowData.hasUnityShadowCasters, Is.False);
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

        [Test]
        public void TryGetCascadeDepthRange_UsesCameraNearPlaneShadowDistanceAndOrderedSplits()
        {
            var cameraObject = new GameObject("Manual Cascade Depth Camera");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.nearClipPlane = 0.5f;
                camera.farClipPlane = 200.0f;
                var cameraData = new VividCameraData();
                cameraData.SetCamera(camera);
                cameraData.CacheCameraFrameProperties(camera);

                var splitRatios = new Vector3(0.1f, 0.3f, 0.6f);
                float shadowRange = 100.0f - camera.nearClipPlane;
                for (int cascadeIndex = 0; cascadeIndex < 4; cascadeIndex++)
                {
                    Assert.That(
                        VividShadowData.TryGetCascadeDepthRange(
                            cameraData,
                            100.0f,
                            cascadeIndex,
                            4,
                            splitRatios,
                            out float nearDistance,
                            out float farDistance),
                        Is.True);

                    float expectedNearRatio = cascadeIndex switch
                    {
                        0 => 0.0f,
                        1 => splitRatios.x,
                        2 => splitRatios.y,
                        _ => splitRatios.z,
                    };
                    float expectedFarRatio = cascadeIndex switch
                    {
                        0 => splitRatios.x,
                        1 => splitRatios.y,
                        2 => splitRatios.z,
                        _ => 1.0f,
                    };
                    Assert.That(
                        nearDistance,
                        Is.EqualTo(camera.nearClipPlane + expectedNearRatio * shadowRange).Within(1e-5f));
                    Assert.That(
                        farDistance,
                        Is.EqualTo(camera.nearClipPlane + expectedFarRatio * shadowRange).Within(1e-5f));
                }
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TryBuildCascadeMatrices_ContainsCameraSliceAndCasterDepth(bool orthographic)
        {
            var cameraObject = new GameObject("Manual Cascade Camera");
            var lightObject = new GameObject("Manual Cascade Light");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.transform.SetPositionAndRotation(
                    new Vector3(3.0f, 2.0f, -4.0f),
                    Quaternion.Euler(8.0f, 25.0f, 0.0f));
                camera.nearClipPlane = 0.3f;
                camera.farClipPlane = 100.0f;
                camera.aspect = 16.0f / 9.0f;
                camera.fieldOfView = 65.0f;
                camera.orthographic = orthographic;
                camera.orthographicSize = 8.0f;

                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(50.0f, -30.0f, 12.0f);

                var cameraData = new VividCameraData();
                cameraData.SetCamera(camera);
                cameraData.CacheCameraFrameProperties(camera);
                var shadowData = new VividShadowData();
                var casterBounds = new Bounds(
                    camera.transform.position + camera.transform.forward * 18.0f,
                    new Vector3(60.0f, 40.0f, 80.0f));

                Assert.That(
                    shadowData.TryBuildCascadeMatrices(
                        cameraData,
                        light,
                        casterBounds,
                        1.0f,
                        35.0f,
                        1024,
                        3.0f,
                        out Matrix4x4 viewMatrix,
                        out Matrix4x4 projectionMatrix,
                        out var cascadeSplitData),
                    Is.True);
                Assert.That(
                    VividShadowData.IsCascadeDataUsable(
                        viewMatrix,
                        projectionMatrix,
                        cascadeSplitData),
                    Is.True);
                Assert.That(cascadeSplitData.cullingPlaneCount, Is.EqualTo(6));
                Matrix4x4 expectedCullingMatrix = projectionMatrix * viewMatrix;
                for (int elementIndex = 0; elementIndex < 16; elementIndex++)
                {
                    Assert.That(
                        cascadeSplitData.cullingMatrix[elementIndex],
                        Is.EqualTo(expectedCullingMatrix[elementIndex]).Within(1e-5f));
                }

                Vector4 sphere = cascadeSplitData.cullingSphere;
                var sphereCenter = new Vector3(sphere.x, sphere.y, sphere.z);
                AssertCameraSliceIsContained(
                    camera,
                    viewMatrix,
                    projectionMatrix,
                    sphereCenter,
                    sphere.w,
                    1.0f);
                AssertCameraSliceIsContained(
                    camera,
                    viewMatrix,
                    projectionMatrix,
                    sphereCenter,
                    sphere.w,
                    35.0f);

                Vector3 casterMinimum = casterBounds.min;
                Vector3 casterMaximum = casterBounds.max;
                for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
                {
                    var corner = new Vector3(
                        (cornerIndex & 1) == 0 ? casterMinimum.x : casterMaximum.x,
                        (cornerIndex & 2) == 0 ? casterMinimum.y : casterMaximum.y,
                        (cornerIndex & 4) == 0 ? casterMinimum.z : casterMaximum.z);
                    Vector4 clip = projectionMatrix * viewMatrix * new Vector4(
                        corner.x,
                        corner.y,
                        corner.z,
                        1.0f);
                    float clipDepth = clip.z / clip.w;
                    Assert.That(clipDepth, Is.InRange(-1.001f, 1.001f));
                }
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(lightObject);
            }
        }

        [Test]
        public void IsCascadeDataUsable_RejectsNonFiniteOrDegenerateUnityResults()
        {
            var splitData = new UnityEngine.Rendering.ShadowSplitData
            {
                cullingMatrix = Matrix4x4.identity,
                cullingSphere = new Vector4(0.0f, 0.0f, 0.0f, 10.0f),
            };

            Assert.That(
                VividShadowData.IsCascadeDataUsable(
                    Matrix4x4.identity,
                    Matrix4x4.Ortho(-1.0f, 1.0f, -1.0f, 1.0f, 0.0f, 10.0f),
                    splitData),
                Is.True);
            Assert.That(
                VividShadowData.IsCascadeDataUsable(
                    Matrix4x4.zero,
                    Matrix4x4.identity,
                    splitData),
                Is.False);
            splitData.cullingMatrix = Matrix4x4.zero;
            Assert.That(
                VividShadowData.IsCascadeDataUsable(
                    Matrix4x4.identity,
                    Matrix4x4.identity,
                    splitData),
                Is.False);
            splitData.cullingMatrix = Matrix4x4.identity;
            splitData.cullingSphere = new Vector4(0.0f, 0.0f, 0.0f, float.NaN);
            Assert.That(
                VividShadowData.IsCascadeDataUsable(
                    Matrix4x4.identity,
                    Matrix4x4.identity,
                    splitData),
                Is.False);
        }

        [Test]
        public void TryCombineShadowCasterBounds_EncapsulatesUnityAndPrimitiveBounds()
        {
            var unityBounds = new Bounds(
                new Vector3(-4.0f, 2.0f, 0.0f),
                new Vector3(6.0f, 8.0f, 10.0f));
            var primitiveBounds = new Bounds(
                new Vector3(5.0f, -3.0f, 4.0f),
                new Vector3(4.0f, 2.0f, 6.0f));

            Assert.That(
                VividShadowData.TryCombineShadowCasterBounds(
                    true,
                    unityBounds,
                    true,
                    primitiveBounds,
                    out Bounds combinedBounds),
                Is.True);
            AssertVectorApproximately(combinedBounds.min, new Vector3(-7.0f, -4.0f, -5.0f));
            AssertVectorApproximately(combinedBounds.max, new Vector3(7.0f, 6.0f, 7.0f));
        }

        [Test]
        public void TryCombineShadowCasterBounds_RejectsMissingSources()
        {
            Assert.That(
                VividShadowData.TryCombineShadowCasterBounds(
                    false,
                    default,
                    false,
                    default,
                    out _),
                Is.False);
        }

        private static void AssertCameraSliceIsContained(
            Camera camera,
            Matrix4x4 lightViewMatrix,
            Matrix4x4 lightProjectionMatrix,
            Vector3 sphereCenter,
            float sphereRadius,
            float viewDistance)
        {
            var corners = new Vector3[4];
            camera.CalculateFrustumCorners(
                new Rect(0.0f, 0.0f, 1.0f, 1.0f),
                viewDistance,
                Camera.MonoOrStereoscopicEye.Mono,
                corners);
            for (int cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
            {
                Vector3 worldCorner = camera.transform.TransformPoint(corners[cornerIndex]);
                Assert.That(
                    Vector3.Distance(worldCorner, sphereCenter),
                    Is.LessThanOrEqualTo(sphereRadius + 1e-3f));
                Vector4 clip = lightProjectionMatrix * lightViewMatrix * new Vector4(
                    worldCorner.x,
                    worldCorner.y,
                    worldCorner.z,
                    1.0f);
                float clipX = clip.x / clip.w;
                float clipY = clip.y / clip.w;
                float clipZ = clip.z / clip.w;
                Assert.That(clipX, Is.InRange(-1.001f, 1.001f));
                Assert.That(clipY, Is.InRange(-1.001f, 1.001f));
                Assert.That(clipZ, Is.InRange(-1.001f, 1.001f));
            }
        }

        private static void AssertVectorApproximately(Vector4 actual, Vector4 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(1e-6f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(1e-6f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(1e-6f));
            Assert.That(actual.w, Is.EqualTo(expected.w).Within(1e-6f));
        }

        private static void AssertVectorApproximately(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(1e-6f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(1e-6f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(1e-6f));
        }
    }
}
