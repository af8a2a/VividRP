using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Mathematics;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests
{
    public sealed class OpenPBROpaqueContractTests
    {
        [Test]
        public void CreateDefault_MatchesFrozenOpenPbrDefaults()
        {
            OpenPBROpaqueInputs inputs = OpenPBROpaqueContract.CreateDefault();

            Assert.That(inputs.BaseWeight, Is.EqualTo(1.0f));
            Assert.That(inputs.BaseColor, Is.EqualTo(new float3(0.8f)));
            Assert.That(inputs.BaseDiffuseRoughness, Is.Zero);
            Assert.That(inputs.BaseMetalness, Is.Zero);
            Assert.That(inputs.SpecularWeight, Is.EqualTo(1.0f));
            Assert.That(inputs.SpecularColor, Is.EqualTo(new float3(1.0f)));
            Assert.That(inputs.SpecularRoughness, Is.EqualTo(0.3f));
            Assert.That(inputs.SpecularIor, Is.EqualTo(1.5f));
            Assert.That(inputs.NormalWS, Is.EqualTo(new float3(0.0f, 0.0f, 1.0f)));
            Assert.That(inputs.EmissionLuminance, Is.Zero);
            Assert.That(inputs.EmissionColor, Is.EqualTo(new float3(1.0f)));
            Assert.That(Validate(inputs), Is.EqualTo(OpenPBROpaqueValidationErrors.None));
        }

        [Test]
        public void Contract_FreezesOpaqueIsotropicSingleClosureAndHostConventions()
        {
            Assert.That(OpenPBROpaqueContract.Version, Is.EqualTo(1u));
            Assert.That(OpenPBROpaqueContract.FingerprintVersion, Is.EqualTo(1u));
            Assert.That(OpenPBROpaqueContract.VendorSpecificationMajor, Is.EqualTo(1u));
            Assert.That(OpenPBROpaqueContract.VendorSpecificationMinor, Is.EqualTo(1u));
            Assert.That(OpenPBROpaqueContract.FieldCount, Is.EqualTo(11u));
            Assert.That(OpenPBROpaqueContract.ClosureCount, Is.EqualTo(1u));
            Assert.That(OpenPBROpaqueContract.MinimumUnitInput, Is.Zero);
            Assert.That(OpenPBROpaqueContract.MaximumUnitInput, Is.EqualTo(1.0f));
            Assert.That(OpenPBROpaqueContract.MinimumSpecularIor, Is.EqualTo(1.0f));
            Assert.That(OpenPBROpaqueContract.MaximumSpecularIor, Is.EqualTo(3.0f));
            Assert.That(OpenPBROpaqueContract.NormalLengthSquaredTolerance, Is.EqualTo(0.0001f));
            Assert.That(OpenPBROpaqueContract.VendorMinimumMicrofacetRoughness, Is.EqualTo(0.001f));
            Assert.That(OpenPBROpaqueContract.ExteriorIor, Is.EqualTo(1.0f));
            Assert.That(OpenPBROpaqueContract.GeometryOpacity, Is.EqualTo(1.0f));
            Assert.That(OpenPBROpaqueContract.PreparePathThroughput, Is.EqualTo(1.0f));
            Assert.That(OpenPBROpaqueContract.RgbWavelengthRed, Is.EqualTo(620.0f));
            Assert.That(OpenPBROpaqueContract.RgbWavelengthGreen, Is.EqualTo(540.0f));
            Assert.That(OpenPBROpaqueContract.RgbWavelengthBlue, Is.EqualTo(450.0f));
            Assert.That(OpenPBROpaqueContract.Isotropic, Is.True);
            Assert.That(OpenPBROpaqueContract.DirectResponseIncludesCosine, Is.True);
            Assert.That(OpenPBROpaqueContract.SampleWeightIncludesCosineOverPdf, Is.True);
            Assert.That(OpenPBROpaqueContract.CoverageIsExternal, Is.True);
            Assert.That(OpenPBROpaqueContract.AmbientOcclusionAffectsDirectLighting, Is.False);
            Assert.That(OpenPBROpaqueContract.AmbientOcclusionAffectsIndirectLighting, Is.True);
            Assert.That(OpenPBROpaqueContract.PreparedEmissionIsAddedOnce, Is.True);
            Assert.That(OpenPBROpaqueContract.AdditionalSimpleSlabCompensation, Is.False);
            Assert.That(OpenPBROpaqueContract.SupportsIdealDeltaSpecular, Is.False);
            Assert.That(OpenPBROpaqueContract.NormalizeAcceptedNormalForBasis, Is.True);
        }

        [Test]
        public void FieldSemanticsAndMasks_HaveFrozenValues()
        {
            var semantics = new[]
            {
                OpenPBROpaqueFieldSemantic.BaseWeight,
                OpenPBROpaqueFieldSemantic.BaseColor,
                OpenPBROpaqueFieldSemantic.BaseDiffuseRoughness,
                OpenPBROpaqueFieldSemantic.BaseMetalness,
                OpenPBROpaqueFieldSemantic.SpecularWeight,
                OpenPBROpaqueFieldSemantic.SpecularColor,
                OpenPBROpaqueFieldSemantic.SpecularRoughness,
                OpenPBROpaqueFieldSemantic.SpecularIor,
                OpenPBROpaqueFieldSemantic.NormalWS,
                OpenPBROpaqueFieldSemantic.EmissionLuminance,
                OpenPBROpaqueFieldSemantic.EmissionColor,
            };
            Assert.That(Enum.GetValues(typeof(OpenPBROpaqueFieldSemantic)).Length,
                Is.EqualTo(semantics.Length));
            for (int index = 0; index < semantics.Length; ++index)
                Assert.That((uint) semantics[index], Is.EqualTo((uint) index));

            var features = new[]
            {
                OpenPBROpaqueUnsupportedFeatures.Coat,
                OpenPBROpaqueUnsupportedFeatures.Fuzz,
                OpenPBROpaqueUnsupportedFeatures.Transmission,
                OpenPBROpaqueUnsupportedFeatures.Subsurface,
                OpenPBROpaqueUnsupportedFeatures.ThinFilm,
                OpenPBROpaqueUnsupportedFeatures.SpecularAnisotropy,
                OpenPBROpaqueUnsupportedFeatures.CoatAnisotropy,
                OpenPBROpaqueUnsupportedFeatures.Dispersion,
                OpenPBROpaqueUnsupportedFeatures.ThinWalled,
            };
            Assert.That(OpenPBROpaqueUnsupportedFeatures.None,
                Is.EqualTo((OpenPBROpaqueUnsupportedFeatures) 0u));
            Assert.That(Enum.GetValues(typeof(OpenPBROpaqueUnsupportedFeatures)).Length,
                Is.EqualTo(features.Length + 1));
            for (int index = 0; index < features.Length; ++index)
                Assert.That((uint) features[index], Is.EqualTo(1u << index));

            Assert.That((uint) OpenPBROpaqueValidationErrors.None, Is.Zero);
            Assert.That((uint) OpenPBROpaqueValidationErrors.UnsupportedFeatures, Is.EqualTo(1u));
            Assert.That((uint) OpenPBROpaqueValidationErrors.NonFinite, Is.EqualTo(2u));
            Assert.That((uint) OpenPBROpaqueValidationErrors.OutOfRange, Is.EqualTo(4u));
            Assert.That((uint) OpenPBROpaqueValidationErrors.InvalidNormal, Is.EqualTo(8u));
            Assert.That(Enum.GetValues(typeof(OpenPBROpaqueValidationErrors)).Length,
                Is.EqualTo(5));
        }

        [TestCase(1.0f)]
        [TestCase(3.0f)]
        public void Validate_AcceptsIorEndpoints(float ior)
        {
            OpenPBROpaqueInputs inputs = OpenPBROpaqueContract.CreateDefault();
            inputs.SpecularIor = ior;

            Assert.That(Validate(inputs), Is.EqualTo(OpenPBROpaqueValidationErrors.None));
            Assert.That(inputs.SpecularIor, Is.EqualTo(ior));
        }

        [TestCase(0.0f)]
        [TestCase(1.0f)]
        public void Validate_AcceptsUnitInputEndpointsWithoutChangingThem(float value)
        {
            OpenPBROpaqueInputs inputs = OpenPBROpaqueContract.CreateDefault();
            inputs.BaseWeight = value;
            inputs.BaseColor = new float3(value);
            inputs.BaseDiffuseRoughness = value;
            inputs.BaseMetalness = value;
            inputs.SpecularWeight = value;
            inputs.SpecularColor = new float3(value);
            inputs.SpecularRoughness = value;

            Assert.That(Validate(inputs), Is.EqualTo(OpenPBROpaqueValidationErrors.None));
            Assert.That(inputs.BaseWeight, Is.EqualTo(value));
            Assert.That(inputs.BaseColor, Is.EqualTo(new float3(value)));
            Assert.That(inputs.BaseDiffuseRoughness, Is.EqualTo(value));
            Assert.That(inputs.BaseMetalness, Is.EqualTo(value));
            Assert.That(inputs.SpecularWeight, Is.EqualTo(value));
            Assert.That(inputs.SpecularColor, Is.EqualTo(new float3(value)));
            Assert.That(inputs.SpecularRoughness, Is.EqualTo(value));
        }

        [Test]
        public void Validate_AllowsDiffuseAndSpecularRoughnessToVaryIndependently()
        {
            OpenPBROpaqueInputs inputs = OpenPBROpaqueContract.CreateDefault();
            inputs.BaseDiffuseRoughness = 1.0f;
            inputs.SpecularRoughness = 0.0f;
            Assert.That(Validate(inputs), Is.EqualTo(OpenPBROpaqueValidationErrors.None));

            inputs.BaseDiffuseRoughness = 0.0f;
            inputs.SpecularRoughness = 1.0f;
            Assert.That(Validate(inputs), Is.EqualTo(OpenPBROpaqueValidationErrors.None));
        }

        [Test]
        public void Validate_RejectsEveryNonFiniteFieldComponent()
        {
            var nonFiniteValues = new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity };
            for (uint semanticIndex = 0u; semanticIndex < OpenPBROpaqueContract.FieldCount; ++semanticIndex)
            {
                var semantic = (OpenPBROpaqueFieldSemantic) semanticIndex;
                for (int component = 0; component < GetComponentCount(semantic); ++component)
                {
                    foreach (float value in nonFiniteValues)
                    {
                        OpenPBROpaqueInputs inputs = OpenPBROpaqueContract.CreateDefault();
                        SetComponent(ref inputs, semantic, component, value);
                        OpenPBROpaqueValidationErrors errors = Validate(inputs);

                        Assert.That(errors & OpenPBROpaqueValidationErrors.NonFinite,
                            Is.EqualTo(OpenPBROpaqueValidationErrors.NonFinite),
                            $"{semantic}[{component}] = {value}");
                        if (semantic == OpenPBROpaqueFieldSemantic.NormalWS)
                        {
                            Assert.That(errors & OpenPBROpaqueValidationErrors.InvalidNormal,
                                Is.EqualTo(OpenPBROpaqueValidationErrors.InvalidNormal));
                        }
                    }
                }
            }
        }

        [Test]
        public void Validate_RejectsEveryUnitFieldComponentOutsideTheProfileRange()
        {
            var semantics = new[]
            {
                OpenPBROpaqueFieldSemantic.BaseWeight,
                OpenPBROpaqueFieldSemantic.BaseColor,
                OpenPBROpaqueFieldSemantic.BaseDiffuseRoughness,
                OpenPBROpaqueFieldSemantic.BaseMetalness,
                OpenPBROpaqueFieldSemantic.SpecularWeight,
                OpenPBROpaqueFieldSemantic.SpecularColor,
                OpenPBROpaqueFieldSemantic.SpecularRoughness,
            };
            var invalidValues = new[] { -0.001f, 1.001f };
            foreach (OpenPBROpaqueFieldSemantic semantic in semantics)
            {
                for (int component = 0; component < GetComponentCount(semantic); ++component)
                {
                    foreach (float value in invalidValues)
                    {
                        OpenPBROpaqueInputs inputs = OpenPBROpaqueContract.CreateDefault();
                        SetComponent(ref inputs, semantic, component, value);

                        Assert.That(Validate(inputs), Is.EqualTo(OpenPBROpaqueValidationErrors.OutOfRange),
                            $"{semantic}[{component}] = {value}");
                    }
                }
            }
        }

        [TestCase(0.999f)]
        [TestCase(3.001f)]
        public void Validate_RejectsOutOfRangeIor(float ior)
        {
            OpenPBROpaqueInputs inputs = OpenPBROpaqueContract.CreateDefault();
            inputs.SpecularIor = ior;

            Assert.That(Validate(inputs), Is.EqualTo(OpenPBROpaqueValidationErrors.OutOfRange));
        }

        [Test]
        public void Validate_RejectsEachExcludedFeatureAndUnknownMaskBits()
        {
            OpenPBROpaqueInputs inputs = OpenPBROpaqueContract.CreateDefault();
            for (int bit = 0; bit < 32; ++bit)
            {
                var features = (OpenPBROpaqueUnsupportedFeatures) (1u << bit);
                Assert.That(OpenPBROpaqueContract.Validate(inputs, features),
                    Is.EqualTo(OpenPBROpaqueValidationErrors.UnsupportedFeatures), $"Feature bit {bit}");
            }
            Assert.That(OpenPBROpaqueContract.Validate(inputs,
                    (OpenPBROpaqueUnsupportedFeatures) uint.MaxValue),
                Is.EqualTo(OpenPBROpaqueValidationErrors.UnsupportedFeatures));
        }

        [Test]
        public void Validate_RejectsInvalidNormalsWithoutNormalizingThem()
        {
            var invalidNormals = new[]
            {
                float3.zero,
                new float3(0.0f, 0.0f, 0.5f),
                new float3(0.0f, 0.0f, 2.0f),
                new float3(float.MaxValue, 0.0f, 0.0f),
            };
            foreach (float3 normal in invalidNormals)
            {
                OpenPBROpaqueInputs inputs = OpenPBROpaqueContract.CreateDefault();
                inputs.NormalWS = normal;
                Assert.That(Validate(inputs), Is.EqualTo(OpenPBROpaqueValidationErrors.InvalidNormal));
                Assert.That(inputs.NormalWS, Is.EqualTo(normal));
            }

            OpenPBROpaqueInputs valid = OpenPBROpaqueContract.CreateDefault();
            valid.NormalWS = new float3(0.6f, 0.8f, 0.0f);
            Assert.That(Validate(valid), Is.EqualTo(OpenPBROpaqueValidationErrors.None));
            valid.NormalWS = new float3(0.0f, 0.0f,
                math.sqrt(1.0f + OpenPBROpaqueContract.NormalLengthSquaredTolerance * 0.25f));
            Assert.That(Validate(valid), Is.EqualTo(OpenPBROpaqueValidationErrors.None));
            valid.NormalWS = new float3(0.0f, 0.0f,
                math.sqrt(1.0f + OpenPBROpaqueContract.NormalLengthSquaredTolerance * 2.0f));
            Assert.That(Validate(valid), Is.EqualTo(OpenPBROpaqueValidationErrors.InvalidNormal));
        }

        [Test]
        public void Validate_RejectsNegativeEmissionAndFiniteInputProductOverflow()
        {
            OpenPBROpaqueInputs inputs = OpenPBROpaqueContract.CreateDefault();
            inputs.EmissionLuminance = -0.001f;
            Assert.That(Validate(inputs), Is.EqualTo(OpenPBROpaqueValidationErrors.OutOfRange));

            for (int component = 0; component < 3; ++component)
            {
                inputs = OpenPBROpaqueContract.CreateDefault();
                inputs.EmissionColor[component] = -0.001f;
                Assert.That(Validate(inputs), Is.EqualTo(OpenPBROpaqueValidationErrors.OutOfRange));

                inputs = OpenPBROpaqueContract.CreateDefault();
                inputs.EmissionLuminance = float.MaxValue;
                inputs.EmissionColor[component] = 2.0f;
                Assert.That(Validate(inputs), Is.EqualTo(OpenPBROpaqueValidationErrors.NonFinite));
            }

            inputs = OpenPBROpaqueContract.CreateDefault();
            inputs.EmissionLuminance = float.MaxValue;
            Assert.That(Validate(inputs), Is.EqualTo(OpenPBROpaqueValidationErrors.None));
            inputs.EmissionLuminance = 2.0f;
            inputs.EmissionColor = new float3(12.0f, 2.0f, 0.0f);
            Assert.That(Validate(inputs), Is.EqualTo(OpenPBROpaqueValidationErrors.None));
        }

        [Test]
        public void Validate_ReportsIndependentFailuresTogether()
        {
            OpenPBROpaqueInputs inputs = OpenPBROpaqueContract.CreateDefault();
            inputs.BaseWeight = -1.0f;
            inputs.NormalWS = new float3(float.NaN, 0.0f, 0.0f);
            OpenPBROpaqueValidationErrors errors = OpenPBROpaqueContract.Validate(
                inputs, OpenPBROpaqueUnsupportedFeatures.Coat);

            Assert.That(errors, Is.EqualTo(OpenPBROpaqueValidationErrors.UnsupportedFeatures
                | OpenPBROpaqueValidationErrors.NonFinite
                | OpenPBROpaqueValidationErrors.OutOfRange
                | OpenPBROpaqueValidationErrors.InvalidNormal));
        }

        [Test]
        public void CreateDefaultAndValidate_AllocateZeroManagedBytesAfterWarmup()
        {
            OpenPBROpaqueValidationErrors errors = OpenPBROpaqueValidationErrors.None;
            float accumulatedBaseWeight = 0.0f;
            const int iterations = 4096;
            const string allocationMessage = "Stable default creation and validation must allocate zero managed bytes.";
            for (int iteration = 0; iteration < 1024; ++iteration)
            {
                OpenPBROpaqueInputs inputs = OpenPBROpaqueContract.CreateDefault();
                errors |= Validate(inputs);
                accumulatedBaseWeight += inputs.BaseWeight;
            }
            accumulatedBaseWeight = 0.0f;
            GC.GetAllocatedBytesForCurrentThread();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < iterations; ++iteration)
            {
                OpenPBROpaqueInputs inputs = OpenPBROpaqueContract.CreateDefault();
                errors |= Validate(inputs);
                accumulatedBaseWeight += inputs.BaseWeight;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero, allocationMessage);
            Assert.That(errors, Is.EqualTo(OpenPBROpaqueValidationErrors.None));
            Assert.That(accumulatedBaseWeight, Is.EqualTo((float) iterations));
        }

        [Test]
        public void Fingerprint_PinsTheFrozenInputAndCallContract()
        {
            Assert.That(OpenPBROpaqueContract.Fingerprint, Is.EqualTo(0x1602062CE683E774ul));
        }

        [Test]
        public void HlslDefines_AgreeWithTheCpuContract()
        {
            string source = ReadPackageSource("Shaders/Core/Public/VividOpenPBROpaqueContract.hlsl");
            AssertDefine(source, "CONTRACT_VERSION", OpenPBROpaqueContract.Version);
            AssertDefine(source, "FINGERPRINT_VERSION", OpenPBROpaqueContract.FingerprintVersion);
            AssertDefine(source, "FINGERPRINT_LO", (uint) OpenPBROpaqueContract.Fingerprint, true);
            AssertDefine(source, "FINGERPRINT_HI", (uint) (OpenPBROpaqueContract.Fingerprint >> 32), true);
            AssertDefine(source, "VENDOR_SPECIFICATION_MAJOR", OpenPBROpaqueContract.VendorSpecificationMajor);
            AssertDefine(source, "VENDOR_SPECIFICATION_MINOR", OpenPBROpaqueContract.VendorSpecificationMinor);
            AssertDefine(source, "FIELD_COUNT", OpenPBROpaqueContract.FieldCount);
            AssertDefine(source, "CLOSURE_COUNT", OpenPBROpaqueContract.ClosureCount);

            string[] fieldNames = { "BASE_WEIGHT", "BASE_COLOR", "BASE_DIFFUSE_ROUGHNESS", "BASE_METALNESS",
                "SPECULAR_WEIGHT", "SPECULAR_COLOR", "SPECULAR_ROUGHNESS", "SPECULAR_IOR", "NORMAL_WS",
                "EMISSION_LUMINANCE", "EMISSION_COLOR" };
            for (uint index = 0u; index < fieldNames.Length; ++index)
                AssertDefine(source, "FIELD_" + fieldNames[index], index);

            AssertDefine(source, "DEFAULT_BASE_WEIGHT", OpenPBROpaqueContract.DefaultBaseWeight);
            AssertDefine(source, "DEFAULT_BASE_COLOR", OpenPBROpaqueContract.DefaultBaseColor);
            AssertDefine(source, "DEFAULT_BASE_DIFFUSE_ROUGHNESS", OpenPBROpaqueContract.DefaultBaseDiffuseRoughness);
            AssertDefine(source, "DEFAULT_BASE_METALNESS", OpenPBROpaqueContract.DefaultBaseMetalness);
            AssertDefine(source, "DEFAULT_SPECULAR_WEIGHT", OpenPBROpaqueContract.DefaultSpecularWeight);
            AssertDefine(source, "DEFAULT_SPECULAR_COLOR", OpenPBROpaqueContract.DefaultSpecularColor);
            AssertDefine(source, "DEFAULT_SPECULAR_ROUGHNESS", OpenPBROpaqueContract.DefaultSpecularRoughness);
            AssertDefine(source, "DEFAULT_SPECULAR_IOR", OpenPBROpaqueContract.DefaultSpecularIor);
            AssertDefine(source, "DEFAULT_EMISSION_LUMINANCE", OpenPBROpaqueContract.DefaultEmissionLuminance);
            AssertDefine(source, "DEFAULT_EMISSION_COLOR", OpenPBROpaqueContract.DefaultEmissionColor);
            AssertDefine(source, "MINIMUM_UNIT_INPUT", OpenPBROpaqueContract.MinimumUnitInput);
            AssertDefine(source, "MAXIMUM_UNIT_INPUT", OpenPBROpaqueContract.MaximumUnitInput);
            AssertDefine(source, "MINIMUM_SPECULAR_IOR", OpenPBROpaqueContract.MinimumSpecularIor);
            AssertDefine(source, "MAXIMUM_SPECULAR_IOR", OpenPBROpaqueContract.MaximumSpecularIor);
            AssertDefine(source, "NORMAL_LENGTH_SQUARED_TOLERANCE", OpenPBROpaqueContract.NormalLengthSquaredTolerance);
            AssertDefine(source, "VENDOR_MINIMUM_MICROFACET_ROUGHNESS", OpenPBROpaqueContract.VendorMinimumMicrofacetRoughness);
            AssertDefine(source, "EXTERIOR_IOR", OpenPBROpaqueContract.ExteriorIor);
            AssertDefine(source, "GEOMETRY_OPACITY", OpenPBROpaqueContract.GeometryOpacity);
            AssertDefine(source, "PREPARE_PATH_THROUGHPUT", OpenPBROpaqueContract.PreparePathThroughput);
            AssertDefine(source, "RGB_WAVELENGTH_RED", OpenPBROpaqueContract.RgbWavelengthRed);
            AssertDefine(source, "RGB_WAVELENGTH_GREEN", OpenPBROpaqueContract.RgbWavelengthGreen);
            AssertDefine(source, "RGB_WAVELENGTH_BLUE", OpenPBROpaqueContract.RgbWavelengthBlue);

            AssertDefine(source, "ISOTROPIC", OpenPBROpaqueContract.Isotropic);
            AssertDefine(source, "DIRECT_RESPONSE_INCLUDES_COSINE", OpenPBROpaqueContract.DirectResponseIncludesCosine);
            AssertDefine(source, "SAMPLE_WEIGHT_INCLUDES_COSINE_OVER_PDF", OpenPBROpaqueContract.SampleWeightIncludesCosineOverPdf);
            AssertDefine(source, "COVERAGE_IS_EXTERNAL", OpenPBROpaqueContract.CoverageIsExternal);
            AssertDefine(source, "AO_AFFECTS_DIRECT_LIGHTING", OpenPBROpaqueContract.AmbientOcclusionAffectsDirectLighting);
            AssertDefine(source, "AO_AFFECTS_INDIRECT_LIGHTING", OpenPBROpaqueContract.AmbientOcclusionAffectsIndirectLighting);
            AssertDefine(source, "PREPARED_EMISSION_ADDED_ONCE", OpenPBROpaqueContract.PreparedEmissionIsAddedOnce);
            AssertDefine(source, "ADDITIONAL_SIMPLE_SLAB_COMPENSATION", OpenPBROpaqueContract.AdditionalSimpleSlabCompensation);
            AssertDefine(source, "SUPPORTS_IDEAL_DELTA_SPECULAR", OpenPBROpaqueContract.SupportsIdealDeltaSpecular);
            AssertDefine(source, "NORMALIZE_ACCEPTED_NORMAL_FOR_BASIS", OpenPBROpaqueContract.NormalizeAcceptedNormalForBasis);

            string[] featureNames = { "COAT", "FUZZ", "TRANSMISSION", "SUBSURFACE", "THIN_FILM",
                "SPECULAR_ANISOTROPY", "COAT_ANISOTROPY", "DISPERSION", "THIN_WALLED" };
            for (int bit = 0; bit < featureNames.Length; ++bit)
                AssertShiftDefine(source, "FEATURE_" + featureNames[bit], bit);
            string[] errorNames = { "UNSUPPORTED_FEATURES", "NON_FINITE", "OUT_OF_RANGE", "INVALID_NORMAL" };
            for (int bit = 0; bit < errorNames.Length; ++bit)
                AssertShiftDefine(source, "ERROR_" + errorNames[bit], bit);
        }

        [Test]
        public void VendorSource_PinsTheDefaultsAndNonDeltaRoughnessConvention()
        {
            const string vendorRoot = "Shaders/Material/ShaderPass/OpenPBR/Vendor/";
            string defaults = ReadPackageSource(vendorRoot + "openpbr_resolved_inputs.h");
            StringAssert.Contains("inputs.base_weight = 1.0f;", defaults);
            StringAssert.Contains("inputs.base_color = OPENPBR_MAKE_VEC3_SPLAT(0.8f);", defaults);
            StringAssert.Contains("inputs.base_diffuse_roughness = 0.0f;", defaults);
            StringAssert.Contains("inputs.base_metalness = 0.0f;", defaults);
            StringAssert.Contains("inputs.specular_weight = 1.0f;", defaults);
            StringAssert.Contains("inputs.specular_color = OPENPBR_MAKE_VEC3_SPLAT(1.0f);", defaults);
            StringAssert.Contains("inputs.specular_roughness = 0.3f;", defaults);
            StringAssert.Contains("inputs.specular_ior = 1.5f;", defaults);
            StringAssert.Contains("inputs.emission_luminance = 0.0f;", defaults);
            StringAssert.Contains("inputs.emission_color = OPENPBR_MAKE_VEC3_SPLAT(1.0f);", defaults);

            string bsdf = ReadPackageSource(vendorRoot + "impl/openpbr_bsdf.h");
            StringAssert.Contains("bool DeltaSpecularSupported = false;", bsdf);
            StringAssert.Contains("float MinMicrofacetRoughness = 0.001f;", bsdf);
            StringAssert.Contains("float MinMicrofacetAlpha = openpbr_square(MinMicrofacetRoughness);", bsdf);
            string constants = ReadPackageSource(vendorRoot + "openpbr_constants.h");
            StringAssert.Contains("OpenPBR_BaseRgbWavelengths_nm = vec3(620.0f, 540.0f, 450.0f);", constants);
        }

        [Test]
        public void ResolvedInputGate_ChecksAllVendorFieldsAndCanonicalDormantParameters()
        {
            string vendor = ReadPackageSource(
                "Shaders/Material/ShaderPass/OpenPBR/Vendor/openpbr_resolved_inputs.h");
            Match declaration = Regex.Match(vendor,
                @"\bstruct\s+OpenPBR_ResolvedInputs\s*\{(?<body>[^{}]*)\}\s*;");
            Assert.That(declaration.Success, Is.True, "Vendor resolved input declaration");
            MatchCollection fields = Regex.Matches(declaration.Groups["body"].Value,
                @"(?m)^\s*(?:float|vec2|vec3)\s+(?<field>\w+)\s*;");
            Assert.That(fields.Count, Is.GreaterThan(0), "Vendor numeric fields");

            string bridge = ReadPackageSource("Shaders/Core/Public/VividOpenPBROpaque.hlsl");
            string finiteBody = GetFunctionBody(bridge,
                "VividOpenPBROpaqueResolvedInputsAreFinite");
            string canonicalBody = GetFunctionBody(bridge,
                "VividOpenPBROpaqueResolvedInputsAreCanonical");
            var nativeFields = new[]
            {
                "base_weight", "base_color", "base_diffuse_roughness", "base_metalness",
                "specular_weight", "specular_color", "specular_roughness", "specular_ior",
                "emission_luminance", "emission_color",
            };
            foreach (Match field in fields)
            {
                string name = field.Groups["field"].Value;
                Assert.That(Regex.IsMatch(finiteBody,
                    @"\bisfinite\s*\(\s*resolved\." + Regex.Escape(name) + @"\s*\)"),
                    Is.True, "Resolved finite check: " + name);
                if (Array.IndexOf(nativeFields, name) >= 0)
                    continue;

                Assert.That(Regex.IsMatch(canonicalBody,
                    @"\bresolved\." + Regex.Escape(name) + @"\s*==\s*defaults\."
                        + Regex.Escape(name) + @"\b"),
                    Is.True, "Canonical dormant parameter: " + name);
            }
            foreach (string basis in new[] { "geometry_basis", "geometry_coat_basis" })
            {
                foreach (string axis in new[] { "n", "t", "b" })
                {
                    Assert.That(Regex.IsMatch(finiteBody,
                        @"\bisfinite\s*\(\s*resolved\." + Regex.Escape(basis)
                            + @"\." + axis + @"\s*\)"),
                        Is.True, "Resolved finite basis: " + basis + "." + axis);
                }
            }
        }

        private static string GetFunctionBody(string source, string name)
        {
            Match function = Regex.Match(source, @"\b" + Regex.Escape(name)
                + @"\s*\([^)]*\)\s*\{(?<body>(?:[^{}]|(?<depth>\{)|(?<-depth>\}))*)(?(depth)(?!))\}");
            Assert.That(function.Success, Is.True, "Bridge helper body: " + name);
            return Regex.Replace(function.Groups["body"].Value,
                @"(?s)/\*.*?\*/|//[^\r\n]*", string.Empty);
        }

        private static OpenPBROpaqueValidationErrors Validate(in OpenPBROpaqueInputs inputs)
        {
            return OpenPBROpaqueContract.Validate(inputs, OpenPBROpaqueUnsupportedFeatures.None);
        }

        private static int GetComponentCount(OpenPBROpaqueFieldSemantic semantic)
        {
            switch (semantic)
            {
                case OpenPBROpaqueFieldSemantic.BaseColor:
                case OpenPBROpaqueFieldSemantic.SpecularColor:
                case OpenPBROpaqueFieldSemantic.NormalWS:
                case OpenPBROpaqueFieldSemantic.EmissionColor:
                    return 3;
                default:
                    return 1;
            }
        }

        private static void SetComponent(
            ref OpenPBROpaqueInputs inputs,
            OpenPBROpaqueFieldSemantic semantic,
            int component,
            float value)
        {
            switch (semantic)
            {
                case OpenPBROpaqueFieldSemantic.BaseWeight: inputs.BaseWeight = value; break;
                case OpenPBROpaqueFieldSemantic.BaseColor: inputs.BaseColor[component] = value; break;
                case OpenPBROpaqueFieldSemantic.BaseDiffuseRoughness: inputs.BaseDiffuseRoughness = value; break;
                case OpenPBROpaqueFieldSemantic.BaseMetalness: inputs.BaseMetalness = value; break;
                case OpenPBROpaqueFieldSemantic.SpecularWeight: inputs.SpecularWeight = value; break;
                case OpenPBROpaqueFieldSemantic.SpecularColor: inputs.SpecularColor[component] = value; break;
                case OpenPBROpaqueFieldSemantic.SpecularRoughness: inputs.SpecularRoughness = value; break;
                case OpenPBROpaqueFieldSemantic.SpecularIor: inputs.SpecularIor = value; break;
                case OpenPBROpaqueFieldSemantic.NormalWS: inputs.NormalWS[component] = value; break;
                case OpenPBROpaqueFieldSemantic.EmissionLuminance: inputs.EmissionLuminance = value; break;
                case OpenPBROpaqueFieldSemantic.EmissionColor: inputs.EmissionColor[component] = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(semantic));
            }
        }

        private static string ReadPackageSource(string relativePath, [CallerFilePath] string sourcePath = "")
        {
            string packageRoot = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(sourcePath), "..", "..", "..", ".."));
            string path = Path.Combine(packageRoot, relativePath);
            Assert.That(File.Exists(path), Is.True, path);
            return File.ReadAllText(path);
        }

        private static void AssertDefine(string source, string suffix, uint expected, bool hexadecimal = false)
        {
            string literal = hexadecimal
                ? "0x" + expected.ToString("X8", CultureInfo.InvariantCulture) + "u"
                : expected.ToString(CultureInfo.InvariantCulture) + "u";
            AssertDefineLiteral(source, suffix, literal);
        }

        private static void AssertDefine(string source, string suffix, float expected)
        {
            string literal = expected.ToString("0.0#######", CultureInfo.InvariantCulture) + "f";
            AssertDefineLiteral(source, suffix, literal);
        }

        private static void AssertDefine(string source, string suffix, bool expected)
        {
            AssertDefine(source, suffix, expected ? 1u : 0u);
        }

        private static void AssertShiftDefine(string source, string suffix, int bit)
        {
            AssertDefineLiteral(source, suffix,
                "(1u << " + bit.ToString(CultureInfo.InvariantCulture) + ")");
        }

        private static void AssertDefineLiteral(string source, string suffix, string literal)
        {
            string name = "VIVID_OPENPBR_OPAQUE_" + suffix;
            Assert.That(Regex.IsMatch(source, "(?m)^\\s*#define\\s+" + Regex.Escape(name)
                + "\\s+" + Regex.Escape(literal) + "\\s*$"), Is.True, name);
        }
    }
}
