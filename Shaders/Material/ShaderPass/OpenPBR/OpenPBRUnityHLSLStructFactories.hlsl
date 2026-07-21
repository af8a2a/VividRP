#ifndef VIVIDRP_OPENPBR_UNITY_HLSL_STRUCT_FACTORIES_INCLUDED
#define VIVIDRP_OPENPBR_UNITY_HLSL_STRUCT_FACTORIES_INCLUDED

OpenPBR_Basis VividOpenPBRMakeOpenPBR_Basis(vec3 tangent, vec3 bitangent, vec3 normal)
{
    OpenPBR_Basis result;
    result.t = tangent;
    result.b = bitangent;
    result.n = normal;
    return result;
}

OpenPBR_AllCoefficients VividOpenPBRMakeOpenPBR_AllCoefficients(vec3 reflection, vec3 transmission)
{
    OpenPBR_AllCoefficients result;
    result.reflection_coefficient = reflection;
    result.transmission_coefficient = transmission;
    return result;
}

OpenPBR_AllCoefficientsAndProbabilities VividOpenPBRMakeOpenPBR_AllCoefficientsAndProbabilities(
    vec3 reflection,
    vec3 transmission,
    float reflectionProbability,
    float transmissionProbability)
{
    OpenPBR_AllCoefficientsAndProbabilities result;
    result.reflection_coefficient = reflection;
    result.transmission_coefficient = transmission;
    result.reflection_probability = reflectionProbability;
    result.transmission_probability = transmissionProbability;
    return result;
}

OpenPBR_ConstantReflectionCoefficient VividOpenPBRMakeOpenPBR_ConstantReflectionCoefficient(vec3 color)
{
    OpenPBR_ConstantReflectionCoefficient result;
    result.color = color;
    return result;
}

OpenPBR_IorReflectionCoefficient VividOpenPBRMakeOpenPBR_IorReflectionCoefficient(float relativeIor)
{
    OpenPBR_IorReflectionCoefficient result;
    result.eta_t_over_eta_i = relativeIor;
    return result;
}

OpenPBR_AnisotropicGGXSmithVNDFMicrofacetDistribution
VividOpenPBRMakeOpenPBR_AnisotropicGGXSmithVNDFMicrofacetDistribution(
    vec2 alpha,
    OpenPBR_Basis basis,
    float isotropicAlpha)
{
    OpenPBR_AnisotropicGGXSmithVNDFMicrofacetDistribution result;
    result.alpha = alpha;
    result.basis_ff = basis;
    result.isotropic_alpha = isotropicAlpha;
    return result;
}

OpenPBR_ComprehensiveReflectionTransmissionCoefficient
VividOpenPBRMakeOpenPBR_ComprehensiveReflectionTransmissionCoefficient(
    vec3 etaTransparent,
    vec3 etaOpaque,
    vec3 transparentReflectionScale,
    vec3 opaqueReflectionScale,
    vec3 transmission,
    vec3 metalF0,
    vec3 metalF82Tint,
    float metalAmount,
    float thinFilmWeight,
    float thinFilmThicknessNm,
    float thinFilmExteriorIor,
    float thinFilmIor,
    vec3 thinFilmInteriorIor,
    vec3 rgbWavelengthsNm,
    vec3 thinWallReflectionAlbedo)
{
    OpenPBR_ComprehensiveReflectionTransmissionCoefficient result;
    result.eta_t_over_eta_i_for_transparent_part = etaTransparent;
    result.eta_t_over_eta_i_for_opaque_part = etaOpaque;
    result.scale_for_reflection_for_transparent_part = transparentReflectionScale;
    result.scale_for_reflection_for_opaque_part = opaqueReflectionScale;
    result.transmission = transmission;
    result.f0_for_metal = metalF0;
    result.f82_tint_for_metal = metalF82Tint;
    result.metal_amount = metalAmount;
    result.thin_film_weight = thinFilmWeight;
    result.thin_film_thickness_nm = thinFilmThicknessNm;
    result.thin_film_exterior_ior = thinFilmExteriorIor;
    result.thin_film_ior = thinFilmIor;
    result.thin_film_interior_ior = thinFilmInteriorIor;
    result.rgb_wavelengths_nm = rgbWavelengthsNm;
    result.thin_wall_constant_reflection_albedo = thinWallReflectionAlbedo;
    return result;
}

OpenPBR_MinimalMicrofacetReflectionLobe_AnisotropicGGXSmithVNDFMicrofacetDistribution_IorReflectionCoefficient
VividOpenPBRMakeOpenPBR_MinimalMicrofacetReflectionLobe_AnisotropicGGXSmithVNDFMicrofacetDistribution_IorReflectionCoefficient(
    vec3 normal,
    OpenPBR_AnisotropicGGXSmithVNDFMicrofacetDistribution distribution,
    OpenPBR_IorReflectionCoefficient coefficient)
{
    OpenPBR_MinimalMicrofacetReflectionLobe_AnisotropicGGXSmithVNDFMicrofacetDistribution_IorReflectionCoefficient result;
    result.normal_ff = normal;
    result.microfacet_distr = distribution;
    result.refl_trans_coeff = coefficient;
    return result;
}

OpenPBR_MinimalMicrofacetReflectionLobe_AnisotropicGGXSmithVNDFMicrofacetDistribution_ConstantReflectionCoefficient
VividOpenPBRMakeOpenPBR_MinimalMicrofacetReflectionLobe_AnisotropicGGXSmithVNDFMicrofacetDistribution_ConstantReflectionCoefficient(
    vec3 normal,
    OpenPBR_AnisotropicGGXSmithVNDFMicrofacetDistribution distribution,
    OpenPBR_ConstantReflectionCoefficient coefficient)
{
    OpenPBR_MinimalMicrofacetReflectionLobe_AnisotropicGGXSmithVNDFMicrofacetDistribution_ConstantReflectionCoefficient result;
    result.normal_ff = normal;
    result.microfacet_distr = distribution;
    result.refl_trans_coeff = coefficient;
    return result;
}

OpenPBR_ComprehensiveMicrofacetReflectionTransmissionLobe
VividOpenPBRMakeOpenPBR_ComprehensiveMicrofacetReflectionTransmissionLobe(
    vec3 normal,
    OpenPBR_AnisotropicGGXSmithVNDFMicrofacetDistribution distribution,
    OpenPBR_ComprehensiveReflectionTransmissionCoefficient coefficient,
    vec3 relativeIor,
    vec3 pathThroughput)
{
    OpenPBR_ComprehensiveMicrofacetReflectionTransmissionLobe result;
    result.normal_ff = normal;
    result.microfacet_distr = distribution;
    result.refl_trans_coeff = coefficient;
    result.eta_t_over_eta_i = relativeIor;
    result.path_throughput = pathThroughput;
    return result;
}

#endif
