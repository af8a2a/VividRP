#ifndef VIVID_SDF_SCENE_DIRECT_LIGHTING_COMMON_INCLUDED
#define VIVID_SDF_SCENE_DIRECT_LIGHTING_COMMON_INCLUDED

#include "SdfScreenProbeCommon.hlsl"
#include "SdfSurfaceCacheLighting.hlsl"
#if defined(VIVID_SDF_CARD_SAMPLED_LIGHTING)
#include "../../Public/ReGIR.hlsl"
#endif
#if defined(VIVID_SDF_CARD_RAY_QUERY)
#include "UnityRayQuery.cginc"
#endif

#if defined(VIVID_SDF_SURFACE_CACHE)
Texture2D<float4> _SdfCardDirectLighting;
RWTexture2D<float4> _SdfCardLightingOutput;
uint _SdfCardLightingFirstCard;
uint _SdfCardLightingHasDirect;
uint _SdfCardLightingPriorityOrder;

void LoadCardLightingWork(uint card, out uint instance, out uint slot)
{
    uint work = _SdfRadiosityWork[0] + 2u * card;
    instance = _SdfRadiosityWork[work];
    slot = _SdfRadiosityWork[work + 1u];
}

float3 FiniteCardLighting(float3 lighting)
{
    return float3(isfinite(lighting.x) ? clamp(lighting.x, 0.0f, 65000.0f) : 0.0f,
        isfinite(lighting.y) ? clamp(lighting.y, 0.0f, 65000.0f) : 0.0f,
        isfinite(lighting.z) ? clamp(lighting.z, 0.0f, 65000.0f) : 0.0f);
}

#if defined(VIVID_SDF_CARD_SAMPLED_LIGHTING)
#define VIVID_CARD_SAMPLED_PRESAMPLED_TILE_SIZE 1024u
#define VIVID_CARD_SAMPLED_PRESAMPLED_TILE_COUNT 128u
#define VIVID_CARD_SAMPLED_PUNCTUAL_THRESHOLD 0.01f

StructuredBuffer<VividReGIRLightData> _ReGIRLights;
StructuredBuffer<VividReGIRParameters> _ReGIRParameters;
StructuredBuffer<VividReGIRReservoir> _ReGIRReservoirs;
StructuredBuffer<uint4> _ReGIRPresampledLights;

#if defined(VIVID_SDF_CARD_RAY_QUERY)
RaytracingAccelerationStructure _AccelerationStructure;
#endif

uint _SdfSampledLightingLocalIterations;
uint _SdfSampledLightingLocalCandidates;
uint _SdfSampledLightingEvaluateAll;
uint _SdfSampledLightingUseReGIR;
uint _SdfSampledLightingFrame;

uint CardSampledHash(uint value)
{
    value ^= value >> 16;
    value *= 0x7feb352du;
    value ^= value >> 15;
    value *= 0x846ca68bu;
    value ^= value >> 16;
    return value;
}

float CardSampledRand01(inout uint rng)
{
    rng = CardSampledHash(rng);
    return (rng & 0x00ffffffu) * (1.0f / 16777216.0f);
}

float CardSampledLuminance(float3 color)
{
    return dot(color, float3(0.2126f, 0.7152f, 0.0722f));
}

float CardSampledDistanceWindowing(float distanceSquared, float rangeAttenuationScale, float rangeAttenuationBias)
{
    float scaledDistanceSquared = distanceSquared * rangeAttenuationScale;
    return saturate(rangeAttenuationBias - scaledDistanceSquared * scaledDistanceSquared);
}

