using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class OpenPBRTextureLutTests
    {
        private static readonly string[] ReferenceNames =
        {
            "OpenPBR_IdealDielectricEnergyComplement_Array", "OpenPBR_IdealDielectricAverageEnergyComplement_Array",
            "OpenPBR_IdealDielectricReflectionRatio_Array", "OpenPBR_OpaqueDielectricEnergyComplement_Array",
            "OpenPBR_OpaqueDielectricAverageEnergyComplement_Array", "OpenPBR_IdealMetalEnergyComplement_Array",
            "OpenPBR_IdealMetalAverageEnergyComplement_Array", "OpenPBR_LTC_Array"
        };

        private static VividOpenPBRLuts Load()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders required.");
            var luts = PipelineResourceManager.Get<VividRPCoreResources>()?.OpenPBRLuts;
            Assert.That(luts, Is.Not.Null);
            return luts;
        }

        private static float[][] ReadTables()
        {
            var result = new float[8][];
            for (int i = 0; i < result.Length; ++i)
                result[i] = VividOpenPBRLutImporter.ReadTable(VividPackagePathUtility.GetPreferredAssetPath(
                    VividOpenPBRLutImporter.DataDirectory + VividOpenPBRLutImporter.DataFiles[i]), i);
            return result;
        }

        private static Vector4[] Dispatch(string entry, VividOpenPBRLuts luts, Vector4[] inputs, float[][] tables)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(VividPackagePathUtility.GetPreferredAssetPath(
                "Tests/Editor/RenderPass/Lighting/OpenPBRTextureLutTests.compute"));
            Assert.That(shader, Is.Not.Null);
            foreach (ShaderMessage message in ShaderUtil.GetComputeShaderMessages(shader))
                Assert.That(message.severity.ToString(), Is.Not.EqualTo("Error"), message.message);
            int kernel = shader.FindKernel(entry);
            using var input = new ComputeBuffer(inputs.Length, 16);
            using var output = new ComputeBuffer(inputs.Length * 3, 16);
            var buffers = new ComputeBuffer[8];
            try
            {
                input.SetData(inputs);
                shader.SetBuffer(kernel, "_Inputs", input);
                shader.SetBuffer(kernel, "_Output", output);
                if (entry.StartsWith("EvaluateTexture", StringComparison.Ordinal))
                    for (int i = 0; i < VividOpenPBRLuts.Count; ++i)
                        shader.SetTexture(kernel, VividOpenPBRLuts.TextureIds[i], luts.GetTexture(i));
                else
                    for (int i = 0; i < buffers.Length; ++i)
                    {
                        buffers[i] = new ComputeBuffer(i == 7 ? 1024 : tables[i].Length, i == 7 ? 12 : 4);
                        if (i == 7) buffers[i].SetData(tables[i]);
                        else
                        {
                            var values = new uint[tables[i].Length];
                            for (int j = 0; j < values.Length; ++j) values[j] = (uint)Mathf.RoundToInt(tables[i][j] * 65535f);
                            buffers[i].SetData(values);
                        }
                        shader.SetBuffer(kernel, ReferenceNames[i], buffers[i]);
                    }
                shader.Dispatch(kernel, (inputs.Length + 63) / 64, 1, 1);
                var result = new Vector4[inputs.Length * 3];
                output.GetData(result);
                return result;
            }
            finally { foreach (var buffer in buffers) buffer?.Dispose(); }
        }

        [Test]
        public void UploadedTexels_MatchAllVendorTablesExactly()
        {
            var luts = Load();
            var tables = ReadTables();
            Assert.That(VividOpenPBRLuts.Count, Is.EqualTo(3));
            Assert.That(luts.GetTexture(2).dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(((Texture2DArray)luts.GetTexture(2)).depth, Is.EqualTo(6));
            for (int t = 0; t < 3; ++t)
            {
                Texture texture = luts.GetTexture(t);
                Assert.That(texture.mipmapCount, Is.EqualTo(1));
                Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Bilinear));
                Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
                var request = AsyncGPUReadback.Request(texture, 0);
                request.WaitForCompletion();
                Assert.That(request.hasError, Is.False);
                if (t < 2)
                    for (int z = 0; z < 32; ++z)
                    {
                        var slice = request.GetData<float>(z);
                        for (int p = 0; p < 1024; ++p)
                            Assert.That(slice[p], Is.EqualTo(tables[t == 0 ? 0 : 3][z * 1024 + p]));
                    }
                else
                    for (int layer = 0; layer < 6; ++layer)
                    {
                        var data = request.GetData<float>(layer);
                        for (int p = 0; p < 1024; ++p)
                            for (int c = 0; c < 4; ++c)
                            {
                                int id = layer < 2 ? layer + 1 : layer + 2;
                                float expected = id == 7 ? (c < 3 ? tables[7][p * 3 + c] : 0)
                                    : c == 0 ? tables[id][id == 6 ? p % 32 : p] : 0;
                                Assert.That(data[p * 4 + c], Is.EqualTo(expected));
                            }
                    }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FilteredLookups_MatchArrayReference_AtBoundariesAndAcrossIorRange(bool fp16)
        {
            var inputs = new List<Vector4>();
            foreach (float ior in new[] { 0.2f, 0.4f, 0.99999f, 1f, 1.00001f, 2.5f, 5f })
                foreach (float alpha in new[] { 0f, 0.00001f, 0.25f, 0.99999f, 1f })
                    foreach (float cosine in new[] { 0f, 0.00001f, 0.5f, 0.99999f, 1f })
                        inputs.Add(new Vector4(ior, alpha, cosine, 0));
            var random = new System.Random(731);
            for (int i = 0; i < 4096; ++i)
                inputs.Add(new Vector4((float)(0.2 + 4.8 * random.NextDouble()),
                    (float)random.NextDouble(), (float)random.NextDouble(), 0));
            var luts = Load();
            Vector4[] points = inputs.ToArray();
            var tables = ReadTables();
            Vector4[] reference = Dispatch(fp16 ? "EvaluateArrayFP16" : "EvaluateArray", luts, points, tables);
            Vector4[] texture = Dispatch(fp16 ? "EvaluateTextureFP16" : "EvaluateTexture", luts, points, tables);
            float maxEnergyError = 0, maxLtcError = 0;
            for (int i = 0; i < texture.Length; ++i)
                for (int c = 0; c < 4; ++c)
                {
                    float error = Mathf.Abs(texture[i][c] - reference[i][c]);
                    Assert.That(float.IsNaN(error) || float.IsInfinity(error), Is.False);
                    if (i % 3 == 2) maxLtcError = Mathf.Max(maxLtcError, error);
                    else maxEnergyError = Mathf.Max(maxEnergyError, error);
                }
            TestContext.WriteLine($"{points.Length} lookup probes: max energy error={maxEnergyError:R}, max LTC error={maxLtcError:R}");
            // Hardware interpolation weights have finite precision (unlike shader lerp).
            Assert.That(maxEnergyError, Is.LessThan(0.005f));
            Assert.That(maxLtcError, Is.LessThan(0.015f));
        }

        [Test]
        public void StableGlobalBinding_AllocatesNothing_AndReusesImportedTextures()
        {
            var luts = Load();
            Texture original = luts.GetTexture(2);
            using var cmd = new CommandBuffer();
            for (int i = 0; i < 32; ++i) { VividPreIntegratedFGDSystem.BindOpenPBRLuts(cmd); cmd.Clear(); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 128; ++i) { VividPreIntegratedFGDSystem.BindOpenPBRLuts(cmd); cmd.Clear(); }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
            Assert.That(luts.GetTexture(2), Is.SameAs(original));
        }
    }
}
