using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class OpenPBROpaqueDeferredTests
    {
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
            Assert.That(maxAlbedoError, Is.LessThan(0.08f));
        }
    }
}
