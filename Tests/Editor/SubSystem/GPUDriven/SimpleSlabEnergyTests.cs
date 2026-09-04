using System;
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests
{
    public sealed class SimpleSlabEnergyTests
    {
        [Test]
        public void LutContract_AndNativeDeferredEnergyStaySynchronized()
        {
            string header = Read("Shaders/Core/Public/VividSimpleSlabEnergy.hlsl");
            StringAssert.Contains($"#define VIVID_SLAB_LUT_VERSION {VividSlabLut.Version}u", header);
            StringAssert.Contains($"#define VIVID_SLAB_LUT_RESOLUTION {VividSlabLut.Resolution}", header);
            StringAssert.Contains($"#define VIVID_SLAB_LUT_SAMPLE_COUNT {VividSlabLut.SampleCount}u", header);
            StringAssert.Contains($"#define VIVID_SIMPLE_SLAB_ENERGY_VERSION {MaterialProgramContract.SimpleSlabEnergyVersion}u", header);
            string deferred = Read("Shaders/Material/DeferredLit.compute");
            StringAssert.DoesNotContain("ApplyVividSlabEnergyToLegacyPreLight", deferred);
            StringAssert.Contains("VividSimpleSlabEnergy baseEnergy = basePreLightData.energy;", deferred);
            StringAssert.Contains("VividSimpleSlabEnergy topEnergy = topPreLightData.energy;", deferred);
            StringAssert.Contains("return topEnergy.diffuseTransmission", deferred);
            StringAssert.Contains("baseEnergy.singleScatterSpecularAlbedo * baseEnvironmentWeight", deferred);
            StringAssert.Contains("if (_VividSlabLutReady == 0u)", deferred);
        }

        [Test]
        public void MinimumRoughnessPeak_HasNoSubtractiveCancellation()
        {
            float alpha = SimpleSlabContract.MinimumAlphaRoughness;
            Assert.That(SimpleSlabBSDFReferenceKernel.DistributionGGX(alpha, 1),
                Is.EqualTo(1.0f / (math.PI * alpha * alpha)).Within(0.02f));
        }

        [Test]
        public void ReferenceIntegrator_UnitRoughNormalIncidenceMatchesClosedForm()
        {
            double2 reference = IntegrateReference(1, 1);
            Assert.That(reference.x + reference.y,
                Is.EqualTo(1 - Math.Log(2)).Within(2e-6));
        }

        [Test]
        public void BakedLut_GpuWhiteFurnaceReciprocityAndReference()
        {
            RequireGpu();
            RTHandles.Initialize(1, 1);
            var lut = new VividSlabLut();
            try
            {
                Assert.That(lut.Create(LoadCompute("Shaders/Core/Private/VividSlabLut.compute")), Is.True);
                ComputeShader test = LoadCompute("Tests/Editor/SubSystem/GPUDriven/SimpleSlabEnergyTests.compute");
                Vector4[] furnace = Dispatch(test, "EvaluateFurnace", 90, lut);
                for (int i = 0; i < furnace.Length; ++i)
                {
                    int material = i % 6;
                    for (int channel = 0; channel < 3; ++channel)
                    {
                        float value = furnace[i][channel];
                        Assert.That(float.IsNaN(value) || float.IsInfinity(value), Is.False, $"case {i}");
                        Assert.That(value, Is.InRange(0.0f, 1.015f), $"case {i}, channel {channel}");
                        // Unit conductor and white diffuse medium preserve white furnace energy.
                        if (material < 3 || material == 4)
                            Assert.That(value, Is.EqualTo(1.0f).Within(0.015f), $"case {i}, channel {channel}");
                        if (material == 5)
                            Assert.That(value, Is.Zero, $"black absorber case {i}");
                    }
                }
                Vector4[] reciprocal = Dispatch(test, "EvaluateReciprocity", 25, lut);
                foreach (Vector4 error in reciprocal)
                    Assert.That(Mathf.Max(error.x, Mathf.Max(error.y, error.z)), Is.LessThan(2e-5f));

                Vector4[] probes = Dispatch(test, "ProbeLut", 25, lut);
                for (int i = 0; i < probes.Length; ++i)
                {
                    Assert.That(probes[i].x + probes[i].y, Is.InRange(0.0f, 1.001f));
                    Assert.That(probes[i].z + probes[i].w, Is.InRange(0.0f, 1.001f));
                    // Independent double-precision hemisphere quadrature, well-resolved rough lobes.
                    if (i >= 15 && i % 5 > 0)
                    {
                        double2 reference = IntegrateReference((i % 5) * 0.25, (i / 5) * 0.25);
                        Assert.That(probes[i].x, Is.EqualTo(reference.y).Within(0.004));
                        Assert.That(probes[i].y, Is.EqualTo(1 - reference.x - reference.y).Within(0.004));
                    }
                }
            }
            finally { lut.Dispose(); }
        }

        [Test]
        public void StableLutCreate_DoesNotAllocateOrRecordCommands_AndDeviceLossRebuilds()
        {
            RequireGpu();
            RTHandles.Initialize(1, 1);
            var lut = new VividSlabLut();
            ComputeShader shader = LoadCompute("Shaders/Core/Private/VividSlabLut.compute");
            using var cmd = new CommandBuffer();
            try
            {
                Assert.That(lut.Create(shader), Is.True);
                RTHandle original = lut.Texture;
                var frameData = new VividPreIntegratedFGDData { slabLutTexture = original };
                frameData.Reset();
                Assert.That(frameData.slabLutTexture, Is.Null);
                for (int i = 0; i < 16; ++i) lut.Create(shader, cmd);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 128; ++i) lut.Create(shader, cmd);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.Zero);
                Assert.That(cmd.sizeInBytes, Is.Zero);
                Assert.That(lut.Texture, Is.SameAs(original));
                lut.Texture.rt.Release();
                Assert.That(lut.Create(shader), Is.True);
                Assert.That(lut.Texture, Is.Not.SameAs(original));
                Assert.That(lut.Texture.rt.IsCreated(), Is.True);
                original = lut.Texture;
                VividSlabLut.InvalidateSource();
                Assert.That(lut.Create(shader), Is.True);
                Assert.That(lut.Texture, Is.Not.SameAs(original));
                lut.Dispose();
                Assert.That(lut.Texture, Is.Null);
                Assert.That(lut.Create(null), Is.False);
                Assert.That(lut.Create(shader), Is.True);
            }
            finally { lut.Dispose(); }
        }

        [Test]
        public void LutGraphDescriptor_MatchesRuntimeFormat()
        {
            RenderGraphTexture texture = VividSlabLut.CreateGraphTexture();
            Assert.That(texture.desc.ColorFormat, Is.EqualTo(VividSlabLut.Format));
            Assert.That(texture.desc.Width, Is.EqualTo(VividSlabLut.Resolution));
            Assert.That(texture.desc.UseMipMap, Is.False);
        }

        // Uniform solid-angle midpoint quadrature: deliberately not the GPU VNDF sampler.
        internal static double2 IntegrateReference(double nv, double roughness)
        {
            const int cosSteps = 256;
            const int phiSteps = 512;
            double alpha = Math.Max(roughness * roughness, 0.002);
            double a2 = alpha * alpha;
            double vx = Math.Sqrt(1 - nv * nv);
            double2 integral = 0;
            for (int y = 0; y < cosSteps; ++y)
            {
                double nl = (y + 0.5) / cosSteps;
                double radius = Math.Sqrt(1 - nl * nl);
                for (int x = 0; x < phiSteps; ++x)
                {
                    double phi = (x + 0.5) * 2 * Math.PI / phiSteps;
                    double hx = vx + radius * Math.Cos(phi);
                    double hy = radius * Math.Sin(phi);
                    double hz = nv + nl;
                    double invLength = 1 / Math.Sqrt(hx * hx + hy * hy + hz * hz);
                    double nh = hz * invLength;
                    double vh = (vx * hx + nv * hz) * invLength;
                    double denom = (1 - nh) * (1 + nh) + a2 * nh * nh;
                    double d = a2 / (Math.PI * denom * denom);
                    double visibility = 0.5 / (nl * Math.Sqrt(nv * nv * (1 - a2) + a2)
                        + nv * Math.Sqrt(nl * nl * (1 - a2) + a2));
                    double fc = Math.Pow(1 - vh, 5);
                    integral += d * visibility * nl * new double2(1 - fc, fc);
                }
            }
            return integral * (2 * Math.PI / (cosSteps * phiSteps));
        }

        private static Vector4[] Dispatch(ComputeShader shader, string entry, int count, VividSlabLut lut)
        {
            int kernel = shader.FindKernel(entry);
            using var output = new ComputeBuffer(count, sizeof(float) * 4);
            shader.SetTexture(kernel, VividSlabLut.TextureId, lut.Texture.rt);
            shader.SetBuffer(kernel, "_Output", output);
            shader.Dispatch(kernel, count, 1, 1);
            var values = new Vector4[count];
            output.GetData(values);
            return values;
        }

        private static ComputeShader LoadCompute(string relative)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(VividPackagePathUtility.GetPreferredAssetPath(relative));
            Assert.That(shader, Is.Not.Null, relative);
            foreach (ShaderMessage message in ShaderUtil.GetComputeShaderMessages(shader))
                Assert.That(message.severity.ToString(), Is.Not.EqualTo("Error"), message.message);
            return shader;
        }

        private static void RequireGpu()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders required.");
        }

        private static string Read(string relative) => File.ReadAllText(VividPackagePathUtility.GetPreferredAssetPath(relative));
    }
}
