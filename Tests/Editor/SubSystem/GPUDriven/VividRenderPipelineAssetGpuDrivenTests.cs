using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public class VividRenderPipelineAssetGpuDrivenTests
    {
        [Test]
        public void Asset_DefaultsToGpuDrivenDisabled()
        {
            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();

            try
            {
                Assert.That(asset.ColorGradingSpace, Is.EqualTo(ColorGradingSpace.sRGB));
                Assert.That(asset.AutoExposureImplementation, Is.EqualTo(AutoExposureImplementationPath.Unreal));
                Assert.That(asset.EnableGPUDriven, Is.False);
                Assert.That(asset.EnableGPUDrivenDebugOverlay, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void SerializedObject_UpdatesGpuDrivenProperty()
        {
            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();

            try
            {
                var serializedObject = new SerializedObject(asset);
                var colorGradingSpaceProperty = serializedObject.FindProperty("m_ColorGradingSpace");
                var implementationProperty = serializedObject.FindProperty("m_AutoExposureImplementation");
                var property = serializedObject.FindProperty("m_EnableGPUDriven");
                var debugOverlayProperty = serializedObject.FindProperty("m_EnableGPUDrivenDebugOverlay");

                Assert.That(colorGradingSpaceProperty, Is.Not.Null);
                Assert.That(implementationProperty, Is.Not.Null);
                Assert.That(property, Is.Not.Null);
                Assert.That(debugOverlayProperty, Is.Not.Null);

                colorGradingSpaceProperty.enumValueIndex = (int)ColorGradingSpace.AcesCg;
                implementationProperty.enumValueIndex = (int)AutoExposureImplementationPath.HDRP;
                property.boolValue = true;
                debugOverlayProperty.boolValue = true;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(asset.ColorGradingSpace, Is.EqualTo(ColorGradingSpace.AcesCg));
                Assert.That(asset.AutoExposureImplementation, Is.EqualTo(AutoExposureImplementationPath.HDRP));
                Assert.That(asset.EnableGPUDriven, Is.True);
                Assert.That(asset.EnableGPUDrivenDebugOverlay, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Asset_DefaultShader_IsStandardLit()
        {
            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();

            try
            {
                Assert.That(asset.defaultShader, Is.Not.Null);
                Assert.That(asset.defaultShader.name, Is.EqualTo("VividRP/Material/StandardLit"));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }

    public class VividGPUDrivenSystemLifecycleTests
    {
        [SetUp]
        public void SetUp()
        {
            VividGPUDrivenSystem.Deinitialize();
            FrameContextSystem.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            VividGPUDrivenSystem.Deinitialize();
            FrameContextSystem.Clear();
        }

        [Test]
        public void Deinitialize_DisposesCurrentSingletonInstance()
        {
            var system = VividGPUDrivenSystem.instance;

            Assert.That(system, Is.Not.Null);
            Assert.That(VividGPUDrivenSystem.HasInstance, Is.True);

            VividGPUDrivenSystem.Deinitialize();

            Assert.That(VividGPUDrivenSystem.HasInstance, Is.False);
        }

        [Test]
        public void Instance_RecreatesSingleton_AfterDeinitialize()
        {
            var first = VividGPUDrivenSystem.instance;

            VividGPUDrivenSystem.Deinitialize();

            var second = VividGPUDrivenSystem.instance;

            Assert.That(second, Is.Not.Null);
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(VividGPUDrivenSystem.HasInstance, Is.True);
        }
    }
}
