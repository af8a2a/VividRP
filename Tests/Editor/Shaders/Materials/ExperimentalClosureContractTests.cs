using System.IO;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime.Experimental.Materials;

namespace VividRP.Editor.Tests
{
    public sealed class ExperimentalClosureContractTests
    {
        private const string ClosureShaderAssetPath =
            "Packages/com.vivid.render-pipelines/Shaders/Material/Experimental/Closure/ExperimentalClosure.hlsl";

        [Test]
        public void ClosureContract_CpuAndHlslIdentifiersMatch()
        {
            string source = File.ReadAllText(ClosureShaderAssetPath);

            StringAssert.Contains(
                $"#define VIVID_EXPERIMENTAL_CLOSURE_SEMANTIC_VERSION {VividExperimentalClosureContract.SemanticVersion}u",
                source);
            StringAssert.Contains(
                $"#define VIVID_EXPERIMENTAL_CLOSURE_MAX_COUNT {VividExperimentalClosureContract.MaxClosureCount}u",
                source);
            StringAssert.Contains(
                "#define VIVID_EXPERIMENTAL_CLOSURE_MODEL_SLAB 0u",
                source);
            StringAssert.Contains(
                "#define VIVID_EXPERIMENTAL_CLOSURE_COMPLEXITY_FAST 0u",
                source);
            StringAssert.Contains(
                "#define VIVID_EXPERIMENTAL_CLOSURE_COMPLEXITY_SINGLE 1u",
                source);
            StringAssert.Contains(
                "#define VIVID_EXPERIMENTAL_CLOSURE_COMPLEXITY_COMPLEX 2u",
                source);
            StringAssert.Contains(
                "#define VIVID_EXPERIMENTAL_CLOSURE_OPERATOR_HORIZONTAL_MIX 0u",
                source);
            StringAssert.Contains(
                "#define VIVID_EXPERIMENTAL_CLOSURE_OPERATOR_VERTICAL_LAYER 1u",
                source);
            StringAssert.Contains(
                "#define VIVID_EXPERIMENTAL_CLOSURE_FEATURE_COAT (1u << 0)",
                source);
            StringAssert.Contains(
                "#define VIVID_EXPERIMENTAL_CLOSURE_FEATURE_TRANSMISSION (1u << 1)",
                source);
            StringAssert.Contains(
                "#define VIVID_EXPERIMENTAL_CLOSURE_FEATURE_SUBSURFACE (1u << 2)",
                source);
            StringAssert.Contains(
                "#define VIVID_EXPERIMENTAL_COMPATIBILITY_LOSS_SPECULAR_IOR (1u << 0)",
                source);
            StringAssert.Contains(
                "#define VIVID_EXPERIMENTAL_COMPATIBILITY_LOSS_COAT_ROUGHNESS (1u << 1)",
                source);
            StringAssert.Contains(
                "#define VIVID_EXPERIMENTAL_COMPATIBILITY_LOSS_TRANSMISSION (1u << 2)",
                source);
            StringAssert.Contains(
                "#define VIVID_EXPERIMENTAL_COMPATIBILITY_LOSS_SUBSURFACE (1u << 3)",
                source);
            StringAssert.Contains(
                "#define VIVID_EXPERIMENTAL_COMPATIBILITY_LOSS_MULTI_LAYER (1u << 4)",
                source);
            StringAssert.Contains(
                "uint VividClassifyExperimentalClosure(",
                source);

            Assert.That((uint)VividExperimentalClosureModel.Slab, Is.Zero);
            Assert.That(
                (uint)VividExperimentalClosureComplexity.Fast,
                Is.Zero);
            Assert.That(
                (uint)VividExperimentalClosureComplexity.Single,
                Is.EqualTo(1u));
            Assert.That(
                (uint)VividExperimentalClosureComplexity.Complex,
                Is.EqualTo(2u));
            Assert.That(
                (uint)VividExperimentalClosureOperator.HorizontalMix,
                Is.Zero);
            Assert.That(
                (uint)VividExperimentalClosureOperator.VerticalLayer,
                Is.EqualTo(1u));
            Assert.That(
                (uint)VividExperimentalClosureFeatures.Coat,
                Is.EqualTo(1u << 0));
            Assert.That(
                (uint)VividExperimentalClosureFeatures.Transmission,
                Is.EqualTo(1u << 1));
            Assert.That(
                (uint)VividExperimentalClosureFeatures.Subsurface,
                Is.EqualTo(1u << 2));
            Assert.That(
                (uint)VividExperimentalCompatibilityLoss.SpecularIor,
                Is.EqualTo(1u << 0));
            Assert.That(
                (uint)VividExperimentalCompatibilityLoss.CoatRoughness,
                Is.EqualTo(1u << 1));
            Assert.That(
                (uint)VividExperimentalCompatibilityLoss.Transmission,
                Is.EqualTo(1u << 2));
            Assert.That(
                (uint)VividExperimentalCompatibilityLoss.Subsurface,
                Is.EqualTo(1u << 3));
            Assert.That(
                (uint)VividExperimentalCompatibilityLoss.MultiLayer,
                Is.EqualTo(1u << 4));
        }

