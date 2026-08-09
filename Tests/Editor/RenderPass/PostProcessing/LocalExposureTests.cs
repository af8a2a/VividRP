using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class LocalExposureTests
    {
        [Test]
        public void LocalExposure_Defaults_MatchUnrealAlignedInactiveState()
        {
            var localExposure = ScriptableObject.CreateInstance<LocalExposure>();

            try
            {
                Assert.That(localExposure.enabled.value, Is.False);
                Assert.That(localExposure.highlightContrastScale.value, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(localExposure.shadowContrastScale.value, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(localExposure.detailStrength.value, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(localExposure.blurredLuminanceBlend.value, Is.EqualTo(0.6f).Within(1e-5f));
                Assert.That(localExposure.blurredLuminanceKernelSizePercent.value, Is.EqualTo(50f).Within(1e-5f));
                Assert.That(localExposure.highlightThreshold.value, Is.Zero);
                Assert.That(localExposure.shadowThreshold.value, Is.Zero);
                Assert.That(localExposure.highlightThresholdStrength.value, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(localExposure.shadowThresholdStrength.value, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(localExposure.middleGreyBias.value, Is.Zero);
                Assert.That(localExposure.IsActive(), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(localExposure);
            }
        }

        [Test]
        public void LocalExposure_IsActive_ReturnsEnabledState()
        {
            var localExposure = ScriptableObject.CreateInstance<LocalExposure>();

            try
            {
                localExposure.enabled.value = false;
                Assert.That(localExposure.IsActive(), Is.False);

                localExposure.enabled.value = true;
                Assert.That(localExposure.IsActive(), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(localExposure);
            }
        }

        [Test]
        public void LocalExposureSettingsResolver_ClampsConfiguredValues_WhenComponentActive()
        {
            var cameraObject = new GameObject("Local Exposure Camera");
            var camera = cameraObject.AddComponent<Camera>();
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var localExposure = profile.Add<LocalExposure>(true);

            localExposure.enabled.value = true;
            localExposure.highlightContrastScale.value = -2f;
            localExposure.shadowContrastScale.value = 2f;
            localExposure.detailStrength.value = 8f;
            localExposure.blurredLuminanceBlend.value = -1f;
            localExposure.blurredLuminanceKernelSizePercent.value = 150f;
            localExposure.highlightThreshold.value = 8f;
            localExposure.shadowThreshold.value = 8f;
            localExposure.highlightThresholdStrength.value = 2f;
            localExposure.shadowThresholdStrength.value = -1f;
            localExposure.middleGreyBias.value = 20f;

            try
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                VolumeManager.instance.Initialize(profile);
                VolumeManager.instance.Update(camera.transform, ~0);

                var settings = LocalExposureSettingsResolver.Resolve();

                Assert.That(settings.enabled, Is.True);
                Assert.That(settings.highlightContrastScale, Is.EqualTo(0f).Within(1e-5f));
                Assert.That(settings.shadowContrastScale, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(settings.detailStrength, Is.EqualTo(4f).Within(1e-5f));
                Assert.That(settings.blurredLuminanceBlend, Is.EqualTo(0f).Within(1e-5f));
                Assert.That(settings.blurredLuminanceKernelSizePercent, Is.EqualTo(100f).Within(1e-5f));
                Assert.That(settings.highlightThreshold, Is.EqualTo(4f).Within(1e-5f));
                Assert.That(settings.shadowThreshold, Is.EqualTo(4f).Within(1e-5f));
                Assert.That(settings.highlightThresholdStrength, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(settings.shadowThresholdStrength, Is.EqualTo(0f).Within(1e-5f));
                Assert.That(settings.middleGreyExposureCompensation, Is.EqualTo(Mathf.Pow(2f, 15f)).Within(1e-3f));
            }
            finally
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void LocalExposureCurveUtility_Resolve_ReturnsDisabledFallback_WhenCurveMissing()
        {
            var curve = LocalExposureCurveUtility.Resolve(null, "Missing Local Exposure Curve");

            Assert.That(curve.enabled, Is.False);
            Assert.That(curve.texture, Is.SameAs(Texture2D.blackTexture));
            Assert.That(curve.minEV100, Is.EqualTo(LocalExposureCurveUtility.DefaultCurveMinEV100).Within(1e-5f));
            Assert.That(curve.invRange, Is.EqualTo(1f / LocalExposureCurveUtility.DefaultCurveRange).Within(1e-5f));
        }

        [Test]
        public void LocalExposureCurveUtility_Resolve_BakesCurveTextureAndDomain()
        {
            var curve = AnimationCurve.Linear(-2f, 0.5f, 2f, 1.5f);

            try
            {
                var textureData = LocalExposureCurveUtility.Resolve(
                    curve,
                    "VividRP Local Exposure Highlight Contrast Curve");
                var texture = textureData.texture as Texture2D;

                Assert.That(textureData.enabled, Is.True);
                Assert.That(textureData.minEV100, Is.EqualTo(-2f).Within(1e-5f));
                Assert.That(textureData.invRange, Is.EqualTo(0.25f).Within(1e-5f));
                Assert.That(texture, Is.Not.Null);
                Assert.That(texture.GetPixelBilinear(0.5f, 0.5f).r, Is.EqualTo(1f).Within(0.02f));
            }
            finally
            {
                LocalExposureCurveUtility.Dispose();
            }
        }

        [Test]
        public void LocalExposurePass_UsesStableResourceLayout_ForSourceOverrides()
        {
            Assert.That(typeof(IStablePassResourceLayout).IsAssignableFrom(typeof(LocalExposurePass)), Is.True);
        }

        [Test]
        public void Initialize_RegistersSourceOutputAndTransientTextures()
        {
            IRenderPass renderPass = new LocalExposurePass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures;

            Assert.That(textureEntries, Has.Length.EqualTo(6));
            Assert.That(textureEntries[0].Name, Is.EqualTo("source"));
            Assert.That(textureEntries[0].Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(textureEntries[1].Name, Is.EqualTo("LocalExposureOutput"));
            Assert.That(textureEntries[1].Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(textureEntries[2].Name, Is.EqualTo("LocalExposureBilateralGrid"));
            Assert.That(textureEntries[2].Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(textureEntries[2].IsTransient, Is.True);
            Assert.That(textureEntries[3].Name, Is.EqualTo("LocalExposureLogLuminance"));
            Assert.That(textureEntries[3].Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(textureEntries[3].IsTransient, Is.True);
            Assert.That(textureEntries[4].Name, Is.EqualTo("LocalExposureBlurTemp"));
            Assert.That(textureEntries[4].Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(textureEntries[4].IsTransient, Is.True);
            Assert.That(textureEntries[5].Name, Is.EqualTo("LocalExposureBlurredLogLuminance"));
            Assert.That(textureEntries[5].Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(textureEntries[5].IsTransient, Is.True);
        }

        [Test]
        public void SetSourceTexture_MarksPassResourceLayoutDirty_AndRestoreRecoversOriginalSource()
        {
            var pass = new LocalExposurePass();
            var setMethod = typeof(LocalExposurePass).GetMethod("SetSourceTexture", BindingFlags.Instance | BindingFlags.NonPublic);
            var restoreMethod = typeof(LocalExposurePass).GetMethod("RestoreSourceTexture", BindingFlags.Instance | BindingFlags.NonPublic);
            var sourceField = typeof(LocalExposurePass).GetField("source", BindingFlags.Instance | BindingFlags.NonPublic);
            var originalSource = RenderGraphTexture.CreateInput("OriginalSource", GraphicsFormat.R16G16B16A16_SFloat);
            var injectedSource = RenderGraphTexture.CreateInput("InjectedSource", GraphicsFormat.R16G16B16A16_SFloat);

            Assert.That(setMethod, Is.Not.Null);
            Assert.That(restoreMethod, Is.Not.Null);
            Assert.That(sourceField, Is.Not.Null);
            sourceField.SetValue(pass, originalSource);

            setMethod.Invoke(pass, new object[] { injectedSource });

            Assert.That(pass.IsPassResourceLayoutDirty, Is.True);
            Assert.That(sourceField.GetValue(pass), Is.SameAs(injectedSource));

            pass.ClearPassResourceLayoutDirty();
            Assert.That(pass.IsPassResourceLayoutDirty, Is.False);

            restoreMethod.Invoke(pass, Array.Empty<object>());

            Assert.That(pass.IsPassResourceLayoutDirty, Is.True);
            Assert.That(sourceField.GetValue(pass), Is.SameAs(originalSource));
        }

        [Test]
        public void Prepare_ConfiguresOutputAndGridDescriptors_FromSourceDescriptor()
        {
            var pass = new LocalExposurePass();
            var setMethod = typeof(LocalExposurePass).GetMethod("SetSourceTexture", BindingFlags.Instance | BindingFlags.NonPublic);
            var outputField = typeof(LocalExposurePass).GetField("m_OutputTexture", BindingFlags.Instance | BindingFlags.NonPublic);
            var gridField = typeof(LocalExposurePass).GetField("m_BilateralGrid", BindingFlags.Instance | BindingFlags.NonPublic);
            var source = RenderGraphTexture.CreateInput("Source", GraphicsFormat.B10G11R11_UFloatPack32);
            source.desc.Width = 130;
            source.desc.Height = 65;
            source.desc.UseDynamicScale = true;

            Assert.That(setMethod, Is.Not.Null);
            Assert.That(outputField, Is.Not.Null);
            Assert.That(gridField, Is.Not.Null);

            setMethod.Invoke(pass, new object[] { source });
            pass.Prepare(new ContextContainer());

            var outputTexture = (RenderGraphTexture)outputField.GetValue(pass);
            var gridTexture = (RenderGraphTexture)gridField.GetValue(pass);

            Assert.That(outputTexture.desc.Name, Is.EqualTo("LocalExposureOutput"));
            Assert.That(outputTexture.desc.Width, Is.EqualTo(130));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(65));
            Assert.That(outputTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.B10G11R11_UFloatPack32));
            Assert.That(outputTexture.desc.EnableRandomWrite, Is.True);
            Assert.That(outputTexture.desc.Dimension, Is.EqualTo(TextureDimension.Tex2D));
            Assert.That(gridTexture.desc.Name, Is.EqualTo("LocalExposureBilateralGrid"));
            Assert.That(gridTexture.desc.Width, Is.EqualTo(3));
            Assert.That(gridTexture.desc.Height, Is.EqualTo(2));
            Assert.That(gridTexture.desc.Slices, Is.EqualTo(32));
            Assert.That(gridTexture.desc.Dimension, Is.EqualTo(TextureDimension.Tex3D));
            Assert.That(gridTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R32G32_SFloat));
            Assert.That(gridTexture.desc.EnableRandomWrite, Is.True);
        }

        [Test]
        public void Prepare_UsesInactiveDefaults_WhenPostProcessingIsUnavailable()
        {
            var pass = new LocalExposurePass();

            pass.Prepare(new ContextContainer());

            var settingsField = typeof(LocalExposurePass).GetField("m_Settings", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(settingsField, Is.Not.Null);

            var settings = (LocalExposureSettingsData)settingsField.GetValue(pass);
            Assert.That(settings.enabled, Is.False);
        }

        [Test]
        public void BuildRegistrations_IncludesLocalExposurePass()
        {
            var registrations = RenderPassNodeRegistryBuilder.BuildRegistrations(new[] { typeof(LocalExposurePass) });

            Assert.That(registrations.Select(registration => registration.NodeClassName), Does.Contain(nameof(LocalExposurePass)));
        }

        [Test]
        public void PipelineResourceUpdater_RecollectsLocalExposureComputeRegistration()
        {
            var container = ScriptableObject.CreateInstance<PipelineResourcesContainer>();

            try
            {
                PipelineResourceUpdater.UpdateContainerResources(container);

                var entry = container.Entries.FirstOrDefault(item =>
                    item.ResourceName == "Shaders/Core/Private/LocalExposure/LocalExposure.compute");
                Assert.That(entry, Is.Not.Null);
                Assert.That(entry.ResourceObject, Is.TypeOf<ComputeShader>());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(container);
            }
        }
    }
}
