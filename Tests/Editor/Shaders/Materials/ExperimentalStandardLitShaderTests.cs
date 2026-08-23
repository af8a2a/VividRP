using System.IO;
using System.Linq;
using System.Reflection;
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
        private const string ExistingShaderAssetPath =
            "Packages/com.vivid.render-pipelines/Shaders/Material/StandardLit/StandardLit.shader";

        [Test]
        public void ExperimentalStandardLitShader_ExposesPackedRMOMap()
        {
            var material = new Material(LoadShader());
            try
            {
                Assert.That(material.HasProperty("_RMOMap"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void SetupMaterial_PrioritizesRMOMapOverLegacyPBRMaps()
        {
            var material = new Material(LoadShader());
            var rmoMap = new Texture2D(1, 1);
            var metallicMap = new Texture2D(1, 1);
            var roughnessMap = new Texture2D(1, 1);
            var occlusionMap = new Texture2D(1, 1);
            try
            {
                material.SetTexture("_RMOMap", rmoMap);
                material.SetTexture("_MetallicGlossMap", metallicMap);
                material.SetTexture("_RoughnessMap", roughnessMap);
                material.SetTexture("_OcclusionMap", occlusionMap);

                StandardLitMaterialUtility.SetupMaterial(material, null, false);

                Assert.That(material.IsKeywordEnabled("_RMOMAP"), Is.True);
                Assert.That(material.IsKeywordEnabled("_METALLICSPECGLOSSMAP"), Is.False);
                Assert.That(material.IsKeywordEnabled("_ROUGHNESSMAP"), Is.False);
                Assert.That(material.IsKeywordEnabled("_OCCLUSIONMAP"), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(rmoMap);
                Object.DestroyImmediate(metallicMap);
                Object.DestroyImmediate(roughnessMap);
                Object.DestroyImmediate(occlusionMap);
                Object.DestroyImmediate(material);
            }
        }

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
                Assert.That(material.HasProperty("_TopLayerBaseMap"), Is.False);
                Assert.That(material.HasProperty("_TopLayerMaskMap"), Is.False);
                Assert.That(material.HasProperty("_TopLayerWeight"), Is.False);
                Assert.That(material.HasProperty("_TopLayerOperator"), Is.False);
                Assert.That(material.HasProperty("_VividExperimentalVBufferMaterialIndex"), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void ExperimentalStandardLitShader_ExposesAuxiliaryPassesOnly()
        {
            Shader shader = LoadShader();
            var material = new Material(shader);
            try
            {
                AssertPass(material, "VividPreDepth", "VividPreDepth");
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
                Assert.That(material.FindPass("VisibilityBuffer"), Is.EqualTo(-1));
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
        public void ExperimentalStandardLitShader_IsSRPBatcherCompatible_ForRasterPasses()
        {
            var material = new Material(LoadShader());
            try
            {
                AssertNoSRPBatcherIssue(material, "VividPreDepth");
                AssertNoSRPBatcherIssue(material, "ShadowCaster");
                AssertNoSRPBatcherIssue(material, "Meta");
                AssertNoSRPBatcherIssue(material, "MotionVectors");
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

            StringAssert.Contains("SampleExperimentalStandardLitSurface", source);
            StringAssert.Contains("VividResolveExperimentalStandardSurface", source);
            StringAssert.Contains("VividCompileExperimentalStandardSurface", source);
            StringAssert.Contains("BuildExperimentalStandardLitMaterial", source);
            StringAssert.DoesNotContain("VividCompileExperimentalLayeredSurface", source);
            StringAssert.DoesNotContain("_TopLayer", source);
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

        private static void AssertNoSRPBatcherIssue(
            Material material,
            string passName)
        {
            int passIndex = material.FindPass(passName);
            Assert.That(passIndex, Is.GreaterThanOrEqualTo(0), passName);

            MethodInfo method = typeof(ShaderUtil).GetMethod(
                "GetSRPBatcherCompatibilityIssueReason",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Shader), typeof(int), typeof(int) },
                null);
            if (method == null)
                Assert.Ignore("This Unity version does not expose ShaderUtil.GetSRPBatcherCompatibilityIssueReason.");

            string issueReason =
                (string)method.Invoke(null, new object[] { material.shader, 0, passIndex })
                ?? string.Empty;
            bool isCompatible = string.IsNullOrEmpty(issueReason)
                || issueReason.StartsWith("OK", System.StringComparison.OrdinalIgnoreCase)
                || issueReason.StartsWith("Not initialized", System.StringComparison.OrdinalIgnoreCase);
            Assert.That(
                isCompatible,
                Is.True,
                $"Pass '{passName}' is not SRP Batcher compatible: {issueReason}");
        }
    }
}
