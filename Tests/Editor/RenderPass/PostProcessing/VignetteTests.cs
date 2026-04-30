using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class VignetteTests
    {
        [Test]
        public void Vignette_IsActive_ReturnsTrue_ForProceduralIntensity()
        {
            var vignette = new Vignette();

            vignette.mode.value = VignetteMode.Procedural;
            vignette.intensity.value = 0f;
            Assert.That(vignette.IsActive(), Is.False);

            vignette.intensity.value = 0.35f;
            Assert.That(vignette.IsActive(), Is.True);
        }

        [Test]
        public void Vignette_IsActive_RequiresMaskAndOpacity_ForMaskedMode()
        {
            var vignette = new Vignette();

            vignette.mode.value = VignetteMode.Masked;
            vignette.opacity.value = 1f;
            vignette.mask.value = null;
            Assert.That(vignette.IsActive(), Is.False);

            vignette.mask.value = Texture2D.whiteTexture;
            Assert.That(vignette.IsActive(), Is.True);

            vignette.opacity.value = 0f;
            Assert.That(vignette.IsActive(), Is.False);
        }

        [Test]
        public void VignetteSettingsData_CreateDefault_ReturnsHdrpDefaults()
        {
            var data = VignetteSettingsData.CreateDefault();

            Assert.That(data.enabled, Is.False);
            Assert.That(data.mode, Is.EqualTo(VignetteMode.Procedural));
            Assert.That(data.color, Is.EqualTo(Color.black));
            Assert.That(data.center, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(data.intensity, Is.EqualTo(0f));
            Assert.That(data.smoothness, Is.EqualTo(0.2f));
            Assert.That(data.roundness, Is.EqualTo(1f));
            Assert.That(data.rounded, Is.False);
            Assert.That(data.mask, Is.Null);
            Assert.That(data.opacity, Is.EqualTo(1f));
        }

        [Test]
        public void VignetteRuntimeUtility_CreateParams_MatchesHdrpScaling()
        {
            var data = new VignetteSettingsData
            {
                enabled = true,
                mode = VignetteMode.Procedural,
                center = new Vector2(0.25f, 0.75f),
                intensity = 0.4f,
                smoothness = 0.2f,
                roundness = 0.4f,
                rounded = true
            };

            var params1 = VignetteRuntimeUtility.CreateParams1(data);
            var params2 = VignetteRuntimeUtility.CreateParams2(data);

            Assert.That(params1, Is.EqualTo(new Vector4(0.25f, 0.75f, 0f, 0f)));
            Assert.That(params2.x, Is.EqualTo(1.2f).Within(0.0001f));
            Assert.That(params2.y, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(params2.z, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(params2.w, Is.EqualTo(1f));
        }

        [Test]
        public void VignetteRuntimeUtility_CreateMaskedParams_StoresModeAndOpacity()
        {
            var data = new VignetteSettingsData
            {
                enabled = true,
                mode = VignetteMode.Masked,
                color = Color.red,
                opacity = 0.45f
            };

            var params1 = VignetteRuntimeUtility.CreateParams1(data);
            var color = VignetteRuntimeUtility.CreateColor(data);

            Assert.That(params1, Is.EqualTo(new Vector4(0f, 0f, 1f, 0f)));
            Assert.That(color.x, Is.EqualTo(1f));
            Assert.That(color.y, Is.EqualTo(0f));
            Assert.That(color.z, Is.EqualTo(0f));
            Assert.That(color.w, Is.EqualTo(0.45f).Within(0.0001f));
        }

        [Test]
        public void VignetteSettingsResolver_Resolve_ReturnsConfiguredValues_WhenComponentActive()
        {
            var cameraObject = new GameObject("VignetteCamera");
            var camera = cameraObject.AddComponent<Camera>();
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var vignette = profile.Add<Vignette>(true);

            vignette.mode.value = VignetteMode.Masked;
            vignette.color.value = Color.green;
            vignette.center.value = new Vector2(0.2f, 0.8f);
            vignette.intensity.value = 0.6f;
            vignette.smoothness.value = 0.4f;
            vignette.roundness.value = 0.7f;
            vignette.rounded.value = true;
            vignette.mask.value = Texture2D.whiteTexture;
            vignette.opacity.value = 0.5f;

            try
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                VolumeManager.instance.Initialize(profile);
                VolumeManager.instance.Update(camera.transform, ~0);

                var settings = VignetteSettingsResolver.Resolve();

                Assert.That(settings.enabled, Is.True);
                Assert.That(settings.mode, Is.EqualTo(VignetteMode.Masked));
                Assert.That(settings.color, Is.EqualTo(Color.green));
                Assert.That(settings.center, Is.EqualTo(new Vector2(0.2f, 0.8f)));
                Assert.That(settings.intensity, Is.EqualTo(0.6f).Within(0.0001f));
                Assert.That(settings.smoothness, Is.EqualTo(0.4f).Within(0.0001f));
                Assert.That(settings.roundness, Is.EqualTo(0.7f).Within(0.0001f));
                Assert.That(settings.rounded, Is.True);
                Assert.That(settings.mask, Is.SameAs(Texture2D.whiteTexture));
                Assert.That(settings.opacity, Is.EqualTo(0.5f).Within(0.0001f));
            }
            finally
            {
                if (VolumeManager.instance.isInitialized)
                    VolumeManager.instance.Deinitialize();

                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void CreateEditor_UsesCustomVignetteEditor()
        {
            var component = ScriptableObject.CreateInstance<Vignette>();
            UnityEditor.Editor editor = null;

            try
            {
                editor = UnityEditor.Editor.CreateEditor(component);

                Assert.That(editor, Is.Not.Null);
                Assert.That(editor.GetType().Name, Is.EqualTo("VignetteEditor"));
            }
            finally
            {
                if (editor != null)
                    Object.DestroyImmediate(editor);

                Object.DestroyImmediate(component);
            }
        }

        [Test]
        public void FinalBlitPass_ResolvesVignetteSettingsAndSetsShaderKeyword()
        {
            var passSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "PostProcessing", "FinalBlitPass.cs"));

            Assert.That(passSource, Does.Contain("VignetteSettingsResolver.Resolve()"));
            Assert.That(passSource, Does.Contain("VignetteRuntimeUtility.CreateParams1"));
            Assert.That(passSource, Does.Contain("VignetteRuntimeUtility.CreateParams2"));
            Assert.That(passSource, Does.Contain("VignetteRuntimeUtility.CreateColor"));
            Assert.That(passSource, Does.Contain("CoreUtils.SetKeyword(m_Material, \"_VIGNETTE\", true)"));
            Assert.That(passSource, Does.Contain("CoreUtils.SetKeyword(m_Material, \"_VIGNETTE\", false)"));
        }

        [Test]
        public void FinalBlitShader_ContainsHdrpVignetteLogic_BeforeFilmGrain()
        {
            var shaderSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "FinalBlit.shader"));

            Assert.That(shaderSource, Does.Contain("#pragma multi_compile_local _ _VIGNETTE"));
            Assert.That(shaderSource, Does.Contain("_VividVignetteParams1"));
            Assert.That(shaderSource, Does.Contain("_VividVignetteParams2"));
            Assert.That(shaderSource, Does.Contain("_VividVignetteColor"));
            Assert.That(shaderSource, Does.Contain("_VividVignetteMask"));
            Assert.That(shaderSource, Does.Contain("VividVignetteIntensity"));
            Assert.That(shaderSource, Does.Contain("FastSRGBToLinear(vfactor)"));
            Assert.That(shaderSource, Does.Contain("postProcessed *= lerp(VividVignetteColor, (1.0).xxx, vfactor);"));
            Assert.That(shaderSource, Does.Contain("postProcessed = lerp(postProcessed, newColor, VividVignetteOpacity);"));

            var bloomIndex = shaderSource.IndexOf("postProcessed += bloom;", System.StringComparison.Ordinal);
            var vignetteIndex = shaderSource.IndexOf("if (VividVignetteMode < 0.5)", System.StringComparison.Ordinal);
            var filmGrainIndex = shaderSource.IndexOf("postProcessed = saturate(postProcessed);", System.StringComparison.Ordinal);

            Assert.That(bloomIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(vignetteIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(filmGrainIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(bloomIndex, Is.LessThan(vignetteIndex));
            Assert.That(vignetteIndex, Is.LessThan(filmGrainIndex));
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