bool CardSampledResolveRectangleBarnDoor(
    VividReGIRLightData light,
    float3 positionWS,
    out float3 lightCenterWS,
    out float2 lightSize,
    out float3 lightRightWS,
    out float3 lightUpWS,
    out float3 lightForwardWS)
{
    lightCenterWS = light.positionWS;
    lightSize = max(light.areaSize, 0.0f);
    lightRightWS = 0.0f;
    lightUpWS = 0.0f;
    lightForwardWS = 0.0f;

    float rightLengthSq = dot(light.rightWS, light.rightWS);
    float upLengthSq = dot(light.upWS, light.upWS);
    float forwardLengthSq = dot(light.directionWS, light.directionWS);
    if (any(lightSize <= 1e-6f) || rightLengthSq <= 1e-8f || upLengthSq <= 1e-8f || forwardLengthSq <= 1e-8f)
        return false;

    lightRightWS = light.rightWS * rsqrt(rightLengthSq);
    lightUpWS = light.upWS * rsqrt(upLengthSq);
    lightForwardWS = light.directionWS * rsqrt(forwardLengthSq);

    float cosBarnDoorAngle = saturate(light.cosBarnDoorAngle);
    float barnDoorLength = max(light.barnDoorLength, 0.0f);
    if (cosBarnDoorAngle <= 0.017f || barnDoorLength <= 0.05f)
        return true;

    float2 halfSize = 0.5f * lightSize;
    float3 lightRel = positionWS - light.positionWS;
    float3 pointLS = float3(
        dot(lightRel, lightRightWS),
        dot(lightRel, lightUpWS),
        dot(lightRel, lightForwardWS));

    float maxDepth = cosBarnDoorAngle * barnDoorLength;
    float pointDepth = min(pointLS.z, maxDepth);
    float pointDepthRatio = pointDepth / max(maxDepth, 1e-5f);
    float sinTheta = sqrt(saturate(1.0f - cosBarnDoorAngle * cosBarnDoorAngle));
    float barnDoorProjection = sinTheta * barnDoorLength * pointDepthRatio;

    float2 pointSign = sign(pointLS.xy);
    pointLS.xy = pointSign * max(abs(pointLS.xy), halfSize + barnDoorProjection.xx);

    float3 closestCorner = float3(
        pointSign.x * (halfSize.x + barnDoorProjection),
        pointSign.y * (halfSize.y + barnDoorProjection),
        pointDepth);
    float3 pointProjection = pointLS - closestCorner;
    float cosPhi = max(0.0f, pointProjection.z);
    float2 tanPhi = cosPhi > 0.001f ? abs(pointProjection.xy) / cosPhi : float2(99999.0f, 99999.0f);
    float2 projectionDistance = pointDepth * tanPhi;

    float2 horizontalBounds = float2(-halfSize.x, halfSize.x);
    float2 verticalBounds = float2(-halfSize.y, halfSize.y);
    horizontalBounds += (projectionDistance.x - barnDoorProjection) * float2(max(0.0f, -pointSign.x), -max(0.0f, pointSign.x));
    verticalBounds += (projectionDistance.y - barnDoorProjection) * float2(max(0.0f, -pointSign.y), -max(0.0f, pointSign.y));
    horizontalBounds = clamp(horizontalBounds, -halfSize.x, halfSize.x);
    verticalBounds = clamp(verticalBounds, -halfSize.y, halfSize.y);

    float2 centerOffset = 0.5f * float2(horizontalBounds.x + horizontalBounds.y, verticalBounds.x + verticalBounds.y);
    lightSize = max(float2(horizontalBounds.y - horizontalBounds.x, verticalBounds.y - verticalBounds.x), 0.0f);
    lightCenterWS += lightRightWS * centerOffset.x + lightUpWS * centerOffset.y;
    return all(lightSize > 1e-6f);
}

