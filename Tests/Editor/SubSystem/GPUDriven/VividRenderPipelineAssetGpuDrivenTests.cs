using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.VirtualTexture;
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
                Assert.That(asset.EnableTerrainRuntimeVirtualTexture, Is.False);
                Assert.That(asset.DecalTechnique, Is.EqualTo(VividDecalTechnique.ClusteredBindless));
                Assert.That(asset.GPUDrivenTextureBackend, Is.EqualTo(GPUDrivenTextureBackendMode.VirtualTexture));
                Assert.That(
                    asset.GPUDrivenVirtualTexturePhysicalPoolQuality,
                    Is.EqualTo(GPUDrivenVirtualTexturePhysicalPoolQuality.Medium));
                Assert.That(asset.VirtualTextureMaxResidencyAllocationsPerFrame, Is.EqualTo(64));
                Assert.That(asset.VirtualTextureMaxPrefetchAllocationsPerFrame, Is.Zero);
                Assert.That(asset.VirtualTextureMaxPageUploadsPerFrame, Is.EqualTo(64));
                Assert.That(asset.VirtualTextureMaxUploadBytesPerFrameMiB, Is.Zero);
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
                var terrainRVTProperty = serializedObject.FindProperty("m_EnableTerrainRuntimeVirtualTexture");
                var decalTechniqueProperty = serializedObject.FindProperty("m_DecalTechnique");
                var textureBackendProperty = serializedObject.FindProperty("m_GPUDrivenTextureBackend");
                var virtualTexturePhysicalPoolQualityProperty = serializedObject.FindProperty(
                    "m_GPUDrivenVirtualTexturePhysicalPoolQuality");
                var maxResidencyProperty = serializedObject.FindProperty(
                    "m_VirtualTextureMaxResidencyAllocationsPerFrame");
                var maxPrefetchProperty = serializedObject.FindProperty(
                    "m_VirtualTextureMaxPrefetchAllocationsPerFrame");
                var maxPageUploadsProperty = serializedObject.FindProperty(
                    "m_VirtualTextureMaxPageUploadsPerFrame");
                var maxUploadMiBProperty = serializedObject.FindProperty(
                    "m_VirtualTextureMaxUploadBytesPerFrameMiB");

                Assert.That(colorGradingSpaceProperty, Is.Not.Null);
                Assert.That(implementationProperty, Is.Not.Null);
                Assert.That(property, Is.Not.Null);
                Assert.That(occlusionProperty, Is.Not.Null);
                Assert.That(terrainRVTProperty, Is.Not.Null);
                Assert.That(decalTechniqueProperty, Is.Not.Null);
                Assert.That(textureBackendProperty, Is.Not.Null);
                Assert.That(virtualTexturePhysicalPoolQualityProperty, Is.Not.Null);
                Assert.That(maxResidencyProperty, Is.Not.Null);
                Assert.That(maxPrefetchProperty, Is.Not.Null);
                Assert.That(maxPageUploadsProperty, Is.Not.Null);
                Assert.That(maxUploadMiBProperty, Is.Not.Null);

                colorGradingSpaceProperty.enumValueIndex = (int)ColorGradingSpace.AcesCg;
                implementationProperty.enumValueIndex = (int)AutoExposureImplementationPath.HDRP;
                property.boolValue = true;
                occlusionProperty.boolValue = false;
                terrainRVTProperty.boolValue = true;
                decalTechniqueProperty.enumValueIndex = (int)VividDecalTechnique.TerrainRuntimeVirtualTexture;
                textureBackendProperty.enumValueIndex = (int) GPUDrivenTextureBackendMode.Bindless;
                virtualTexturePhysicalPoolQualityProperty.enumValueIndex =
                    (int)GPUDrivenVirtualTexturePhysicalPoolQuality.High;
                maxResidencyProperty.intValue = 41;
                maxPrefetchProperty.intValue = 7;
                maxPageUploadsProperty.intValue = 13;
                maxUploadMiBProperty.intValue = 23;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(asset.ColorGradingSpace, Is.EqualTo(ColorGradingSpace.AcesCg));
                Assert.That(asset.AutoExposureImplementation, Is.EqualTo(AutoExposureImplementationPath.HDRP));
                Assert.That(asset.EnableGPUDriven, Is.True);
                Assert.That(asset.EnableGPUDrivenOcclusionCulling, Is.False);
                Assert.That(asset.EnableTerrainRuntimeVirtualTexture, Is.True);
                Assert.That(
                    asset.DecalTechnique,
                    Is.EqualTo(VividDecalTechnique.TerrainRuntimeVirtualTexture));
                Assert.That(asset.GPUDrivenTextureBackend, Is.EqualTo(GPUDrivenTextureBackendMode.Bindless));
                Assert.That(
                    asset.GPUDrivenVirtualTexturePhysicalPoolQuality,
                    Is.EqualTo(GPUDrivenVirtualTexturePhysicalPoolQuality.High));
                Assert.That(asset.VirtualTextureMaxResidencyAllocationsPerFrame, Is.EqualTo(41));
                Assert.That(asset.VirtualTextureMaxPrefetchAllocationsPerFrame, Is.EqualTo(7));
                Assert.That(asset.VirtualTextureMaxPageUploadsPerFrame, Is.EqualTo(13));
                Assert.That(asset.VirtualTextureMaxUploadBytesPerFrameMiB, Is.EqualTo(23));
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
        public void TerrainRuntimeVirtualTextureDecals_ValidateRequiredPipelineDependenciesInOrder()
        {
            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();
            try
            {
                Assert.That(
                    asset.TryValidateTerrainRuntimeVirtualTextureDecals(out string gpuReason),
                    Is.False);
                Assert.That(gpuReason, Does.Contain("GPUDriven"));

                asset.EnableGPUDriven = true;
                asset.GPUDrivenTextureBackend = GPUDrivenTextureBackendMode.Bindless;
                Assert.That(
                    asset.TryValidateTerrainRuntimeVirtualTextureDecals(out string backendReason),
                    Is.False);
                Assert.That(backendReason, Does.Contain("Virtual Texture"));

                asset.GPUDrivenTextureBackend = GPUDrivenTextureBackendMode.VirtualTexture;
                Assert.That(
                    asset.TryValidateTerrainRuntimeVirtualTextureDecals(out string terrainReason),
                    Is.False);
                Assert.That(terrainReason, Does.Contain("Terrain Runtime Virtual Texture"));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ResolveConfiguredVirtualTextureDescriptorProfile_UsesMediumByDefaultAndAssetQuality()
        {
            GPUDrivenVirtualTextureDescriptorProfile defaultProfile =
                VividGPUDrivenSystem.ResolveConfiguredVirtualTextureDescriptorProfile(null);
            Assert.That(defaultProfile.CachePageCount, Is.EqualTo(512));

            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();
            try
            {
                asset.GPUDrivenVirtualTexturePhysicalPoolQuality =
                    GPUDrivenVirtualTexturePhysicalPoolQuality.High;

                GPUDrivenVirtualTextureDescriptorProfile highProfile =
                    VividGPUDrivenSystem.ResolveConfiguredVirtualTextureDescriptorProfile(asset);

                Assert.That(highProfile.CachePageCount, Is.EqualTo(1024));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void RequiresTextureBackendRecreation_TracksBackendModeOnly()
        {
            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();
            try
            {
                Assert.That(
                    VividGPUDrivenSystem.RequiresTextureBackendRecreation(
                        GPUDrivenTextureBackendMode.VirtualTexture,
                        asset),
                    Is.False);

                asset.GPUDrivenVirtualTexturePhysicalPoolQuality =
                    GPUDrivenVirtualTexturePhysicalPoolQuality.High;
                Assert.That(
                    VividGPUDrivenSystem.RequiresTextureBackendRecreation(
                        GPUDrivenTextureBackendMode.VirtualTexture,
                        asset),
                    Is.False,
                    "Physical-pool quality takes effect on the next backend initialization.");

                asset.GPUDrivenTextureBackend = GPUDrivenTextureBackendMode.Bindless;
                Assert.That(
                    VividGPUDrivenSystem.RequiresTextureBackendRecreation(
                        GPUDrivenTextureBackendMode.VirtualTexture,
                        asset),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void RequiresTextureBackendRecreation_TracksTerrainRVTOptInForVirtualTextureBackend()
        {
            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();
            try
            {
                Assert.That(
                    VividGPUDrivenSystem.RequiresTextureBackendRecreation(
                        GPUDrivenTextureBackendMode.VirtualTexture,
                        terrainRuntimeVirtualTextureRequested: false,
                        asset),
                    Is.False);

                asset.EnableTerrainRuntimeVirtualTexture = true;
                Assert.That(
                    VividGPUDrivenSystem.RequiresTextureBackendRecreation(
                        GPUDrivenTextureBackendMode.VirtualTexture,
                        terrainRuntimeVirtualTextureRequested: false,
                        asset),
                    Is.True);
                Assert.That(
                    VividGPUDrivenSystem.RequiresTextureBackendRecreation(
                        GPUDrivenTextureBackendMode.VirtualTexture,
                        terrainRuntimeVirtualTextureRequested: true,
                        asset),
                    Is.False);

                asset.GPUDrivenTextureBackend = GPUDrivenTextureBackendMode.Bindless;
                Assert.That(
                    VividGPUDrivenSystem.RequiresTextureBackendRecreation(
                        GPUDrivenTextureBackendMode.Bindless,
                        terrainRuntimeVirtualTextureRequested: false,
                        asset),
                    Is.False,
                    "Bindless ignores the experimental Terrain RVT toggle.");
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
