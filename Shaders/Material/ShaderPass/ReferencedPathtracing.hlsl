#ifndef VIVIDRP_REFERENCED_PATH_TRACING_MATERIAL_PASS_INCLUDED
#define VIVIDRP_REFERENCED_PATH_TRACING_MATERIAL_PASS_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingCommon.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingLightList.hlsl"

#define VIVIDRP_INDIRECT_DIFFUSE_DEFINE_RAYTRACING_SHADERS 0
#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/IndirectDiffuse.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/StandardLitOpenPBRAdapter.hlsl"

static const float kReferencedPathtracingTextureLodBias = 0.5;

float3 ReferencedPathtracingTransformPositionToWorld(float3 positionOS)
{
    return mul(ObjectToWorld3x4(), float4(positionOS, 1.0));
}

float ComputeReferencedPathtracingTextureBaseLambda(
    VividIndirectDiffuseHitGeometry geometry,
    float rayConeWidth,
    float rayConeSpreadAngle,
    out float hitConeWidth)
{
    uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());

    float3 position0WS = ReferencedPathtracingTransformPositionToWorld(
        UnityRayTracingFetchVertexAttribute3(triangleIndices.x, kVertexAttributePosition));
    float3 position1WS = ReferencedPathtracingTransformPositionToWorld(
        UnityRayTracingFetchVertexAttribute3(triangleIndices.y, kVertexAttributePosition));
    float3 position2WS = ReferencedPathtracingTransformPositionToWorld(
        UnityRayTracingFetchVertexAttribute3(triangleIndices.z, kVertexAttributePosition));
    float triangleAreaWS = length(cross(position1WS - position0WS, position2WS - position0WS));

    float2 uv0 = UnityRayTracingFetchVertexAttribute4(triangleIndices.x, kVertexAttributeTexCoord0).xy
        * _BaseMap_ST.xy;
    float2 uv1 = UnityRayTracingFetchVertexAttribute4(triangleIndices.y, kVertexAttributeTexCoord0).xy
        * _BaseMap_ST.xy;
    float2 uv2 = UnityRayTracingFetchVertexAttribute4(triangleIndices.z, kVertexAttributeTexCoord0).xy
        * _BaseMap_ST.xy;
    float2 uvEdge1 = uv1 - uv0;
    float2 uvEdge2 = uv2 - uv0;
    float triangleAreaUV = abs(uvEdge1.x * uvEdge2.y - uvEdge1.y * uvEdge2.x);

    hitConeWidth = max(rayConeWidth + geometry.hitDistance * rayConeSpreadAngle, 0.000001);
    return computeBaseTextureLOD(
        WorldRayDirection(),
        geometry.faceNormalWS,
        hitConeWidth,
        max(triangleAreaUV, 0.000000000001),
        max(triangleAreaWS, 0.000000000001));
}

bool VividReferencedPathtracingIsFinite(float value)
{
    return !isnan(value) && !isinf(value);
}

bool VividReferencedPathtracingIsFinite(float3 value)
{
    return !any(isnan(value)) && !any(isinf(value));
}

bool VividReferencedPathtracingIsFinite(float2 value)
{
    return !any(isnan(value)) && !any(isinf(value));
}

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingNEECandidate.hlsl"

