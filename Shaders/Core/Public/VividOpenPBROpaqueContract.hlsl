#ifndef VIVIDRP_OPENPBR_OPAQUE_CONTRACT_INCLUDED
#define VIVIDRP_OPENPBR_OPAQUE_CONTRACT_INCLUDED

// Semantic profile only; this struct is not a frozen GPU buffer or GBuffer ABI.
// Keep these constants in agreement with C# OpenPBROpaqueContract.
#define VIVID_OPENPBR_OPAQUE_CONTRACT_VERSION 1u
#define VIVID_OPENPBR_OPAQUE_FINGERPRINT_VERSION 1u
#define VIVID_OPENPBR_OPAQUE_FINGERPRINT_LO 0xE683E774u
#define VIVID_OPENPBR_OPAQUE_FINGERPRINT_HI 0x1602062Cu
#define VIVID_OPENPBR_OPAQUE_VENDOR_SPECIFICATION_MAJOR 1u
#define VIVID_OPENPBR_OPAQUE_VENDOR_SPECIFICATION_MINOR 1u
#define VIVID_OPENPBR_OPAQUE_FIELD_COUNT 11u
#define VIVID_OPENPBR_OPAQUE_CLOSURE_COUNT 1u

#define VIVID_OPENPBR_OPAQUE_FIELD_BASE_WEIGHT 0u
#define VIVID_OPENPBR_OPAQUE_FIELD_BASE_COLOR 1u
#define VIVID_OPENPBR_OPAQUE_FIELD_BASE_DIFFUSE_ROUGHNESS 2u
#define VIVID_OPENPBR_OPAQUE_FIELD_BASE_METALNESS 3u
#define VIVID_OPENPBR_OPAQUE_FIELD_SPECULAR_WEIGHT 4u
#define VIVID_OPENPBR_OPAQUE_FIELD_SPECULAR_COLOR 5u
#define VIVID_OPENPBR_OPAQUE_FIELD_SPECULAR_ROUGHNESS 6u
#define VIVID_OPENPBR_OPAQUE_FIELD_SPECULAR_IOR 7u
#define VIVID_OPENPBR_OPAQUE_FIELD_NORMAL_WS 8u
#define VIVID_OPENPBR_OPAQUE_FIELD_EMISSION_LUMINANCE 9u
#define VIVID_OPENPBR_OPAQUE_FIELD_EMISSION_COLOR 10u

#define VIVID_OPENPBR_OPAQUE_DEFAULT_BASE_WEIGHT 1.0f
#define VIVID_OPENPBR_OPAQUE_DEFAULT_BASE_COLOR 0.8f
#define VIVID_OPENPBR_OPAQUE_DEFAULT_BASE_DIFFUSE_ROUGHNESS 0.0f
#define VIVID_OPENPBR_OPAQUE_DEFAULT_BASE_METALNESS 0.0f
#define VIVID_OPENPBR_OPAQUE_DEFAULT_SPECULAR_WEIGHT 1.0f
#define VIVID_OPENPBR_OPAQUE_DEFAULT_SPECULAR_COLOR 1.0f
#define VIVID_OPENPBR_OPAQUE_DEFAULT_SPECULAR_ROUGHNESS 0.3f
#define VIVID_OPENPBR_OPAQUE_DEFAULT_SPECULAR_IOR 1.5f
#define VIVID_OPENPBR_OPAQUE_DEFAULT_EMISSION_LUMINANCE 0.0f
#define VIVID_OPENPBR_OPAQUE_DEFAULT_EMISSION_COLOR 1.0f
#define VIVID_OPENPBR_OPAQUE_MINIMUM_UNIT_INPUT 0.0f
#define VIVID_OPENPBR_OPAQUE_MAXIMUM_UNIT_INPUT 1.0f
#define VIVID_OPENPBR_OPAQUE_MINIMUM_SPECULAR_IOR 1.0f
#define VIVID_OPENPBR_OPAQUE_MAXIMUM_SPECULAR_IOR 3.0f
#define VIVID_OPENPBR_OPAQUE_NORMAL_LENGTH_SQUARED_TOLERANCE 0.0001f
#define VIVID_OPENPBR_OPAQUE_VENDOR_MINIMUM_MICROFACET_ROUGHNESS 0.001f
#define VIVID_OPENPBR_OPAQUE_EXTERIOR_IOR 1.0f
#define VIVID_OPENPBR_OPAQUE_GEOMETRY_OPACITY 1.0f
#define VIVID_OPENPBR_OPAQUE_PREPARE_PATH_THROUGHPUT 1.0f
#define VIVID_OPENPBR_OPAQUE_RGB_WAVELENGTH_RED 620.0f
#define VIVID_OPENPBR_OPAQUE_RGB_WAVELENGTH_GREEN 540.0f
#define VIVID_OPENPBR_OPAQUE_RGB_WAVELENGTH_BLUE 450.0f

