using System;
using System.Linq;
using System.Reflection;
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
                Assert.That(asset.EnableGPUDrivenOcclusionCulling, Is.True);
                Assert.That(asset.GPUDrivenTextureBackend, Is.EqualTo(GPUDrivenTextureBackendMode.VirtualTexture));
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
                var occlusionProperty = serializedObject.FindProperty("m_EnableGPUDrivenOcclusionCulling");
                var textureBackendProperty = serializedObject.FindProperty("m_GPUDrivenTextureBackend");

                Assert.That(colorGradingSpaceProperty, Is.Not.Null);
                Assert.That(implementationProperty, Is.Not.Null);
                Assert.That(property, Is.Not.Null);
                Assert.That(occlusionProperty, Is.Not.Null);
                Assert.That(textureBackendProperty, Is.Not.Null);

                colorGradingSpaceProperty.enumValueIndex = (int)ColorGradingSpace.AcesCg;
                implementationProperty.enumValueIndex = (int)AutoExposureImplementationPath.HDRP;
                property.boolValue = true;
                occlusionProperty.boolValue = false;
                textureBackendProperty.enumValueIndex = (int) GPUDrivenTextureBackendMode.Bindless;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(asset.ColorGradingSpace, Is.EqualTo(ColorGradingSpace.AcesCg));
                Assert.That(asset.AutoExposureImplementation, Is.EqualTo(AutoExposureImplementationPath.HDRP));
                Assert.That(asset.EnableGPUDriven, Is.True);
                Assert.That(asset.EnableGPUDrivenOcclusionCulling, Is.False);
                Assert.That(asset.GPUDrivenTextureBackend, Is.EqualTo(GPUDrivenTextureBackendMode.Bindless));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Asset_DoesNotExposeLegacyGpuDrivenDebugOverlayToggle()
        {
            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();

            try
            {
                var serializedObject = new SerializedObject(asset);

                Assert.That(serializedObject.FindProperty("m_EnableGPUDrivenDebugOverlay"), Is.Null);
                Assert.That(typeof(VividRenderPipelineAsset).GetProperty("EnableGPUDrivenDebugOverlay"), Is.Null);
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

        [Test]
        public void Asset_DefaultMaterial_UsesPrecreatedStandardLitMaterial()
        {
            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();

            try
            {
                Material expectedMaterial = Resources.Load<Material>("DefaultMaterial");
                Material material = asset.defaultMaterial;

                Assert.That(expectedMaterial, Is.Not.Null);
                Assert.That(material, Is.Not.Null);
                Assert.That(material, Is.SameAs(expectedMaterial));
                Assert.That(material.name, Is.EqualTo("DefaultMaterial"));
                Assert.That(material.shader, Is.Not.Null);
                Assert.That(material.shader.name, Is.EqualTo("VividRP/Material/StandardLit"));
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
        public void ResolveConfiguredTextureBackendMode_DefaultsToVirtualTextureIndependentlyOfFeatureState()
        {
            Assert.That(
                VividGPUDrivenSystem.ResolveConfiguredTextureBackendMode(null),
                Is.EqualTo(GPUDrivenTextureBackendMode.VirtualTexture));

            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();
            try
            {
                Assert.That(asset.EnableGPUDriven, Is.False);
                Assert.That(
                    VividGPUDrivenSystem.ResolveConfiguredTextureBackendMode(asset),
                    Is.EqualTo(GPUDrivenTextureBackendMode.VirtualTexture));

                asset.GPUDrivenTextureBackend = GPUDrivenTextureBackendMode.Bindless;
                Assert.That(
                    VividGPUDrivenSystem.ResolveConfiguredTextureBackendMode(asset),
                    Is.EqualTo(GPUDrivenTextureBackendMode.Bindless));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
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

        [Test]
        public void ShouldPrepareFrame_ReturnsTrue_ForRepeatedEditorFrameIndex()
        {
            Assert.That(VividGPUDrivenSystem.ShouldPrepareFrame(12, 12, isPlaying: false), Is.True);
        }

        [Test]
        public void ShouldPrepareFrame_ReturnsFalse_ForRepeatedPlayModeFrameIndex()
        {
            Assert.That(VividGPUDrivenSystem.ShouldPrepareFrame(12, 12, isPlaying: true), Is.False);
        }

        [Test]
        public void FrameContextClear_KeepsGPUDrivenPreRenderCallbackRegistered_InEditor()
        {
            VividGPUDrivenSystem.Initialize();

            FrameContextSystem.Clear();

            Assert.That(
                HasFrameContextSubscriber(
                    "SubsystemPreRender",
                    typeof(VividSubsystem<VividGPUDrivenSystem>),
                    "DispatchUpdate"),
                Is.True);
        }

        [Test]
        public void FrameContextClear_DoesNotRegisterLegacyGpuDrivenDebugOverlayCallback_InEditor()
        {
            VividGPUDrivenSystem.Initialize();

            FrameContextSystem.Clear();

            Assert.That(
                HasFrameContextSubscriber("SubsystemPostRender", typeof(VividGPUDrivenSystem), "RenderDebugOverlay"),
                Is.False);
        }

        private static bool HasFrameContextSubscriber(string eventName, Type declaringType, string methodName)
        {
            FieldInfo eventField = typeof(FrameContextSystem).GetField(
                eventName,
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(eventField, Is.Not.Null);

            var multicastDelegate = eventField.GetValue(null) as MulticastDelegate;
            return multicastDelegate != null
                && multicastDelegate.GetInvocationList().Any(
                    callback => callback.Method.DeclaringType == declaringType
                        && callback.Method.Name == methodName);
        }
    }
}
