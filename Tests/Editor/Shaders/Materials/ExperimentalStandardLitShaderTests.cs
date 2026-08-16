using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Editor.Tests
{
    public sealed class ExperimentalStandardLitShaderTests
    {
        private const string ExperimentalShaderAssetPath =
            "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/StandardLit/ExperimentalStandardLit.shader";
        private const string ExperimentalInputAssetPath =
            "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/StandardLit/ExperimentalStandardLitInput.hlsl";
        private const string ExistingShaderAssetPath =
            "Packages/com.vivid.render-pipelines/Shaders/Material/StandardLit/StandardLit.shader";

        [Test]
        public void ExperimentalStandardLitShader_ImportsWithoutCompilerMessages()
        {
            Shader shader = LoadShader();
            ShaderMessage[] messages = ShaderUtil.GetShaderMessages(shader);

            Assert.That(
                messages,
                Is.Empty,
                string.Join(
                    "\n",
                    messages.Select(message =>
                        $"{message.file}:{message.line}: {message.message}")));
        }

        [Test]
        public void ExperimentalStandardLitShader_IsolatedFromExistingShader()
        {
            Shader shader = LoadShader();
            Shader existingShader = AssetDatabase.LoadAssetAtPath<Shader>(
                ExistingShaderAssetPath);

            Assert.That(existingShader, Is.Not.Null);
            Assert.That(shader.name, Is.EqualTo("VividRP/Experimental/Material/StandardLit"));
            Assert.That(shader, Is.Not.SameAs(existingShader));

            var material = new Material(shader);
            try
            {
                Assert.That(
                    material.GetTag("VividMaterialSystem", false),
                    Is.EqualTo("ExperimentalClosure"));
                Assert.That(
                    material.GetFloat("_VividExperimentalClosureVersion"),
                    Is.EqualTo(1.0f));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void ExperimentalStandardLitShader_ExposesRequiredDeferredPasses()
        {
            Shader shader = LoadShader();
            var material = new Material(shader);
            try
            {
                AssertPass(material, "VividPreDepth", "VividPreDepth");
                AssertPass(material, "ShadowCaster", "ShadowCaster");
                AssertPass(material, "VividGBuffer", "VividGBuffer");
                AssertPass(
                    material,
                    "VividGBufferGPUDrivenDecal",
                    "VividGBufferGPUDrivenDecal");
                AssertPass(material, "Meta", "Meta");
                AssertPass(material, "MotionVectors", "MotionVectors");
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void ExperimentalStandardLitGBuffer_UsesClosureAdapter()
        {
            string source = File.ReadAllText(ExperimentalInputAssetPath);
            string existingSource = File.ReadAllText(ExistingShaderAssetPath);

            StringAssert.Contains("BuildExperimentalStandardLitSurface", source);
            StringAssert.Contains("VividCompileExperimentalStandardSurface", source);
            StringAssert.Contains("VividExportExperimentalClosureToLegacyGBuffer", source);
            StringAssert.DoesNotContain("Material/Experimental/", existingSource);
        }

        private static Shader LoadShader()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                ExperimentalShaderAssetPath);
            Assert.That(
                shader,
                Is.Not.Null,
                $"Expected shader asset at '{ExperimentalShaderAssetPath}'.");
            return shader;
        }

        private static void AssertPass(
            Material material,
            string passName,
            string expectedLightMode)
        {
            int passIndex = material.FindPass(passName);
            Assert.That(passIndex, Is.GreaterThanOrEqualTo(0), passName);
            Assert.That(
                material.shader.FindPassTagValue(
                    passIndex,
                    new ShaderTagId("LightMode")),
                Is.EqualTo(new ShaderTagId(expectedLightMode)));
        }
    }
}