#define VIVID_OPENPBR_OPAQUE_ISOTROPIC 1u
#define VIVID_OPENPBR_OPAQUE_DIRECT_RESPONSE_INCLUDES_COSINE 1u
#define VIVID_OPENPBR_OPAQUE_SAMPLE_WEIGHT_INCLUDES_COSINE_OVER_PDF 1u
#define VIVID_OPENPBR_OPAQUE_COVERAGE_IS_EXTERNAL 1u
#define VIVID_OPENPBR_OPAQUE_AO_AFFECTS_DIRECT_LIGHTING 0u
#define VIVID_OPENPBR_OPAQUE_AO_AFFECTS_INDIRECT_LIGHTING 1u
#define VIVID_OPENPBR_OPAQUE_PREPARED_EMISSION_ADDED_ONCE 1u
#define VIVID_OPENPBR_OPAQUE_ADDITIONAL_SIMPLE_SLAB_COMPENSATION 0u
#define VIVID_OPENPBR_OPAQUE_SUPPORTS_IDEAL_DELTA_SPECULAR 0u
#define VIVID_OPENPBR_OPAQUE_NORMALIZE_ACCEPTED_NORMAL_FOR_BASIS 1u

#define VIVID_OPENPBR_OPAQUE_FEATURE_COAT (1u << 0)
#define VIVID_OPENPBR_OPAQUE_FEATURE_FUZZ (1u << 1)
#define VIVID_OPENPBR_OPAQUE_FEATURE_TRANSMISSION (1u << 2)
#define VIVID_OPENPBR_OPAQUE_FEATURE_SUBSURFACE (1u << 3)
#define VIVID_OPENPBR_OPAQUE_FEATURE_THIN_FILM (1u << 4)
#define VIVID_OPENPBR_OPAQUE_FEATURE_SPECULAR_ANISOTROPY (1u << 5)
#define VIVID_OPENPBR_OPAQUE_FEATURE_COAT_ANISOTROPY (1u << 6)
#define VIVID_OPENPBR_OPAQUE_FEATURE_DISPERSION (1u << 7)
#define VIVID_OPENPBR_OPAQUE_FEATURE_THIN_WALLED (1u << 8)

#define VIVID_OPENPBR_OPAQUE_ERROR_UNSUPPORTED_FEATURES (1u << 0)
#define VIVID_OPENPBR_OPAQUE_ERROR_NON_FINITE (1u << 1)
#define VIVID_OPENPBR_OPAQUE_ERROR_OUT_OF_RANGE (1u << 2)
#define VIVID_OPENPBR_OPAQUE_ERROR_INVALID_NORMAL (1u << 3)

struct VividOpenPBROpaqueInputs
{
    float baseWeight;
    float3 baseColor;
    float baseDiffuseRoughness;
    float baseMetalness;
    float specularWeight;
    float3 specularColor;
    float specularRoughness;
    float specularIor;
    float3 normalWS;
    float emissionLuminance;
    float3 emissionColor;
};

