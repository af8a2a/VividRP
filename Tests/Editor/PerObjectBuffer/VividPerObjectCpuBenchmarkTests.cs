using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.Examples;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class VividPerObjectCpuBenchmarkTests
    {
        [TearDown]
        public void TearDown()
        {
            VividPerObjectBuffer.DisposeAll();
        }

        [Test]
        public void Run_ReturnsAllCasesAndRestoresRendererState()
        {
            RequireGraphicsDevice();
            GameObject root = CreateRenderers(4, out Renderer[] renderers);

            try
            {
                VividPerObjectCpuBenchmarkReport report =
                    VividPerObjectCpuBenchmark.Run(
                        renderers,
                        warmupIterations: 1,
                        measurementIterations: 2);

                Assert.That(report.RendererCount, Is.EqualTo(4));
                Assert.That(report.MaterialPropertyBlockChanging.OperationCount, Is.EqualTo(8));
                Assert.That(report.PerObjectBufferChanging.OperationCount, Is.EqualTo(8));
                Assert.That(report.PerObjectBufferChangingSubmit.OperationCount, Is.EqualTo(2));
                Assert.That(report.MaterialPropertyBlockUnchanged.OperationCount, Is.EqualTo(8));
                Assert.That(report.PerObjectBufferUnchanged.OperationCount, Is.EqualTo(8));
                Assert.That(report.PerObjectBufferUnchangedSubmit.OperationCount, Is.EqualTo(2));
                Assert.That(report.ToString(), Does.Contain("Changing write speedup"));

                for (int i = 0; i < renderers.Length; i++)
                {
                    Assert.That(renderers[i].HasPropertyBlock(), Is.False);
                    Assert.That(VividPerObjectBuffer.IsBound(renderers[i]), Is.False);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Run_RejectsRendererThatAlreadyOwnsPerObjectBinding()
        {
            RequireGraphicsDevice();
            GameObject root = CreateRenderers(1, out Renderer[] renderers);
            VividPerObjectBuffer.Bind<VividPerObjectColorExampleLayout>(renderers[0]);

            try
            {
                Assert.Throws<System.InvalidOperationException>(() =>
                    VividPerObjectCpuBenchmark.Run(
                        renderers,
                        warmupIterations: 0,
                        measurementIterations: 1));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Run_RestoresExistingRendererPropertyBlock()
        {
            RequireGraphicsDevice();
            GameObject root = CreateRenderers(1, out Renderer[] renderers);
            int originalPropertyId = Shader.PropertyToID("_BenchmarkOriginalValue");
            var originalPropertyBlock = new MaterialPropertyBlock();
            originalPropertyBlock.SetFloat(originalPropertyId, 0.75f);
            renderers[0].SetPropertyBlock(originalPropertyBlock);

            try
            {
                VividPerObjectCpuBenchmark.Run(
                    renderers,
                    warmupIterations: 0,
                    measurementIterations: 1);

                var restoredPropertyBlock = new MaterialPropertyBlock();
                renderers[0].GetPropertyBlock(restoredPropertyBlock);
                Assert.That(renderers[0].HasPropertyBlock(), Is.True);
                Assert.That(restoredPropertyBlock.GetFloat(originalPropertyId), Is.EqualTo(0.75f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        [Explicit("Manual CPU benchmark; results depend on the current machine and Editor state.")]
        public void CompareThousandRenderers_PrintsOptimizationBaseline()
        {
            RequireGraphicsDevice();
            GameObject root = CreateRenderers(1000, out Renderer[] renderers);

            try
            {
                VividPerObjectCpuBenchmarkReport report =
                    VividPerObjectCpuBenchmark.Run(
                        renderers,
                        warmupIterations: 16,
                        measurementIterations: 128,
                        perObjectAccessMode:
                            VividPerObjectColorExampleController.PropertyAccessMode.CachedHandle);

                TestContext.Out.WriteLine(report.ToString());
                Assert.That(report.RendererCount, Is.EqualTo(1000));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject CreateRenderers(int count, out Renderer[] renderers)
        {
            var root = new GameObject("Per-Object CPU Benchmark Root");
            renderers = new Renderer[count];
            for (int i = 0; i < count; i++)
            {
                var child = new GameObject($"Benchmark Renderer {i}");
                child.transform.SetParent(root.transform, false);
                renderers[i] = child.AddComponent<MeshRenderer>();
            }
            return root;
        }

        private static void RequireGraphicsDevice()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("The benchmark requires a graphics device.");
        }
    }
}
