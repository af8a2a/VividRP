using System;
using System.IO;
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
                var textureBackendProperty = serializedObject.FindProperty("m_GPUDrivenTextureBackend");

                Assert.That(colorGradingSpaceProperty, Is.Not.Null);
                Assert.That(implementationProperty, Is.Not.Null);
                Assert.That(property, Is.Not.Null);
                Assert.That(textureBackendProperty, Is.Not.Null);

                colorGradingSpaceProperty.enumValueIndex = (int)ColorGradingSpace.AcesCg;
                implementationProperty.enumValueIndex = (int)AutoExposureImplementationPath.HDRP;
                property.boolValue = true;
                textureBackendProperty.enumValueIndex = (int) GPUDrivenTextureBackendMode.Bindless;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(asset.ColorGradingSpace, Is.EqualTo(ColorGradingSpace.AcesCg));
                Assert.That(asset.AutoExposureImplementation, Is.EqualTo(AutoExposureImplementationPath.HDRP));
                Assert.That(asset.EnableGPUDriven, Is.True);
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

        [Test]
        public void Update_UsesCachedCameraNameAndAvoidsCommandSamples_ForNoGcStats()
        {
            string systemSource = File.ReadAllText(
                GetPackageFilePath("Runtime", "SubSystem", "GPUDriven", "VividGPUDrivenSystem.cs"));
            string cameraDataSource = File.ReadAllText(
                GetPackageFilePath("Runtime", "RenderGraph", "FrameContext", "VividCameraData.cs"));
            string passRecorderSource = File.ReadAllText(
                GetPackageFilePath("Runtime", "RenderGraph", "PassRecorder.Execution.cs"));

            Assert.That(cameraDataSource, Does.Contain("internal void SetCamera(Camera value)"));
            Assert.That(cameraDataSource, Does.Contain("cameraName = value != null ? value.name : null;"));
            Assert.That(passRecorderSource, Does.Contain("cameraData.SetCamera(camera);"));
            Assert.That(systemSource, Does.Contain("public void PrepareFrame(bool reportStats = true)"));
            Assert.That(systemSource, Does.Contain("if (reportStats)"));
            Assert.That(systemSource, Does.Contain("PrepareFrameIfNeeded(gpuDrivenSystem, cameraData.frameIndex, reportStats: false);"));
            Assert.That(systemSource, Does.Contain("ReportStats(camera, cameraData.cameraName);"));
            Assert.That(systemSource, Does.Contain("cameraName: cameraData.cameraName"));
            Assert.That(systemSource, Does.Contain("camera != null ? cameraName : null"));
            Assert.That(systemSource, Does.Contain("ResolveCullingCameraForDebug(camera)"));
            Assert.That(systemSource, Does.Not.Contain("camera.name"));
            Assert.That(systemSource, Does.Not.Contain("RenderDebugOverlay"));
            Assert.That(systemSource, Does.Not.Contain("VividGPUDrivenDebugOverlayRenderer"));
            Assert.That(systemSource, Does.Not.Contain("SubsystemPostRender"));
            Assert.That(systemSource, Does.Not.Contain("s_CullingSampler"));
            Assert.That(systemSource, Does.Not.Contain("new ProfilingScope(cmd, s_CullingSampler)"));
            Assert.That(systemSource, Does.Not.Contain("BeginSample(\"GPUDrivenCulling\")"));
            Assert.That(systemSource, Does.Not.Contain("EndSample(\"GPUDrivenCulling\")"));
        }

        [Test]
        public void Resources_DoNotReferenceLegacyGpuDrivenDebugOverlayShader()
        {
            string resourcesSource = File.ReadAllText(
                GetPackageFilePath("Runtime", "Utility", "PipelineResource", "VividResources.cs"));
            string resourceAssetSource = File.ReadAllText(
                GetPackageFilePath("Runtime", "Resources", "PipelineResources.asset"));

            Assert.That(resourcesSource, Does.Not.Contain("GPUDrivenMeshletDebug"));
            Assert.That(resourceAssetSource, Does.Not.Contain("GPUDrivenMeshletDebug"));
            Assert.That(
                File.Exists(GetPackageFilePath("Shaders", "Core", "Private", "GPUDriven", "GPUDrivenMeshletDebug.shader")),
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

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var path = Path.Combine("Packages", "VividRP");
            foreach (var part in relativeParts)
                path = Path.Combine(path, part);

            return path;
        }
    }
}
