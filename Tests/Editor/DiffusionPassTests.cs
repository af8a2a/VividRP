using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class DiffusionPassTests
    {
        [Test]
        public void Diffusion_IsActive_ReturnsEnabledState()
        {
            var diffusion = ScriptableObject.CreateInstance<Diffusion>();

            try
            {
                diffusion.enabled.value = false;
                Assert.That(diffusion.IsActive(), Is.False);

                diffusion.enabled.value = true;
                Assert.That(diffusion.IsActive(), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(diffusion);
            }
        }

        [Test]
        public void DiffusionSettingsResolver_Resolve_ReturnsConfiguredValues_WhenComponentActive()
        {
            var cameraObject = new GameObject("DiffusionCamera");
            var camera = cameraObject.AddComponent<Camera>();
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var diffusion = profile.Add<Diffusion>(true);

            diffusion.enabled.value = true;
            diffusion.mode.value = DiffusionMode.Max;
            diffusion.multiply.value = 0.35f;
            diffusion.blurScale.value = 1.25f;
            diffusion.filter.value = 0.75f;
            diffusion.intensity.value = 0.6f;
            diffusion.blurIntensity.value = 0.8f;

            try
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                VolumeManager.instance.Initialize(profile);
                VolumeManager.instance.Update(camera.transform, ~0);

                var settings = DiffusionSettingsResolver.Resolve();

                Assert.That(settings.enabled, Is.True);
                Assert.That(settings.mode, Is.EqualTo(DiffusionMode.Max));
                Assert.That(settings.multiply, Is.EqualTo(0.35f).Within(0.0001f));
                Assert.That(settings.blurScale, Is.EqualTo(1.25f).Within(0.0001f));
                Assert.That(settings.filter, Is.EqualTo(0.75f).Within(0.0001f));
                Assert.That(settings.intensity, Is.EqualTo(0.6f).Within(0.0001f));
                Assert.That(settings.blurIntensity, Is.EqualTo(0.8f).Within(0.0001f));
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
        public void CreateEditor_UsesCustomDiffusionEditor()
        {
            var component = ScriptableObject.CreateInstance<Diffusion>();
            UnityEditor.Editor editor = null;

            try
            {
                editor = UnityEditor.Editor.CreateEditor(component);

                Assert.That(editor, Is.Not.Null);
                Assert.That(editor.GetType().Name, Is.EqualTo("DiffusionEditor"));
            }
            finally
            {
                if (editor != null)
                    UnityEngine.Object.DestroyImmediate(editor);

                UnityEngine.Object.DestroyImmediate(component);
            }
        }

        [Test]
        public void Initialize_RegistersSourceOutputAndHiddenTextures()
        {
            IRenderPass renderPass = new DiffusionPass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures;

            Assert.That(textureEntries, Has.Length.EqualTo(5));
            Assert.That(textureEntries[0].Name, Is.EqualTo("source"));
            Assert.That(textureEntries[0].Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(textureEntries[1].Name, Is.EqualTo("DiffusionTexture"));
            Assert.That(textureEntries[1].Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(textureEntries[2].Name, Is.EqualTo("DiffusionTemp1"));
            Assert.That(textureEntries[2].Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(textureEntries[3].Name, Is.EqualTo("DiffusionTemp2"));
            Assert.That(textureEntries[3].Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(textureEntries[4].Name, Is.EqualTo("DiffusionOutput"));
            Assert.That(textureEntries[4].Access, Is.EqualTo(AccessFlags.Write));
        }

        [Test]
        public void SetSourceTexture_MarksPassResourceLayoutDirty_AndRestoreRecoversOriginalSource()
        {
            var pass = new DiffusionPass();
            var setMethod = typeof(DiffusionPass).GetMethod("SetSourceTexture", BindingFlags.Instance | BindingFlags.NonPublic);
            var restoreMethod = typeof(DiffusionPass).GetMethod("RestoreSourceTexture", BindingFlags.Instance | BindingFlags.NonPublic);
            var sourceField = typeof(DiffusionPass).GetField("source", BindingFlags.Instance | BindingFlags.NonPublic);
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
        public void Prepare_ClonesSourceDescriptor_ForOutputAndHalfResolutionTexture()
        {
            var pass = new DiffusionPass();
            var setMethod = typeof(DiffusionPass).GetMethod("SetSourceTexture", BindingFlags.Instance | BindingFlags.NonPublic);
            var outputField = typeof(DiffusionPass).GetField("m_OutputTexture", BindingFlags.Instance | BindingFlags.NonPublic);
            var diffusionField = typeof(DiffusionPass).GetField("m_DiffusionTexture", BindingFlags.Instance | BindingFlags.NonPublic);
            var source = RenderGraphTexture.CreateInput("Source", GraphicsFormat.B10G11R11_UFloatPack32);
            source.desc.Width = 320;
            source.desc.Height = 180;
            source.desc.UseDynamicScale = true;

            Assert.That(setMethod, Is.Not.Null);
            Assert.That(outputField, Is.Not.Null);
            Assert.That(diffusionField, Is.Not.Null);

            setMethod.Invoke(pass, new object[] { source });
            pass.Prepare(new ContextContainer());

            var outputTexture = (RenderGraphTexture)outputField.GetValue(pass);
            var diffusionTexture = (RenderGraphTexture)diffusionField.GetValue(pass);

            Assert.That(outputTexture.desc.Width, Is.EqualTo(320));
            Assert.That(outputTexture.desc.Height, Is.EqualTo(180));
            Assert.That(outputTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.B10G11R11_UFloatPack32));
            Assert.That(outputTexture.desc.Name, Is.EqualTo("DiffusionOutput"));
            Assert.That(diffusionTexture.desc.Width, Is.EqualTo(160));
            Assert.That(diffusionTexture.desc.Height, Is.EqualTo(90));
            Assert.That(diffusionTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.B10G11R11_UFloatPack32));
            Assert.That(diffusionTexture.desc.Name, Is.EqualTo("DiffusionTexture"));
        }

        [Test]
        public void GeneratedNodeRegistry_IncludesDiffusionPass()
        {
            var nodeType = RenderPassNodeRegistry.GetNodeType(typeof(DiffusionPass));

            Assert.That(nodeType, Is.Not.Null);
            Assert.That(nodeType.Name, Is.EqualTo(nameof(DiffusionPass)));
        }


        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var packageRoots = new[]
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(relativeParts));
        }
    }
}