VividOpenPBROpaqueInputs VividCreateDefaultOpenPBROpaqueInputs()
{
    VividOpenPBROpaqueInputs inputs;
    inputs.baseWeight = VIVID_OPENPBR_OPAQUE_DEFAULT_BASE_WEIGHT;
    inputs.baseColor = VIVID_OPENPBR_OPAQUE_DEFAULT_BASE_COLOR.xxx;
    inputs.baseDiffuseRoughness = VIVID_OPENPBR_OPAQUE_DEFAULT_BASE_DIFFUSE_ROUGHNESS;
    inputs.baseMetalness = VIVID_OPENPBR_OPAQUE_DEFAULT_BASE_METALNESS;
    inputs.specularWeight = VIVID_OPENPBR_OPAQUE_DEFAULT_SPECULAR_WEIGHT;
    inputs.specularColor = VIVID_OPENPBR_OPAQUE_DEFAULT_SPECULAR_COLOR.xxx;
    inputs.specularRoughness = VIVID_OPENPBR_OPAQUE_DEFAULT_SPECULAR_ROUGHNESS;
    inputs.specularIor = VIVID_OPENPBR_OPAQUE_DEFAULT_SPECULAR_IOR;
    inputs.normalWS = float3(0.0f, 0.0f, 1.0f);
    inputs.emissionLuminance = VIVID_OPENPBR_OPAQUE_DEFAULT_EMISSION_LUMINANCE;
    inputs.emissionColor = VIVID_OPENPBR_OPAQUE_DEFAULT_EMISSION_COLOR.xxx;
    return inputs;
}

bool VividOpenPBROpaqueIsUnitInput(float value)
{
    return value >= VIVID_OPENPBR_OPAQUE_MINIMUM_UNIT_INPUT
        && value <= VIVID_OPENPBR_OPAQUE_MAXIMUM_UNIT_INPUT;
}

bool VividOpenPBROpaqueIsUnitInput(float3 value)
{
    return all(value >= VIVID_OPENPBR_OPAQUE_MINIMUM_UNIT_INPUT)
        && all(value <= VIVID_OPENPBR_OPAQUE_MAXIMUM_UNIT_INPUT);
}

uint VividValidateOpenPBROpaqueInputs(
    VividOpenPBROpaqueInputs inputs, uint requestedFeatures)
{
    uint errors = requestedFeatures == 0u
        ? 0u : VIVID_OPENPBR_OPAQUE_ERROR_UNSUPPORTED_FEATURES;
    bool finite = isfinite(inputs.baseWeight)
        && all(isfinite(inputs.baseColor))
        && isfinite(inputs.baseDiffuseRoughness)
        && isfinite(inputs.baseMetalness)
        && isfinite(inputs.specularWeight)
        && all(isfinite(inputs.specularColor))
        && isfinite(inputs.specularRoughness)
        && isfinite(inputs.specularIor)
        && all(isfinite(inputs.normalWS))
        && isfinite(inputs.emissionLuminance)
        && all(isfinite(inputs.emissionColor))
        && all(isfinite(inputs.emissionColor * inputs.emissionLuminance));
    if (!finite)
        errors |= VIVID_OPENPBR_OPAQUE_ERROR_NON_FINITE;
    if (!VividOpenPBROpaqueIsUnitInput(inputs.baseWeight)
        || !VividOpenPBROpaqueIsUnitInput(inputs.baseColor)
        || !VividOpenPBROpaqueIsUnitInput(inputs.baseDiffuseRoughness)
        || !VividOpenPBROpaqueIsUnitInput(inputs.baseMetalness)
        || !VividOpenPBROpaqueIsUnitInput(inputs.specularWeight)
        || !VividOpenPBROpaqueIsUnitInput(inputs.specularColor)
        || !VividOpenPBROpaqueIsUnitInput(inputs.specularRoughness)
        || inputs.specularIor < VIVID_OPENPBR_OPAQUE_MINIMUM_SPECULAR_IOR
        || inputs.specularIor > VIVID_OPENPBR_OPAQUE_MAXIMUM_SPECULAR_IOR
        || inputs.emissionLuminance < 0.0f
        || any(inputs.emissionColor < 0.0f))
    {
        errors |= VIVID_OPENPBR_OPAQUE_ERROR_OUT_OF_RANGE;
    }
    float normalLengthSquared = dot(inputs.normalWS, inputs.normalWS);
    if (!isfinite(normalLengthSquared)
        || abs(normalLengthSquared - 1.0f)
            > VIVID_OPENPBR_OPAQUE_NORMAL_LENGTH_SQUARED_TOLERANCE)
    {
        errors |= VIVID_OPENPBR_OPAQUE_ERROR_INVALID_NORMAL;
    }
    return errors;
}

#endif