// Evaluates incident diffuse irradiance E = Li * cos(theta_i) / p_shape matching VividRP's
// PunctualLightCommon / ReferencedPathtracingNEECandidate radiometric conventions.
bool EvaluateReGIRLocalLightSample(
    float3 positionWS,
    float3 shadingNormalWS,
    float3 hitNormalWS,
    VividReGIRLightData light,
    float2 shapeSample,
    out float3 lightSamplePosWS,
    out float3 irradiance)
{
    lightSamplePosWS = light.positionWS;
    irradiance = 0.0f;

    float indirectScale = max(light.indirectMultiplier, 0.0f);
    if (indirectScale <= 0.0f || light.range <= 1e-5f)
        return false;

    float3 lightColor = max(light.color, 0.0f) * indirectScale;
    if (!any(lightColor > 0.0f))
        return false;

    if (light.lightType == VIVID_REGIR_LIGHT_TYPE_POINT || light.lightType == VIVID_REGIR_LIGHT_TYPE_SPOT)
    {
        float3 toLight = light.positionWS - positionWS;
        float distSq = dot(toLight, toLight);
        if (distSq <= 1e-8f || distSq >= light.range * light.range)
            return false;

        float invDist = rsqrt(distSq);
        float3 L = toLight * invDist;
        float nDotL = dot(shadingNormalWS, L);
        if (nDotL <= 0.0f || dot(hitNormalWS, L) <= 0.0f)
            return false;

        float invModDist = rsqrt(distSq + max(light.shapeRadius * light.shapeRadius, 0.0f));
        float atten = min(invModDist, 1.0f / VIVID_CARD_SAMPLED_PUNCTUAL_THRESHOLD);
        atten *= CardSampledDistanceWindowing(distSq, light.rangeAttenuationScale, light.rangeAttenuationBias);

        if (light.lightType == VIVID_REGIR_LIGHT_TYPE_SPOT)
        {
            float dirLenSq = dot(light.directionWS, light.directionWS);
            if (dirLenSq <= 1e-8f)
                return false;
            float3 spotDir = light.directionWS * rsqrt(dirLenSq);
            atten *= saturate(dot(spotDir, -L) * light.angleScale + light.angleOffset);
        }

        atten *= atten;
        if (atten <= 0.0f)
            return false;

        lightSamplePosWS = light.positionWS;
        irradiance = lightColor * (atten * nDotL);
        return true;
    }

    if (light.lightType == VIVID_REGIR_LIGHT_TYPE_RECTANGLE)
    {
        float3 rectCenterWS;
        float2 rectSize;
        float3 rectRightWS;
        float3 rectUpWS;
        float3 rectForwardWS;
        if (!CardSampledResolveRectangleBarnDoor(
                light, positionWS, rectCenterWS, rectSize, rectRightWS, rectUpWS, rectForwardWS))
            return false;

        float2 centered = saturate(shapeSample) - 0.5f;
        lightSamplePosWS = rectCenterWS + centered.x * rectSize.x * rectRightWS + centered.y * rectSize.y * rectUpWS;
        float3 toLight = lightSamplePosWS - positionWS;
        float distSq = dot(toLight, toLight);
        if (distSq <= 1e-8f)
            return false;

        float invDist = rsqrt(distSq);
        float3 L = toLight * invDist;
        float nDotL = dot(shadingNormalWS, L);
        if (nDotL <= 0.0f || dot(hitNormalWS, L) <= 0.0f)
            return false;

        float lightFacingCos = saturate(dot(-L, rectForwardWS));
        if (lightFacingCos <= 0.0f)
            return false;

        float w = CardSampledDistanceWindowing(distSq, light.rangeAttenuationScale, light.rangeAttenuationBias);
        float rangeWindow = w * w;
        if (rangeWindow <= 0.0f)
            return false;

        float sampleArea = rectSize.x * rectSize.y;
        float geomFactor = (lightFacingCos * sampleArea / distSq) * nDotL;
        irradiance = lightColor * (rangeWindow * geomFactor);
        return any(irradiance > 0.0f);
    }

    if (light.lightType == VIVID_REGIR_LIGHT_TYPE_TUBE)
    {
        float tubeLength = max(light.areaSize.x, 0.0f);
        float rightLenSq = dot(light.rightWS, light.rightWS);
        if (tubeLength <= 1e-6f || rightLenSq <= 1e-8f)
            return false;

        float3 rightWS = light.rightWS * rsqrt(rightLenSq);
        lightSamplePosWS = light.positionWS + (saturate(shapeSample.x) - 0.5f) * tubeLength * rightWS;
        float3 toLight = lightSamplePosWS - positionWS;
        float distSq = dot(toLight, toLight);
        if (distSq <= 1e-8f)
            return false;

        float invDist = rsqrt(distSq);
        float3 L = toLight * invDist;
        float nDotL = dot(shadingNormalWS, L);
        if (nDotL <= 0.0f || dot(hitNormalWS, L) <= 0.0f)
            return false;

        float axialCos = abs(dot(L, rightWS));
        float radialCos = sqrt(saturate(1.0f - axialCos * axialCos));
        if (radialCos <= 0.0f)
            return false;

        float w = CardSampledDistanceWindowing(distSq, light.rangeAttenuationScale, light.rangeAttenuationBias);
        float rangeWindow = w * w;
        if (rangeWindow <= 0.0f)
            return false;

        float shapeJacobian = 2.0f * radialCos * tubeLength / distSq;
        irradiance = lightColor * (rangeWindow * shapeJacobian * nDotL);
        return any(irradiance > 0.0f);
    }

    return false;
}

