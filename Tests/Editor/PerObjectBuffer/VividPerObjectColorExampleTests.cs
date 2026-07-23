using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.Examples;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public sealed class VividPerObjectColorExampleTests
    {
        [TearDown]
        public void TearDown()
        {
            VividPerObjectBuffer.DisposeAll();
        }

        [Test]
        public void Layout_DeclaresWhiteColorWithExpectedPacking()
        {
            VividPerObjectColorExampleLayout layout =
                VividPerObjectColorExampleLayout.Instance;

            Assert.That(layout.ShaderIdentifier, Is.EqualTo("PerObjectColorExample"));
            Assert.That(layout.RecordStride, Is.EqualTo(32));
            Assert.That(
                layout.GetProperty(VividPerObjectColorExampleLayout.ColorPropertyId).Offset,
                Is.EqualTo(4));
        }

        [TestCase(VividPerObjectColorExampleController.PropertyAccessMode.CachedHandle)]
        [TestCase(VividPerObjectColorExampleController.PropertyAccessMode.PropertyId)]
        [TestCase(VividPerObjectColorExampleController.PropertyAccessMode.PropertyName)]
        public void Controller_PushesColorWithoutMaterialPropertyBlock(
            VividPerObjectColorExampleController.PropertyAccessMode accessMode)
        {
            var gameObject = new GameObject("Per-Object Color Example");
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            VividPerObjectColorExampleController controller =
                gameObject.AddComponent<VividPerObjectColorExampleController>();
            var expectedColor = new Color(0.2f, 0.4f, 0.6f, 0.8f);

            try
            {
                controller.AccessMode = accessMode;
                controller.SetColor(expectedColor);

                VividPerObjectBlock block =
                    VividPerObjectBuffer.Bind<VividPerObjectColorExampleLayout>(renderer);
                int address = VividPerObjectBufferSystem.GetRecordAddressForTests(block);
                int colorOffset = VividPerObjectColorExampleLayout.ColorProperty.Offset;
                byte[] data = VividPerObjectBufferSystem.GetDataForTests();

                Assert.That(ReadFloat(data, address + colorOffset), Is.EqualTo(expectedColor.r));
                Assert.That(ReadFloat(data, address + colorOffset + 4), Is.EqualTo(expectedColor.g));
                Assert.That(ReadFloat(data, address + colorOffset + 8), Is.EqualTo(expectedColor.b));
                Assert.That(ReadFloat(data, address + colorOffset + 12), Is.EqualTo(expectedColor.a));
                Assert.That(renderer.HasPropertyBlock(), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void ExampleShader_CompilesAndRemainsSrpBatcherCompatible()
        {
            Shader shader = Shader.Find("VividRP/Examples/Per-Object Color");

            Assert.That(shader, Is.Not.Null);
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
            MethodInfo method = typeof(ShaderUtil).GetMethod(
                "GetSRPBatcherCompatibilityIssueReason",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Shader), typeof(int), typeof(int) },
                null);
            if (method == null)
                Assert.Ignore("This Unity version does not expose SRP Batcher compatibility diagnostics.");

            string compatibilityIssue =
                (string)method.Invoke(null, new object[] { shader, 0, 0 }) ?? string.Empty;
            bool compatible = string.IsNullOrEmpty(compatibilityIssue)
                || compatibilityIssue.StartsWith("OK", StringComparison.OrdinalIgnoreCase)
                || compatibilityIssue.StartsWith("Not initialized", StringComparison.OrdinalIgnoreCase);
            Assert.That(compatible, Is.True, compatibilityIssue);
        }

        private static float ReadFloat(byte[] data, int offset)
        {
            return BitConverter.ToSingle(data, offset);
        }
    }
}
