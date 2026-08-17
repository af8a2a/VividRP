using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.Experimental.Materials;

namespace VividRP.Editor.Tests
{
    public sealed class ExperimentalStandardLitShaderTests
    {
        private const string ExperimentalShaderAssetPath =
            "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/StandardLit/ExperimentalStandardLit.shader";
        private const string ExperimentalInputAssetPath =
            "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/StandardLit/ExperimentalStandardLitInput.hlsl";
        private const string ExperimentalVisibilityAssetPath =
            "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/StandardLit/ExperimentalStandardLitVisibilityBufferPass.hlsl";
        private const string ExistingShaderAssetPath =
            "Packages/com.vivid.render-pipelines/Shaders/Material/StandardLit/StandardLit.shader";

        [Test]
        public void ExperimentalStandardLitShader_ImportsWithoutCompilerErrors()
        {
            Shader shader = LoadShader();
            ShaderMessage[] errors = ShaderUtil.GetShaderMessages(shader)
                .Where(message => message.severity.ToString() == "Error")
                .ToArray();

            Assert.That(
                errors,
                Is.Empty,
                string.Join(
                    "\n",
                    errors.Select(message =>
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
                    Is.EqualTo(
                        (float)VividExperimentalClosureContract.SemanticVersion));
                Assert.That(material.HasProperty("_TopLayerBaseMap"), Is.True);
                Assert.That(material.HasProperty("_TopLayerMaskMap"), Is.True);
                Assert.That(material.HasProperty("_TopLayerWeight"), Is.True);
                Assert.That(material.HasProperty("_TopLayerOperator"), Is.True);
                Assert.That(material.HasProperty("_VividExperimentalVBufferMaterialIndex"), Is.True);
                Assert.That(material.GetFloat("_VividExperimentalVBufferMaterialIndex"), Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void ExperimentalStandardLitShader_ExposesVBufferAndAuxiliaryPassesOnly()
        {
            Shader shader = LoadShader();
            var material = new Material(shader);
            try
            {
                AssertPass(material, "VividPreDepth", "VividPreDepth");
                AssertPass(
                    material,
                    "ExperimentalVisibilityBuffer",
                    "ExperimentalVisibilityBuffer");
                AssertPass(material, "Meta", "Meta");
                AssertPass(material, "MotionVectors", "MotionVectors");
                AssertPass(material, "IndirectDXR", "IndirectDXR");
                AssertPass(
                    material,
                    "ReferencedPathtracingDXR",
                    "ReferencedPathtracingDXR");
                AssertPass(
                    material,
                    "RaytracingGBufferDXR",
                    "RaytracingGBufferDXR");
                Assert.That(material.FindPass("ExperimentalClosureBuffer"), Is.EqualTo(-1));
                Assert.That(material.FindPass("VividGBuffer"), Is.EqualTo(-1));
                Assert.That(material.FindPass("VividGBufferGPUDrivenDecal"), Is.EqualTo(-1));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void ExperimentalVisibilityBuffer_UsesAttributeAbiWithoutMaterialSampling()
        {
            string source = File.ReadAllText(ExperimentalVisibilityAssetPath);

            StringAssert.Contains("uint2 visibility : SV_Target0", source);
            StringAssert.Contains("float4 attributes0 : SV_Target1", source);
            StringAssert.Contains("float4 attributes1 : SV_Target2", source);
            StringAssert.Contains("uint primitiveID : SV_PrimitiveID", source);
            StringAssert.Contains("ddx(input.uv0)", source);
            StringAssert.Contains("ddy(input.uv0)", source);
            StringAssert.Contains("VividExperimentalEncodeNormalOct", source);
            StringAssert.DoesNotContain("SAMPLE_TEXTURE2D", source);
            StringAssert.DoesNotContain("ExperimentalClosureBuffer", source);
        }

        [Test]
        public void ExperimentalStandardLitGBuffer_UsesClosureAdapter()
        {
            string source = File.ReadAllText(ExperimentalInputAssetPath);
            string existingSource = File.ReadAllText(ExistingShaderAssetPath);

            StringAssert.Contains("SampleExperimentalStandardLitSurface", source);
            StringAssert.Contains("VividResolveExperimentalStandardSurface", source);
            StringAssert.Contains("VividCompileExperimentalStandardSurface", source);
            StringAssert.Contains("BuildExperimentalStandardLitMaterial", source);
            StringAssert.Contains("VividCompileExperimentalLayeredSurface", source);
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
