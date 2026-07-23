using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class VividPerObjectBufferGpuTests
    {
        private const string TestShaderName = "Hidden/VividRP/Tests/PerObjectBuffer";
        private static readonly int s_TestAddressWordsId = Shader.PropertyToID("_VividPerObjectTestAddressWords");

        [TearDown]
        public void TearDown()
        {
            VividPerObjectBuffer.DisposeAll();
            Shader.SetGlobalVector(s_TestAddressWordsId, Vector4.zero);
            RenderTexture.active = null;
        }

        [Test]
        public void GpuAbi_DrawsDifferentValuesAndUnboundDefaults_WithRendererEncodedAddresses()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("A graphics device is required for the per-object GPU validation.");

            Shader shader = Shader.Find(TestShaderName);
            Assert.That(shader, Is.Not.Null);
            VividPerObjectLayout layout = CreateGpuLayout();
            Mesh mesh = CreateQuad();
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            GameObject firstObject = CreateRendererObject("First", mesh, material, -2.0f);
            GameObject secondObject = CreateRendererObject("Second", mesh, material, 0.0f);
            GameObject defaultObject = CreateRendererObject("Default", mesh, material, 2.0f);
            var target = new RenderTexture(96, 32, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(96, 32, TextureFormat.RGBA32, mipChain: false, linear: true);
            var commandBuffer = new CommandBuffer { name = "Vivid Per-Object GPU ABI Test" };
            try
            {
                MeshRenderer firstRenderer = firstObject.GetComponent<MeshRenderer>();
                MeshRenderer secondRenderer = secondObject.GetComponent<MeshRenderer>();
                MeshRenderer defaultRenderer = defaultObject.GetComponent<MeshRenderer>();
                target.Create();

                VividPerObjectBlock firstBlock = VividPerObjectBuffer.Bind(firstRenderer, layout);
                VividPerObjectBlock secondBlock = VividPerObjectBuffer.Bind(secondRenderer, layout);
                SetGpuValues(firstBlock, 1.0f, 0.0f, 1.0f, 0.0f);
                SetGpuValues(secondBlock, 0.0f, 1.0f, 0.0f, 0.5f);

                uint firstUserValue = firstRenderer.GetShaderUserValue();
                uint secondUserValue = secondRenderer.GetShaderUserValue();
                Shader.SetGlobalVector(s_TestAddressWordsId, new Vector4(
                    firstUserValue & 0xffffu,
                    firstUserValue >> 16,
                    secondUserValue & 0xffffu,
                    secondUserValue >> 16));

                commandBuffer.SetRenderTarget(target);
                commandBuffer.ClearRenderTarget(clearDepth: true, clearColor: true, Color.black);
                VividPerObjectBuffer.PrepareAndBind(commandBuffer);
                commandBuffer.DrawMesh(mesh, firstObject.transform.localToWorldMatrix, material, submeshIndex: 0, shaderPass: 0);
                commandBuffer.DrawMesh(mesh, secondObject.transform.localToWorldMatrix, material, submeshIndex: 0, shaderPass: 0);
                commandBuffer.DrawMesh(mesh, defaultObject.transform.localToWorldMatrix, material, submeshIndex: 0, shaderPass: 0);
                Graphics.ExecuteCommandBuffer(commandBuffer);

                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 96, 32), 0, 0);
                readback.Apply(updateMipmaps: false, makeNoLongerReadable: false);

                AssertColor(readback.GetPixel(16, 16), new Color(1, 0, 1, 0));
                AssertColor(readback.GetPixel(48, 16), new Color(0, 1, 0, 0.5f));
                AssertColor(readback.GetPixel(80, 16), new Color(0.25f, 0.5f, 0.75f, 1));
                Assert.That(firstRenderer.HasPropertyBlock(), Is.False);
                Assert.That(secondRenderer.HasPropertyBlock(), Is.False);
                Assert.That(defaultRenderer.HasPropertyBlock(), Is.False);
            }
            finally
            {
                commandBuffer.Dispose();
                Shader.SetGlobalVector(s_TestAddressWordsId, Vector4.zero);
                RenderTexture.active = null;
                target.Release();
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(firstObject);
                Object.DestroyImmediate(secondObject);
                Object.DestroyImmediate(defaultObject);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(mesh);
            }
        }

        [TestCase("Forward")]
        [TestCase("ShadowCaster")]
        [TestCase("MotionVectors")]
        public void ShaderPass_RemainsSrpBatcherCompatible(string passName)
        {
            Shader shader = Shader.Find(TestShaderName);
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                int passIndex = material.FindPass(passName);
                Assert.That(passIndex, Is.GreaterThanOrEqualTo(0));

                MethodInfo method = typeof(ShaderUtil).GetMethod(
                    "GetSRPBatcherCompatibilityIssueReason",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(Shader), typeof(int), typeof(int) },
                    null);
                if (method == null)
                    Assert.Ignore("This Unity version does not expose SRP Batcher compatibility diagnostics.");

                string issue = (string)method.Invoke(null, new object[] { shader, 0, passIndex }) ?? string.Empty;
                bool compatible = string.IsNullOrEmpty(issue)
                    || issue.StartsWith("OK", StringComparison.OrdinalIgnoreCase)
                    || issue.StartsWith("Not initialized", StringComparison.OrdinalIgnoreCase);
                string baselineIssue = string.Equals(passName, "MotionVectors", StringComparison.Ordinal)
                    ? GetSrpBatcherIssue(method, "VividRP/Material/Unlit", "MotionVectors")
                    : string.Empty;
                if (!compatible
                    && IsBuiltinDiagnostic(issue)
                    && IsBuiltinDiagnostic(baselineIssue))
                {
                    // Unity 6000.6 alpha reports a built-in UnityPerDraw layout issue for its own
                    // MotionVectors baseline. Reject material/buffer issues, but do not attribute
                    // this editor diagnostic regression to the per-object include.
                    compatible = true;
                }
                Assert.That(
                    compatible,
                    Is.True,
                    $"Pass '{passName}' is not SRP Batcher compatible: {issue}; baseline MotionVectors: {baselineIssue}");
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        private static VividPerObjectLayout CreateGpuLayout()
        {
            VividPerObjectLayout layout = VividPerObjectLayoutTests.CreateLayout("GpuTest", builder =>
            {
                builder.AddFloat("_Scalar", 0.25f);
                builder.AddVector("_Vector", new Vector4(0.5f, 0, 0, 0));
                builder.AddColor("_Color", new Color(0, 0, 0.75f, 0));
                builder.AddMatrix("_Matrix", Matrix4x4.identity);
            });
            Assert.That(layout.GetProperty("_Scalar").Offset, Is.EqualTo(4));
            Assert.That(layout.GetProperty("_Vector").Offset, Is.EqualTo(8));
            Assert.That(layout.GetProperty("_Color").Offset, Is.EqualTo(24));
            Assert.That(layout.GetProperty("_Matrix").Offset, Is.EqualTo(40));
            Assert.That(layout.Signature, Is.EqualTo(0x7047a0e7u));
            return layout;
        }

        private static void SetGpuValues(
            VividPerObjectBlock block,
            float scalar,
            float vectorX,
            float blue,
            float matrix00)
        {
            var matrix = Matrix4x4.identity;
            matrix.m00 = matrix00;
            block.SetFloat("_Scalar", scalar);
            block.SetVector("_Vector", new Vector4(vectorX, 0, 0, 0));
            block.SetColor("_Color", new Color(0, 0, blue, 0));
            block.SetMatrix("_Matrix", matrix);
        }

        private static GameObject CreateRendererObject(string name, Mesh mesh, Material material, float x)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.position = new Vector3(x, 0, 0);
            gameObject.transform.localScale = new Vector3(1.8f, 1.8f, 1.0f);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            return gameObject;
        }

        private static Mesh CreateQuad()
        {
            var mesh = new Mesh { name = "Per Object Test Quad" };
            mesh.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0),
                new Vector3(0.5f, -0.5f, 0),
                new Vector3(0.5f, 0.5f, 0),
                new Vector3(-0.5f, 0.5f, 0),
            });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.08f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.08f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.08f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.08f));
        }

        private static string GetSrpBatcherIssue(MethodInfo compatibilityMethod, string shaderName, string passName)
        {
            Shader shader = Shader.Find(shaderName);
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                int passIndex = material.FindPass(passName);
                Assert.That(passIndex, Is.GreaterThanOrEqualTo(0));
                return (string)compatibilityMethod.Invoke(
                    null,
                    new object[] { shader, 0, passIndex }) ?? string.Empty;
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        private static bool IsBuiltinDiagnostic(string issue)
        {
            return !string.IsNullOrEmpty(issue)
                && issue.StartsWith("Builtin property", StringComparison.OrdinalIgnoreCase);
        }

    }
}
