using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests
{
    public sealed class SimpleSlabBSDFKernelTests
    {
        private const string ComputeRelativePath =
            "Tests/Editor/SubSystem/GPUDriven/SimpleSlabBSDFTests.compute";

        [Test]
        public void ReferenceKernel_MatchesFrozenAnalyticBaselines()
        {
            SimpleSlabBSDFResponse normal = Evaluate(
                new float3(0.0f, 0.0f, 1.0f),
                new float3(0.0f, 0.0f, 1.0f));
            SimpleSlabBSDFResponse oblique = Evaluate(
                new float3(0.0f, 0.0f, 1.0f),
                new float3(0.8660254037844386f, 0.0f, 0.5f));

            AssertFloat3(normal.Diffuse, new float3(0.05729578f));
            AssertFloat3(normal.Specular, new float3(0.05092958f));
            AssertFloat3(oblique.Diffuse, new float3(0.02864789f));
            AssertFloat3(oblique.Specular, new float3(0.002162586f));
        }

        [Test]
        public void ReferenceKernel_MatchesFrozenGgxTermsAtNormalIncidence()
        {
            Assert.That(
                SimpleSlabBSDFReferenceKernel.DistributionGGX(0.25f, 1.0f),
                Is.EqualTo(5.092958f).Within(1e-5f));
            Assert.That(
                SimpleSlabBSDFReferenceKernel
                    .VisibilitySmithGGXCorrelated(0.25f, 1.0f, 1.0f),
                Is.EqualTo(0.25f).Within(1e-6f));
        }

        [Test]
        public void ReferenceKernel_IsReciprocalBeforeDirectionalCosine()
        {
            var normal = new float3(0.0f, 0.0f, 1.0f);
            var oblique = new float3(0.8660254037844386f, 0.0f, 0.5f);
            SimpleSlabBSDFResponse forward = Evaluate(normal, oblique);
            SimpleSlabBSDFResponse reverse = Evaluate(oblique, normal);

            AssertFloat3(forward.Diffuse / 0.5f, reverse.Diffuse);
            AssertFloat3(forward.Specular / 0.5f, reverse.Specular);
        }

        [Test]
        public void ReferenceKernel_RejectsBackfacingDirections()
        {
            SimpleSlabBSDFResponse response = Evaluate(
                new float3(0.0f, 0.0f, 1.0f),
                new float3(0.0f, 0.0f, -1.0f));

            AssertFloat3(response.Diffuse, float3.zero);
            AssertFloat3(response.Specular, float3.zero);
        }

        [Test]
        public void HlslKernel_IsVividOwnedAndIndependentFromHdrpBSDF()
        {
            string packageRoot =
                VividPackagePathUtility.GetPreferredPackageRoot();
            string path = Path.Combine(
                packageRoot,
                "Shaders",
                "Core",
                "Public",
                "VividSimpleSlabBSDF.hlsl");
            string source = File.ReadAllText(path);
            string[] includes = File.ReadLines(path)
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("#include"))
                .ToArray();

            StringAssert.Contains(
                $"#define VIVID_SIMPLE_SLAB_BSDF_KERNEL_VERSION "
                    + $"{SimpleSlabBSDFReferenceKernel.Version}u",
                source);
            StringAssert.Contains("VividSimpleSlabDGGX(", source);
            StringAssert.Contains(
                "VividSimpleSlabVSmithGGXCorrelated(",
                source);
            StringAssert.Contains(
                "VividEvaluateSimpleSlabAnalyticDirect(",
                source);
            StringAssert.Contains("VividCombineSimpleSlabBSDFResponse(", source);
            StringAssert.Contains(
                "#define VIVID_SIMPLE_SLAB_MIN_HALF_VECTOR_LENGTH_SQ 1e-8f",
                source);
            StringAssert.Contains(
                "#define VIVID_SIMPLE_SLAB_MIN_VISIBILITY_DENOMINATOR 1e-6f",
                source);
            CollectionAssert.AreEqual(
                new[] { "#include \"VividSimpleSlabContract.hlsl\"" },
                includes);
            StringAssert.DoesNotContain("DisneyDiffuse", source);
            StringAssert.DoesNotContain("DV_SmithJointGGX", source);
        }

        [Test]
        public void HlslKernel_MatchesCpuReferenceBaselines()
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
            int kernel = compute.FindKernel("EvaluateBaselines");
            using var output = new ComputeBuffer(
                4,
                sizeof(float) * 4,
                ComputeBufferType.Structured);
            compute.SetBuffer(kernel, "_Output", output);
            compute.Dispatch(kernel, 1, 1, 1);

            var actual = new Vector4[4];
            output.GetData(actual);
            var normal = new float3(0.0f, 0.0f, 1.0f);
            var oblique = new float3(0.8660254037844386f, 0.0f, 0.5f);
            AssertGpuResponse(actual[0], Evaluate(normal, normal));
            AssertGpuResponse(actual[1], Evaluate(normal, oblique));
            AssertGpuResponse(actual[2], Evaluate(oblique, normal));
            AssertGpuResponse(
                actual[3],
                Evaluate(normal, new float3(0.0f, 0.0f, -1.0f)));
        }

        private static SimpleSlabBSDFResponse Evaluate(
            float3 viewDirectionWS,
            float3 lightDirectionWS)
        {
            return SimpleSlabBSDFReferenceKernel.EvaluateAnalyticDirect(
                new float3(0.18f),
                new float3(0.04f),
                0.5f,
                new float3(0.0f, 0.0f, 1.0f),
                viewDirectionWS,
                lightDirectionWS);
        }

        private static void AssertGpuResponse(
            Vector4 actual,
            SimpleSlabBSDFResponse expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.Diffuse.x).Within(1e-5f));
            Assert.That(actual.y, Is.EqualTo(expected.Specular.x).Within(1e-5f));
            Assert.That(actual.z, Is.EqualTo(expected.Combined.x).Within(1e-5f));
            Assert.That(actual.w, Is.EqualTo(1.0f));
        }

        private static void AssertFloat3(float3 actual, float3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(1e-6f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(1e-6f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(1e-6f));
        }
    }
}
