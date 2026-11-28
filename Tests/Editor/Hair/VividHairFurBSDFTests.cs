using System;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests
{
    public sealed class VividHairFurBSDFTests
    {
        private const string ComputeRelativePath =
            "Tests/Editor/Hair/VividHairFurBSDFTests.compute";

        [Test]
        public void KarisMarschner_LobePeaksAndCuticleShift_AgreeWithChiangFarFieldReference()
        {
            ComputeShader compute = LoadComputeOrIgnore();
            float4[] data = RunKernel(compute, "EvaluateLobeSweep", 128, 1);

            // Front sweep (phi = 0, indices 0..31, theta in [-75, +75] deg, alpha = 3 deg)
            // Expected peaks:
            //   R   peaks near +2*alpha = +6 deg
            //   TRT peaks near -4*alpha = -12 deg
            int peakRIdx = FindMaxIndex(data, 0, 32, v => v.x);
            int peakTrtIdx = FindMaxIndex(data, 0, 32, v => v.z);
            int peakChiangFrontIdx = FindMaxIndex(data, 64, 32, v => v.x);

            float peakRDeg = IndexToThetaDeg(peakRIdx);
            float peakTrtDeg = IndexToThetaDeg(peakTrtIdx);
            float peakChiangFrontDeg = IndexToThetaDeg(peakChiangFrontIdx);

            // Backlit sweep (phi = pi, indices 32..63): TT peaks near -alpha = -3 deg
            int peakTtIdx = FindMaxIndex(data, 32, 32, v => v.y);
            int peakChiangBackIdx = FindMaxIndex(data, 96, 32, v => v.x);

            float peakTtDeg = IndexToThetaDeg(peakTtIdx);
            float peakChiangBackDeg = IndexToThetaDeg(peakChiangBackIdx);

            Assert.That(peakRDeg, Is.InRange(1.0f, 12.0f), $"R peak expected near +6 deg, got {peakRDeg:F1} deg");
            Assert.That(peakTrtDeg, Is.InRange(-20.0f, -4.0f), $"TRT peak expected near -12 deg, got {peakTrtDeg:F1} deg");
            Assert.That(peakTtDeg, Is.InRange(-10.0f, 2.0f), $"TT peak expected near -3 deg, got {peakTtDeg:F1} deg");

            // Peak separation: R is shifted toward +T (root-side shift) while TRT & TT shift toward -T
            Assert.That(peakRDeg - peakTrtDeg, Is.GreaterThan(10.0f), "R and TRT lobes must exhibit physical cuticle tilt separation.");

            // Agreement with 16-point h-integrated RTXCR Chiang reference peaks (within 10 deg across 5-deg grid)
            Assert.That(Mathf.Abs(peakRDeg - peakChiangFrontDeg), Is.LessThanOrEqualTo(10.0f),
                $"Front R peak ({peakRDeg:F1} deg) should track Chiang far-field peak ({peakChiangFrontDeg:F1} deg).");
            Assert.That(Mathf.Abs(peakTtDeg - peakChiangBackDeg), Is.LessThanOrEqualTo(10.0f),
                $"Backlit TT peak ({peakTtDeg:F1} deg) should track Chiang far-field peak ({peakChiangBackDeg:F1} deg).");

            // TRT on warm chestnut hair (R=0.65 > B=0.15) must be warmer (colored) than achromatic R
            Assert.That(data[peakTrtIdx].z, Is.GreaterThan(data[64 + peakTrtIdx].w * 2.0f),
                "TRT secondary lobe must be strongly tinted by hair absorption (R > B).");
        }

        [Test]
        public void KarisMarschner_WhiteFurnace_IsEnergyConservingAcrossRoughnessMedullaAndScatter()
        {
            ComputeShader compute = LoadComputeOrIgnore();
            float4[] results = RunKernel(compute, "EvaluateWhiteFurnace", 12, 1);

            for (int i = 0; i < 12; i++)
            {
                float totalRefl = results[i].x;
                float singleRefl = results[i].y;
                float dualRefl = results[i].z;
                float maxSample = results[i].w;

                Assert.That(float.IsFinite(totalRefl) && float.IsFinite(singleRefl) && float.IsFinite(dualRefl), Is.True,
                    $"Case {i}: non-finite furnace integral.");
                Assert.That(singleRefl, Is.GreaterThan(0.15f).And.LessThanOrEqualTo(1.05f),
                    $"Case {i}: single-scatter white furnace reflectance={singleRefl:F4} out of energy bounds.");
                Assert.That(totalRefl, Is.GreaterThan(0.15f).And.LessThanOrEqualTo(1.05f),
                    $"Case {i}: total white furnace reflectance={totalRefl:F4} exceeds energy conservation limit.");
                Assert.That(maxSample, Is.GreaterThan(0.0f), $"Case {i}: BSDF response collapsed to zero.");
            }

            // Enabling Dual Scattering must contribute non-trivial multi-scatter energy
            // and boost energy-deficient rough hair (rough >= 0.65) while staying <= 1.05 everywhere.
            for (int r = 0; r < 4; r++)
            {
                Assert.That(results[4 + r].z, Is.GreaterThan(0.10f),
                    $"Roughness slot {r}: Dual Scattering lobe should contribute measurable multiple-scatter energy.");
            }
            Assert.That(results[6].x, Is.GreaterThan(results[2].x), "Dual Scattering should recover energy at rough=0.65.");
            Assert.That(results[7].x, Is.GreaterThan(results[3].x), "Dual Scattering should recover energy at rough=0.90.");
        }

        [Test]
        public void MedullaAndDualScattering_PreserveEnergyCouplingAndLuminanceMonotonicity()
        {
            ComputeShader compute = LoadComputeOrIgnore();
            float4[] results = RunKernel(compute, "EvaluateMedullaAndDualScatter", 6, 1);

            float4 cortexOnly = results[0]; // (R_side, TRT_side, Medulla_side, TT_back)
            float4 highMedulla = results[1];

            // R (air-cuticle specular) is unaffected by internal medullaWeight
            Assert.That(highMedulla.x, Is.EqualTo(cortexOnly.x).Within(1e-5f),
                "Outer cuticle reflection R must be invariant to internal medullaWeight.");
            // Cortex TRT & TT scale down by (1 - 0.75) = 0.25
            Assert.That(highMedulla.y, Is.EqualTo(cortexOnly.y * 0.25f).Within(1e-4f),
                "Cortex TRT must attenuate by (1 - medullaWeight).");
            Assert.That(highMedulla.w, Is.EqualTo(cortexOnly.w * 0.25f).Within(1e-4f),
                "Cortex TT must attenuate by (1 - medullaWeight).");
            // Medulla scattering activates strongly when medullaWeight = 0.75
            Assert.That(cortexOnly.z, Is.EqualTo(0.0f).Within(1e-6f));
            Assert.That(highMedulla.z, Is.GreaterThan(0.01f), "High medulla must produce soft body scattering.");

            // Dual scattering in partial shadow boosts light blonde fur substantially;
            // dark brown retains achromatic cuticle R (~0.0057) while blonde's internal scattering is >20x brighter (>2x total).
            float lightWithScatter = results[2].x;
            float lightNoScatter = results[2].w;
            float darkWithScatter = results[3].x;
            Assert.That(lightWithScatter, Is.GreaterThan(lightNoScatter),
                "Dual Scattering must boost shadowed light fur via multi-scatter penetration.");
            Assert.That(lightWithScatter, Is.GreaterThan(darkWithScatter * 2.0f),
                "Blonde fur must remain substantially brighter than dark brown fur under Dual Scattering.");

            // Melanin mapping: 0 -> white, 0.5 -> warm (R > B), 0.95 -> dark
            float4 mel = results[4];
            Assert.That(mel.x, Is.GreaterThan(0.98f), "Zero melanin should map near white.");
            Assert.That(mel.y, Is.GreaterThan(mel.z), "Mid-melanin with redness should be warmer (R > B).");
            Assert.That(mel.w, Is.LessThan(0.15f), "High melanin (0.95) should map to dark hair.");

            // Indirect IBL helper & cylinder normal orthogonality (dot(V_perp, T) == 0)
            float4 ind = results[5];
            Assert.That(ind.x, Is.GreaterThan(ind.z), "Indirect lighting should preserve warm hair tint.");
            Assert.That(ind.w, Is.LessThan(1e-5f), "Virtual cylinder normal V_perp must be orthogonal to T.");
        }

        [Test]
        public void KarisMarschner_DegenerateAndGrazingInputs_ProduceFiniteNonNegativeResults()
        {
            ComputeShader compute = LoadComputeOrIgnore();
            float4[] results = RunKernel(compute, "EvaluateDegenerateCases", 5, 1);

            for (int i = 0; i < 5; i++)
            {
                float4 v = results[i];
                Assert.That(float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z), Is.True,
                    $"Degenerate case {i} produced NaN/Inf: {v}");
                Assert.That(v.x >= 0.0f && v.y >= 0.0f && v.z >= 0.0f, Is.True,
                    $"Degenerate case {i} produced negative energy: {v}");
            }

            // Pitch-black hair (case 3) still retains non-zero dielectric specular R
            Assert.That(results[3].x, Is.GreaterThan(0.0f), "Pitch-black hair must retain achromatic cuticle R specular.");
        }

        private static ComputeShader LoadComputeOrIgnore()
        {
            if (!SystemInfo.supportsComputeShaders)
                Assert.Ignore("Compute shaders are not supported on the active graphics device.");

            string assetPath = VividPackagePathUtility.GetPreferredAssetPath(ComputeRelativePath);
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(assetPath);
            Assert.That(compute, Is.Not.Null, $"Could not load compute shader at '{assetPath}'.");
            return compute;
        }

        private static float4[] RunKernel(ComputeShader compute, string kernelName, int outputCount, int groupsX)
        {
            int kernel = compute.FindKernel(kernelName);
            using var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, outputCount, sizeof(float) * 4);
            var zeros = new float4[outputCount];
            buffer.SetData(zeros);
            compute.SetBuffer(kernel, "_Output", buffer);
            compute.Dispatch(kernel, groupsX, 1, 1);
            var output = new float4[outputCount];
            buffer.GetData(output);
            return output;
        }

        private static int FindMaxIndex(float4[] data, int offset, int count, Func<float4, float> selector)
        {
            int best = 0;
            float bestVal = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                float v = selector(data[offset + i]);
                if (v > bestVal)
                {
                    bestVal = v;
                    best = i;
                }
            }
            return best;
        }

        private static float IndexToThetaDeg(int localIndex)
        {
            return Mathf.Lerp(-75.0f, 75.0f, localIndex / 31.0f);
        }
    }
}