[shader("closesthit")]
void StandardLitReferencedPathtracingClosestHit(
    inout ReferencedPathtracingPayload payload : SV_RayPayload,
    AttributeData attributeData : SV_IntersectionAttributes)
{
    VividIndirectDiffuseHitGeometry geometry = VividIndirectDiffuseBuildHitGeometry(attributeData);
    float hitConeWidth;
    float textureBaseLambda = ComputeReferencedPathtracingTextureBaseLambda(
        geometry,
        payload.rayConeWidth,
        payload.rayConeSpreadAngle,
        hitConeWidth);
    float baseTextureLod = max(
        computeTargetTextureLOD(_BaseMap, textureBaseLambda) + kReferencedPathtracingTextureLodBias,
        0.0);
    float normalTextureLod = 0.0;
#if defined(_NORMALMAP)
    normalTextureLod = max(
        computeTargetTextureLOD(_BumpMap, textureBaseLambda) + kReferencedPathtracingTextureLodBias,
        0.0);
#endif
    float3 viewDirectionWS = normalize(-WorldRayDirection());
    VividReferencedPathtracingMaterial material =
        VividReferencedPathtracingResolveStandardLitOpenPBR(
        geometry,
        textureBaseLambda,
        baseTextureLod,
        normalTextureLod,
        viewDirectionWS);

    float exteriorIor = payload.activeMediumIor;
    if (material.isSolidTransmissionBoundary
        && !geometry.isFrontFace
        && payload.activeMediumInstanceIndex == InstanceIndex())
    {
        exteriorIor = payload.parentMediumIor;
    }
    if (!VividReferencedPathtracingIsFinite(exteriorIor))
        exteriorIor = OpenPBR_VacuumIor;
    exteriorIor = clamp(exteriorIor, 1.0, 3.0);
    float3 segmentMediumTransmittance =
        ReferencedPathtracingEvaluateMaterialMediumTransmittance(
            payload.activeMediumExtinction,
            geometry.hitDistance);
    OpenPBR_PreparedBsdf preparedBsdf = openpbr_prepare(
        material.openPbrInputs,
        max(
            payload.pathThroughput * segmentMediumTransmittance,
            0.0),
        OpenPBR_BaseRgbWavelengths_nm,
        exteriorIor,
        viewDirectionWS);

    payload.positionWS = geometry.positionWS;
    payload.faceNormalWS = geometry.faceNormalWS;
    payload.rayConeWidth = hitConeWidth;
    payload.emission = VividReferencedPathtracingIsFinite(preparedBsdf.emission)
        ? max(preparedBsdf.emission, 0.0)
        : 0.0;
    payload.neeDiffuseRadiance = 0.0;
    payload.neeSpecularRadiance = 0.0;
    payload.neeDirectionWS = 0.0;
    payload.neeDistance = 0.0;
    payload.neeSelectionPdf = 0.0;
    payload.neeSolidAnglePdf = 0.0;
    payload.neeBsdfPdf = 0.0;
    payload.neeShadowStrength = 0.0;
    payload.neeLightIndex = 0xffffffffu;
    payload.neeLightType = REFERENCED_LIGHT_TYPE_INVALID;
    payload.neeFlags = 0u;
    payload.neeValid = 0u;
    payload.nextDirectionWS = 0.0;
    payload.nextThroughputWeight = 0.0;
    payload.nextPdf = 0.0;
    payload.linearRoughness = material.openPbrInputs.specular_roughness;
    payload.hitDistance = geometry.hitDistance;
    // Match HDRP's reference-denoising AOV contract: diffuse reflectance and
    // the actual shading normal, both evaluated at primary visibility.
    payload.denoisingAlbedo = saturate(
        material.openPbrInputs.base_color
        * (1.0 - material.openPbrInputs.base_metalness)
        * (1.0 - material.openPbrInputs.transmission_weight));
    payload.denoisingNormalWS = material.shadingNormalWS;
    payload.nextLobeClass = 0u;
    payload.nextLobeIsDelta = 0u;
    payload.nextLobeIsTransmission = 0u;
    payload.thinWalledTransmissionWeight =
        material.openPbrInputs.geometry_thin_walled
            ? saturate(material.effectiveTransmissionWeight)
            : 0.0;
    bool allowsThinWalledTransmission =
        payload.thinWalledTransmissionWeight > 0.0;
    bool allowsSurfaceTransmission =
        material.effectiveTransmissionWeight > 0.0;
    payload.shadingNormalDiagnostics = float3(
        saturate(dot(
            material.unadjustedShadingNormalWS,
            geometry.faceNormalWS)),
        saturate(dot(
            material.shadingNormalWS,
            geometry.faceNormalWS)),
        1.0);

    ReferencedPathtracingNEECandidate neeCandidate;
    bool validNeeCandidate =
        ReferencedPathtracingSampleUnifiedNEECandidate(
            geometry.positionWS,
            geometry.faceNormalWS,
            allowsThinWalledTransmission,
            payload.directLightRandom,
            neeCandidate);
    // Preserve the selected source and declared densities even when its
    // conditional sample is rejected. Phase 4.4 diagnostics use this to
    // measure the selector itself rather than only positive contributions.
    if (neeCandidate.selectionPdf > 0.0)
    {
        payload.neeDirectionWS = neeCandidate.directionWS;
        payload.neeDistance = neeCandidate.distance;
        payload.neeSelectionPdf = neeCandidate.selectionPdf;
        payload.neeSolidAnglePdf = neeCandidate.solidAnglePdf;
        payload.neeShadowStrength = neeCandidate.shadowStrength;
        payload.neeLightIndex = neeCandidate.lightIndex;
        payload.neeLightType = neeCandidate.lightType;
        payload.neeFlags = neeCandidate.flags;
    }

    bool neeUsesReflectionHemisphere =
        ReferencedPathtracingIsValidOpaqueReflectionDirection(
            neeCandidate.directionWS,
            geometry.faceNormalWS,
            material.shadingNormalWS);
    if (validNeeCandidate
        && ReferencedPathtracingIsValidThinWalledSurfaceDirection(
            neeCandidate.directionWS,
            geometry.faceNormalWS,
            material.shadingNormalWS,
            allowsThinWalledTransmission))
    {
        OpenPBR_DiffuseSpecular neeResponse = openpbr_eval(
            preparedBsdf,
            neeCandidate.directionWS);
        float neeBsdfPdf = openpbr_pdf(
            preparedBsdf,
            neeCandidate.directionWS);
        float3 neeDiffuseBsdf =
            openpbr_extract_diffuse_from_diffuse_specular(neeResponse);
        float3 neeSpecularBsdf =
            openpbr_extract_specular_from_diffuse_specular(neeResponse);
        float diffuseShadowTerminator = neeUsesReflectionHemisphere
            ? ReferencedPathtracingEvaluateDiffuseShadowTerminator(
                neeCandidate.directionWS,
                material.shadingNormalWS,
                geometry.normalWS)
            : 1.0;
        neeDiffuseBsdf *= diffuseShadowTerminator;
        payload.shadingNormalDiagnostics.z = min(
            payload.shadingNormalDiagnostics.z,
            diffuseShadowTerminator);
        if (VividReferencedPathtracingIsFinite(neeDiffuseBsdf)
            && VividReferencedPathtracingIsFinite(neeSpecularBsdf)
            && VividReferencedPathtracingIsFinite(neeBsdfPdf)
            && neeBsdfPdf >= 0.0)
        {
            payload.neeDiffuseRadiance =
                max(neeDiffuseBsdf, 0.0)
                * neeCandidate.incidentRadianceOverPdf;
            payload.neeSpecularRadiance =
                max(neeSpecularBsdf, 0.0)
                * neeCandidate.incidentRadianceOverPdf;
            payload.neeBsdfPdf = neeBsdfPdf;
            payload.neeValid = 1u;
        }
    }

    float3 sampledDirectionWS;
    OpenPBR_DiffuseSpecular sampledWeight;
    float sampledPdf;
    OpenPBR_BsdfLobeType sampledLobeType;
    openpbr_sample(
        preparedBsdf,
        saturate(payload.bsdfRandom),
        sampledDirectionWS,
        sampledWeight,
        sampledPdf,
        sampledLobeType);

    bool sampledTransmission =
        (sampledLobeType & OpenPBR_BsdfLobeTypeTransmission) != 0u;
    bool sampledReflection =
        (sampledLobeType & OpenPBR_BsdfLobeTypeReflection) != 0u;
    bool sampledDirectionIsSupported =
        (sampledReflection
            && ReferencedPathtracingIsValidOpaqueReflectionDirection(
                sampledDirectionWS,
                geometry.faceNormalWS,
                material.shadingNormalWS))
        || (sampledTransmission
            && allowsSurfaceTransmission
            && ReferencedPathtracingIsValidTransmissionDirection(
                sampledDirectionWS,
                geometry.faceNormalWS,
                material.shadingNormalWS));
    if (sampledPdf > 0.0
        && VividReferencedPathtracingIsFinite(sampledPdf)
        && VividReferencedPathtracingIsFinite(sampledDirectionWS)
        && sampledDirectionIsSupported)
    {
        float diffuseShadowTerminator = sampledReflection
            ? ReferencedPathtracingEvaluateDiffuseShadowTerminator(
                sampledDirectionWS,
                material.shadingNormalWS,
                geometry.normalWS)
            : 1.0;
        openpbr_set_diffuse_in_diffuse_specular(
            sampledWeight,
            openpbr_extract_diffuse_from_diffuse_specular(sampledWeight)
                * diffuseShadowTerminator);
        payload.shadingNormalDiagnostics.z = min(
            payload.shadingNormalDiagnostics.z,
            diffuseShadowTerminator);
        float3 nextThroughputWeight = openpbr_get_sum_of_diffuse_specular(sampledWeight);
        if (VividReferencedPathtracingIsFinite(nextThroughputWeight)
            && any(nextThroughputWeight > 0.0))
        {
            payload.nextDirectionWS = normalize(sampledDirectionWS);
            payload.nextThroughputWeight = max(nextThroughputWeight, 0.0);
            payload.nextPdf = sampledPdf;
            payload.nextLobeClass = sampledTransmission
                ? 2u
                : ((sampledLobeType & OpenPBR_BsdfLobeTypeDiffuse) != 0u
                    ? 1u
                    : 2u);
            payload.nextLobeIsTransmission =
                sampledTransmission ? 1u : 0u;
            if (sampledTransmission
                && material.isSolidTransmissionBoundary)
            {
                payload.mediumTransition =
                    geometry.isFrontFace ? 1 : -1;
                payload.nextMediumIor =
                    material.openPbrInputs.specular_ior;
                payload.nextMediumExtinction =
                    VividReferencedPathtracingIsFinite(
                        preparedBsdf.volume.extinction_coefficient)
                    ? max(
                        preparedBsdf.volume.extinction_coefficient,
                        0.0)
                    : 0.0;
                payload.nextMediumInstanceIndex = InstanceIndex();
            }
            // OpenPBR uses the Specular flag for a singular (delta) event. Glossy
            // reflection remains non-delta and competes with environment NEE.
            payload.nextLobeIsDelta =
                (sampledLobeType & OpenPBR_BsdfLobeTypeSpecular) != 0u
                    ? 1u
                    : 0u;
        }
    }

    payload.hit = 1u;
}

