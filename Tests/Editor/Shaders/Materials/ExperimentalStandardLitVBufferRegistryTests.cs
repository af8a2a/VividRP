using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VividRP.Runtime.Experimental.Material;

namespace VividRP.Editor.Tests
{
    public sealed class ExperimentalStandardLitVBufferRegistryTests
    {
        private Shader m_Shader;

        [SetUp]
        public void SetUp()
        {
            ExperimentalStandardLitVBufferMaterialRegistry.Shutdown();
            m_Shader = Shader.Find(
                ExperimentalStandardLitVBufferMaterialRegistry.ShaderName);
            Assert.That(m_Shader, Is.Not.Null);
        }

        [TearDown]
        public void TearDown()
        {
            ExperimentalStandardLitVBufferMaterialRegistry.Shutdown();
        }

        [Test]
        public void SharedMaterial_IsDeduplicatedAndReferenceCountedAcrossRenderers()
        {
            var material = new Material(m_Shader);
            GameObject first = CreateRenderer("First", material, out var firstBridge);
            GameObject second = CreateRenderer("Second", material, out _);
            try
            {
                ExpectUnavailableWarning();
                ExperimentalStandardLitVBufferMaterialRegistry.Prepare(null, out _);

                Assert.That(
                    ExperimentalStandardLitVBufferMaterialRegistry.RegisteredRendererCount,
                    Is.EqualTo(2));
                Assert.That(
                    ExperimentalStandardLitVBufferMaterialRegistry.RegisteredMaterialCount,
                    Is.EqualTo(1));
                Assert.That(
                    ExperimentalStandardLitVBufferMaterialRegistry.GetReferenceCount(material),
                    Is.EqualTo(2));
                Assert.That(
                    ExperimentalStandardLitVBufferMaterialRegistry.GetMaterialSlot(material),
                    Is.GreaterThan(0));
                Assert.That(
                    material.GetFloat(
                        ExperimentalStandardLitVBufferMaterialRegistry.MaterialIndexPropertyName),
                    Is.Zero,
                    "Unavailable VT must resolve through error slot 0.");

                firstBridge.enabled = false;
                ExperimentalStandardLitVBufferMaterialRegistry.Prepare(null, out _);
                Assert.That(
                    ExperimentalStandardLitVBufferMaterialRegistry.GetReferenceCount(material),
                    Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void MissingBridge_UsesErrorSlotEvenWhenSharedMaterialIsRegisteredElsewhere()
        {
            var material = new Material(m_Shader);
            GameObject registered = CreateRenderer("Registered", material, out _);
            var unregistered = new GameObject("Unregistered");
            MeshRenderer unregisteredRenderer = unregistered.AddComponent<MeshRenderer>();
            unregisteredRenderer.sharedMaterial = material;
            try
            {
                ExpectUnavailableWarning();
                ExperimentalStandardLitVBufferMaterialRegistry.Prepare(null, out _);

                Assert.That(ReadRendererMaterialIndex(unregisteredRenderer), Is.Zero);
                Assert.That(
                    material.GetFloat(
                        ExperimentalStandardLitVBufferMaterialRegistry.MaterialIndexPropertyName),
                    Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(registered);
                Object.DestroyImmediate(unregistered);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void Bridge_RegistersAllChildMeshRenderersIncludingInactiveChildren()
        {
            var root = new GameObject("Root");
            var child = new GameObject("Child");
            var inactiveChild = new GameObject("Inactive Child");
            child.transform.SetParent(root.transform);
            inactiveChild.transform.SetParent(root.transform);
            child.AddComponent<MeshRenderer>();
            inactiveChild.AddComponent<MeshRenderer>();
            inactiveChild.SetActive(false);

            try
            {
                var rootBridge = root.AddComponent<ExperimentalStandardLitVBufferRenderer>();
                Assert.That(
                    ExperimentalStandardLitVBufferMaterialRegistry.RegisteredRendererCount,
                    Is.EqualTo(3));

                var childBridge = child.AddComponent<ExperimentalStandardLitVBufferRenderer>();
                Assert.That(
                    ExperimentalStandardLitVBufferMaterialRegistry.RegisteredRendererCount,
                    Is.EqualTo(3),
                    "Overlapping bridge hierarchies must not double-count a renderer.");

                childBridge.enabled = false;
                Assert.That(
                    ExperimentalStandardLitVBufferMaterialRegistry.RegisteredRendererCount,
                    Is.EqualTo(3),
                    "The parent bridge must retain the child renderer registration.");

                inactiveChild.transform.SetParent(null);
                Assert.That(
                    ExperimentalStandardLitVBufferMaterialRegistry.RegisteredRendererCount,
                    Is.EqualTo(2));

                rootBridge.enabled = false;
                Assert.That(
                    ExperimentalStandardLitVBufferMaterialRegistry.RegisteredRendererCount,
                    Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(inactiveChild);
            }
        }

        [Test]
        public void SharedMaterialSlotChange_ReusesStableFreedSlotAndReleasesDestroyedMaterial()
        {
            var firstMaterial = new Material(m_Shader);
            var secondMaterial = new Material(m_Shader);
            GameObject gameObject = CreateRenderer(
                "Slot Change",
                firstMaterial,
                out _);
            MeshRenderer renderer = gameObject.GetComponent<MeshRenderer>();
            try
            {
                ExpectUnavailableWarning();
                ExperimentalStandardLitVBufferMaterialRegistry.Prepare(null, out _);
                int firstSlot = ExperimentalStandardLitVBufferMaterialRegistry.GetMaterialSlot(
                    firstMaterial);

                renderer.sharedMaterial = secondMaterial;
                ExperimentalStandardLitVBufferMaterialRegistry.Prepare(null, out _);
                Assert.That(
                    ExperimentalStandardLitVBufferMaterialRegistry.GetReferenceCount(firstMaterial),
                    Is.Zero);
                Assert.That(
                    ExperimentalStandardLitVBufferMaterialRegistry.GetMaterialSlot(secondMaterial),
                    Is.EqualTo(firstSlot));

                Object.DestroyImmediate(secondMaterial);
                secondMaterial = null;
                ExperimentalStandardLitVBufferMaterialRegistry.Prepare(null, out _);
                Assert.That(
                    ExperimentalStandardLitVBufferMaterialRegistry.RegisteredMaterialCount,
                    Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(firstMaterial);
                if (secondMaterial != null)
                    Object.DestroyImmediate(secondMaterial);
            }
        }

        private static GameObject CreateRenderer(
            string name,
            Material material,
            out ExperimentalStandardLitVBufferRenderer bridge)
        {
            var gameObject = new GameObject(name);
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            bridge = gameObject.AddComponent<ExperimentalStandardLitVBufferRenderer>();
            return gameObject;
        }

        private static void ExpectUnavailableWarning()
        {
            LogAssert.Expect(
                LogType.Warning,
                new Regex("Experimental StandardLit VBuffer resolved to error material slot 0"));
        }

        private static float ReadRendererMaterialIndex(MeshRenderer renderer)
        {
            var propertyBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(propertyBlock, 0);
            return propertyBlock.GetFloat(
                ExperimentalStandardLitVBufferMaterialRegistry.MaterialIndexPropertyName);
        }
    }
}
