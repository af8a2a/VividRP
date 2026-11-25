using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class OpenPBROpaqueDeferredTests
    {
        [Test]
        public void DiffuseAlbedo_IsContinuousAcrossViewAngles_AndMatchesReference()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders required.");
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(VividPackagePathUtility.GetPreferredAssetPath(
                "Tests/Editor/RenderPass/Lighting/OpenPBROpaqueDeferredTests.compute"));
            Assert.That(shader, Is.Not.Null);
            int kernel = shader.FindKernel("EvaluateDiffuseAlbedoContinuity");
            var luts = PipelineResourceManager.Get<VividRPCoreResources>()?.OpenPBRLuts;
            Assert.That(luts, Is.Not.Null);
            Assert.That(luts.IsReady, Is.True);
            for (int i = 0; i < VividOpenPBRLuts.Count; ++i)
                shader.SetTexture(kernel, VividOpenPBRLuts.TextureIds[i], luts.GetTexture(i));
            using var output = new ComputeBuffer(256 * 4 * 2, 16);
            shader.SetBuffer(kernel, "_Output", output);
            shader.Dispatch(kernel, 4, 4, 1);
            var values = new Vector4[256 * 4 * 2];
            output.GetData(values);
            for (int material = 0; material < 4; ++material)
                for (int angle = 0; angle < 256; ++angle)
                    for (int c = 0; c < 3; ++c)
                    {
                        int offset = (material * 256 + angle) * 2;
                        float value = values[offset][c];
                        Assert.That(float.IsNaN(value) || float.IsInfinity(value), Is.False);
                        Assert.That(Mathf.Abs(value - values[offset + 1][c]), Is.LessThan(0.005f),
                            $"Diffuse energy mismatch at material {material}, angle {angle}, channel {c}.");
                        if (angle > 0)
                            Assert.That(Mathf.Abs(value - values[offset - 2][c]) / Mathf.Max(value, 0.02f),
                                Is.LessThan(0.0025f),
                                $"Diffuse energy jump at material {material}, angle {angle}, channel {c}.");
                    }
        }

        [Test]
        public void NativeStorageAndLighting_MatchVendor_AtProfileBoundaries()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders required.");
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(VividPackagePathUtility.GetPreferredAssetPath(
                "Tests/Editor/RenderPass/Lighting/OpenPBROpaqueDeferredTests.compute"));
            Assert.That(shader, Is.Not.Null);
            foreach (ShaderMessage message in ShaderUtil.GetComputeShaderMessages(shader))
                Assert.That(message.severity.ToString(), Is.Not.EqualTo("Error"), message.message);
            int kernel = shader.FindKernel("EvaluateNativeDeferred");
            var luts = PipelineResourceManager.Get<VividRPCoreResources>()?.OpenPBRLuts;
            Assert.That(luts, Is.Not.Null);
            Assert.That(luts.IsReady, Is.True);
            for (int i = 0; i < VividOpenPBRLuts.Count; ++i)
                shader.SetTexture(kernel, VividOpenPBRLuts.TextureIds[i], luts.GetTexture(i));
            using var output = new ComputeBuffer(96 * 8, 16);
            shader.SetBuffer(kernel, "_Output", output);
            shader.Dispatch(kernel, 2, 1, 1);
            var values = new Vector4[96 * 8];
            output.GetData(values);
            float maxAlbedoError = 0;
            for (int i = 0; i < 96; ++i)
            {
                int offset = i * 8;
                for (int row = 0; row < 8; ++row)
                    for (int c = 0; c < 4; ++c)
                        Assert.That(float.IsNaN(values[offset + row][c]) || float.IsInfinity(values[offset + row][c]), Is.False,
                            $"Non-finite result at case {i}, row {row}, channel {c}.");
                Assert.That(values[offset].w, Is.EqualTo(1));
                Assert.That((Vector3)values[offset], Is.EqualTo(Vector3.zero));
                Assert.That((Vector3)values[offset + 1], Is.EqualTo(Vector3.zero));
                Assert.That(values[offset + 6].x, Is.LessThan(1e-5f));
                Assert.That(values[offset + 6].y, Is.EqualTo(1));
                Assert.That(values[offset + 6].z, Is.EqualTo(1));
                Assert.That(values[offset + 6].w, Is.EqualTo(1));
                Assert.That(values[offset + 7], Is.EqualTo(Vector4.zero));
                for (int lobe = 0; lobe < 2; ++lobe)
                    for (int c = 0; c < 3; ++c)
                        maxAlbedoError = Mathf.Max(maxAlbedoError,
                            Mathf.Abs(values[offset + 2 + lobe][c] - values[offset + 4 + lobe][c]));
            }
            TestContext.WriteLine($"OpenPBR Deferred directional albedo: max absolute error {maxAlbedoError:R} against 4096 samples.");
            Assert.That(maxAlbedoError, Is.LessThan(0.02f));
        }
    }
}
