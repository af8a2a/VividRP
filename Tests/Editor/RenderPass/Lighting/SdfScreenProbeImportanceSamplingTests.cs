using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime.RenderPass.Core;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class SdfScreenProbeImportanceSamplingTests
    {
        private const string ImportanceSamplingShaderPath =
            "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/SdfScreenProbeImportanceSampling.compute";

        private const uint SphereRayLevel1Base = 64u;
        private const uint SphereRayLevel2Base = 320u;
        private const uint SphereRayCulled = 2047u;

        [Test]
        public void DefaultSettings_EnableAdaptiveRayBudgetWithMultiLevelImportanceSampling()
        {
            var settings = ScriptableObject.CreateInstance<SdfDiffuseGISettings>();
            try
            {
                Assert.That(settings.screenProbeAdaptiveRayBudget.value, Is.True);
                Assert.That(settings.screenProbeImportanceSubdivisionLevels.value, Is.EqualTo(2));
                Assert.That(settings.screenProbeImportanceVarianceWeight.value, Is.EqualTo(1.0f).Within(1e-5f));
                Assert.That(settings.screenProbeMinAdaptiveRayFraction.value, Is.EqualTo(0.5f).Within(1e-5f));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void DefaultSettings_ConvergeMatureLumenPipelineDefaults()
        {
            var settings = ScriptableObject.CreateInstance<SdfDiffuseGISettings>();
            try
            {
                Assert.That(settings.enabled.value, Is.True);
                Assert.That(settings.gatherMethod.value, Is.EqualTo(SdfDiffuseGatherMethod.ScreenProbeGather));
                Assert.That(settings.traceMode.value, Is.EqualTo(SdfDiffuseTraceMode.Hybrid));
                Assert.That(settings.lumenScreenTracing.value, Is.True);
                Assert.That(settings.segmentedSdfTracing.value, Is.True);
                Assert.That(settings.occludedSky.value, Is.True);
                Assert.That(settings.farRadianceCache.value, Is.True);
                Assert.That(settings.surfaceCacheBounceOrders.value, Is.EqualTo(3));
                Assert.That(settings.surfaceCacheRadiositySamples.value, Is.EqualTo(32));
                Assert.That(settings.surfaceCacheRadiosityAccumulate.value, Is.True);
                Assert.That(settings.surfaceCacheUpdateBoost.value, Is.EqualTo(2));
                Assert.That(settings.surfaceCacheRadiosityBatchLimit.value, Is.EqualTo(4));
                Assert.That(settings.nearOcclusionStrength.value, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(settings.screenProbeBentNormalAO.value, Is.True);
                Assert.That(settings.directionalNearOcclusion.value, Is.False);
                Assert.That(settings.screenProbeFullSphere.value, Is.True);
                Assert.That(settings.screenProbeDitherTraceDirections.value, Is.True);
                Assert.That(settings.screenProbeStochasticInterpolation.value, Is.True);
                Assert.That(settings.screenProbeTemporalFilter.value, Is.False);
                Assert.That(settings.screenProbeSamplingGuide.value, Is.True);
                Assert.That(settings.adaptiveScreenProbes.value, Is.True);
                Assert.That(settings.screenProbeSpatialFilter.value, Is.True);
                Assert.That(settings.pixelTemporal.value, Is.True);
                Assert.That(settings.pixelHistoryMaxAge.value, Is.EqualTo(10));
                Assert.That(settings.spatialDenoise.value, Is.EqualTo(SdfDiffuseSpatialDenoise.Legacy));
                Assert.That(settings.correctReceiverSelfHits.value, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void PrepareSphereDirectionalGuide_HighVariancePeakSubdividesToLevel2AndRemainsUnbiased()
        {
            RequireComputeAndReversedZ();
            using var fixture = new GuideFixture();

            // Front-facing bin (4, 4) corresponds to +Z (towards camera/surface normal = (0, 0, -1))
            // or build a surface facing (0, 0, -1) with a sharp bright peak in the normal direction.
            Vector3 normal = new Vector3(0f, 0f, -1f);
            int peakBin = FindNearestSphereBin(normal);
            fixture.SetupUniformPlane(normal, (bin, tap) =>
            {
                if (bin == peakBin)
                    return new Color(80f, 80f, 80f, 1f);
                return new Color(0.25f, 0.25f, 0.25f, 1f);
            });

            var guide = fixture.Dispatch(
                adaptive: true,
                maxLevel: 2,
                varianceWeight: 1.0f,
                minRayFraction: 0.5f,
                hasHistory: true,
                budgetFlags: 0,
                preExposure: 1.0f);

            var stats = AnalyzeProbeGuide(guide);
            Assert.That(stats.UnbiasedViolations, Is.Zero,
                $"Every active octahedral bin must preserve exact sum(invPdf) == 1.0 (max error {stats.MaxWeightError:E2}).");
            Assert.That(stats.DuplicateSubStrata, Is.Zero,
                "Multi-level subdivision must never emit duplicate sub-strata inside any octahedral bin.");
            Assert.That(stats.Level2Rays, Is.GreaterThan(0),
                "High-variance peak direction must subdivide into Level-2 (16x) rays.");
            Assert.That(stats.PeakBinLevel2Rays, Is.GreaterThanOrEqualTo(4),
                $"Peak bin {peakBin} must receive Level-2 sub-rays.");
            Assert.That(stats.ProbeImportance, Is.GreaterThan(0.8f),
                "High-contrast bright probe must receive high importance.");
        }

        [Test]
        public void PrepareSphereDirectionalGuide_LowVarianceDimProbeCullsDeadRaysWithinAdaptiveBudget()
        {
            RequireComputeAndReversedZ();
            using var fixture = new GuideFixture();

            Vector3 normal = new Vector3(0f, 0f, -1f);
            fixture.SetupUniformPlane(normal, (_, _) => new Color(0.04f, 0.04f, 0.04f, 1f));

            var guide = fixture.Dispatch(
                adaptive: true,
                maxLevel: 2,
                varianceWeight: 1.0f,
                minRayFraction: 0.5f,
                hasHistory: true,
                budgetFlags: 0,
                preExposure: 1.0f);

            var stats = AnalyzeProbeGuide(guide);
            Assert.That(stats.UnbiasedViolations, Is.Zero,
                "Adaptive culling must never drop partial strata or break sum(invPdf) == 1.0.");
            Assert.That(stats.DuplicateSubStrata, Is.Zero);
            Assert.That(stats.ProbeImportance, Is.LessThan(0.1f),
                "Uniform converged dim probe should produce low probeImportance.");
            Assert.That(stats.CulledRays, Is.GreaterThanOrEqualTo(20),
                "Low-variance converged probe should cull unneeded back-facing rays.");
            Assert.That(stats.ActiveRays, Is.LessThanOrEqualTo(40),
                "Active ray count should drop near the adaptive floor while keeping all active front-facing cells.");
            Assert.That(stats.ActiveBins, Is.GreaterThanOrEqualTo(24),
                "All front-facing hemisphere cells (brdf >= 0.1) must remain covered.");
        }

        [Test]
        public void PrepareSphereDirectionalGuide_DisoccludedOrDynamicProbePreservesFullBudget()
        {
            RequireComputeAndReversedZ();
            using var fixture = new GuideFixture();

            Vector3 normal = new Vector3(0f, 0f, -1f);
            fixture.SetupUniformPlane(normal, (_, _) => new Color(0.04f, 0.04f, 0.04f, 1f));

            var staticGuide = fixture.Dispatch(
                adaptive: true, maxLevel: 2, varianceWeight: 1.0f, minRayFraction: 0.5f,
                hasHistory: true, budgetFlags: 0, preExposure: 1.0f);
            var disoccludedGuide = fixture.Dispatch(
                adaptive: true, maxLevel: 2, varianceWeight: 1.0f, minRayFraction: 0.5f,
                hasHistory: false, budgetFlags: 0, preExposure: 1.0f);
            var dynamicGuide = fixture.Dispatch(
                adaptive: true, maxLevel: 2, varianceWeight: 1.0f, minRayFraction: 0.5f,
                hasHistory: true, budgetFlags: 1, preExposure: 1.0f);

            var staticStats = AnalyzeProbeGuide(staticGuide);
            var disoccludedStats = AnalyzeProbeGuide(disoccludedGuide);
            var dynamicStats = AnalyzeProbeGuide(dynamicGuide);

            Assert.That(disoccludedStats.UnbiasedViolations, Is.Zero);
            Assert.That(dynamicStats.UnbiasedViolations, Is.Zero);
            Assert.That(disoccludedStats.ProbeImportance, Is.EqualTo(1.0f).Within(1e-4f),
                "Disoccluded probes without history must force full probeImportance = 1.0.");
            Assert.That(dynamicStats.ProbeImportance, Is.GreaterThanOrEqualTo(0.75f - 1e-4f),
                "Dynamic lighting / camera motion flag must enforce >= 0.75 probeImportance floor.");
            Assert.That(disoccludedStats.ActiveRays, Is.GreaterThan(staticStats.ActiveRays),
                "Disoccluded probe must allocate more rays than a converged static dim probe.");
            Assert.That(dynamicStats.ActiveRays, Is.GreaterThan(staticStats.ActiveRays),
                "Dynamic probe must allocate more rays than a converged static dim probe.");
        }

        private static void RequireComputeAndReversedZ()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.usesReversedZBuffer)
                Assert.Ignore("Test requires compute shaders and reversed-Z.");
        }

        private static int FindNearestSphereBin(Vector3 targetDir)
        {
            targetDir = targetDir.normalized;
            int bestBin = 0;
            float bestDot = -2f;
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    Vector2 uv = new Vector2((x + 0.5f) / 8f, (y + 0.5f) / 8f);
                    Vector3 d = EqualAreaOctahedralToSphere(uv);
                    float dp = Vector3.Dot(d, targetDir);
                    if (dp > bestDot)
                    {
                        bestDot = dp;
                        bestBin = x + y * 8;
                    }
                }
            }
            return bestBin;
        }

        private static Vector3 EqualAreaOctahedralToSphere(Vector2 uv)
        {
            Vector2 p = 2f * uv - Vector2.one;
            float r = 1f - Mathf.Abs(p.x) - Mathf.Abs(p.y);
            Vector2 s = p;
            if (r < 0f)
            {
                s.x = (1f - Mathf.Abs(p.y)) * (p.x >= 0f ? 1f : -1f);
                s.y = (1f - Mathf.Abs(p.x)) * (p.y >= 0f ? 1f : -1f);
            }
            return new Vector3(s.x, s.y, r).normalized;
        }

        private struct GuideStats
        {
            public int ActiveRays;
            public int CulledRays;
            public int Level0Rays;
            public int Level1Rays;
            public int Level2Rays;
            public int ActiveBins;
            public int PeakBinLevel2Rays;
            public int UnbiasedViolations;
            public int DuplicateSubStrata;
            public float MaxWeightError;
            public float ProbeImportance;
        }

        private static GuideStats AnalyzeProbeGuide(Vector4[] guide8x8)
        {
            var stats = new GuideStats { ProbeImportance = guide8x8[0].w };
            float[] binWeight = new float[64];
            int[] binL2Count = new int[64];
            bool[,] subUsed = new bool[64, 16];

            for (int i = 0; i < 64; i++)
            {
                uint code = (uint)Mathf.RoundToInt(guide8x8[i].x);
                if (code >= SphereRayCulled)
                {
                    stats.CulledRays++;
                    continue;
                }
                stats.ActiveRays++;
                if (code < SphereRayLevel1Base)
                {
                    stats.Level0Rays++;
                    binWeight[code] += 1.0f;
                    for (int s = 0; s < 16; s++)
                    {
                        if (subUsed[code, s]) stats.DuplicateSubStrata++;
                        subUsed[code, s] = true;
                    }
                }
                else if (code < SphereRayLevel2Base)
                {
                    stats.Level1Rays++;
                    uint idx = code - SphereRayLevel1Base;
                    uint bin = idx & 63u;
                    uint c1 = idx >> 6;
                    binWeight[bin] += 0.25f;
                    int bx = (int)(c1 & 1u) * 2;
                    int by = (int)(c1 >> 1) * 2;
                    for (int dy = 0; dy < 2; dy++)
                        for (int dx = 0; dx < 2; dx++)
                        {
                            int s = (bx + dx) + (by + dy) * 4;
                            if (subUsed[bin, s]) stats.DuplicateSubStrata++;
                            subUsed[bin, s] = true;
                        }
                }
                else
                {
                    stats.Level2Rays++;
                    uint idx = code - SphereRayLevel2Base;
                    uint bin = idx & 63u;
                    uint sub = idx >> 6;
                    binWeight[bin] += 0.0625f;
                    binL2Count[bin]++;
                    if (sub < 16u)
                    {
                        if (subUsed[bin, sub]) stats.DuplicateSubStrata++;
                        subUsed[bin, sub] = true;
                    }
                    else
                    {
                        stats.DuplicateSubStrata++;
                    }
                }
            }

            for (int b = 0; b < 64; b++)
            {
                if (binL2Count[b] > stats.PeakBinLevel2Rays)
                    stats.PeakBinLevel2Rays = binL2Count[b];
                if (binWeight[b] > 0f)
                {
                    stats.ActiveBins++;
                    float err = Mathf.Abs(binWeight[b] - 1.0f);
                    if (err > stats.MaxWeightError) stats.MaxWeightError = err;
                    if (err > 1e-4f) stats.UnbiasedViolations++;
                }
            }
            return stats;
        }

        private sealed class GuideFixture : IDisposable
        {
            private const int ScreenWidth = 32;
            private const int ScreenHeight = 32;
            private const int GridWidth = ScreenWidth / 16;
            private const int GridHeight = ScreenHeight / 16;

            private readonly ComputeShader shader;
            private readonly int kernel;
            private readonly Matrix4x4 view;
            private readonly Matrix4x4 proj;
            private readonly Matrix4x4 vp;
            private readonly Matrix4x4 invVp;

            private readonly Texture2D depthTex;
            private readonly Texture2D gbuffer0;
            private readonly Texture2D gbuffer1;
            private readonly Texture2D prevGeoTex;
            private readonly Texture2D prevDirTex;
            private readonly Texture2D blackTex;
            private readonly RenderTexture guideOut;
            private readonly GraphicsBuffer preExposureBuffer;

            public GuideFixture()
            {
                var asset = AssetDatabase.LoadAssetAtPath<ComputeShader>(ImportanceSamplingShaderPath);
                Assert.That(asset, Is.Not.Null, $"Could not load {ImportanceSamplingShaderPath}");
                shader = Object.Instantiate(asset);
                kernel = shader.FindKernel("PrepareSphereDirectionalGuide");
                Assert.That(kernel, Is.GreaterThanOrEqualTo(0));

                view = Matrix4x4.identity;
                view.m22 = -1f;
                proj = Matrix4x4.Perspective(60f, 1f, 0.1f, 100f);
                vp = GL.GetGPUProjectionMatrix(proj, true) * view;
                invVp = vp.inverse;

                depthTex = new Texture2D(ScreenWidth, ScreenHeight, GraphicsFormat.R32_SFloat, TextureCreationFlags.None);
                gbuffer0 = new Texture2D(ScreenWidth, ScreenHeight, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None);
                gbuffer1 = new Texture2D(ScreenWidth, ScreenHeight, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None);
                prevGeoTex = new Texture2D(GridWidth, GridHeight, GraphicsFormat.R32G32B32A32_SFloat, TextureCreationFlags.None);
                prevDirTex = new Texture2D(GridWidth * 8, GridHeight * 8, GraphicsFormat.R32G32B32A32_SFloat, TextureCreationFlags.None);
                blackTex = new Texture2D(4, 4, GraphicsFormat.R32G32B32A32_SFloat, TextureCreationFlags.None);
                blackTex.SetPixelData(new Vector4[16], 0);
                blackTex.Apply(false, false);

                guideOut = new RenderTexture(GridWidth * 8, GridHeight * 8, 0, GraphicsFormat.R16G16B16A16_SFloat)
                {
                    enableRandomWrite = true
                };
                guideOut.Create();

                preExposureBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(float) * 4);
                preExposureBuffer.SetData(new[] { Vector4.one });
            }

            public void SetupUniformPlane(Vector3 normal, Func<int, int, Color> prevRadianceAtBinAndTile)
            {
                normal = normal.normalized;
                float worldZ = 5.0f;
                float deviceZ = WorldZToDeviceZ(worldZ);
                Vector2 oct = EncodeOctNormal(normal);

                var depths = new float[ScreenWidth * ScreenHeight];
                var gb0 = new byte[ScreenWidth * ScreenHeight * 4];
                var gb1 = new byte[ScreenWidth * ScreenHeight * 4];
                for (int i = 0; i < ScreenWidth * ScreenHeight; i++)
                {
                    depths[i] = deviceZ;
                    gb0[i * 4 + 0] = 200;
                    gb0[i * 4 + 1] = 200;
                    gb0[i * 4 + 2] = 200;
                    gb0[i * 4 + 3] = 2; // VIVID_DEFERRED_EXPORT_CLASS_FAST_SLAB
                    gb1[i * 4 + 0] = (byte)Mathf.RoundToInt(oct.x * 255f);
                    gb1[i * 4 + 1] = (byte)Mathf.RoundToInt(oct.y * 255f);
                    gb1[i * 4 + 2] = 128;
                    gb1[i * 4 + 3] = 255;
                }
                depthTex.SetPixelData(depths, 0); depthTex.Apply(false, false);
                gbuffer0.SetPixelData(gb0, 0); gbuffer0.Apply(false, false);
                gbuffer1.SetPixelData(gb1, 0); gbuffer1.Apply(false, false);

                // Populate previous geometry for all 2x2 tiles on the exact z = 5 plane.
                var prevGeo = new Vector4[GridWidth * GridHeight];
                for (int ty = 0; ty < GridHeight; ty++)
                {
                    for (int tx = 0; tx < GridWidth; tx++)
                    {
                        float u = (tx * 16f + 8f) / ScreenWidth;
                        float v = (ty * 16f + 8f) / ScreenHeight;
                        Vector3 ws = UnprojectWorld(u, v, deviceZ);
                        prevGeo[ty * GridWidth + tx] = new Vector4(ws.x, ws.y, ws.z, 1f);
                    }
                }
                prevGeoTex.SetPixelData(prevGeo, 0); prevGeoTex.Apply(false, false);

                // Populate previous directional radiance atlas (GridWidth*8 x GridHeight*8).
                var prevDir = new Vector4[GridWidth * 8 * GridHeight * 8];
                for (int ty = 0; ty < GridHeight; ty++)
                {
                    for (int tx = 0; tx < GridWidth; tx++)
                    {
                        int tileIdx = ty * GridWidth + tx;
                        for (int ly = 0; ly < 8; ly++)
                        {
                            for (int lx = 0; lx < 8; lx++)
                            {
                                int bin = lx + ly * 8;
                                Color c = prevRadianceAtBinAndTile(bin, tileIdx);
                                prevDir[(ty * 8 + ly) * (GridWidth * 8) + (tx * 8 + lx)] = new Vector4(c.r, c.g, c.b, c.a);
                            }
                        }
                    }
                }
                prevDirTex.SetPixelData(prevDir, 0); prevDirTex.Apply(false, false);
            }

            public Vector4[] Dispatch(bool adaptive, int maxLevel, float varianceWeight, float minRayFraction,
                bool hasHistory, int budgetFlags, float preExposure)
            {
                preExposureBuffer.SetData(new[] { new Vector4(preExposure, 1f / preExposure, 0f, 0f) });

                shader.SetVector("_SdfDiffuseScreenSize", new Vector4(ScreenWidth, ScreenHeight, 1f / ScreenWidth, 1f / ScreenHeight));
                shader.SetMatrix("_SdfDiffuseInverseVP", invVp);
                shader.SetMatrix("_SdfDiffuseVP", vp);
                shader.SetMatrix("_SdfDiffusePreviousVP", vp);
                shader.SetVector("_SdfProbeCameraForward", new Vector4(0f, 0f, 1f, 0f));
                shader.SetVector("_SdfHistoryLightScale", Vector4.one);
                shader.SetVector("_SdfScreenJitterDelta", Vector4.zero);
                shader.SetInt("_SdfScreenProbeFullSphere", 1);
                shader.SetInt("_SdfDiffuseAdaptiveProbes", 0);
                shader.SetInt("_SdfDirectionalCacheEnabled", 1);
                shader.SetInt("_SdfDirectionalGuideEnabled", 1);
                shader.SetInt("_SdfDirectionalWorldGuide", 0);
                shader.SetInt("_SdfDirectionalHasHistory", hasHistory ? 1 : 0);
                shader.SetInt("_SdfDirectionalHasMotion", 0);
                shader.SetInt("_SdfHasProbeDirectionNoise", 0);
                shader.SetInt("_SdfDiffuseFrame", 0);
                shader.SetFloat("_SdfDiffuseRayBias", 0.02f);
                shader.SetFloat("_SdfFarTraceDistance", 0f);

                shader.SetInt("_SdfImportanceAdaptiveBudget", adaptive ? 1 : 0);
                shader.SetInt("_SdfImportanceMaxLevel", maxLevel);
                shader.SetFloat("_SdfImportanceVarianceWeight", varianceWeight);
                shader.SetFloat("_SdfImportanceMinRayFraction", minRayFraction);
                shader.SetInt("_SdfImportanceBudgetFlags", budgetFlags);

                shader.SetBuffer(kernel, "_VividAutoExposurePreExposureBuffer", preExposureBuffer);
                shader.SetTexture(kernel, "_SdfDiffuseDepth", depthTex);
                shader.SetTexture(kernel, "_SdfDiffuseGBuffer0", gbuffer0);
                shader.SetTexture(kernel, "_SdfDiffuseGBuffer1", gbuffer1);
                shader.SetTexture(kernel, "_SdfAdaptivePlacement", blackTex);
                shader.SetTexture(kernel, "_SdfProbeDirectionNoise", blackTex);
                shader.SetTexture(kernel, "_SdfDiffusePreviousGeometry", prevGeoTex);
                shader.SetTexture(kernel, "_SdfDirectionalPrevious", prevDirTex);
                shader.SetTexture(kernel, "_SdfFarRadiance", blackTex);
                shader.SetTexture(kernel, "_SdfScreenMotion", blackTex);
                shader.SetTexture(kernel, "_SdfDirectionalGuideOutput", guideOut);

                shader.Dispatch(kernel, GridWidth, GridHeight, 1);

                var req = AsyncGPUReadback.Request(guideOut, 0, GraphicsFormat.R32G32B32A32_SFloat);
                req.WaitForCompletion();
                Assert.That(req.hasError, Is.False);
                var full = req.GetData<Vector4>().ToArray();

                // Return the 8x8 guide block for tile (0, 0).
                var probe = new Vector4[64];
                for (int ly = 0; ly < 8; ly++)
                    for (int lx = 0; lx < 8; lx++)
                        probe[ly * 8 + lx] = full[ly * (GridWidth * 8) + lx];
                return probe;
            }

            private float WorldZToDeviceZ(float worldZ)
            {
                Vector4 clip = vp * new Vector4(0f, 0f, worldZ, 1f);
                return clip.z / clip.w;
            }

            private Vector3 UnprojectWorld(float u, float v, float deviceZ)
            {
                float ndcX = 2f * u - 1f;
                float ndcY = 1f - 2f * v;
                Vector4 ws = invVp * new Vector4(ndcX, ndcY, deviceZ, 1f);
                return new Vector3(ws.x, ws.y, ws.z) / ws.w;
            }

            private static Vector2 EncodeOctNormal(Vector3 n)
            {
                n = n.normalized;
                n /= Mathf.Abs(n.x) + Mathf.Abs(n.y) + Mathf.Abs(n.z);
                Vector2 oct = new Vector2(n.x, n.y);
                if (n.z < 0f)
                {
                    oct = new Vector2(
                        (1f - Mathf.Abs(oct.y)) * (oct.x >= 0f ? 1f : -1f),
                        (1f - Mathf.Abs(oct.x)) * (oct.y >= 0f ? 1f : -1f));
                }
                return oct * 0.5f + Vector2.one * 0.5f;
            }

            public void Dispose()
            {
                Object.DestroyImmediate(shader);
                Object.DestroyImmediate(depthTex);
                Object.DestroyImmediate(gbuffer0);
                Object.DestroyImmediate(gbuffer1);
                Object.DestroyImmediate(prevGeoTex);
                Object.DestroyImmediate(prevDirTex);
                Object.DestroyImmediate(blackTex);
                guideOut.Release();
                Object.DestroyImmediate(guideOut);
                preExposureBuffer.Dispose();
            }
        }
    }
}
