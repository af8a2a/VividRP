using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests
{
    public sealed class SimpleSlabDirectLightingTests
    {
        private const string HeaderRelativePath =
            "Shaders/Core/Public/VividSimpleSlabDirectLighting.hlsl";
        private const string DeferredRelativePath =
            "Shaders/Material/DeferredLit.compute";
        private const string ComputeRelativePath =
            "Tests/Editor/SubSystem/GPUDriven/"
            + "SimpleSlabDirectLightingTests.compute";

        [Test]
        public void DirectLightingHeader_OwnsDirectionalAndPunctualIntegration()
        {
            string source = ReadPackageFile(HeaderRelativePath);
            string[] includes = source.Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("#include"))
                .ToArray();

            StringAssert.Contains(
                "#define VIVID_SIMPLE_SLAB_DIRECT_LIGHTING_VERSION "
                    + $"{MaterialProgramContract.SimpleSlabDirectLightingVersion}u",
                source);
            CollectionAssert.AreEqual(
                new[]
                {
                    "#include \"VividSimpleSlabBSDF.hlsl\"",
                    "#include \"PunctualLightCommon.hlsl\"",
                },
                includes);
            StringAssert.Contains(
                "VividEvaluateSimpleSlabDirectionalLight(",
                source);
            StringAssert.Contains(
                "VividEvaluateSimpleSlabPunctualLight(",
                source);
            StringAssert.Contains(
                "VividPunctualLightAttenuationWithDistanceModification(",
                source);
            StringAssert.Contains(
                "VividEvaluateSimpleSlabAnalyticDirect(",
                source);
            StringAssert.DoesNotContain("HdrpLitLighting.hlsl", source);
            StringAssert.DoesNotContain("EvaluateBSDF_Directional", source);
            StringAssert.DoesNotContain("EvaluateBSDF_Punctual", source);
        }

        [Test]
        public void DeferredLighting_ConsumesSimpleSlabForAnalyticLights()
        {
            string source = ReadPackageFile(DeferredRelativePath);

            StringAssert.Contains(
                "VividSimpleSlabDirectLighting.hlsl",
                source);
            Assert.That(
                CountOccurrences(
                    source,
                    "VividEvaluateSimpleSlabDirectionalLight("),
                Is.EqualTo(3));
            Assert.That(
                CountOccurrences(
                    source,
                    "VividEvaluateSimpleSlabPunctualLight("),
                Is.EqualTo(3));
            StringAssert.DoesNotContain("EvaluateBSDF_Directional(", source);
            StringAssert.DoesNotContain("EvaluateBSDF_Punctual(", source);
            StringAssert.Contains("EvaluateBSDF_Area(", source);
            StringAssert.Contains("EvaluateBSDF_Env(", source);
            StringAssert.Contains(
                "lightLoopOutput.diffuseLighting += simpleDirectLighting.diffuse;",
                source);
            StringAssert.Contains(
                "lightLoopOutput.specularLighting += simpleDirectLighting.specular;",
                source);
        }

        [Test]
        public void DirectLightingGpu_MatchesFrozenLightScaling()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Assert.Ignore(
                    "The active graphics device does not support compute shaders.");
            }

            string assetPath = VividPackagePathUtility.GetPreferredAssetPath(
                ComputeRelativePath);
            ComputeShader compute =
                AssetDatabase.LoadAssetAtPath<ComputeShader>(assetPath);
            Assert.That(compute, Is.Not.Null, assetPath);
            ShaderMessage[] errors = ShaderUtil
                .GetComputeShaderMessages(compute)
                .Where(message => message.severity.ToString() == "Error")
                .ToArray();
            Assert.That(
                errors,
                Is.Empty,
                string.Join("\n", errors.Select(error => error.message)));
            int kernel = compute.FindKernel("EvaluateDirectLights");
            using var output = new ComputeBuffer(
                2,
                sizeof(float) * 4,
                ComputeBufferType.Structured);
            compute.SetBuffer(kernel, "_Output", output);
            compute.Dispatch(kernel, 1, 1, 1);

            var actual = new Vector4[2];
            output.GetData(actual);
            SimpleSlabBSDFResponse response =
                SimpleSlabBSDFReferenceKernel.EvaluateAnalyticDirect(
                    new float3(0.18f),
                    new float3(0.04f),
                    0.5f,
                    new float3(0.0f, 0.0f, 1.0f),
                    new float3(0.0f, 0.0f, 1.0f),
                    new float3(0.0f, 0.0f, 1.0f));
            float3 expectedScale = new float3(0.5f, 0.125f, 0.25f);

            AssertLighting(actual[0], response, expectedScale);
            AssertLighting(actual[1], response, expectedScale);
        }

        private static string ReadPackageFile(string relativePath)
        {
            string assetPath = VividPackagePathUtility.GetPreferredAssetPath(
                relativePath);
            return File.ReadAllText(assetPath);
        }

        private static int CountOccurrences(string source, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = source.IndexOf(value, index)) >= 0)
            {
                count++;
                index += value.Length;
            }
            return count;
        }

        private static void AssertLighting(
            Vector4 actual,
            SimpleSlabBSDFResponse response,
            float3 scale)
        {
            Assert.That(
                actual.x,
                Is.EqualTo(response.Diffuse.x * scale.x).Within(1e-5f));
            Assert.That(
                actual.y,
                Is.EqualTo(response.Specular.x * scale.x).Within(1e-5f));
            Assert.That(
                actual.z,
                Is.EqualTo(response.Diffuse.y * scale.y).Within(1e-5f));
            Assert.That(
                actual.w,
                Is.EqualTo(response.Specular.y * scale.y).Within(1e-5f));
        }
    }
}
