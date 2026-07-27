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

    OpenPBR_PreparedBsdf preparedBsdf = openpbr_prepare(
        material.openPbrInputs,
        max(payload.pathThroughput, 0.0),
        OpenPBR_BaseRgbWavelengths_nm,
        OpenPBR_VacuumIor,
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
        * (1.0 - material.openPbrInputs.base_metalness));
    payload.denoisingNormalWS = material.shadingNormalWS;
    payload.nextLobeClass = 0u;
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

    if (validNeeCandidate
        && ReferencedPathtracingIsValidOpaqueReflectionDirection(
            neeCandidate.directionWS,
            geometry.faceNormalWS,
            material.shadingNormalWS))
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
        float diffuseShadowTerminator =
            ReferencedPathtracingEvaluateDiffuseShadowTerminator(
                neeCandidate.directionWS,
                material.shadingNormalWS,
                geometry.normalWS);
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

    if (sampledPdf > 0.0
        && VividReferencedPathtracingIsFinite(sampledPdf)
        && VividReferencedPathtracingIsFinite(sampledDirectionWS)
        && ReferencedPathtracingIsValidOpaqueReflectionDirection(
            sampledDirectionWS,
            geometry.faceNormalWS,
            material.shadingNormalWS))
    {
        float diffuseShadowTerminator =
            ReferencedPathtracingEvaluateDiffuseShadowTerminator(
                sampledDirectionWS,
                material.shadingNormalWS,
                geometry.normalWS);
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
            payload.nextLobeClass = (sampledLobeType & OpenPBR_BsdfLobeTypeDiffuse) != 0u
                ? 1u
                : 2u;
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
#if defined(_ALPHATEST_ON)
    float2 uv = VividIndirectDiffuseFetchUV(attributeData);
    if (VividIndirectDiffuseIsAlphaClipped(SampleBase(uv).a))
        IgnoreHit();
#endif
}

#endif