[shader("anyhit")]
void StandardLitReferencedPathtracingAnyHit(
    inout ReferencedPathtracingPayload payload : SV_RayPayload,
    AttributeData attributeData : SV_IntersectionAttributes)
{
    float2 uv = VividIndirectDiffuseFetchUV(attributeData);
    bool isSolidTransmissionBoundary =
        _ThinWalledTransmission <= 0.5
        && SampleOpenPbrTransmissionWeight(uv) > 0.0;
    bool isUnmatchedNestedExit =
        isSolidTransmissionBoundary
        && HitKind() == HIT_KIND_TRIANGLE_BACK_FACE
        && payload.activeMediumInstanceIndex
            != kReferencedPathtracingInvalidMediumInstance
        && payload.activeMediumInstanceIndex != InstanceIndex();
    if (isUnmatchedNestedExit)
    {
        // Match RTXPT's nested-dielectric false-hit handling: an exit that
        // does not belong to the active medium cannot change the current
        // interface, so continue traversal to the next candidate.
        IgnoreHit();
        return;
    }

#if defined(_ALPHATEST_ON) || defined(_SURFACE_TYPE_TRANSPARENT)
#if defined(_ALPHATEST_ON)
    float opacity = saturate(SampleBase(uv).a);
    if (VividIndirectDiffuseIsAlphaClipped(opacity))
    {
        IgnoreHit();
        return;
    }
#endif

#if defined(_SURFACE_TYPE_TRANSPARENT)
    float geometryOpacity = SampleOpenPbrGeometryOpacity(uv);
    uint candidateSeed =
        ReferencedPathtracingHashStochasticTransparency(
            payload.stochasticAlphaSeed ^ 0x9e3779b9u);
    candidateSeed ^=
        ReferencedPathtracingHashStochasticTransparency(
            InstanceIndex() + 0x85ebca6bu);
    candidateSeed ^=
        ReferencedPathtracingHashStochasticTransparency(
            PrimitiveIndex() + 0xc2b2ae35u);
    candidateSeed ^=
        ReferencedPathtracingHashStochasticTransparency(
            asuint(RayTCurrent()) + 0x27d4eb2du);
    float opacityRandom =
        ReferencedPathtracingHashStochasticTransparencyToUnitFloat(
            candidateSeed);

    bool surfaceBranch = opacityRandom < geometryOpacity;
    payload.stochasticTransparencyDiagnostics.rgb = geometryOpacity;
    payload.stochasticTransparencyDiagnostics.a += 1.0;
    if (!surfaceBranch)
    {
        IgnoreHit();
    }
#endif
#endif
}

#endif