#if defined(VIVID_SDF_CARD_RAY_QUERY)
bool TraceCardRayQueryOccluded(float3 originWS, float3 directionWS, float tMax)
{
    if (tMax <= 1e-4f)
        return false;

    RayDesc shadowRay;
    shadowRay.Origin = originWS;
    shadowRay.Direction = directionWS;
    shadowRay.TMin = 0.0f;
    shadowRay.TMax = tMax;

    const uint rayFlags = RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH
        | RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES
        | RAY_FLAG_FORCE_OPAQUE;
    UnityRayQuery<rayFlags> query;
    query.TraceRayInline(_AccelerationStructure, rayFlags, 0xFF, shadowRay);
    while (query.Proceed())
    {
    }
    return query.CommittedStatus() == COMMITTED_TRIANGLE_HIT;
}

float3 TraceCardDirectSunLighting(float3 position, float3 normal, VividMeshSdfHit hit, DiffuseSurface surface)
{
    float range = _SdfDiffuseLightPositionRange.w;
    VividMeshSdfInstance instance = _MeshSdfInstances[hit.instanceIndex];
    if (range == 0.0f || (instance.layerMask & _SdfDiffuseLightLayerMask) == 0u)
        return 0.0f;

    bool directional = range < 0.0f;
    float3 direction = _SdfDiffuseLightPositionRange.xyz;
    float attenuation = 1.0f;
    if (!directional)
    {
        float3 toLight = direction - position;
        float distanceSquared = dot(toLight, toLight);
        if (distanceSquared <= 1e-6f || distanceSquared >= range * range)
            return 0.0f;
        direction = toLight * rsqrt(distanceSquared);
        float relativeDistance = distanceSquared / (range * range);
        float window = saturate(1.0f - relativeDistance * relativeDistance);
        attenuation = window * window / max(distanceSquared, 0.01f);
    }

    float nDotL = dot(normal, direction);
    if (nDotL <= 0.0f || dot(hit.normalWS, direction) <= 0.0f)
        return 0.0f;

    float shadow = 1.0f;
    float strength = _SdfDiffuseLightColorShadow.w;
    if (strength > 0.0f)
    {
        float bias = max(_SdfDiffuseRayBias, 0.02f);
        float3 origin = position + hit.normalWS * bias;
        float3 shadowDirection = direction;
        float shadowDistance = -range;
        if (!directional)
        {
            float3 shadowRay = _SdfDiffuseLightPositionRange.xyz - origin;
            shadowDistance = max(length(shadowRay) - 0.02f, 0.0f);
            shadowDirection = shadowRay / max(length(shadowRay), 1e-6f);
        }
        if (shadowDistance > bias && TraceCardRayQueryOccluded(origin, shadowDirection, shadowDistance))
            shadow = saturate(1.0f - strength);
    }

    return saturate(surface.albedoAndValid.rgb) * max(_SdfDiffuseLightColorShadow.rgb, 0.0f)
        * (INV_PI * nDotL * attenuation * shadow);
}

float TraceCardLocalLightVisibility(
    float3 positionWS,
    float3 hitNormalWS,
    float3 lightSamplePosWS,
    float shadowStrength,
    VividMeshSdfInstance instance,
    uint signMode)
{
    if (shadowStrength <= 0.0f)
        return 1.0f;

    float bias = max(_SdfDiffuseRayBias, 0.02f);
    float3 origin = positionWS + hitNormalWS * bias;
    float3 shadowRay = lightSamplePosWS - origin;
    float shadowDistance = length(shadowRay);
    if (shadowDistance <= bias + 0.02f)
        return 1.0f;

    float3 shadowDirection = shadowRay / shadowDistance;
    bool occluded = TraceCardRayQueryOccluded(origin, shadowDirection, shadowDistance - 0.02f);
    return occluded ? saturate(1.0f - shadowStrength) : 1.0f;
}
#else
float3 TraceCardDirectSunLighting(float3 position, float3 normal, VividMeshSdfHit hit, DiffuseSurface surface)
{
    return TraceHitDirectLighting(position, normal, hit, surface);
}

