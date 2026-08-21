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
    float effectiveSubsurfaceWeight;
    float effectiveSubsurfaceTransmissionWeight;
    float3 subsurfaceAlbedo;
    float3 subsurfaceRadius;
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
#if defined(VIVIDRP_REFERENCED_PATH_TRACING_USE_RTXTF)
    inout STF_SamplerState rtxtfSamplerState,
#endif
    VividIndirectDiffuseHitGeometry geometry,
    float textureBaseLambda,
    float baseTextureLod,
    float normalTextureLod,
    float3 viewDirectionWS)
{
    VividReferencedPathtracingMaterial material;

    float4 baseSample;
#if defined(VIVIDRP_REFERENCED_PATH_TRACING_USE_RTXTF)
    baseSample = ReferencedPathtracingSampleBaseRTXTF(
        rtxtfSamplerState,
        geometry.uv,
        baseTextureLod);
#else
    baseSample = SampleBase(geometry.uv, baseTextureLod);
#endif
    float materialTextureLod = baseTextureLod;
#if defined(_RMOMAP)
    materialTextureLod = max(computeTargetTextureLOD(_RMOMap, textureBaseLambda), 0.0);
#elif defined(_METALLICSPECGLOSSMAP)
    materialTextureLod = max(computeTargetTextureLOD(_MetallicGlossMap, textureBaseLambda), 0.0);
#elif defined(_ROUGHNESSMAP)
    materialTextureLod = max(computeTargetTextureLOD(_RoughnessMap, textureBaseLambda), 0.0);
#endif
    float2 metallicSmoothness;
#if defined(VIVIDRP_REFERENCED_PATH_TRACING_USE_RTXTF)
    metallicSmoothness =
        ReferencedPathtracingSampleMetallicSmoothnessRTXTF(
        rtxtfSamplerState,
        geometry.uv,
        baseSample.a,
        materialTextureLod);
#else
    metallicSmoothness = SampleMetallicSmoothness(
        geometry.uv,
        baseSample.a,
        materialTextureLod);
#endif

    float emissionTextureLod = baseTextureLod;
#if defined(_EMISSION)
    emissionTextureLod = max(computeTargetTextureLOD(_EmissionMap, textureBaseLambda), 0.0);
#endif
    float3 sampledEmission;
#if defined(VIVIDRP_REFERENCED_PATH_TRACING_USE_RTXTF)
    sampledEmission = ReferencedPathtracingSampleEmissionRTXTF(
        rtxtfSamplerState,
        geometry.uv,
        emissionTextureLod);
#else
    sampledEmission = SampleEmission(geometry.uv, emissionTextureLod);
#endif
    material.emission = sampledEmission;
    float3 unadjustedShadingNormalWS;
#if defined(VIVIDRP_REFERENCED_PATH_TRACING_USE_RTXTF)
    unadjustedShadingNormalWS = ReferencedPathtracingSampleNormalWSRTXTF(
        rtxtfSamplerState,
        geometry,
        normalTextureLod);
#else
    unadjustedShadingNormalWS = VividIndirectDiffuseSampleNormalWS(
        geometry,
        normalTextureLod);
#endif
    material.unadjustedShadingNormalWS =
        VividReferencedPathtracingConstrainShadingNormal(
            unadjustedShadingNormalWS,
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
    float geometryOpacity;
#if defined(VIVIDRP_REFERENCED_PATH_TRACING_USE_RTXTF)
    // Reuse the same stochastic base/opacity texel used by base color and
    // smoothness so one material evaluation does not decorrelate coverage.
    geometryOpacity = saturate(baseSample.a);
#else
    geometryOpacity = SampleOpenPbrGeometryOpacity(
        geometry.uv,
        baseTextureLod);
#endif
    inputs.geometry_opacity = geometryOpacity;
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
    float subsurfaceWeight = _SubsurfaceWeight;
    if (isnan(subsurfaceWeight) || isinf(subsurfaceWeight))
        subsurfaceWeight = 0.0;
    inputs.subsurface_weight = saturate(subsurfaceWeight);
    float3 subsurfaceColor = _SubsurfaceColor.rgb;
    if (any(isnan(subsurfaceColor)) || any(isinf(subsurfaceColor)))
        subsurfaceColor = 1.0;
    inputs.subsurface_color = saturate(
        baseSample.rgb * max(subsurfaceColor, 0.0));
    float subsurfaceRadius = _SubsurfaceRadius;
    if (isnan(subsurfaceRadius) || isinf(subsurfaceRadius))
        subsurfaceRadius = 0.0;
    inputs.subsurface_radius = max(subsurfaceRadius, 0.0);
    float3 subsurfaceRadiusScale = _SubsurfaceRadiusScale.rgb;
    if (any(isnan(subsurfaceRadiusScale))
        || any(isinf(subsurfaceRadiusScale)))
    {
        subsurfaceRadiusScale = float3(1.0, 0.5, 0.25);
    }
    inputs.subsurface_radius_scale = max(
        subsurfaceRadiusScale,
        0.0001);
    float subsurfaceScatterAnisotropy =
        _SubsurfaceScatterAnisotropy;
    if (isnan(subsurfaceScatterAnisotropy)
        || isinf(subsurfaceScatterAnisotropy))
    {
        subsurfaceScatterAnisotropy = 0.0;
    }
    inputs.subsurface_scatter_anisotropy = clamp(
        subsurfaceScatterAnisotropy,
        -0.95,
        0.95);
    float transmissionTextureLod = baseTextureLod;
#if defined(_TRANSMISSIONMAP)
    transmissionTextureLod = max(
        computeTargetTextureLOD(_TransmissionMap, textureBaseLambda),
        0.0);
#endif
    float transmissionWeight;
#if defined(VIVIDRP_REFERENCED_PATH_TRACING_USE_RTXTF)
    transmissionWeight =
        ReferencedPathtracingSampleTransmissionWeightRTXTF(
        rtxtfSamplerState,
        geometry.uv,
        transmissionTextureLod);
#else
    transmissionWeight = SampleOpenPbrTransmissionWeight(
        geometry.uv,
        transmissionTextureLod);
#endif
    inputs.transmission_weight = transmissionWeight;
    inputs.transmission_color = saturate(_TransmissionColor.rgb);
    float transmissionDepth = _TransmissionDepth;
    if (isnan(transmissionDepth) || isinf(transmissionDepth))
        transmissionDepth = 0.0;
    inputs.transmission_depth = max(transmissionDepth, 0.0);
    float3 transmissionScatter = _TransmissionScatter.rgb;
    if (any(isnan(transmissionScatter))
        || any(isinf(transmissionScatter)))
    {
        transmissionScatter = 0.0;
    }
    inputs.transmission_scatter = max(transmissionScatter, 0.0);
    float transmissionScatterAnisotropy =
        _TransmissionScatterAnisotropy;
    if (isnan(transmissionScatterAnisotropy)
        || isinf(transmissionScatterAnisotropy))
    {
        transmissionScatterAnisotropy = 0.0;
    }
    inputs.transmission_scatter_anisotropy =
        clamp(transmissionScatterAnisotropy, -0.95, 0.95);
    inputs.fuzz_weight = 0.0;
    inputs.thin_film_weight = 0.0;

    material.effectiveTransmissionWeight =
        inputs.transmission_weight
        * (1.0 - inputs.base_metalness);
    // Face SSS is an opaque dielectric hybrid. OpenPBR surface refraction stays
    // mutually exclusive; V1.1 adds a separate measured-thickness ear term.
    bool surfaceIsOpaque = true;
#if defined(_SURFACE_TYPE_TRANSPARENT)
    surfaceIsOpaque = false;
#endif
    bool supportsHybridSubsurface =
        surfaceIsOpaque
        && !inputs.geometry_thin_walled
        && inputs.transmission_weight <= 0.0001
        && inputs.base_metalness <= 0.0001;
    material.effectiveSubsurfaceWeight = supportsHybridSubsurface
        ? (1.0 - inputs.transmission_weight)
            * inputs.subsurface_weight
            * (1.0 - inputs.base_metalness)
        : 0.0;
    float subsurfaceTransmissionWeight = _SubsurfaceTransmissionWeight;
    if (isnan(subsurfaceTransmissionWeight)
        || isinf(subsurfaceTransmissionWeight))
    {
        subsurfaceTransmissionWeight = 0.0;
    }
    material.effectiveSubsurfaceTransmissionWeight =
        supportsHybridSubsurface
            ? material.effectiveSubsurfaceWeight
                * saturate(subsurfaceTransmissionWeight)
            : 0.0;
    material.subsurfaceAlbedo = inputs.subsurface_color;
    material.subsurfaceRadius = max(
        inputs.subsurface_radius * inputs.subsurface_radius_scale,
        0.000001);
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
