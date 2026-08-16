using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime.Experimental.Materials;

namespace VividRP.Editor.Tests
{
    public sealed class ExperimentalClosureBufferContractTests
    {
        private const string BufferAssetPath =
            "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/Closure/ExperimentalClosureBuffer.hlsl";
        private const string ClassificationAssetPath =
            "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/Closure/ExperimentalClosureClassification.compute";
        private const string DeferredLitAssetPath =
            "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/Closure/ExperimentalClosureDeferredLit.compute";

        [Test]
        public void ClosureBuffer_CpuAndHlslAbiIdentifiersMatch()
        {
            string source = File.ReadAllText(BufferAssetPath);

            StringAssert.Contains(
                $"#define VIVID_EXPERIMENTAL_CLOSURE_BUFFER_VERSION {VividExperimentalClosureBufferContract.BufferVersion}u",
                source);
            StringAssert.Contains(
                $"#define VIVID_EXPERIMENTAL_CLOSURE_BUFFER_ATTACHMENT_COUNT {VividExperimentalClosureBufferContract.AttachmentCount}u",
                source);
            StringAssert.Contains(
                $"#define VIVID_EXPERIMENTAL_CLOSURE_BUFFER_BYTES_PER_PIXEL {VividExperimentalClosureBufferContract.BytesPerPixel}u",
                source);
            StringAssert.Contains(
                "VividPackExperimentalClosureBuffer(",
                source);
            StringAssert.Contains(
                "VividUnpackExperimentalClosureBuffer(",
                source);

            Assert.That(
                VividExperimentalClosureBufferContract.BytesPerPixel,
                Is.EqualTo(4 + 4 + 4 + 4 + 4 + 8));
        }

        [Test]
        public void ClosureHeader_RoundTripsModelComplexityAndFeatures()
        {
            const VividExperimentalClosureFeatures features =
                VividExperimentalClosureFeatures.Coat
                | VividExperimentalClosureFeatures.Transmission;
            byte header = VividExperimentalClosureBufferContract.PackHeader(
                VividExperimentalClosureModel.Slab,
                VividExperimentalClosureComplexity.Single,
                features);

            Assert.That(
                VividExperimentalClosureBufferContract.IsValid(header),
                Is.True);
            Assert.That(
                VividExperimentalClosureBufferContract.GetModel(header),
                Is.EqualTo(VividExperimentalClosureModel.Slab));
            Assert.That(
                VividExperimentalClosureBufferContract.GetComplexity(header),
                Is.EqualTo(VividExperimentalClosureComplexity.Single));
            Assert.That(
                VividExperimentalClosureBufferContract.GetFeatures(header),
                Is.EqualTo(features));
        }

        [TestCase(
            ClassificationAssetPath,
            "ClearExperimentalClosureArgs",
            "ClassifyExperimentalClosureTiles")]
        [TestCase(
            DeferredLitAssetPath,
            "ExperimentalClosureLit_Fast",
            "ExperimentalClosureLit_Single",
            "ExperimentalClosureLit_Complex")]
        public void Stage2ComputeShader_ImportsWithoutErrorsAndExposesKernels(
            string assetPath,
            params string[] kernelNames)
        {
            ComputeShader compute =
                AssetDatabase.LoadAssetAtPath<ComputeShader>(assetPath);
            Assert.That(compute, Is.Not.Null, assetPath);

            ShaderMessage[] errors = ShaderUtil.GetComputeShaderMessages(compute)
                .Where(message => message.severity.ToString() == "Error")
                .ToArray();
            Assert.That(
                errors,
                Is.Empty,
                string.Join(
                    "\n",
                    errors.Select(message =>
                        $"{message.file}:{message.line}: {message.message}")));

            foreach (string kernelName in kernelNames)
                Assert.That(compute.HasKernel(kernelName), Is.True, kernelName);
        }
    }
}