float TraceCardLocalLightVisibility(
    float3 positionWS,
    float3 hitNormalWS,
    float3 lightSamplePosWS,
    float shadowStrength,
    VividMeshSdfInstance instance,
    uint signMode)
{
    if (shadowStrength <= 0.0f)
        return 1.0f;

    float bias = _SdfDiffuseRayBias;
    if (signMode == 2u)
        bias = ResolveSunShadowBias(_MeshSdfFields[instance.fieldIndex], instance, hitNormalWS);

    float3 origin = positionWS + hitNormalWS * bias;
    float3 shadowRay = lightSamplePosWS - origin;
    float shadowDistance = length(shadowRay);
    if (shadowDistance <= _SdfDiffuseRayBias)
        return 1.0f;

    float3 shadowDirection = shadowRay / shadowDistance;
    bool occluded = false;

    if (_SdfUseGlobalClipmap != 0u && _SdfMeshDetailDistance > 0.0f)
    {
        float detailDistance = min(shadowDistance, _SdfMeshDetailDistance);
        if (detailDistance > _SdfDiffuseRayBias)
            occluded = VividOccludeMeshSdfScene(origin, shadowDirection, detailDistance, _SdfDiffuseLayerMask);

        if (!occluded && shadowDistance > detailDistance)
        {
            VividGlobalSdfHit globalHit;
            occluded = VividTraceGlobalSdf(origin, shadowDirection, detailDistance, shadowDistance, globalHit, false, false, 64);
        }
    }
    else
    {
        occluded = VividOccludeMeshSdfScene(origin, shadowDirection, shadowDistance, _SdfDiffuseLayerMask);
    }

    return occluded ? saturate(1.0f - shadowStrength) : 1.0f;
}
#endif

bool DrawPresampledOrUniformCandidate(
    VividReGIRParameters parameters,
    uint tileOffset,
    inout uint rng,
    out uint lightIndex,
    out float invProposalPdf,
    out float2 shapeSample)
{
    if (_SdfSampledLightingUseReGIR != 0u && parameters.sourceSamplingMode == VIVID_REGIR_SOURCE_SAMPLING_POWER_RIS)
    {
        uint sampleInTile = min(
            (uint)floor(CardSampledRand01(rng) * VIVID_CARD_SAMPLED_PRESAMPLED_TILE_SIZE),
            VIVID_CARD_SAMPLED_PRESAMPLED_TILE_SIZE - 1u);
        uint4 presampled = _ReGIRPresampledLights[tileOffset + sampleInTile];
        lightIndex = presampled.x;
        invProposalPdf = asfloat(presampled.y);
        shapeSample = float2(asfloat(presampled.z), asfloat(presampled.w));
        if (lightIndex < parameters.lightCount && invProposalPdf > 0.0f)
            return true;
    }

    lightIndex = min((uint)floor(CardSampledRand01(rng) * parameters.lightCount), parameters.lightCount - 1u);
    invProposalPdf = float(parameters.lightCount);
    shapeSample = float2(CardSampledRand01(rng), CardSampledRand01(rng));
    return true;
}