        [Test]
        public void StandardSurface_DefaultIorResolvesToFourPercentF0()
        {
            float f0 = VividExperimentalClosureContract.IorToF0(1.5f);
            Assert.That(f0, Is.EqualTo(0.04f).Within(0.000001f));

            Vector3 dielectricF0 =
                VividExperimentalClosureContract.ResolveSpecularF0(
                    new Vector3(0.8f, 0.4f, 0.2f),
                    0.0f,
                    1.5f);
            Assert.That(dielectricF0.x, Is.EqualTo(0.04f).Within(0.000001f));
            Assert.That(dielectricF0.y, Is.EqualTo(0.04f).Within(0.000001f));
            Assert.That(dielectricF0.z, Is.EqualTo(0.04f).Within(0.000001f));

            Vector3 metallicF0 =
                VividExperimentalClosureContract.ResolveSpecularF0(
                    new Vector3(0.8f, 0.4f, 0.2f),
                    1.0f,
                    1.5f);
            Assert.That(metallicF0.x, Is.EqualTo(0.8f).Within(0.000001f));
            Assert.That(metallicF0.y, Is.EqualTo(0.4f).Within(0.000001f));
            Assert.That(metallicF0.z, Is.EqualTo(0.2f).Within(0.000001f));
        }

        [Test]
        public void ClosureComplexity_UsesStableCpuBudgetClassification()
        {
            Assert.That(
                VividExperimentalClosureContract.Classify(
                    1,
                    VividExperimentalClosureFeatures.None),
                Is.EqualTo(VividExperimentalClosureComplexity.Fast));
            Assert.That(
                VividExperimentalClosureContract.Classify(
                    1,
                    VividExperimentalClosureFeatures.Coat),
                Is.EqualTo(VividExperimentalClosureComplexity.Single));
            Assert.That(
                VividExperimentalClosureContract.Classify(
                    2,
                    VividExperimentalClosureFeatures.None),
                Is.EqualTo(VividExperimentalClosureComplexity.Complex));
        }

        [Test]
        public void LegacyCompatibilityLoss_IsExplicitForUnsupportedSemantics()
        {
            Assert.That(
                VividExperimentalClosureContract.GetLegacyCompatibilityLoss(
                    1.5f,
                    0.0f,
                    0.1f,
                    0.0f,
                    0.0f),
                Is.EqualTo(VividExperimentalCompatibilityLoss.None));

            VividExperimentalCompatibilityLoss loss =
                VividExperimentalClosureContract.GetLegacyCompatibilityLoss(
                    2.0f,
                    1.0f,
                    0.0f,
                    0.5f,
                    0.5f);
            Assert.That(
                loss,
                Is.EqualTo(
                    VividExperimentalCompatibilityLoss.SpecularIor
                    | VividExperimentalCompatibilityLoss.CoatRoughness
                    | VividExperimentalCompatibilityLoss.Transmission
                    | VividExperimentalCompatibilityLoss.Subsurface));
        }
    }
}
