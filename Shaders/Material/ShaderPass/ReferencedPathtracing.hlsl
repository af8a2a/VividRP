#ifndef VIVIDRP_REFERENCED_PATH_TRACING_MATERIAL_PASS_INCLUDED
#define VIVIDRP_REFERENCED_PATH_TRACING_MATERIAL_PASS_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingCommon.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingLightList.hlsl"

#define VIVIDRP_REFERENCED_PATH_TRACING_USE_RTXTF 1
#define VIVIDRP_INDIRECT_DIFFUSE_DEFINE_RAYTRACING_SHADERS 0
#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/IndirectDiffuse.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/ReferencedPathtracingRTXTF.hlsl"
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

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingNEECandidate.hlsl"

[shader("closesthit")]
void StandardLitReferencedPathtracingClosestHit(
    inout ReferencedPathtracingPayload payload : SV_RayPayload,
    AttributeData attributeData : SV_IntersectionAttributes)
{
    ReferencedPathtracingPayloadInput payloadInput;
    UnpackReferencedPathtracingPayloadInput(payload, payloadInput);
    ReferencedPathtracingSurfaceResult result;
    InitializeReferencedPathtracingSurfaceResult(result);
    result.stochasticTransparencyDiagnostics =
        LoadReferencedPathtracingStochasticTransparencyDiagnostics(payload);

    VividIndirectDiffuseHitGeometry geometry = VividIndirectDiffuseBuildHitGeometry(attributeData);
    float hitConeWidth;
    float textureBaseLambda = ComputeReferencedPathtracingTextureBaseLambda(
        geometry,
        payloadInput.rayConeWidth,
        payloadInput.rayConeSpreadAngle,
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
    STF_SamplerState rtxtfSamplerState =
        ReferencedPathtracingCreateRTXTFState(payloadInput.rtxtfRandom);
    VividReferencedPathtracingMaterial material =
        VividReferencedPathtracingResolveStandardLitOpenPBR(
        rtxtfSamplerState,
        geometry,
        textureBaseLambda,
        baseTextureLod,
        normalTextureLod,
        viewDirectionWS);

    if (payloadInput.queryMode
        == REFERENCED_PATHTRACING_QUERY_SUBSURFACE_SURFACE)
    {
        // A surface query returns only the sampled exit point identity and a
        // Lambertian direct-light proposal. Raygen owns both the Burley
        // spatial estimator and the visibility trace, keeping recursion at 1.
        result.faceNormalWS = geometry.faceNormalWS;
        result.rayConeWidth = hitConeWidth;
        result.hitDistance = geometry.hitDistance;
        result.denoisingNormalWS = material.shadingNormalWS;
        result.surfaceInstanceIndex = InstanceIndex();
        result.isSurfaceQuery = 1u;

        ReferencedPathtracingNEECandidate queryNeeCandidate;
        bool validQueryNeeCandidate =
            ReferencedPathtracingSampleUnifiedNEECandidate(
                geometry.positionWS,
                geometry.faceNormalWS,
                false,
                payloadInput.directLightRandom,
                queryNeeCandidate);
        if (queryNeeCandidate.selectionPdf > 0.0)
        {
            result.neeDirectionWS = queryNeeCandidate.directionWS;
            result.neeDistance = queryNeeCandidate.distance;
            result.neeSelectionPdf = queryNeeCandidate.selectionPdf;
            result.neeSolidAnglePdf = queryNeeCandidate.solidAnglePdf;
            result.neeShadowStrength = queryNeeCandidate.shadowStrength;
            result.neeLightIndex = queryNeeCandidate.lightIndex;
            result.neeLightType = queryNeeCandidate.lightType;
            result.neeFlags = queryNeeCandidate.flags;
        }

        if (validQueryNeeCandidate
            && ReferencedPathtracingIsValidOpaqueReflectionDirection(
                queryNeeCandidate.directionWS,
                geometry.faceNormalWS,
                material.shadingNormalWS))
        {
            float diffuseShadowTerminator =
                ReferencedPathtracingEvaluateDiffuseShadowTerminator(
                    queryNeeCandidate.directionWS,
                    material.shadingNormalWS,
                    geometry.normalWS);
            float cosineToLight = max(
                dot(
                    material.shadingNormalWS,
                    queryNeeCandidate.directionWS),
                0.0);
            result.neeDiffuseRadiance =
                (cosineToLight * INV_PI * diffuseShadowTerminator)
                * queryNeeCandidate.incidentRadianceOverPdf;
            result.neeBsdfPdf = 0.0;
            result.neeValid = any(result.neeDiffuseRadiance > 0.0)
                ? 1u
                : 0u;
        }

        result.hit = 1u;
        PackReferencedPathtracingSurfaceResult(result, payload);
        return;
    }

    float exteriorIor = payloadInput.activeMediumIor;
    if (material.isSolidTransmissionBoundary
        && !geometry.isFrontFace
        && payloadInput.activeMediumInstanceIndex == InstanceIndex())
    {
        exteriorIor = payloadInput.parentMediumIor;
    }
    if (!VividReferencedPathtracingIsFinite(exteriorIor))
        exteriorIor = OpenPBR_VacuumIor;
    exteriorIor = clamp(exteriorIor, 1.0, 3.0);
    float3 segmentMediumTransmittance =
        ReferencedPathtracingEvaluateMaterialMediumTransmittance(
            payloadInput.activeMediumExtinction,
            geometry.hitDistance);
    OpenPBR_ResolvedInputs transportInputs = material.openPbrInputs;
    // The Burley estimator replaces only the selected dielectric diffuse
    // fraction. Keep the microfacet/coat response in OpenPBR and leave the
    // remaining local diffuse fraction available for partial SSS weights.
    transportInputs.base_color *=
        1.0 - saturate(material.effectiveSubsurfaceWeight);
    transportInputs.subsurface_weight = 0.0;
    OpenPBR_PreparedBsdf preparedBsdf = openpbr_prepare(
        transportInputs,
        max(
            payloadInput.pathThroughput * segmentMediumTransmittance,
            0.0),
        OpenPBR_BaseRgbWavelengths_nm,
        exteriorIor,
        viewDirectionWS);

    result.faceNormalWS = geometry.faceNormalWS;
    result.rayConeWidth = hitConeWidth;
    result.emission = VividReferencedPathtracingIsFinite(preparedBsdf.emission)
        ? max(preparedBsdf.emission, 0.0)
        : 0.0;
    result.linearRoughness = material.openPbrInputs.specular_roughness;
    result.hitDistance = geometry.hitDistance;
    // Match HDRP's reference-denoising AOV contract: diffuse reflectance and
    // the actual shading normal, both evaluated at primary visibility.
    float3 surfaceDiffuseAlbedo = saturate(
        material.openPbrInputs.base_color
        * (1.0 - material.openPbrInputs.base_metalness)
        * (1.0 - material.openPbrInputs.transmission_weight));
    result.denoisingAlbedo = saturate(lerp(
        surfaceDiffuseAlbedo,
        material.subsurfaceAlbedo,
        saturate(material.effectiveSubsurfaceWeight)));
    result.denoisingNormalWS = material.shadingNormalWS;
    if (geometry.isFrontFace
        && material.effectiveSubsurfaceWeight > 0.0)
    {
        result.subsurfaceWeight =
            saturate(material.effectiveSubsurfaceWeight);
        result.subsurfaceAlbedo = material.subsurfaceAlbedo;
        result.subsurfaceRadius = material.subsurfaceRadius;
        result.surfaceInstanceIndex = InstanceIndex();
        result.isSubsurface = 1u;
    }
    result.thinWalledTransmissionWeight =
        material.openPbrInputs.geometry_thin_walled
            ? saturate(material.effectiveTransmissionWeight)
            : 0.0;
    bool allowsThinWalledTransmission =
        result.thinWalledTransmissionWeight > 0.0;
    bool allowsSurfaceTransmission =
        material.effectiveTransmissionWeight > 0.0;
    result.shadingNormalDiagnostics = float3(
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
            payloadInput.directLightRandom,
            neeCandidate);
    // Preserve the selected source and declared densities even when its
    // conditional sample is rejected. Phase 4.4 diagnostics use this to
    // measure the selector itself rather than only positive contributions.
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
        result.shadingNormalDiagnostics.z = min(
            result.shadingNormalDiagnostics.z,
            diffuseShadowTerminator);
        if (VividReferencedPathtracingIsFinite(neeDiffuseBsdf)
            && VividReferencedPathtracingIsFinite(neeSpecularBsdf)
            && VividReferencedPathtracingIsFinite(neeBsdfPdf)
            && neeBsdfPdf >= 0.0)
        {
            result.neeDiffuseRadiance =
                max(neeDiffuseBsdf, 0.0)
                * neeCandidate.incidentRadianceOverPdf;
            result.neeSpecularRadiance =
                max(neeSpecularBsdf, 0.0)
                * neeCandidate.incidentRadianceOverPdf;
            result.neeBsdfPdf = neeBsdfPdf;
            result.neeValid = 1u;
        }
    }

    float3 sampledDirectionWS;
    OpenPBR_DiffuseSpecular sampledWeight;
    float sampledPdf;
    OpenPBR_BsdfLobeType sampledLobeType;
    openpbr_sample(
        preparedBsdf,
        saturate(payloadInput.bsdfRandom),
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
        result.shadingNormalDiagnostics.z = min(
            result.shadingNormalDiagnostics.z,
            diffuseShadowTerminator);
        float3 nextThroughputWeight = openpbr_get_sum_of_diffuse_specular(sampledWeight);
        if (VividReferencedPathtracingIsFinite(nextThroughputWeight)
            && any(nextThroughputWeight > 0.0))
        {
            result.nextDirectionWS = normalize(sampledDirectionWS);
            result.nextThroughputWeight = max(nextThroughputWeight, 0.0);
            result.nextPdf = sampledPdf;
            result.nextLobeClass = sampledTransmission
                ? 2u
                : ((sampledLobeType & OpenPBR_BsdfLobeTypeDiffuse) != 0u
                    ? 1u
                    : 2u);
            result.nextLobeIsTransmission =
                sampledTransmission ? 1u : 0u;
            if (sampledTransmission
                && material.isSolidTransmissionBoundary)
            {
                result.mediumTransition =
                    geometry.isFrontFace ? 1 : -1;
                result.nextMediumIor =
                    material.openPbrInputs.specular_ior;
                result.nextMediumExtinction =
                    VividReferencedPathtracingIsFinite(
                        preparedBsdf.volume.extinction_coefficient)
                    ? max(
                        preparedBsdf.volume.extinction_coefficient,
                        0.0)
                    : 0.0;
                float3 mediumScatteringAlbedo =
                    VividReferencedPathtracingIsFinite(
                        preparedBsdf.volume.albedo)
                    ? saturate(preparedBsdf.volume.albedo)
                    : 0.0;
                float mediumScatteringAnisotropy =
                    VividReferencedPathtracingIsFinite(
                        preparedBsdf.volume.anisotropy)
                    ? clamp(
                        preparedBsdf.volume.anisotropy,
                        -0.95,
                        0.95)
                    : 0.0;
                result.nextMediumScattering =
                    PackReferencedPathtracingMaterialMediumScattering(
                        mediumScatteringAlbedo,
                        mediumScatteringAnisotropy);
                result.nextMediumInstanceIndex = InstanceIndex();
            }
            // OpenPBR uses the Specular flag for a singular (delta) event. Glossy
            // reflection remains non-delta and competes with environment NEE.
            result.nextLobeIsDelta =
                (sampledLobeType & OpenPBR_BsdfLobeTypeSpecular) != 0u
                    ? 1u
                    : 0u;
        }
    }

    result.hit = 1u;
    PackReferencedPathtracingSurfaceResult(result, payload);
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
    uint activeMediumInstanceIndex =
        LoadReferencedPathtracingActiveMediumInstanceIndex(payload);
    bool isUnmatchedNestedExit =
        isSolidTransmissionBoundary
        && HitKind() == HIT_KIND_TRIANGLE_BACK_FACE
        && activeMediumInstanceIndex
            != kReferencedPathtracingInvalidMediumInstance
        && activeMediumInstanceIndex != InstanceIndex();
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
            LoadReferencedPathtracingStochasticAlphaSeed(payload)
                ^ 0x9e3779b9u);
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
    RecordReferencedPathtracingStochasticTransparency(
        payload,
        geometryOpacity);
    if (!surfaceBranch)
    {
        IgnoreHit();
    }
#endif
#endif
}

#endif