// Single-pass multi-iteration RIS local-light direct lighting for Lumen Surface Cache cards.
// Matches NvRTX SampledLightsSinglePass.ush / RTXDILumenSurfaceCache.usf.
float3 EvaluateCardSampledLocalLights(
    float3 positionWS,
    float3 normalWS,
    VividMeshSdfHit hit,
    VividMeshSdfInstance instance,
    float3 albedo,
    uint card,
    uint2 texelCoord)
{
    VividReGIRParameters parameters = _ReGIRParameters[0];
    if (parameters.lightCount == 0u || !any(albedo > 0.0f))
        return 0.0f;

    uint rng = CardSampledHash(
        (card * 1973u)
        ^ (texelCoord.x * 9277u)
        ^ (texelCoord.y * 26699u)
        ^ (_SdfSampledLightingFrame * 1664525u)
        ^ 0x9e3779b9u);

    // Exact evaluation mode: used for <= 4 local lights and golden-reference validation.
    if (_SdfSampledLightingEvaluateAll != 0u)
    {
        float3 totalDirect = 0.0f;
        [loop]
        for (uint lightIndex = 0u; lightIndex < parameters.lightCount; lightIndex++)
        {
            VividReGIRLightData light = _ReGIRLights[lightIndex];
            bool isAreaLight = light.lightType == VIVID_REGIR_LIGHT_TYPE_RECTANGLE
                || light.lightType == VIVID_REGIR_LIGHT_TYPE_TUBE;
            uint shapeSampleCount = isAreaLight ? 4u : 1u;
            float invShapeCount = 1.0f / float(shapeSampleCount);
            float3 lightSum = 0.0f;

            [loop]
            for (uint s = 0u; s < shapeSampleCount; s++)
            {
                float2 shapeSample;
                if (light.lightType == VIVID_REGIR_LIGHT_TYPE_RECTANGLE)
                {
                    float2 strata = float2(float(s & 1u), float(s >> 1u));
                    shapeSample = (strata + float2(CardSampledRand01(rng), CardSampledRand01(rng))) * 0.5f;
                }
                else if (light.lightType == VIVID_REGIR_LIGHT_TYPE_TUBE)
                {
                    shapeSample = float2((float(s) + CardSampledRand01(rng)) * 0.25f, 0.5f);
                }
                else
                {
                    shapeSample = 0.5f;
                }

                float3 lightSamplePosWS;
                float3 irradiance;
                if (EvaluateReGIRLocalLightSample(positionWS, normalWS, hit.normalWS, light, shapeSample, lightSamplePosWS, irradiance))
                {
                    float shadow = TraceCardLocalLightVisibility(
                        positionWS, hit.normalWS, lightSamplePosWS, light.shadowStrength, instance, hit.signMode);
                    lightSum += irradiance * shadow;
                }
            }

            totalDirect += albedo * (INV_PI * invShapeCount) * lightSum;
        }
        return totalDirect;
    }

    // Single-pass multi-iteration RIS backed by world-space Onion ReGIR (with PowerRIS fallback outside Onion).
    uint numIterations = max(_SdfSampledLightingLocalIterations, 1u);
    uint numCandidates = max(_SdfSampledLightingLocalCandidates, 1u);
    float invNumCandidates = 1.0f / float(numCandidates);
    float invNumIterations = 1.0f / float(numIterations);
    uint lightsPerCell = max(parameters.lightsPerCell, 1u);
    float jitterScale = _SdfSampledLightingUseReGIR != 0u ? VividReGIRGetJitterScale(parameters, positionWS) : 0.0f;

    float3 accumulatedDirect = 0.0f;
    [loop]
    for (uint iter = 0u; iter < numIterations; iter++)
    {
        int cellIndex = -1;
        if (_SdfSampledLightingUseReGIR != 0u)
        {
            float3 jitterOffset = (float3(CardSampledRand01(rng), CardSampledRand01(rng), CardSampledRand01(rng)) - 0.5f) * jitterScale;
            cellIndex = VividReGIRWorldPosToCellIndex(parameters, positionWS + jitterOffset);
        }

        uint presampledTileOffset = 0u;
        if (cellIndex < 0 && _SdfSampledLightingUseReGIR != 0u)
        {
            uint tileIndex = min(
                (uint)floor(CardSampledRand01(rng) * VIVID_CARD_SAMPLED_PRESAMPLED_TILE_COUNT),
                VIVID_CARD_SAMPLED_PRESAMPLED_TILE_COUNT - 1u);
            presampledTileOffset = tileIndex * VIVID_CARD_SAMPLED_PRESAMPLED_TILE_SIZE;
        }

        float weightSum = 0.0f;
        float selectedTarget = 0.0f;
        float3 selectedIrradiance = 0.0f;
        float3 selectedLightSamplePos = 0.0f;
        float selectedShadowStrength = 0.0f;

        [loop]
        for (uint cand = 0u; cand < numCandidates; cand++)
        {
            uint lightIndex = VIVID_REGIR_INVALID_LIGHT_INDEX;
            float invProposalPdf = 0.0f;
            float2 shapeSample = 0.5f;

            if (cellIndex >= 0)
            {
                uint slotInCell = min((uint)floor(CardSampledRand01(rng) * lightsPerCell), lightsPerCell - 1u);
                uint slotIndex = uint(cellIndex) * lightsPerCell + slotInCell;
                if (slotIndex < parameters.slotCount)
                {
                    VividReGIRReservoir res = _ReGIRReservoirs[slotIndex];
                    lightIndex = res.lightIndex;
                    invProposalPdf = res.weight;
                    shapeSample = res.shapeSample;
                }
            }
            else
            {
                DrawPresampledOrUniformCandidate(parameters, presampledTileOffset, rng, lightIndex, invProposalPdf, shapeSample);
            }

            if (lightIndex >= parameters.lightCount || invProposalPdf <= 0.0f)
                continue;

            VividReGIRLightData light = _ReGIRLights[lightIndex];
            float3 lightSamplePosWS;
            float3 irradiance;
            if (!EvaluateReGIRLocalLightSample(positionWS, normalWS, hit.normalWS, light, shapeSample, lightSamplePosWS, irradiance))
                continue;

            float targetPdf = CardSampledLuminance(albedo * irradiance);
            float risWeight = targetPdf * invProposalPdf * invNumCandidates;
            if (risWeight <= 0.0f)
                continue;

            weightSum += risWeight;
            if (CardSampledRand01(rng) * weightSum < risWeight)
            {
                selectedTarget = targetPdf;
                selectedIrradiance = irradiance;
                selectedLightSamplePos = lightSamplePosWS;
                selectedShadowStrength = light.shadowStrength;
            }
        }

        if (selectedTarget > 0.0f && weightSum > 0.0f)
        {
            float ucw = weightSum / selectedTarget;
            float shadow = TraceCardLocalLightVisibility(
                positionWS, hit.normalWS, selectedLightSamplePos, selectedShadowStrength, instance, hit.signMode);
            accumulatedDirect += albedo * (INV_PI * ucw * shadow) * selectedIrradiance;
        }
    }

    return accumulatedDirect * invNumIterations;
}

