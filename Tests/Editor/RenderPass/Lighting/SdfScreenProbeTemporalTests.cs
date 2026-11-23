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
                Assert.That(settings.screenProbeTemporalRejectBasedOnNormal.value, Is.True);
                Assert.That(settings.screenProbeTemporalPlaneDisocclusion.value, Is.True);
                Assert.That(settings.screenProbeCoherentRadianceFastUpdate.value, Is.True);
                Assert.That(settings.screenProbeDisocclusionSpatialFilter.value, Is.True);
                Assert.That(settings.screenProbeRoughSpecular.value, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void FullSphereGather_RetainsTemporalHistoryAcrossLightingAndSkyChanges()
        {
            Assert.That(SdfDiffuseGatherPass.AllowsContinuousLightingHistory(
                fullSphere: true, preserveRelitHistory: false, matchesLighting: false), Is.True,
                "UE Lumen FullSphere Screen Probe Gather must keep pixel temporal and directional guide history through dynamic light/sky changes.");
            Assert.That(SdfDiffuseGatherPass.AllowsContinuousLightingHistory(
                fullSphere: false, preserveRelitHistory: false, matchesLighting: false), Is.False,
                "Legacy non-FullSphere gather must retain its explicit lighting invalidation when not rescaling.");
            Assert.That(SdfDiffuseGatherPass.AllowsContinuousLightingHistory(
                fullSphere: false, preserveRelitHistory: true, matchesLighting: false), Is.True);
            Assert.That(SdfDiffuseGatherPass.AllowsContinuousLightingHistory(
                fullSphere: false, preserveRelitHistory: false, matchesLighting: true), Is.True);
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

        [Test]
        public void DisocclusionSpatialFilter_SmoothsFreshPixelsWithoutBleedingAcrossEdgesOrAffectingSteadyHistory()
        {
            RequireComputeAndReversedZ();
            var wallNormal = new Vector3(0f, 0f, -1f);
            var sideNormal = new Vector3(1f, 0f, 0f);

            // 1. Fresh disocclusion (hasHistory = false): checkerboard noise on coplanar wall at x >= 8
            //    adjacent to a bright perpendicular foreground occluder at x < 8.
            using var disoccludedFixture = new TemporalFixture(16, 16, useMoments: false);
            disoccludedFixture.SetCurrentFrame(
                (u, _) => u * 16f < 8f ? 3.0f : 8.0f,
                (x, _) => x < 8 ? sideNormal : wallNormal,
                (x, y) => x < 8
                    ? new Color(10.0f, 10.0f, 10.0f, 1f)
                    : (((x + y) & 1) == 0 ? new Color(1.0f, 1.0f, 1.0f, 1f) : new Color(0.2f, 0.2f, 0.2f, 1f)),
                moving: 0.004f);

            var filtered = disoccludedFixture.Step(
                hasHistory: false, jitterDeltaUv: Vector2.zero, clampEnabled: false,
                disocclusionSpatialFilter: true);

            // Interior coplanar pixel (12, 8) has center 1.0 surrounded by 4 axial 0.2 and 4 diagonal 1.0:
            // weighted avg = (1.0*1 + 4*0.75*0.2 + 4*0.5*1.0) / 6.0 = 3.6 / 6.0 = 0.6.
            int interior = 8 * 16 + 12;
            Assert.That(filtered.OutputRgba[interior * 4], Is.EqualTo(0.6f).Within(0.02f),
                "Disocclusion 3x3 bilateral filter must smooth single-frame stochastic noise on coplanar pixels.");

            // Edge pixel (8, 8) borders foreground occluder at x = 7 (value 10.0, depth 3.0 vs 8.0, perpendicular normal).
            int edgePixel = 8 * 16 + 8;
            Assert.That(filtered.OutputRgba[edgePixel * 4], Is.LessThan(0.85f),
                "Disocclusion filter must reject foreground depth/normal discontinuities without bleeding bright values.");

            // 2. Steady history (age == 10 >= 2): filter must NOT run at all.
            using var steadyFixture = new TemporalFixture(16, 16, useMoments: false);
            steadyFixture.WarmupSteadyHistory(
                (_, _) => 8.0f,
                (_, _) => wallNormal,
                (_, _) => new Color(0.5f, 0.5f, 0.5f, 1f),
                frames: 12);
            steadyFixture.SetCurrentFrame(
                (_, _) => 8.0f,
                (_, _) => wallNormal,
                (x, y) => x == 8 && y == 8 ? new Color(1.0f, 1.0f, 1.0f, 1f) : new Color(0.0f, 0.0f, 0.0f, 1f),
                moving: 0.004f);
            var steadyFiltered = steadyFixture.Step(
                hasHistory: true, jitterDeltaUv: Vector2.zero, clampEnabled: false,
                disocclusionSpatialFilter: true);
            int center = 8 * 16 + 8;
            float expectedSteady = Mathf.Lerp(0.5f, 1.0f, 1f / 11f);
            Assert.That(steadyFiltered.OutputRgba[center * 4], Is.EqualTo(expectedSteady).Within(0.006f),
                "Steady pixels (numFramesAccumulated >= 2) must skip the disocclusion spatial filter completely.");
        }

        [Test]
        public void PreExposedHistory_PreservesHighLuxRadianceAcrossExposureChange()
        {
            RequireComputeAndReversedZ();
            using var fixture = new TemporalFixture(16, 16, useMoments: true);
            var wallNormal = new Vector3(0f, 0f, -1f);

            // Simulate a 100,000 Lux sunlit indirect bounce (~25,000 unexposed linear radiance):
            // Warmup at prevPreExposure = 0.0004 (PreExposed value = 10.0, clean FP16 precision).
            fixture.SetCurrentFrame(
                (_, _) => 8.0f,
                (_, _) => wallNormal,
                (_, _) => new Color(25000f, 20000f, 15000f, 1f),
                moving: 0.004f);
            for (int f = 0; f < 12; f++)
            {
                fixture.Step(
                    hasHistory: f > 0, jitterDeltaUv: Vector2.zero, clampEnabled: false,
                    readResult: false, preExposure: 0.0004f);
            }

            // Now jump PreExposure 8x (0.0004 -> 0.0032, 3 EV shift) while physical radiance stays constant.
            var output = fixture.Step(
                hasHistory: true, jitterDeltaUv: Vector2.zero, clampEnabled: false,
                readResult: true, preExposure: 0.0032f);

            int center = 8 * 16 + 8;
            Assert.That(output.NumFrames[center], Is.EqualTo(10f).Within(0.01f));
            Assert.That(output.OutputRgba[center * 4 + 0], Is.EqualTo(25000f).Within(64f),
                "PreExposed history rescaled by currentPreExposure/prevPreExposure must remain invariant to an 8x EV jump.");
            Assert.That(output.OutputRgba[center * 4 + 1], Is.EqualTo(20000f).Within(64f));
            Assert.That(output.OutputRgba[center * 4 + 2], Is.EqualTo(15000f).Within(64f));
        }

        [Test]
        public void RoughSpecularTemporal_AccumulatesPreExposedRadianceAndSkyVisibilityAcrossExposureChange()
        {
            RequireComputeAndReversedZ();
            using var fixture = new TemporalFixture(16, 16, useMoments: false);
            var wallNormal = new Vector3(0f, 0f, -1f);

            // Warmup steady indoor colonnade: warm golden rough specular radiance (2000, 1500, 1000)
            // with occluded sky visibility (0.0) at PreExposure = 0.001 (stored PreExposed = 2.0, 1.5, 1.0).
            fixture.SetCurrentFrame(
                (_, _) => 8.0f,
                (_, _) => wallNormal,
                (_, _) => new Color(2000f, 1500f, 1000f, 1f),
                moving: 0.004f,
                roughSpecularAtPixel: (_, _) => new Color(2000f, 1500f, 1000f, 0.0f));
            for (int f = 0; f < 12; f++)
            {
                fixture.Step(
                    hasHistory: f > 0, jitterDeltaUv: Vector2.zero, clampEnabled: false,
                    readResult: false, preExposure: 0.001f, roughSpecularEnabled: true);
            }

            // Jump PreExposure 4x (0.001 -> 0.004) while rough specular stays constant and skyVis steps to 0.55.
            fixture.SetCurrentFrame(
                (_, _) => 8.0f,
                (_, _) => wallNormal,
                (_, _) => new Color(2000f, 1500f, 1000f, 1f),
                moving: 0.004f,
                roughSpecularAtPixel: (_, _) => new Color(2000f, 1500f, 1000f, 0.55f));
            var output = fixture.Step(
                hasHistory: true, jitterDeltaUv: Vector2.zero, clampEnabled: false,
                readResult: true, preExposure: 0.004f, roughSpecularEnabled: true);

            int center = 8 * 16 + 8;
            // Stored in PreExposed space at 0.004 => (8.0, 6.0, 4.0) and skyVis = lerp(0.0, 0.55, 1/11) = 0.05.
            Assert.That(output.RoughSpecularRgba[center * 4 + 0], Is.EqualTo(8.0f).Within(0.05f),
                "Rough specular temporal history must rescale cleanly across PreExposure changes.");
            Assert.That(output.RoughSpecularRgba[center * 4 + 1], Is.EqualTo(6.0f).Within(0.05f));
            Assert.That(output.RoughSpecularRgba[center * 4 + 2], Is.EqualTo(4.0f).Within(0.05f));
            Assert.That(output.RoughSpecularRgba[center * 4 + 3], Is.EqualTo(0.05f).Within(0.006f),
                "Directional sky visibility must accumulate with the same temporal sample weight as radiance.");
        }

        [Test]
        public void CoherentRadianceFastUpdate_ReducesLagOnCoherentShiftWithoutDisturbingNoisyStaticNeighborhood()
        {
            RequireComputeAndReversedZ();
            int center = 8 * 16 + 8;
            var wallNormal = new Vector3(0f, 0f, -1f);

            // 1. Spatially coherent radiance drop (0.9 -> 0.25 across the 3x3 neighborhood):
            //    bit 2 (reprojectionFlags = 4) must pull output much closer to 0.25 than pure 1/11 lag (~0.84).
            using var shiftFixture = new TemporalFixture(16, 16, useMoments: false);
            shiftFixture.WarmupSteadyHistory(
                (_, _) => 6.0f,
                (_, _) => wallNormal,
                (_, _) => new Color(0.9f, 0.9f, 0.9f, 1f),
                frames: 12);
            shiftFixture.SetCurrentFrame(
                (_, _) => 6.0f,
                (_, _) => wallNormal,
                (_, _) => new Color(0.25f, 0.25f, 0.25f, 1f),
                moving: 0.004f);
            var shifted = shiftFixture.Step(hasHistory: true, jitterDeltaUv: Vector2.zero, clampEnabled: false, reprojectionFlags: 4);
            Assert.That(shifted.OutputRgba[center * 4], Is.LessThan(0.76f),
                "Spatially coherent radiance jump must accelerate temporal convergence smoothly without 3x3 hard-clamping blotches.");
            Assert.That(shifted.NumFrames[center], Is.LessThan(5.0f),
                "Spatially coherent radiance jump must shorten effective temporal history window.");

            // 2. High-variance single-frame checkerboard noise centered around history (0.5):
            //    coherence gate must suppress fast-update so steady history remains smooth.
            using var noisyFixture = new TemporalFixture(16, 16, useMoments: false);
            noisyFixture.WarmupSteadyHistory(
                (_, _) => 6.0f,
                (_, _) => wallNormal,
                (_, _) => new Color(0.5f, 0.5f, 0.5f, 1f),
                frames: 12);
            noisyFixture.SetCurrentFrame(
                (_, _) => 6.0f,
                (_, _) => wallNormal,
                (x, y) => ((x + y) & 1) == 0 ? new Color(0.9f, 0.9f, 0.9f, 1f) : new Color(0.1f, 0.1f, 0.1f, 1f),
                moving: 0.004f);
            var noisy = noisyFixture.Step(hasHistory: true, jitterDeltaUv: Vector2.zero, clampEnabled: false, reprojectionFlags: 4);
            Assert.That(noisy.OutputRgba[center * 4], Is.EqualTo(Mathf.Lerp(0.5f, 0.9f, 1f / 11f)).Within(0.02f),
                "Noisy high-variance neighborhood centered on history must not trigger coherent fast-update.");
        }

        [Test]
        public void RoughSpecularTemporal_ClampsParallaxGhostingWhilePreservingSkyVisibility()
        {
            RequireComputeAndReversedZ();
            int center = 8 * 16 + 8;
            var wallNormal = new Vector3(0f, 0f, -1f);

            using var fixture = new TemporalFixture(16, 16, useMoments: false);
            fixture.SetCurrentFrame(
                (_, _) => 6.0f,
                (_, _) => wallNormal,
                (_, _) => new Color(0.4f, 0.4f, 0.4f, 1f),
                moving: 0.004f,
                roughSpecularAtPixel: (_, _) => new Color(2.0f, 2.0f, 2.0f, 0.0f));
            for (int f = 0; f < 12; f++)
            {
                fixture.Step(hasHistory: f > 0, jitterDeltaUv: Vector2.zero, clampEnabled: false,
                    readResult: false, roughSpecularEnabled: true);
            }

            // Specular highlight moves off-surface (2.0 -> 0.2) while skyVis stays 0.0 and camera translates.
            fixture.SetCurrentFrame(
                (_, _) => 6.0f,
                (_, _) => wallNormal,
                (_, _) => new Color(0.4f, 0.4f, 0.4f, 1f),
                moving: 0.004f,
                roughSpecularAtPixel: (_, _) => new Color(0.2f, 0.2f, 0.2f, 0.55f));
            var output = fixture.Step(
                hasHistory: true, jitterDeltaUv: Vector2.zero, clampEnabled: false,
                readResult: true, roughSpecularEnabled: true,
                previousCameraPosition: new Vector4(0.5f, 0f, 0f, 1f));

            Assert.That(output.RoughSpecularRgba[center * 4], Is.LessThan(0.35f),
                "Tonemapped YCoCg neighborhood clamp + view-parallax decay must cut Rough Specular ghost trails.");
            Assert.That(output.RoughSpecularRgba[center * 4 + 3], Is.EqualTo(0.05f).Within(0.006f),
                "View-independent sky visibility must keep Diffuse GI's smooth accumulation weight.");
        }

        [Test]
        public void RoughSpecularTemporal_StaticCameraSuppressesProbeFlickerBoiling()
        {
            RequireComputeAndReversedZ();
            int center = 8 * 16 + 8;
            var wallNormal = new Vector3(0f, 0f, -1f);

            using var fixture = new TemporalFixture(16, 16, useMoments: false);
            fixture.SetCurrentFrame(
                (_, _) => 6.0f,
                (_, _) => wallNormal,
                (_, _) => new Color(0.4f, 0.4f, 0.4f, 1f),
                moving: 0.004f,
                roughSpecularAtPixel: (_, _) => new Color(2.0f, 2.0f, 2.0f, 0.0f));
            for (int f = 0; f < 12; f++)
            {
                fixture.Step(hasHistory: f > 0, jitterDeltaUv: Vector2.zero, clampEnabled: false,
                    readResult: false, reprojectionFlags: 8, roughSpecularEnabled: true);
            }

            // Simulate a single-frame 16x16 Screen Probe jitter drop across the entire 3x3 pixel neighborhood
            // under a static camera and static lighting (bit 3 = CAMERA_STATIC, bit 2 = 0, parallax = 0).
            fixture.SetCurrentFrame(
                (_, _) => 6.0f,
                (_, _) => wallNormal,
                (_, _) => new Color(0.4f, 0.4f, 0.4f, 1f),
                moving: 0.004f,
                roughSpecularAtPixel: (_, _) => new Color(0.15f, 0.15f, 0.15f, 0.0f));
            var output = fixture.Step(
                hasHistory: true, jitterDeltaUv: Vector2.zero, clampEnabled: false,
                readResult: true, reprojectionFlags: 8, roughSpecularEnabled: true);

            Assert.That(output.RoughSpecularRgba[center * 4], Is.GreaterThan(1.80f),
                "Static camera rough specular history must not collapse on probe-wide single-frame flicker.");
        }

        [Test]
        public void RoughSpecularTemporal_DynamicLightingChangeSmoothlyConvergesWithoutProbeCollapse()
        {
            RequireComputeAndReversedZ();
            int center = 8 * 16 + 8;
            var wallNormal = new Vector3(0f, 0f, -1f);

            using var fixture = new TemporalFixture(16, 16, useMoments: false);
            fixture.SetCurrentFrame(
                (_, _) => 6.0f,
                (_, _) => wallNormal,
                (_, _) => new Color(0.4f, 0.4f, 0.4f, 1f),
                moving: 0.004f,
                roughSpecularAtPixel: (_, _) => new Color(2.0f, 2.0f, 2.0f, 0.0f));
            for (int f = 0; f < 12; f++)
            {
                fixture.Step(hasHistory: f > 0, jitterDeltaUv: Vector2.zero, clampEnabled: false,
                    readResult: false, reprojectionFlags: 8, roughSpecularEnabled: true);
            }

            // During dynamic lighting changes (bit 2 = 4) on a static camera (bit 3 = 8, parallax = 0),
            // a single-frame 16x16 probe-wide dropout must NOT hard-clamp history to 0.15 in one frame.
            fixture.SetCurrentFrame(
                (_, _) => 6.0f,
                (_, _) => wallNormal,
                (_, _) => new Color(0.4f, 0.4f, 0.4f, 1f),
                moving: 0.004f,
                roughSpecularAtPixel: (_, _) => new Color(0.15f, 0.15f, 0.15f, 0.0f));
            var oneFrame = fixture.Step(
                hasHistory: true, jitterDeltaUv: Vector2.zero, clampEnabled: false,
                readResult: true, reprojectionFlags: 4 | 8, roughSpecularEnabled: true);
            Assert.That(oneFrame.RoughSpecularRgba[center * 4], Is.GreaterThan(1.50f),
                "Dynamic lighting flag must not hard-clamp rough specular history to single-frame probe dropouts.");

            // Sustained new lighting over 10 frames must still converge smoothly without excessive lag.
            TemporalOutput sustained = oneFrame;
            for (int f = 0; f < 10; f++)
            {
                sustained = fixture.Step(
                    hasHistory: true, jitterDeltaUv: Vector2.zero, clampEnabled: false,
                    readResult: f == 9, reprojectionFlags: 4 | 8, roughSpecularEnabled: true);
            }
            Assert.That(sustained.RoughSpecularRgba[center * 4], Is.LessThan(0.65f),
                "Dynamic lighting rough specular must still converge promptly over sustained lighting changes.");
        }

        [Test]
        public void DynamicLightingFastUpdateWindow_EngagesOnlyOnLightingChangesAndDetectsCameraMotion()
        {
            Assert.That(SdfDiffuseGatherPass.StepDynamicLightingFastUpdateWindow(0, hasPreviousState: false, lightingChanged: true), Is.Zero);
            Assert.That(SdfDiffuseGatherPass.StepDynamicLightingFastUpdateWindow(0, hasPreviousState: true, lightingChanged: false), Is.Zero);
            int frames = SdfDiffuseGatherPass.StepDynamicLightingFastUpdateWindow(0, hasPreviousState: true, lightingChanged: true);
            Assert.That(frames, Is.EqualTo(8));
            Assert.That(SdfDiffuseGatherPass.StepDynamicLightingFastUpdateWindow(frames, hasPreviousState: true, lightingChanged: false), Is.EqualTo(7));

            var pos = new Vector4(4f, 0.5f, -7f, 1f);
            var fwd = new Vector4(0f, 0f, 1f, 0f);
            Assert.That(SdfDiffuseGatherPass.HasCameraMotion(pos, pos, fwd, fwd), Is.False);
            Assert.That(SdfDiffuseGatherPass.HasCameraMotion(pos, pos + new Vector4(0.01f, 0f, 0f, 0f), fwd, fwd), Is.True);
            Assert.That(SdfDiffuseGatherPass.HasCameraMotion(pos, pos, fwd, new Vector4(0.01f, 0f, 1f, 0f).normalized), Is.True);
        }

        [Test]
        public void StaticCameraSubpixelBevel_RetainsHistoryUnderPlaneAndNormalRejection()
        {
            RequireComputeAndReversedZ();
            int center = 8 * 16 + 8;
            var normalA = new Vector3(0.6f, 0f, -0.8f).normalized;
            var normalB = new Vector3(-0.6f, 0f, -0.8f).normalized; // dot(A, B) = 0.28 < 0.5

            using var fixture = new TemporalFixture(16, 16, useMoments: false);
            fixture.WarmupSteadyHistory(
                (_, _) => 6.0f,
                (_, _) => normalA,
                (_, _) => new Color(0.85f, 0.85f, 0.85f, 1f),
                frames: 12,
                reprojectionFlags: 1 | 2 | 8);
            fixture.SetCurrentFrame(
                (_, _) => 6.0f,
                (_, _) => normalB,
                (_, _) => new Color(0.25f, 0.25f, 0.25f, 1f),
                moving: 0.004f);

            // Bit 3 (CAMERA_STATIC) with zero motion vector represents pure subpixel TAA/TSR jitter on a thin bevel.
            var staticOut = fixture.Step(hasHistory: true, jitterDeltaUv: new Vector2(0.35f / 16f, 0f),
                clampEnabled: false, reprojectionFlags: 1 | 2 | 8);
            Assert.That(staticOut.NumFrames[center], Is.EqualTo(10f).Within(0.01f),
                "Static camera subpixel jitter must retain full history on thin bevels without normal-rejection flicker.");
            // Steady static pixels also use the extended 2.5x convergence window (1 / (1 + 25) instead of 1 / 11).
            float expectedStaticBlend = Mathf.Lerp(0.85f, 0.25f, 1f / 26f);
            Assert.That(staticOut.OutputRgba[center * 4], Is.EqualTo(expectedStaticBlend).Within(0.008f),
                "Converged static camera pixels must damp residual probe flicker with the extended steady-state weight.");
        }

        [Test]
        public void DisocclusionSpatialFilter_FillsUncoveredZeroCoverageHoleFromCoplanarNeighbors()
        {
            RequireComputeAndReversedZ();
            var wallNormal = new Vector3(0f, 0f, -1f);
            var sideNormal = new Vector3(1f, 0f, 0f);

            using var fixture = new TemporalFixture(16, 16, useMoments: false);
            fixture.SetCurrentFrame(
                (u, _) => u * 16f < 6f ? 3.0f : 8.0f,
                (x, _) => x < 6 ? sideNormal : wallNormal,
                (x, y) =>
                {
                    if (x < 6)
                        return new Color(10.0f, 10.0f, 10.0f, 1f);
                    // Uncovered black hole (alpha = 0) at (7, 8) adjacent to valid coplanar wall pixels (0.6).
                    if (x == 7 && y == 8)
                        return new Color(0.0f, 0.0f, 0.0f, 0f);
                    return new Color(0.6f, 0.6f, 0.6f, 1f);
                },
                moving: 0.004f);

            var output = fixture.Step(
                hasHistory: false, jitterDeltaUv: Vector2.zero, clampEnabled: false,
                disocclusionSpatialFilter: true);

            int holePixel = 8 * 16 + 7;
            Assert.That(output.OutputRgba[holePixel * 4], Is.EqualTo(0.6f).Within(0.02f),
                "Uncovered zero-coverage disoccluded pixel must be filled from valid coplanar neighbors without foreground bleed.");
            Assert.That(output.OutputRgba[holePixel * 4 + 3], Is.GreaterThan(0.9f),
                "Filled hole pixel must emit valid positive coverage.");
        }

        [Test]
        public void SlopedWallMotion_RetainsHistoryUnderPlaneDisocclusionWithoutLinearDepthDrops()
        {
            RequireComputeAndReversedZ();
            // Moderately sloped wall (N.V ~ 0.45): 1-pixel motion changes linear depth by ~2.5%
            // (> 1% raw distanceThreshold, < 3x grazing-expanded guard) while plane distance == 0.
            var slopedNormal = new Vector3(0.89f, 0f, -0.455f).normalized;
            var planeAnchor = new Vector3(0f, 0f, 8f);

            using var fixture = new TemporalFixture(16, 16, useMoments: false);
            fixture.WarmupSteadyHistory(
                (u, v) => fixture.IntersectPlaneWorldZ(u, v, planeAnchor, slopedNormal),
                (_, _) => slopedNormal,
                (_, _) => new Color(0.8f, 0.6f, 0.4f, 1f),
                frames: 12,
                reprojectionFlags: 1 | 2);

            var jitterDeltaUv = new Vector2(0.45f / 16f, -0.35f / 16f);
            fixture.SetCurrentFrame(
                (u, v) => fixture.IntersectPlaneWorldZ(u, v, planeAnchor, slopedNormal),
                (_, _) => slopedNormal,
                (_, _) => new Color(0.2f, 0.2f, 0.2f, 1f),
                moving: 0.004f,
                currentJitterUv: -jitterDeltaUv);

            var output = fixture.Step(hasHistory: true, jitterDeltaUv: jitterDeltaUv, clampEnabled: false,
                reprojectionFlags: 1 | 2);

            for (int y = 4; y < 12; y++)
            {
                for (int x = 4; x < 12; x++)
                {
                    int idx = y * 16 + x;
                    Assert.That(output.NumFrames[idx], Is.EqualTo(10f).Within(0.01f),
                        $"Moving sloped wall pixel ({x},{y}) must retain full history without salt-and-pepper drops.");
                }
            }
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

            private readonly Texture2D depthTex, gbuffer0, gbuffer1, motionTex, resolvedTex, roughSpecTex, movingTex, scalarNoise;
            private RenderTexture prevFast, outFast, prevLighting, outLighting, prevGeom, outGeom, prevDepth, outDepth, prevMoments, outMoments;
            private RenderTexture prevRoughSpec, outRoughSpec;
            private RenderTexture prevPreExposure, outPreExposure;
            private readonly ComputeBuffer preExposureBuffer;
            private readonly Vector4[] preExposureScratch = new Vector4[1];
            private readonly RenderTexture outResolved;

            internal struct Result
            {
                public float[] OutputRgba;
                public float[] RoughSpecularRgba;
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
                roughSpecTex = CreateTex2D(GraphicsFormat.R32G32B32A32_SFloat);
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
                prevRoughSpec = CreateRT(GraphicsFormat.R16G16B16A16_SFloat);
                outRoughSpec = CreateRT(GraphicsFormat.R16G16B16A16_SFloat);
                prevPreExposure = CreateRT(GraphicsFormat.R32_SFloat, 1, 1);
                outPreExposure = CreateRT(GraphicsFormat.R32_SFloat, 1, 1);
                preExposureBuffer = new ComputeBuffer(1, sizeof(float) * 4);
                preExposureScratch[0] = new Vector4(1f, 1f, 0f, 0f);
                preExposureBuffer.SetData(preExposureScratch);
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

            private RenderTexture CreateRT(GraphicsFormat format, int w = -1, int h = -1)
            {
                var rt = new RenderTexture(new RenderTextureDescriptor(w > 0 ? w : width, h > 0 ? h : height)
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
                Vector2 currentJitterUv = default,
                Func<int, int, Color> roughSpecularAtPixel = null)
            {
                var depths = new float[width * height];
                var gb0 = new byte[width * height * 4];
                var gb1 = new byte[width * height * 4];
                var motions = new float[width * height * 4];
                var cRes = new float[width * height * 4];
                var cSpec = new float[width * height * 4];
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
                        Color s = roughSpecularAtPixel != null ? roughSpecularAtPixel(x, y) : Color.clear;
                        cSpec[i * 4 + 0] = s.r;
                        cSpec[i * 4 + 1] = s.g;
                        cSpec[i * 4 + 2] = s.b;
                        cSpec[i * 4 + 3] = s.a;
                        cMov[i] = moving;
                    }
                }

                depthTex.SetPixelData(depths, 0); depthTex.Apply(false, false);
                gbuffer0.SetPixelData(gb0, 0); gbuffer0.Apply(false, false);
                gbuffer1.SetPixelData(gb1, 0); gbuffer1.Apply(false, false);
                motionTex.SetPixelData(motions, 0); motionTex.Apply(false, false);
                resolvedTex.SetPixelData(cRes, 0); resolvedTex.Apply(false, false);
                roughSpecTex.SetPixelData(cSpec, 0); roughSpecTex.Apply(false, false);
                movingTex.SetPixelData(cMov, 0); movingTex.Apply(false, false);
            }

            internal Result Step(bool hasHistory, Vector2 jitterDeltaUv, bool clampEnabled,
                bool readResult = true, int reprojectionFlags = 0, bool disocclusionSpatialFilter = false,
                float preExposure = 0f, bool roughSpecularEnabled = false, Vector4? previousCameraPosition = null)
            {
                SdfDiffuseGatherPass.GetHistoryScreenCoordCodec(
                    width, height, out var subPixel, out var shift, out var gatherMinMax);

                var currJitterMat = Matrix4x4.identity;
                currJitterMat.m03 = -2f * jitterDeltaUv.x;
                currJitterMat.m13 = -2f * jitterDeltaUv.y;
                var currVp = GL.GetGPUProjectionMatrix(currJitterMat * proj, true) * view;
                var currInvVp = currVp.inverse;

                bool usePreExposure = preExposure > 0f;
                if (usePreExposure)
                {
                    preExposureScratch[0] = new Vector4(preExposure, 1f / preExposure, 0f, 0f);
                    preExposureBuffer.SetData(preExposureScratch);
                }

                shader.SetVector("_SdfDiffuseScreenSize", new Vector4(width, height, 1f / width, 1f / height));
                shader.SetMatrix("_SdfDiffuseInverseVP", currInvVp);
                shader.SetMatrix("_SdfDiffuseVP", currVp);
                shader.SetMatrix("_SdfDiffusePreviousVP", vp);
                shader.SetMatrix("_SdfScreenPreviousInverseVP", invVp);
                shader.SetVector("_SdfProbeCameraForward", new Vector4(0f, 0f, 1f, 0f));
                shader.SetVector("_SdfProbePreviousCameraForward", new Vector4(0f, 0f, 1f, 0f));
                shader.SetVector("_SdfProbeCameraPosition", new Vector4(0f, 0f, 0f, 1f));
                shader.SetVector("_SdfProbePreviousCameraPosition", previousCameraPosition ?? new Vector4(0f, 0f, 0f, 1f));
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
                shader.SetInt("_SdfPreExposureHistoryEnabled", usePreExposure ? 1 : 0);
                shader.SetInt("_SdfPreviousPreExposureValid", usePreExposure && hasHistory ? 1 : 0);
                shader.SetInt("_SdfRoughSpecularEnabled", roughSpecularEnabled ? 1 : 0);
                shader.SetInt("_SdfPreviousRoughSpecularValid", roughSpecularEnabled && hasHistory ? 1 : 0);
                shader.SetInt("_SdfDisocclusionSpatialFilter", disocclusionSpatialFilter ? 1 : 0);

                shader.SetBuffer(kernel, "_VividAutoExposurePreExposureBuffer", preExposureBuffer);
                shader.SetTexture(kernel, "_SdfDiffuseDepth", depthTex);
                shader.SetTexture(kernel, "_SdfDiffuseGBuffer0", gbuffer0);
                shader.SetTexture(kernel, "_SdfDiffuseGBuffer1", gbuffer1);
                shader.SetTexture(kernel, "_SdfScreenMotion", motionTex);
                shader.SetTexture(kernel, "_SdfDiffuseCurrentResolved", resolvedTex);
                shader.SetTexture(kernel, "_SdfCurrentRoughSpecular", roughSpecTex);
                shader.SetTexture(kernel, "_SdfPreviousRoughSpecular", prevRoughSpec);
                shader.SetTexture(kernel, "_SdfRoughSpecularHistoryOutput", outRoughSpec);
                shader.SetTexture(kernel, "_SdfResolvedMoving", movingTex);
                shader.SetTexture(kernel, "_SdfDiffusePreviousPixelLighting", prevLighting);
                shader.SetTexture(kernel, "_SdfDiffusePreviousPixelGeometry", prevGeom);
                shader.SetTexture(kernel, "_SdfDiffusePreviousPixelDepth", prevDepth);
                shader.SetTexture(kernel, "_SdfPreviousPreExposure", prevPreExposure);
                shader.SetTexture(kernel, "_SdfPreviousFastUpdate", prevFast);
                shader.SetTexture(kernel, "_SdfTemporalScalarNoise", scalarNoise);
                shader.SetTexture(kernel, "_SdfFastUpdateOutput", outFast);
                shader.SetTexture(kernel, "_SdfDiffusePixelLightingOutput", outLighting);
                shader.SetTexture(kernel, "_SdfDiffusePixelGeometryOutput", outGeom);
                shader.SetTexture(kernel, "_SdfDiffusePixelDepthOutput", outDepth);
                shader.SetTexture(kernel, "_SdfCurrentPreExposureOutput", outPreExposure);
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

                    float[] roughSpecData = null;
                    if (roughSpecularEnabled)
                    {
                        var reqSpec = AsyncGPUReadback.Request(outRoughSpec, 0, GraphicsFormat.R32G32B32A32_SFloat);
                        reqSpec.WaitForCompletion();
                        Assert.That(reqSpec.hasError, Is.False);
                        roughSpecData = reqSpec.GetData<float>().ToArray();
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
                        RoughSpecularRgba = roughSpecData,
                        MomentsRgba = momentsData,
                        FastUpdate = fastVals,
                        NumFrames = numFramesVals
                    };
                }

                (prevFast, outFast) = (outFast, prevFast);
                (prevLighting, outLighting) = (outLighting, prevLighting);
                (prevGeom, outGeom) = (outGeom, prevGeom);
                (prevDepth, outDepth) = (outDepth, prevDepth);
                (prevPreExposure, outPreExposure) = (outPreExposure, prevPreExposure);
                (prevRoughSpec, outRoughSpec) = (outRoughSpec, prevRoughSpec);
                (prevMoments, outMoments) = (outMoments, prevMoments);
                return res;
            }

            public void Dispose()
            {
                preExposureBuffer?.Dispose();
                foreach (Object obj in owned)
                {
                    if (obj is RenderTexture rt) rt.Release();
                    Object.DestroyImmediate(obj);
                }
            }
        }
    }
}
