#ifndef VIVIDRP_HAIR_REFERENCED_PATH_TRACING_INCLUDED
#define VIVIDRP_HAIR_REFERENCED_PATH_TRACING_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingCommon.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingLightList.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Material/Hair/HairChiangAdapter.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingNEECandidate.hlsl"

[shader("closesthit")]
void HairReferencedPathtracingClosestHit(
    inout ReferencedPathtracingPayload payload : SV_RayPayload,
    AttributeData attributeData : SV_IntersectionAttributes)
{
    ReferencedPathtracingPayloadInput payloadInput;
    UnpackReferencedPathtracingPayloadInput(payload, payloadInput);
    ReferencedPathtracingSurfaceResult result;
    InitializeReferencedPathtracingSurfaceResult(result);
    result.stochasticTransparencyDiagnostics =
        LoadReferencedPathtracingStochasticTransparencyDiagnostics(payload);

    VividHairSurfaceGeometry geometry =
        VividHairBuildDotsSurfaceGeometry(attributeData);
    float3 viewDirectionWS = normalize(-WorldRayDirection());
    VividHairPreparedChiang prepared = VividHairPrepareChiang(
        geometry,
        viewDirectionWS);

    result.rayConeWidth = max(
        payloadInput.rayConeWidth
            + geometry.hitDistance * payloadInput.rayConeSpreadAngle,
        0.000001);
    result.positionWS = geometry.positionWS;
    result.faceNormalWS = geometry.faceNormalWS;
    result.emission = VividHairGetEmission();
    result.linearRoughness = VividHairGetLongitudinalRoughness();
    result.hitDistance = geometry.hitDistance;
    result.denoisingAlbedo = VividHairGetBaseColor();
    result.denoisingNormalWS = geometry.radialNormalWS;
    result.shadingNormalDiagnostics = 1.0;
    result.strandRadius = geometry.radius;
    result.isStrand = 1u;

    ReferencedPathtracingNEECandidate neeCandidate;
    bool validNeeCandidate =
        ReferencedPathtracingSampleUnifiedNEECandidate(
            geometry.positionWS,
            geometry.faceNormalWS,
            true,
            payloadInput.directLightRandom,
            neeCandidate);
    if (neeCandidate.selectionPdf > 0.0)
    {
        result.neeDirectionWS = neeCandidate.directionWS;
        result.neeDistance = neeCandidate.distance;
        result.neeSelectionPdf = neeCandidate.selectionPdf;
        result.neeSolidAnglePdf = neeCandidate.solidAnglePdf;
        result.neeShadowStrength = neeCandidate.shadowStrength;
        result.neeLightIndex = neeCandidate.lightIndex;
        result.neeLightType = neeCandidate.lightType;
        result.neeFlags = neeCandidate.flags;
    }

    if (validNeeCandidate)
    {
        float neeBsdfPdf;
        float3 neeBsdf = VividHairEvaluateChiang(
            prepared,
            neeCandidate.directionWS,
            neeBsdfPdf);
        if (VividReferencedPathtracingIsFinite(neeBsdf)
            && VividReferencedPathtracingIsFinite(neeBsdfPdf)
            && neeBsdfPdf >= 0.0)
        {
            result.neeDiffuseRadiance = 0.0;
            result.neeSpecularRadiance = max(neeBsdf, 0.0)
                * neeCandidate.incidentRadianceOverPdf;
            result.neeBsdfPdf = neeBsdfPdf;
            result.neeValid = 1u;
        }
    }

    float3 sampledDirectionWS;
    float3 sampledValue;
    float sampledPdf;
    uint sampledLobe;
    bool sampled = VividHairSampleChiang(
        prepared,
        float4(
            saturate(payloadInput.bsdfRandom),
            saturate(payloadInput.hairBsdfExtraRandom)),
        sampledDirectionWS,
        sampledValue,
        sampledPdf,
        sampledLobe);
    if (sampled && sampledPdf > 0.0)
    {
        float3 throughputWeight = sampledValue / sampledPdf;
        if (VividReferencedPathtracingIsFinite(throughputWeight)
            && any(throughputWeight > 0.0))
        {
            result.nextDirectionWS = normalize(sampledDirectionWS);
            result.nextThroughputWeight = max(throughputWeight, 0.0);
            result.nextPdf = sampledPdf;
            result.nextLobeClass = 2u;
            result.nextLobeIsDelta = 0u;
            result.nextLobeIsTransmission = 0u;
            result.mediumTransition = 0;
        }
    }

    result.hit = 1u;
    PackReferencedPathtracingSurfaceResult(result, payload);
}

#endif
