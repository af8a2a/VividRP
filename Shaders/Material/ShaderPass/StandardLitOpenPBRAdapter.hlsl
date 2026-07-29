#ifndef VIVIDRP_STANDARD_LIT_OPENPBR_ADAPTER_INCLUDED
#define VIVIDRP_STANDARD_LIT_OPENPBR_ADAPTER_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/OpenPBR/OpenPBR.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingShadingNormal.hlsl"

struct VividReferencedPathtracingMaterial
{
    OpenPBR_ResolvedInputs openPbrInputs;
    float3 unadjustedShadingNormalWS;
    float3 shadingNormalWS;
    float3 emission;
    float effectiveTransmissionWeight;
    bool isSolidTransmissionBoundary;
};

float3 VividReferencedPathtracingConstrainShadingNormal(
    float3 shadingNormalWS,
    float3 faceNormalWS)
{
    float3 normalWS = SafeNormalize(shadingNormalWS);
    float3 geometricNormalWS = SafeNormalize(faceNormalWS);
    float normalCosine = dot(normalWS, geometricNormalWS);
    if (normalCosine <= 0.0001)
    {
        normalWS = SafeNormalize(normalWS + geometricNormalWS * (0.0001 - normalCosine));
    }

    return normalWS;
}

OpenPBR_Basis VividReferencedPathtracingBuildOpenPBRBasis(
    VividIndirectDiffuseHitGeometry geometry,
    float3 shadingNormalWS)
{
    float3 tangentWS = geometry.tangentWS - shadingNormalWS * dot(geometry.tangentWS, shadingNormalWS);
    float tangentLengthSquared = dot(tangentWS, tangentWS);
    if (tangentLengthSquared <= 0.00000001)
        return openpbr_make_basis(shadingNormalWS);

    tangentWS *= rsqrt(tangentLengthSquared);
    float handedness = geometry.tangentSign < 0.0 ? -1.0 : 1.0;
    return openpbr_make_basis(shadingNormalWS, tangentWS, handedness);
}

VividReferencedPathtracingMaterial VividReferencedPathtracingResolveStandardLitOpenPBR(
    VividIndirectDiffuseHitGeometry geometry,
    float textureBaseLambda,
    float baseTextureLod,
    float normalTextureLod,
    float3 viewDirectionWS)
{
    VividReferencedPathtracingMaterial material;

    float4 baseSample = SampleBase(geometry.uv, baseTextureLod);
    float materialTextureLod = baseTextureLod;
#if defined(_METALLICSPECGLOSSMAP)
    materialTextureLod = max(computeTargetTextureLOD(_MetallicGlossMap, textureBaseLambda), 0.0);
#elif defined(_ROUGHNESSMAP)
    materialTextureLod = max(computeTargetTextureLOD(_RoughnessMap, textureBaseLambda), 0.0);
#endif
    float2 metallicSmoothness = SampleMetallicSmoothness(
        geometry.uv,
        baseSample.a,
        materialTextureLod);

    float emissionTextureLod = baseTextureLod;
#if defined(_EMISSION)
    emissionTextureLod = max(computeTargetTextureLOD(_EmissionMap, textureBaseLambda), 0.0);
#endif
    material.emission = SampleEmission(geometry.uv, emissionTextureLod);
    material.unadjustedShadingNormalWS =
        VividReferencedPathtracingConstrainShadingNormal(
        VividIndirectDiffuseSampleNormalWS(geometry, normalTextureLod),
        geometry.faceNormalWS);
    material.shadingNormalWS =
        ReferencedPathtracingComputeConsistentShadingNormal(
            viewDirectionWS,
            geometry.faceNormalWS,
            material.unadjustedShadingNormalWS);

    OpenPBR_ResolvedInputs inputs = openpbr_make_default_resolved_inputs();
    inputs.base_color = saturate(baseSample.rgb);
    inputs.base_metalness = saturate(metallicSmoothness.x);
    inputs.base_diffuse_roughness = saturate(1.0 - metallicSmoothness.y);
    inputs.specular_roughness = max(1.0 - metallicSmoothness.y, 0.001);
    inputs.geometry_opacity = SampleOpenPbrGeometryOpacity(
        geometry.uv,
        baseTextureLod);
    inputs.geometry_thin_walled = _ThinWalledTransmission > 0.5;
    float specularIor = _SpecularIOR;
    if (isnan(specularIor) || isinf(specularIor))
        specularIor = 1.5;
    inputs.specular_ior = clamp(specularIor, 1.0, 3.0);

#if defined(_CLEARCOAT)
    inputs.coat_weight = saturate(_ClearCoatMask);
    inputs.coat_roughness = max(1.0 - saturate(_ClearCoatSmoothness), 0.001);
#else
    inputs.coat_weight = 0.0;
#endif

    inputs.emission_luminance = any(material.emission > 0.0) ? 1.0 : 0.0;
    inputs.emission_color = material.emission;
    inputs.subsurface_weight = 0.0;
    float transmissionTextureLod = baseTextureLod;
#if defined(_TRANSMISSIONMAP)
    transmissionTextureLod = max(
        computeTargetTextureLOD(_TransmissionMap, textureBaseLambda),
        0.0);
#endif
    inputs.transmission_weight = SampleOpenPbrTransmissionWeight(
        geometry.uv,
        transmissionTextureLod);
    inputs.transmission_color = saturate(_TransmissionColor.rgb);
    float transmissionDepth = _TransmissionDepth;
    if (isnan(transmissionDepth) || isinf(transmissionDepth))
        transmissionDepth = 0.0;
    inputs.transmission_depth = max(transmissionDepth, 0.0);
    inputs.fuzz_weight = 0.0;
    inputs.thin_film_weight = 0.0;

    material.effectiveTransmissionWeight =
        inputs.transmission_weight
        * (1.0 - inputs.base_metalness);
    material.isSolidTransmissionBoundary =
        !inputs.geometry_thin_walled
        && material.effectiveTransmissionWeight > 0.0;
    float3 openPbrShadingNormalWS =
        material.isSolidTransmissionBoundary
            && !geometry.isFrontFace
        ? -material.shadingNormalWS
        : material.shadingNormalWS;
    inputs.geometry_basis = VividReferencedPathtracingBuildOpenPBRBasis(
        geometry,
        openPbrShadingNormalWS);
    inputs.geometry_coat_basis = inputs.geometry_basis;
    material.openPbrInputs = inputs;
    return material;
}

#endif