void RunSdfCardSampledDirectLighting(uint3 id)
{
    uint2 size = uint2(_SdfFinalLightingLayout.xy);
    if (any(id.xy >= size)) return;
    uint card = _SdfFinalLightingOrder[_SdfCardLightingFirstCard + id.z];
    uint instanceIndex, slot;
    LoadCardLightingWork(card, instanceIndex, slot);
    DiffuseSurface surface = _SdfDiffuseSurfaces[instanceIndex];
    uint slice, axis;
    float3 lower, extent;
    VividGetSdfCardProjection(surface, slot, slice, axis, lower, extent);
    float4 packedNormal = _SdfSurfaceNormalCoverage.Load(int4(id.xy, slice, 0));
    float3 direct = 0.0;
    if (packedNormal.a > 0.0f)
    {
        VividMeshSdfInstance instance = _MeshSdfInstances[instanceIndex];
        float3 r0 = instance.worldToLocal0.xyz;
        float3 r1 = instance.worldToLocal1.xyz;
        float3 r2 = instance.worldToLocal2.xyz;
        float4 albedoDepth = _SdfSurfaceAlbedoDepth.Load(int4(id.xy, slice, 0));
        float2 uv = (float2(id.xy) + 0.5f) / float2(size);
        float depth = albedoDepth.w;
        float3 relative = axis == 0u ? float3(depth, uv) : axis == 1u ? float3(uv.x, depth, uv.y) : float3(uv, depth);
        float3 delta = lower + relative * extent
            - float3(instance.worldToLocal0.w, instance.worldToLocal1.w, instance.worldToLocal2.w);
        float3 c0 = cross(r1, r2);
        float3 position = (c0 * delta.x + cross(r2, r0) * delta.y + cross(r0, r1) * delta.z) / dot(r0, c0);
        float3 normalLS = packedNormal.xyz * 2.0f - 1.0f;
        float3 normal = SafeNormalize(r0 * normalLS.x + r1 * normalLS.y + r2 * normalLS.z);
        VividMeshSdfHit hit = (VividMeshSdfHit)0;
        hit.normalWS = normal;
        hit.instanceIndex = instanceIndex;
        hit.signMode = _MeshSdfFields[instance.fieldIndex].signMode;
        float3 albedo = saturate(albedoDepth.rgb);
        surface.albedoAndValid.rgb = albedo;
        if (all(isfinite(position)))
        {
            direct = TraceCardDirectSunLighting(position, normal, hit, surface);
            direct += EvaluateCardSampledLocalLights(position, normal, hit, instance, albedo, card, id.xy);
        }
    }
    _SdfCardLightingOutput[FinalLightingCardOrigin(card) + id.xy] = float4(FiniteCardLighting(direct), 0.0f);
}
#endif
#endif

#endif
