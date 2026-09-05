using System.Globalization;
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests
{
    public sealed class SimpleSlabContractTests
    {
        [Test]
        public void Contract_FreezesSimpleSlabV1Semantics()
        {
            Assert.That(SimpleSlabContract.Version, Is.EqualTo(1u));
            Assert.That(SimpleSlabContract.FingerprintVersion, Is.EqualTo(1u));
            Assert.That(SimpleSlabContract.FieldCount, Is.EqualTo(4u));
            Assert.That(
                SimpleSlabContract.SurfaceSummaryAbi,
                Is.EqualTo(MaterialDeferredExportSurfaceSummaryAbi.SurfaceSummaryV1));
            Assert.That(
                SimpleSlabContract.DiffuseModel,
                Is.EqualTo(SimpleSlabDiffuseModel.Lambert));
            Assert.That(
                SimpleSlabContract.SpecularModel,
                Is.EqualTo(SimpleSlabSpecularModel.IsotropicGgxSmithCorrelated));
            Assert.That(
                SimpleSlabContract.FresnelModel,
                Is.EqualTo(SimpleSlabFresnelModel.SchlickDerivedAchromaticF90));
            Assert.That(
                SimpleSlabContract.EnergyModel,
                Is.EqualTo(SimpleSlabEnergyModel.DirectionalAlbedoMultipleScattering));
            Assert.That(SimpleSlabContract.DirectResponseIncludesNdotL, Is.True);
            Assert.That(SimpleSlabContract.AmbientOcclusionAffectsDirectLighting, Is.False);
            Assert.That(SimpleSlabContract.AmbientOcclusionAffectsIndirectLighting, Is.True);
            Assert.That(SimpleSlabContract.EmissionIsAddedAfterLighting, Is.True);
        }

        [TestCase(-1.0f, 0.002f)]
        [TestCase(0.0f, 0.002f)]
        [TestCase(0.01f, 0.002f)]
        [TestCase(0.05f, 0.0025f)]
        [TestCase(0.25f, 0.0625f)]
        [TestCase(0.5f, 0.25f)]
        [TestCase(1.0f, 1.0f)]
        [TestCase(2.0f, 1.0f)]
        public void PerceptualRoughnessToAlpha_MatchesFrozenBaseline(
            float perceptualRoughness,
            float expectedAlpha)
        {
            Assert.That(
                SimpleSlabContract.PerceptualRoughnessToAlpha(perceptualRoughness),
                Is.EqualTo(expectedAlpha).Within(1e-7f));
        }

        [TestCase(0.0f, 0.0f)]
        [TestCase(0.01f, 0.5f)]
        [TestCase(0.02f, 1.0f)]
        [TestCase(0.04f, 1.0f)]
        [TestCase(1.0f, 1.0f)]
        public void DerivedF90_MatchesFrozenGrayDielectricBaseline(
            float f0,
            float expectedF90)
        {
            Assert.That(
                SimpleSlabContract.DeriveAchromaticF90(new float3(f0)),
                Is.EqualTo(expectedF90).Within(1e-6f));
        }

        [Test]
        public void DerivedF90_UsesTheFrozenAchromaticAverage()
        {
            Assert.That(
                SimpleSlabContract.DeriveAchromaticF90(
                    new float3(0.03f, 0.0f, 0.0f)),
                Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(
                SimpleSlabContract.DerivedF90FadeThreshold
                    * SimpleSlabContract.DerivedF90Scale,
                Is.EqualTo(1.0f).Within(1e-6f));
        }

        [Test]
        public void SchlickFresnel_ReachesF0AndDerivedF90AtEndpoints()
        {
            var f0 = new float3(0.01f, 0.02f, 0.03f);
            float3 atNormal = SimpleSlabContract.EvaluateSchlickFresnel(f0, 1.0f);
            float3 atGrazing = SimpleSlabContract.EvaluateSchlickFresnel(f0, 0.0f);

            AssertFloat3(atNormal, f0);
            AssertFloat3(
                atGrazing,
                new float3(SimpleSlabContract.DeriveAchromaticF90(f0)));
        }

        [Test]
        public void SchlickFresnel_MatchesFrozenMidAngleBaseline()
        {
            float3 result = SimpleSlabContract.EvaluateSchlickFresnel(
                new float3(0.04f),
                0.5f);

            AssertFloat3(result, new float3(0.07f));
        }

        [Test]
        public void Fingerprint_FreezesEverySimpleSlabConvention()
        {
            Assert.That(SimpleSlabContract.Fingerprint.Version, Is.EqualTo(1u));
            Assert.That(
                SimpleSlabContract.Fingerprint.Value,
                Is.EqualTo(0x26E2E47BB6B790D8ul));
        }

        [Test]
        public void FrozenV1_UsesTheApprovedLightingVersionSet()
        {
            // Pin the implemented version set, not just equality between C#/HLSL.
            // The input fingerprint is intentionally independent of evaluator fixes.
            Assert.That(MaterialProgramContract.SimpleSlabBSDFKernelVersion, Is.EqualTo(1u));
            Assert.That(MaterialProgramContract.SimpleSlabDirectLightingVersion, Is.EqualTo(2u));
            Assert.That(MaterialProgramContract.SimpleSlabEnergyVersion, Is.EqualTo(1u));
            Assert.That(MaterialProgramContract.SimpleSlabDeferredLightingVersion, Is.EqualTo(5u));
            Assert.That(Runtime.VividSlabLut.Version, Is.EqualTo(1u));
            Assert.That(Runtime.VividSlabLut.Resolution, Is.EqualTo(64));
            Assert.That(Runtime.VividSlabLut.SampleCount, Is.EqualTo(4096));
            Assert.That(Runtime.VividSlabLut.Format,
                Is.EqualTo(UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat));
        }

        [Test]
        public void HlslContract_MatchesCSharpSourceOfTruth()
        {
            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(SimpleSlabContract).Assembly);
            Assert.That(package, Is.Not.Null);
            string path = Path.Combine(
                package.resolvedPath,
                "Shaders",
                "Core",
                "Public",
                "VividSimpleSlabContract.hlsl");
            Assert.That(File.Exists(path), Is.True, path);
            string source = File.ReadAllText(path);

            AssertDefine(source, "VIVID_SIMPLE_SLAB_CONTRACT_VERSION", SimpleSlabContract.Version);
            AssertDefine(source, "VIVID_SIMPLE_SLAB_FINGERPRINT_VERSION", SimpleSlabContract.FingerprintVersion);
            AssertDefine(source, "VIVID_SIMPLE_SLAB_FINGERPRINT_LO", (uint) SimpleSlabContract.Fingerprint.Value, true);
            AssertDefine(source, "VIVID_SIMPLE_SLAB_FINGERPRINT_HI", (uint) (SimpleSlabContract.Fingerprint.Value >> 32), true);
            AssertDefine(source, "VIVID_SIMPLE_SLAB_SURFACE_SUMMARY_ABI_VERSION", (uint) SimpleSlabContract.SurfaceSummaryAbi);
            AssertDefine(source, "VIVID_SIMPLE_SLAB_FIELD_COUNT", SimpleSlabContract.FieldCount);
            AssertDefine(source, "VIVID_SIMPLE_SLAB_FIELD_DIFFUSE_ALBEDO", (uint) SimpleSlabFieldSemantic.DiffuseAlbedo);
            AssertDefine(source, "VIVID_SIMPLE_SLAB_FIELD_SPECULAR_F0", (uint) SimpleSlabFieldSemantic.SpecularF0);
            AssertDefine(source, "VIVID_SIMPLE_SLAB_FIELD_PERCEPTUAL_ROUGHNESS", (uint) SimpleSlabFieldSemantic.PerceptualRoughness);
            AssertDefine(source, "VIVID_SIMPLE_SLAB_FIELD_NORMAL_WS", (uint) SimpleSlabFieldSemantic.NormalWS);
            AssertDefine(source, "VIVID_SIMPLE_SLAB_DIFFUSE_MODEL_LAMBERT", (uint) SimpleSlabContract.DiffuseModel);
            AssertDefine(source, "VIVID_SIMPLE_SLAB_SPECULAR_MODEL_ISOTROPIC_GGX_SMITH_CORRELATED", (uint) SimpleSlabContract.SpecularModel);
            AssertDefine(source, "VIVID_SIMPLE_SLAB_FRESNEL_MODEL_SCHLICK_DERIVED_ACHROMATIC_F90", (uint) SimpleSlabContract.FresnelModel);
            AssertDefine(source, "VIVID_SIMPLE_SLAB_ENERGY_MODEL_DIRECTIONAL_ALBEDO_MULTIPLE_SCATTERING", (uint) SimpleSlabContract.EnergyModel);
            AssertDefine(source, "VIVID_SIMPLE_SLAB_MINIMUM_ALPHA_ROUGHNESS", SimpleSlabContract.MinimumAlphaRoughness);
            AssertDefine(source, "VIVID_SIMPLE_SLAB_DERIVED_F90_FADE_THRESHOLD", SimpleSlabContract.DerivedF90FadeThreshold);
            AssertDefine(source, "VIVID_SIMPLE_SLAB_DERIVED_F90_SCALE", SimpleSlabContract.DerivedF90Scale);
            AssertDefine(source, "VIVID_SIMPLE_SLAB_DIRECT_RESPONSE_INCLUDES_NDOTL", SimpleSlabContract.DirectResponseIncludesNdotL);
            AssertDefine(source, "VIVID_SIMPLE_SLAB_AO_AFFECTS_DIRECT_LIGHTING", SimpleSlabContract.AmbientOcclusionAffectsDirectLighting);
            AssertDefine(source, "VIVID_SIMPLE_SLAB_AO_AFFECTS_INDIRECT_LIGHTING", SimpleSlabContract.AmbientOcclusionAffectsIndirectLighting);
            AssertDefine(source, "VIVID_SIMPLE_SLAB_EMISSION_ADDED_AFTER_LIGHTING", SimpleSlabContract.EmissionIsAddedAfterLighting);
            StringAssert.Contains("struct VividSimpleSlabData", source);
            StringAssert.Contains("float3 diffuseAlbedo;", source);
            StringAssert.Contains("float3 specularF0;", source);
            StringAssert.Contains("float perceptualRoughness;", source);
            StringAssert.Contains("float3 normalWS;", source);
        }

        private static void AssertDefine(string source, string name, uint value)
        {
            StringAssert.Contains($"#define {name} {value}u", source);
        }

        private static void AssertDefine(
            string source,
            string name,
            uint value,
            bool hexadecimal)
        {
            string formatted = hexadecimal
                ? $"0x{value:X8}u"
                : $"{value}u";
            StringAssert.Contains($"#define {name} {formatted}", source);
        }

        private static void AssertDefine(string source, string name, bool value)
        {
            AssertDefine(source, name, value ? 1u : 0u);
        }

        private static void AssertDefine(string source, string name, float value)
        {
            string formatted = value.ToString("0.0########", CultureInfo.InvariantCulture);
            StringAssert.Contains($"#define {name} {formatted}f", source);
        }

        private static void AssertFloat3(float3 actual, float3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(1e-6f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(1e-6f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(1e-6f));
        }
    }
}
