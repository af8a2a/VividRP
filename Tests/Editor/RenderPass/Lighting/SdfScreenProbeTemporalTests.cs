using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime.RenderPass.Core;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class SdfScreenProbeTemporalTests
    {
        [TestCase(1920, 1080, 32, 32, 5, 5)]
        [TestCase(3840, 2160, 16, 16, 4, 4)]
        [TestCase(1280, 720, 32, 64, 5, 6)]
        [TestCase(16, 16, 2048, 2048, 11, 11)]
        public void HistoryScreenCoordCodec_MatchesUePowerOfTwoSubPixelGridAndAllocatesZeroBytes(
            int width, int height, int expectedSubX, int expectedSubY, int expectedShiftX, int expectedShiftY)
        {
            SdfDiffuseGatherPass.GetHistoryScreenCoordCodec(
                width, height, out var subPixel, out var shift, out var gatherMinMax);

            Assert.That(subPixel.x, Is.EqualTo(expectedSubX));
            Assert.That(subPixel.y, Is.EqualTo(expectedSubY));
            Assert.That(subPixel.z, Is.EqualTo(1f / expectedSubX).Within(1e-7f));
            Assert.That(subPixel.w, Is.EqualTo(1f / expectedSubY).Within(1e-7f));
            Assert.That(shift, Is.EqualTo(new Vector2Int(expectedShiftX, expectedShiftY)));
            Assert.That(gatherMinMax.x, Is.GreaterThan(0.5f / width));
            Assert.That(gatherMinMax.z, Is.LessThan(1f - 0.5f / width));
            Assert.That(gatherMinMax.y, Is.GreaterThan(0.5f / height));
            Assert.That(gatherMinMax.w, Is.LessThan(1f - 0.5f / height));

            for (int i = 0; i < 32; i++)
                SdfDiffuseGatherPass.GetHistoryScreenCoordCodec(width, height, out _, out _, out _);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++)
                SdfDiffuseGatherPass.GetHistoryScreenCoordCodec(width, height, out _, out _, out _);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void DefaultSettings_MatchUeLumenTemporalDefaults()
        {
            var settings = ScriptableObject.CreateInstance<SdfDiffuseGISettings>();
            try
            {
                Assert.That(settings.screenProbeFastUpdateNeighborhoodClamp.value, Is.False);
                Assert.That(settings.screenProbeTemporalRejectBasedOnNormal.value, Is.False);
                Assert.That(settings.screenProbeTemporalPlaneDisocclusion.value, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void GrazingPlaneReprojection_RetainsFullHistoryWhereRayDepthVariesSteeply()
        {
            RequireComputeAndReversedZ();
            using var fixture = new TemporalFixture(16, 16, useMoments: false);

            // Steep grazing plane (V.N ~ 0.08 at screen center): 1-px screen-Y step changes
            // depth by > 0.25 m (> 0.008 * z), while perpendicular plane distance == 0.
            var grazingNormal = new Vector3(0f, -1f, -0.08f).normalized;
            var planeAnchor = new Vector3(0f, 0f, 8f);

            fixture.WarmupSteadyHistory(
                (u, v) => fixture.IntersectPlaneWorldZ(u, v, planeAnchor, grazingNormal),
                (_, _) => grazingNormal,
                (_, _) => new Color(0.8f, 0.6f, 0.4f, 1f),
                frames: 12,
                reprojectionFlags: 1);

            var jitterDeltaUv = new Vector2(0.25f / 16f, -0.45f / 16f);
            fixture.SetCurrentFrame(
                (u, v) => fixture.IntersectPlaneWorldZ(u, v, planeAnchor, grazingNormal),
                (_, _) => grazingNormal,
                (_, _) => new Color(0.2f, 0.2f, 0.2f, 1f),
                moving: 0.004f,
                currentJitterUv: -jitterDeltaUv);

            var output = fixture.Step(hasHistory: true, jitterDeltaUv: jitterDeltaUv, clampEnabled: true, reprojectionFlags: 1);
            int center = 8 * 16 + 8;
            Assert.That(output.NumFrames[center], Is.EqualTo(10f).Within(0.01f));
            Assert.That(output.OutputRgba[center * 4], Is.EqualTo(Mathf.Lerp(0.8f, 0.2f, 1f / 11f)).Within(0.006f));
        }

        [Test]
        public void GrazingAngleDepthExpansion_RetainsHistoryUnderUeDefaultLinearDepth()
        {
            RequireComputeAndReversedZ();
            using var fixture = new TemporalFixture(16, 16, useMoments: false);

            // Grazing surface (V.N ~ 0.15 => ~6.7x UE threshold expansion: 0.01 / 0.15 = 6.7%).
            // 1-px neighbor differs by 3% depth (> 1% unexpanded, < 6.7% expanded).
            var grazingNormal = new Vector3(0f, -1f, -0.15f).normalized;
            fixture.WarmupSteadyHistory(
                (_, uvY) => 8.0f + (uvY - 0.5f) * 4.0f,
                (_, _) => grazingNormal,
                (_, _) => new Color(0.8f, 0.6f, 0.4f, 1f),
                frames: 12);

            var jitterDeltaUv = new Vector2(0.25f / 16f, -0.45f / 16f);
            fixture.SetCurrentFrame(
                (_, uvY) => 8.0f + (uvY - 0.5f) * 4.0f,
                (_, _) => grazingNormal,
                (_, _) => new Color(0.2f, 0.2f, 0.2f, 1f),
                moving: 0.004f,
                currentJitterUv: -jitterDeltaUv);

            var output = fixture.Step(hasHistory: true, jitterDeltaUv: jitterDeltaUv, clampEnabled: false, reprojectionFlags: 0);
            int center = 8 * 16 + 8;
            Assert.That(output.NumFrames[center], Is.EqualTo(10f).Within(0.01f));
            Assert.That(output.OutputRgba[center * 4], Is.EqualTo(Mathf.Lerp(0.8f, 0.2f, 1f / 11f)).Within(0.006f));
        }

        [Test]
        public void NormalMappedSurfaceMotion_RetainsHistoryByDefaultAndRejectsWhenNormalRejectEnabled()
        {
            RequireComputeAndReversedZ();
            int center = 8 * 16 + 8;
            var normalA = new Vector3(0.6f, 0f, -0.8f).normalized;
            var normalB = new Vector3(-0.6f, 0f, -0.8f).normalized; // dot(A, B) = 0.28 < 0.7071

            using var defaultFixture = new TemporalFixture(16, 16, useMoments: false);
            defaultFixture.WarmupSteadyHistory(
                (_, _) => 6.0f,
                (_, _) => normalA,
                (_, _) => new Color(0.85f, 0.85f, 0.85f, 1f),
                frames: 12);
            defaultFixture.SetCurrentFrame(
                (_, _) => 6.0f,
                (_, _) => normalB,
                (_, _) => new Color(0.25f, 0.25f, 0.25f, 1f),
                moving: 0.004f);
            var defaultOut = defaultFixture.Step(hasHistory: true, jitterDeltaUv: new Vector2(0.35f / 16f, 0f),
                clampEnabled: false, reprojectionFlags: 0);
            Assert.That(defaultOut.NumFrames[center], Is.EqualTo(10f).Within(0.01f),
                "UE default (RejectBasedOnNormal = 0) must retain history across normal-map variation at matching depth.");
            Assert.That(defaultOut.OutputRgba[center * 4], Is.EqualTo(Mathf.Lerp(0.85f, 0.25f, 1f / 11f)).Within(0.006f));

            using var normalRejectFixture = new TemporalFixture(16, 16, useMoments: false);
            normalRejectFixture.WarmupSteadyHistory(
                (_, _) => 6.0f,
                (_, _) => normalA,
                (_, _) => new Color(0.85f, 0.85f, 0.85f, 1f),
                frames: 12,
                reprojectionFlags: 2);
            normalRejectFixture.SetCurrentFrame(
                (_, _) => 6.0f,
                (_, _) => normalB,
                (_, _) => new Color(0.25f, 0.25f, 0.25f, 1f),
                moving: 0.004f);
            var rejectOut = normalRejectFixture.Step(hasHistory: true, jitterDeltaUv: new Vector2(0.35f / 16f, 0f),
                clampEnabled: false, reprojectionFlags: 2);
            Assert.That(rejectOut.NumFrames[center], Is.LessThan(0.5f),
                "Opt-in RejectBasedOnNormal flag must reject history when normal angle exceeds 45 degrees.");
        }

        [Test]
        public void SmallNearClipFp32DepthHistory_RetainsFullHistoryAtSceneViewNearPlane()
        {
            RequireComputeAndReversedZ();
            using var fixture = new TemporalFixture(16, 16, useMoments: false, nearClip: 0.025f, farClip: 5000f);

            var wallNormal = new Vector3(0f, 0f, -1f);
            fixture.WarmupSteadyHistory(
                (_, _) => 25.0f,
                (_, _) => wallNormal,
                (_, _) => new Color(0.9f, 0.7f, 0.5f, 1f),
                frames: 12);

            fixture.SetCurrentFrame(
                (_, _) => 25.0f,
                (_, _) => wallNormal,
                (_, _) => new Color(0.2f, 0.2f, 0.2f, 1f),
                moving: 0.004f);

            var output = fixture.Step(hasHistory: true, jitterDeltaUv: new Vector2(0.4f / 16f, -0.3f / 16f),
                clampEnabled: false, reprojectionFlags: 0);
            int center = 8 * 16 + 8;
            Assert.That(output.NumFrames[center], Is.EqualTo(10f).Within(0.01f));
            Assert.That(output.OutputRgba[center * 4], Is.EqualTo(Mathf.Lerp(0.9f, 0.2f, 1f / 11f)).Within(0.006f));
        }

        [Test]
        public void SubpixelSeamCrossing_NormalizesValidTapWeightsWithoutPerpendicularBleedOrHistoryReset()
        {
            RequireComputeAndReversedZ();
            using var fixture = new TemporalFixture(16, 16, useMoments: true);

            // Rows y < 8: lower plane at z = 7.8 m, normal (0, -1, 0), history RGB = 0.1
            // Rows y >= 8: upper plane at z = 7.7 m (closer!), normal (0, 0, -1), history RGB = 0.9
            var lowerNormal = new Vector3(0f, -1f, 0f);
            var upperNormal = new Vector3(0f, 0f, -1f);
            fixture.WarmupSteadyHistory(
                (_, uvY) => uvY * 16f >= 8f ? 7.7f : 7.8f,
                (_, y) => y >= 8 ? upperNormal : lowerNormal,
                (_, y) => y >= 8 ? new Color(0.9f, 0.9f, 0.9f, 1f) : new Color(0.1f, 0.1f, 0.1f, 1f),
                frames: 12);

            fixture.SetCurrentFrame(
                (_, uvY) => uvY * 16f >= 8f ? 7.7f : 7.8f,
                (_, y) => y >= 8 ? upperNormal : lowerNormal,
                (_, _) => new Color(0.5f, 0.5f, 0.5f, 1f),
                moving: 0.004f);

            // Shift previousPixel.y for row y = 8 to 8 - 0.75 = 7.25:
            // lower.y = 7 (perpendicular normal, weight 0.75, rejected) + tap.y = 8 (same plane, weight 0.25 < 0.35, accepted!).
            var output = fixture.Step(hasHistory: true, jitterDeltaUv: new Vector2(0f, -0.75f / 16f), clampEnabled: true);
            int seamPixel = 8 * 16 + 8;
            float expected = Mathf.Lerp(0.9f, 0.5f, 1f / 11f);
            Assert.That(output.NumFrames[seamPixel], Is.EqualTo(10f).Within(0.01f),
                "Valid subpixel tap weight 0.25 (> 0.01) must retain MaxFramesAccumulated instead of resetting to 0.");
            Assert.That(output.OutputRgba[seamPixel * 4], Is.EqualTo(expected).Within(0.006f),
                "Normalized WeightedAverage must recover pure upper-plane history without perpendicular lower-plane bleed.");
            Assert.That(output.MomentsRgba[seamPixel * 4 + 2], Is.EqualTo(10f).Within(0.05f),
                "Moments age must stay at MaxFramesAccumulated when normalized valid history taps exist.");
        }

        [Test]
        public void FastUpdateNeighborhoodClamp_ClampsMovingHistoryWithoutDisturbingStaticPixels()
        {
            RequireComputeAndReversedZ();
            int center = 8 * 16 + 8;

            using var staticFixture = new TemporalFixture(16, 16, useMoments: false);
            staticFixture.WarmupSteadyHistory(
                (_, _) => 5.0f,
                (_, _) => new Vector3(0f, 0f, -1f),
                (_, _) => new Color(0.9f, 0.9f, 0.9f, 1f),
                frames: 12);
            staticFixture.SetCurrentFrame(
                (_, _) => 5.0f,
                (_, _) => new Vector3(0f, 0f, -1f),
                (x, y) => x == 9 && y == 8 ? new Color(0.35f, 0.35f, 0.35f, 1f) : new Color(0.25f, 0.25f, 0.25f, 1f),
                moving: 0.004f);
            var staticOut = staticFixture.Step(hasHistory: true, jitterDeltaUv: Vector2.zero, clampEnabled: true);
            Assert.That(staticOut.NumFrames[center], Is.EqualTo(10f).Within(0.01f));
            Assert.That(staticOut.FastUpdate[center], Is.Zero);
            Assert.That(staticOut.OutputRgba[center * 4], Is.GreaterThan(0.8f),
                "Static pixel (moving = 0.004 => FastUpdate = 0) must not clamp history to the 3x3 current neighborhood.");

            using var movingFixture = new TemporalFixture(16, 16, useMoments: false);
            movingFixture.WarmupSteadyHistory(
                (_, _) => 5.0f,
                (_, _) => new Vector3(0f, 0f, -1f),
                (_, _) => new Color(0.9f, 0.9f, 0.9f, 1f),
                frames: 12);
            movingFixture.SetCurrentFrame(
                (_, _) => 5.0f,
                (_, _) => new Vector3(0f, 0f, -1f),
                (x, y) => x == 9 && y == 8 ? new Color(0.35f, 0.35f, 0.35f, 1f) : new Color(0.25f, 0.25f, 0.25f, 1f),
                moving: 0.10f); // 0.10 * 10 = 1.0 => FastUpdateModeAmount = 0.9
            var movingClamped = movingFixture.Step(hasHistory: true, jitterDeltaUv: Vector2.zero, clampEnabled: true);
            Assert.That(movingClamped.FastUpdate[center], Is.GreaterThan(0.85f));
            Assert.That(movingClamped.NumFrames[center], Is.LessThanOrEqualTo(1.5f));
            Assert.That(movingClamped.OutputRgba[center * 4], Is.LessThanOrEqualTo(0.355f));
        }

        private static void RequireComputeAndReversedZ()
        {
            Assume.That(
                SystemInfo.supportsComputeShaders
                && SystemInfo.supportsAsyncGPUReadback
                && SystemInfo.usesReversedZBuffer,
                Is.True);
        }

        private sealed class TemporalFixture : IDisposable
        {
            private readonly int width, height;
            private readonly bool useMoments;
            private readonly List<Object> owned = new();
            private readonly ComputeShader shader;
            private readonly int kernel;
            private readonly Matrix4x4 proj, view, vp, invVp;

            private readonly Texture2D depthTex, gbuffer0, gbuffer1, motionTex, resolvedTex, movingTex, scalarNoise;
            private RenderTexture prevFast, outFast, prevLighting, outLighting, prevGeom, outGeom, prevDepth, outDepth, prevMoments, outMoments;
            private readonly RenderTexture outResolved;

            internal struct Result
            {
                public float[] OutputRgba;
                public float[] MomentsRgba;
                public float[] FastUpdate;
                public float[] NumFrames;
            }

            internal TemporalFixture(int width, int height, bool useMoments, float nearClip = 0.1f, float farClip = 100f)
            {
                this.width = width;
                this.height = height;
                this.useMoments = useMoments;

                var asset = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/SdfScreenProbeGatherTemporal.compute");
                Assert.That(asset, Is.Not.Null);
                shader = Object.Instantiate(asset);
                owned.Add(shader);
                kernel = shader.FindKernel(useMoments
                    ? "ScreenProbeTemporalReprojectionWithMomentsCS"
                    : "ScreenProbeTemporalReprojectionCS");

                view = Matrix4x4.identity;
                view.m22 = -1f;
                proj = Matrix4x4.Perspective(45f, 1f, nearClip, farClip);
                var gpuProj = GL.GetGPUProjectionMatrix(proj, true);
                vp = gpuProj * view;
                invVp = vp.inverse;

                depthTex = CreateTex2D(GraphicsFormat.R32_SFloat);
                gbuffer0 = CreateTex2D(GraphicsFormat.R8G8B8A8_UNorm);
                gbuffer1 = CreateTex2D(GraphicsFormat.R8G8B8A8_UNorm);
                motionTex = CreateTex2D(GraphicsFormat.R32G32B32A32_SFloat);
                resolvedTex = CreateTex2D(GraphicsFormat.R32G32B32A32_SFloat);
                movingTex = CreateTex2D(GraphicsFormat.R32_SFloat);
                scalarNoise = CreateTex2D(GraphicsFormat.R32_SFloat, 1, 1);
                scalarNoise.SetPixelData(new[] { 0.5f }, 0);
                scalarNoise.Apply(false, false);

                prevFast = CreateRT(GraphicsFormat.R8_UNorm);
                outFast = CreateRT(GraphicsFormat.R8_UNorm);
                prevLighting = CreateRT(GraphicsFormat.R16G16B16A16_SFloat);
                outLighting = CreateRT(GraphicsFormat.R16G16B16A16_SFloat);
                prevGeom = CreateRT(GraphicsFormat.R32_UInt);
                outGeom = CreateRT(GraphicsFormat.R32_UInt);
                prevDepth = CreateRT(GraphicsFormat.R32_SFloat);
                outDepth = CreateRT(GraphicsFormat.R32_SFloat);
                prevMoments = CreateRT(GraphicsFormat.R16G16B16A16_SFloat);
                outMoments = CreateRT(GraphicsFormat.R16G16B16A16_SFloat);
                outResolved = CreateRT(GraphicsFormat.R16G16B16A16_SFloat);
            }

            private Texture2D CreateTex2D(GraphicsFormat format, int w = -1, int h = -1)
            {
                var tex = new Texture2D(w > 0 ? w : width, h > 0 ? h : height, format, TextureCreationFlags.None)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                owned.Add(tex);
                return tex;
            }

            private RenderTexture CreateRT(GraphicsFormat format)
            {
                var rt = new RenderTexture(new RenderTextureDescriptor(width, height)
                {
                    graphicsFormat = format,
                    depthStencilFormat = GraphicsFormat.None,
                    enableRandomWrite = true,
                    msaaSamples = 1
                });
                Assert.That(rt.Create(), Is.True);
                owned.Add(rt);
                return rt;
            }

            internal float IntersectPlaneWorldZ(float u, float v, Vector3 planePoint, Vector3 planeNormal)
            {
                float ndcX = u * 2f - 1f;
                float ndcY = v * 2f - 1f;
                if (SystemInfo.graphicsUVStartsAtTop)
                    ndcY = -ndcY;
                Vector4 clip = new Vector4(ndcX, ndcY, 0.5f, 1f);
                Vector4 worldH = invVp * clip;
                Vector3 rayDir = (new Vector3(worldH.x, worldH.y, worldH.z) / worldH.w).normalized;
                float t = Vector3.Dot(planePoint, planeNormal) / Vector3.Dot(rayDir, planeNormal);
                return rayDir.z * t;
            }

            private float WorldZToDeviceZ(float worldZ)
            {
                Vector4 clip = vp * new Vector4(0f, 0f, worldZ, 1f);
                return clip.z / clip.w;
            }

            private static Vector2 EncodeOctNormal(Vector3 n)
            {
                n.Normalize();
                float invL1 = 1f / (Mathf.Abs(n.x) + Mathf.Abs(n.y) + Mathf.Abs(n.z));
                Vector2 p = new Vector2(n.x, n.y) * invL1;
                if (n.z < 0f)
                {
                    p = new Vector2(
                        (1f - Mathf.Abs(p.y)) * (p.x >= 0f ? 1f : -1f),
                        (1f - Mathf.Abs(p.x)) * (p.y >= 0f ? 1f : -1f));
                }
                return new Vector2(Mathf.Clamp01(p.x * 0.5f + 0.5f), Mathf.Clamp01(p.y * 0.5f + 0.5f));
            }

            internal void WarmupSteadyHistory(
                Func<float, float, float> worldDepthAtUv,
                Func<int, int, Vector3> normalAtPixel,
                Func<int, int, Color> resolvedAtPixel,
                int frames,
                int reprojectionFlags = 0)
            {
                SetCurrentFrame(worldDepthAtUv, normalAtPixel, resolvedAtPixel, moving: 0.004f);
                for (int f = 0; f < frames; f++)
                    Step(hasHistory: f > 0, jitterDeltaUv: Vector2.zero, clampEnabled: false,
                        readResult: false, reprojectionFlags: reprojectionFlags);
            }

            internal void SetCurrentFrame(
                Func<float, float, float> worldDepthAtUv,
                Func<int, int, Vector3> normalAtPixel,
                Func<int, int, Color> resolvedAtPixel,
                float moving,
                Vector2 currentJitterUv = default)
            {
                var depths = new float[width * height];
                var gb0 = new byte[width * height * 4];
                var gb1 = new byte[width * height * 4];
                var motions = new float[width * height * 4];
                var cRes = new float[width * height * 4];
                var cMov = new float[width * height];

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int i = y * width + x;
                        float u = (x + 0.5f) / width - currentJitterUv.x;
                        float v = (y + 0.5f) / height - currentJitterUv.y;
                        float wz = worldDepthAtUv(u, v);
                        depths[i] = WorldZToDeviceZ(wz);

                        Vector2 oct = EncodeOctNormal(normalAtPixel(x, y));
                        gb0[i * 4 + 0] = 200;
                        gb0[i * 4 + 1] = 200;
                        gb0[i * 4 + 2] = 200;
                        gb0[i * 4 + 3] = 2; // VIVID_DEFERRED_EXPORT_CLASS_FAST_SLAB

                        gb1[i * 4 + 0] = (byte)Mathf.RoundToInt(oct.x * 255f);
                        gb1[i * 4 + 1] = (byte)Mathf.RoundToInt(oct.y * 255f);
                        gb1[i * 4 + 2] = 128;
                        gb1[i * 4 + 3] = 255; // ABI tag 3/3 == 1.0

                        motions[i * 4 + 3] = 1f;

                        Color c = resolvedAtPixel(x, y);
                        cRes[i * 4 + 0] = c.r;
                        cRes[i * 4 + 1] = c.g;
                        cRes[i * 4 + 2] = c.b;
                        cRes[i * 4 + 3] = c.a;
                        cMov[i] = moving;
                    }
                }

                depthTex.SetPixelData(depths, 0); depthTex.Apply(false, false);
                gbuffer0.SetPixelData(gb0, 0); gbuffer0.Apply(false, false);
                gbuffer1.SetPixelData(gb1, 0); gbuffer1.Apply(false, false);
                motionTex.SetPixelData(motions, 0); motionTex.Apply(false, false);
                resolvedTex.SetPixelData(cRes, 0); resolvedTex.Apply(false, false);
                movingTex.SetPixelData(cMov, 0); movingTex.Apply(false, false);
            }

            internal Result Step(bool hasHistory, Vector2 jitterDeltaUv, bool clampEnabled,
                bool readResult = true, int reprojectionFlags = 0)
            {
                SdfDiffuseGatherPass.GetHistoryScreenCoordCodec(
                    width, height, out var subPixel, out var shift, out var gatherMinMax);

                var currJitterMat = Matrix4x4.identity;
                currJitterMat.m03 = -2f * jitterDeltaUv.x;
                currJitterMat.m13 = -2f * jitterDeltaUv.y;
                var currVp = GL.GetGPUProjectionMatrix(currJitterMat * proj, true) * view;
                var currInvVp = currVp.inverse;

                shader.SetVector("_SdfDiffuseScreenSize", new Vector4(width, height, 1f / width, 1f / height));
                shader.SetMatrix("_SdfDiffuseInverseVP", currInvVp);
                shader.SetMatrix("_SdfDiffuseVP", currVp);
                shader.SetMatrix("_SdfDiffusePreviousVP", vp);
                shader.SetMatrix("_SdfScreenPreviousInverseVP", invVp);
                shader.SetVector("_SdfProbeCameraForward", new Vector4(0f, 0f, 1f, 0f));
                shader.SetVector("_SdfProbePreviousCameraForward", new Vector4(0f, 0f, 1f, 0f));
                shader.SetVector("_SdfProbeCameraPosition", new Vector4(0f, 0f, 0f, 1f));
                shader.SetVector("_SdfHistoryLightScale", Vector4.one);
                shader.SetVector("_SdfScreenJitterDelta", new Vector4(jitterDeltaUv.x, jitterDeltaUv.y, 0f, 0f));
                shader.SetFloat("_SdfPixelHistoryMaxAge", 10f);
                shader.SetInt("_SdfDiffuseHasHistory", hasHistory ? 1 : 0);
                shader.SetInt("_SdfScreenProbeFullSphere", 1);
                shader.SetInt("_SdfGatherHistoryDiagnostics", 0);
                shader.SetInt("_SdfDiffuseFrame", 0);
                shader.SetInt("_SdfTemporalScalarNoiseEnabled", 0);
                shader.SetVector("_SdfHistorySubPixelGridSizeAndInvSize", subPixel);
                shader.SetVector("_SdfHistoryGatherUvMinMax", gatherMinMax);
                shader.SetInt("_SdfHistoryCoordShiftX", shift.x);
                shader.SetInt("_SdfHistoryCoordShiftY", shift.y);
                shader.SetVector("_SdfHistoryTemporalParams", new Vector4(0.01f, 0.70710678f, 10f, 0.9f));
                shader.SetInt("_SdfFastUpdateNeighborhoodClamp", clampEnabled ? 1 : 0);
                shader.SetInt("_SdfTemporalReprojectionFlags", reprojectionFlags);
                shader.SetInt("_SdfPreviousPixelDepthValid", hasHistory ? 1 : 0);

                shader.SetTexture(kernel, "_SdfDiffuseDepth", depthTex);
                shader.SetTexture(kernel, "_SdfDiffuseGBuffer0", gbuffer0);
                shader.SetTexture(kernel, "_SdfDiffuseGBuffer1", gbuffer1);
                shader.SetTexture(kernel, "_SdfScreenMotion", motionTex);
                shader.SetTexture(kernel, "_SdfDiffuseCurrentResolved", resolvedTex);
                shader.SetTexture(kernel, "_SdfResolvedMoving", movingTex);
                shader.SetTexture(kernel, "_SdfDiffusePreviousPixelLighting", prevLighting);
                shader.SetTexture(kernel, "_SdfDiffusePreviousPixelGeometry", prevGeom);
                shader.SetTexture(kernel, "_SdfDiffusePreviousPixelDepth", prevDepth);
                shader.SetTexture(kernel, "_SdfPreviousFastUpdate", prevFast);
                shader.SetTexture(kernel, "_SdfTemporalScalarNoise", scalarNoise);
                shader.SetTexture(kernel, "_SdfFastUpdateOutput", outFast);
                shader.SetTexture(kernel, "_SdfDiffusePixelLightingOutput", outLighting);
                shader.SetTexture(kernel, "_SdfDiffusePixelGeometryOutput", outGeom);
                shader.SetTexture(kernel, "_SdfDiffusePixelDepthOutput", outDepth);
                shader.SetTexture(kernel, "_SdfDiffuseOutput", outResolved);
                if (useMoments)
                {
                    shader.SetInt("_SdfScreenProbeMomentsHistoryValid", hasHistory ? 1 : 0);
                    shader.SetTexture(kernel, "_SdfScreenProbePreviousMoments", prevMoments);
                    shader.SetTexture(kernel, "_SdfScreenProbeMomentsOutput", outMoments);
                }

                shader.Dispatch(kernel, width / 8, height / 8, 1);

                Result res = default;
                if (readResult)
                {
                    var reqOut = AsyncGPUReadback.Request(outResolved, 0, GraphicsFormat.R32G32B32A32_SFloat);
                    var reqFast = AsyncGPUReadback.Request(outFast, 0);
                    reqOut.WaitForCompletion();
                    reqFast.WaitForCompletion();
                    Assert.That(reqOut.hasError || reqFast.hasError, Is.False);

                    float[] momentsData = null;
                    if (useMoments)
                    {
                        var reqMom = AsyncGPUReadback.Request(outMoments, 0, GraphicsFormat.R32G32B32A32_SFloat);
                        reqMom.WaitForCompletion();
                        Assert.That(reqMom.hasError, Is.False);
                        momentsData = reqMom.GetData<float>().ToArray();
                    }

                    byte[] rawFast = reqFast.GetData<byte>().ToArray();
                    var fastVals = new float[rawFast.Length];
                    var numFramesVals = new float[rawFast.Length];
                    for (int i = 0; i < rawFast.Length; i++)
                    {
                        float encF = Mathf.Floor(rawFast[i] / 16f);
                        float encN = rawFast[i] - encF * 16f;
                        fastVals[i] = encF / 15f;
                        numFramesVals[i] = encN == 15f ? 10f : encN / 15f * 10f;
                    }

                    res = new Result
                    {
                        OutputRgba = reqOut.GetData<float>().ToArray(),
                        MomentsRgba = momentsData,
                        FastUpdate = fastVals,
                        NumFrames = numFramesVals
                    };
                }

                (prevFast, outFast) = (outFast, prevFast);
                (prevLighting, outLighting) = (outLighting, prevLighting);
                (prevGeom, outGeom) = (outGeom, prevGeom);
                (prevDepth, outDepth) = (outDepth, prevDepth);
                (prevMoments, outMoments) = (outMoments, prevMoments);
                return res;
            }

            public void Dispose()
            {
                foreach (Object obj in owned)
                {
                    if (obj is RenderTexture rt) rt.Release();
                    Object.DestroyImmediate(obj);
                }
            }
        }
    }
}
