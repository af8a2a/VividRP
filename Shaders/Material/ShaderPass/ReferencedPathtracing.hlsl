#ifndef VIVIDRP_REFERENCED_PATH_TRACING_MATERIAL_PASS_INCLUDED
#define VIVIDRP_REFERENCED_PATH_TRACING_MATERIAL_PASS_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GlobalIllumination/ReferencedPathtracing/ReferencedPathtracingCommon.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/ReGIR.hlsl"

#define VIVIDRP_INDIRECT_DIFFUSE_DEFINE_RAYTRACING_SHADERS 0
#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/IndirectDiffuse.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/StandardLitOpenPBRAdapter.hlsl"

StructuredBuffer<VividReGIRLightData> _ReGIRLights;
StructuredBuffer<VividReGIRParameters> _ReGIRParameters;
StructuredBuffer<VividReGIRReservoir> _ReGIRReservoirs;

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

bool VividReferencedPathtracingResolveRectangleBarnDoor(
    VividReGIRLightData light,
    float3 positionWS,
    out float3 lightCenterWS,
    out float2 lightSize,
    out float3 lightRightWS,
    out float3 lightUpWS,
    out float3 lightForwardWS)
{
    lightCenterWS = light.positionWS;
    lightSize = max(light.areaSize, 0.0);
    lightRightWS = 0.0;
    lightUpWS = 0.0;
    lightForwardWS = 0.0;

    float rightLengthSquared = dot(light.rightWS, light.rightWS);
    float upLengthSquared = dot(light.upWS, light.upWS);
    float forwardLengthSquared = dot(light.directionWS, light.directionWS);
    if (light.lightType != VIVID_REGIR_LIGHT_TYPE_RECTANGLE
        || any(lightSize <= 1e-6)
        || rightLengthSquared <= 1e-8
        || upLengthSquared <= 1e-8
        || forwardLengthSquared <= 1e-8)
    {
        return false;
    }

    lightRightWS = light.rightWS * rsqrt(rightLengthSquared);
    lightUpWS = light.upWS * rsqrt(upLengthSquared);
    lightForwardWS = light.directionWS * rsqrt(forwardLengthSquared);

    // Match HDRP/Vivid raster semantics: near-90-degree or <=5 cm doors are treated as open.
    float cosBarnDoorAngle = saturate(light.cosBarnDoorAngle);
    float barnDoorLength = max(light.barnDoorLength, 0.0);
    if (cosBarnDoorAngle <= 0.017 || barnDoorLength <= 0.05)
        return true;

    float2 halfSize = 0.5 * lightSize;
    float3 lightRelativePosition = positionWS - light.positionWS;
    float3 pointLS = float3(
        dot(lightRelativePosition, lightRightWS),
        dot(lightRelativePosition, lightUpWS),
        dot(lightRelativePosition, lightForwardWS));

    float maxDepth = cosBarnDoorAngle * barnDoorLength;
    float pointDepth = min(pointLS.z, maxDepth);
    float pointDepthRatio = pointDepth / max(maxDepth, 1e-5);
    float sinTheta = sqrt(saturate(1.0 - cosBarnDoorAngle * cosBarnDoorAngle));
    float barnDoorProjection = sinTheta * barnDoorLength * pointDepthRatio;

    float2 pointSign = sign(pointLS.xy);
    pointLS.xy = pointSign
        * max(abs(pointLS.xy), halfSize + barnDoorProjection.xx);

    float3 closestLightCorner = float3(
        pointSign.x * (halfSize.x + barnDoorProjection),
        pointSign.y * (halfSize.y + barnDoorProjection),
        pointDepth);
    float3 pointProjection = pointLS - closestLightCorner;
    float cosPhi = max(0.0, pointProjection.z);
    float2 tanPhi = cosPhi > 0.001
        ? abs(pointProjection.xy) / cosPhi
        : float2(99999.0, 99999.0);
    float2 projectionDistance = pointDepth * tanPhi;

    float2 horizontalBounds = float2(-halfSize.x, halfSize.x);
    float2 verticalBounds = float2(-halfSize.y, halfSize.y);
    horizontalBounds += (projectionDistance.x - barnDoorProjection)
        * float2(max(0.0, -pointSign.x), -max(0.0, pointSign.x));
    verticalBounds += (projectionDistance.y - barnDoorProjection)
        * float2(max(0.0, -pointSign.y), -max(0.0, pointSign.y));
    horizontalBounds = clamp(horizontalBounds, -halfSize.x, halfSize.x);
    verticalBounds = clamp(verticalBounds, -halfSize.y, halfSize.y);

    float2 lightCenterOffset = 0.5 * float2(
        horizontalBounds.x + horizontalBounds.y,
        verticalBounds.x + verticalBounds.y);
    lightSize = max(
        float2(
            horizontalBounds.y - horizontalBounds.x,
            verticalBounds.y - verticalBounds.x),
        0.0);
    lightCenterWS += lightRightWS * lightCenterOffset.x
        + lightUpWS * lightCenterOffset.y;
    return all(lightSize > 1e-6);
}

bool VividReferencedPathtracingSelectReGIRLight(
    float3 positionWS,
    float3 randomSample,
    out VividReGIRLightData selectedLight,
    out float sampleWeight,
    out float2 shapeSample)
{
    selectedLight = (VividReGIRLightData)0;
    sampleWeight = 0.0;
    shapeSample = saturate(randomSample.yz);
    if (_ReferencedReGIREnabled == 0)
        return false;

    VividReGIRParameters parameters = _ReGIRParameters[0];
    if ((parameters.mode != VIVID_REGIR_MODE_GRID
            && parameters.mode != VIVID_REGIR_MODE_ONION)
        || parameters.lightCount == 0u)
    {
        return false;
    }

    uint lightIndex = VIVID_REGIR_INVALID_LIGHT_INDEX;
    float fallbackRandom = min(saturate(randomSample.x), 0.99999994);
    int cellIndex = VividReGIRWorldPosToCellIndex(parameters, positionWS);
    if (cellIndex >= 0 && parameters.lightsPerCell > 0u)
    {
        float slotSample = fallbackRandom * parameters.lightsPerCell;
        uint lightInCell = min(
            (uint)floor(slotSample),
            parameters.lightsPerCell - 1u);
        // Conditioned on lightInCell, frac(slotSample) remains uniform in [0, 1). Use it
        // for the fallback so an invalid slot cannot skew the global-light distribution.
        fallbackRandom = frac(slotSample);
        uint reservoirIndex = uint(cellIndex) * parameters.lightsPerCell + lightInCell;
        if (reservoirIndex < parameters.slotCount)
        {
            VividReGIRReservoir reservoir = _ReGIRReservoirs[reservoirIndex];
            if (reservoir.lightIndex < parameters.lightCount
                && reservoir.weight > 0.0
                && VividReferencedPathtracingIsFinite(reservoir.weight))
            {
                lightIndex = reservoir.lightIndex;
                // Each slot is an independent, complete RIS estimator. Selecting one uniformly
                // therefore needs neither a lightsPerCell multiplier nor divisor.
                sampleWeight = reservoir.weight;
                shapeSample = saturate(reservoir.shapeSample);
            }
        }
    }

    // A global uniform estimator preserves support outside the ReGIR volume and when a reservoir
    // is invalid. It also makes grid-boundary behavior deterministic and easy to validate.
    if (lightIndex == VIVID_REGIR_INVALID_LIGHT_INDEX)
    {
        lightIndex = min(
            (uint)floor(fallbackRandom * parameters.lightCount),
            parameters.lightCount - 1u);
        sampleWeight = float(parameters.lightCount);
    }

    selectedLight = _ReGIRLights[lightIndex];
    return sampleWeight > 0.0
        && VividReferencedPathtracingIsFinite(sampleWeight)
        && VividReferencedPathtracingIsFinite(shapeSample);
}

bool VividReferencedPathtracingEvaluateReGIRPunctual(
    float3 positionWS,
    VividReGIRLightData light,
    float sampleWeight,
    out float3 lightDirectionWS,
    out float lightDistance,
    out float3 weightedIncidentRadiance)
{
    lightDirectionWS = 0.0;
    lightDistance = 0.0;
    weightedIncidentRadiance = 0.0;

    float3 surfaceToLight = light.positionWS - positionWS;
    float distanceSquared = dot(surfaceToLight, surfaceToLight);
    if (distanceSquared <= 1e-8)
        return false;

    float inverseGeometricDistance = rsqrt(distanceSquared);
    lightDistance = distanceSquared * inverseGeometricDistance;
    lightDirectionWS = surfaceToLight * inverseGeometricDistance;

    // VividRP punctual RGB is luminous intensity in candela. The attenuation converts it to
    // illuminance at the shading point and matches the raster lighting path, including the
    // finite-range window, fill-light radius and spot-cone smoothing.
    float inverseModifiedDistance = rsqrt(
        distanceSquared + max(light.shapeRadius * light.shapeRadius, 0.0));
    float attenuation = min(inverseModifiedDistance, 1.0 / PUNCTUAL_LIGHT_THRESHOLD);
    attenuation *= DistanceWindowing(
        distanceSquared,
        rcp(max(light.range * light.range, 1e-6)),
        1.0);

    if (light.lightType == VIVID_REGIR_LIGHT_TYPE_SPOT)
    {
        float directionLengthSquared = dot(light.directionWS, light.directionWS);
        if (directionLengthSquared <= 1e-8)
            return false;

        float3 spotDirectionWS = light.directionWS * rsqrt(directionLengthSquared);
        attenuation *= AngleAttenuation(
            dot(spotDirectionWS, -lightDirectionWS),
            light.angleScale,
            light.angleOffset);
    }

    attenuation *= attenuation;
    weightedIncidentRadiance = max(light.color, 0.0) * attenuation * sampleWeight;
    return attenuation > 0.0
        && VividReferencedPathtracingIsFinite(weightedIncidentRadiance)
        && any(weightedIncidentRadiance > 0.0);
}

bool VividReferencedPathtracingEvaluateReGIRArea(
    float3 positionWS,
    VividReGIRLightData light,
    float sampleWeight,
    float2 shapeSample,
    out float3 lightDirectionWS,
    out float lightDistance,
    out float3 weightedIncidentRadiance)
{
    lightDirectionWS = 0.0;
    lightDistance = 0.0;
    weightedIncidentRadiance = 0.0;

    float3 lightSamplePositionWS = 0.0;
    float shapeJacobian = 0.0;
    if (light.lightType == VIVID_REGIR_LIGHT_TYPE_RECTANGLE)
    {
        float3 clippedLightCenterWS;
        float2 clippedLightSize;
        float3 rightWS;
        float3 upWS;
        float3 forwardWS;
        if (!VividReferencedPathtracingResolveRectangleBarnDoor(
                light,
                positionWS,
                clippedLightCenterWS,
                clippedLightSize,
                rightWS,
                upWS,
                forwardWS))
        {
            return false;
        }

        float2 centeredSample = saturate(shapeSample) - 0.5;
        lightSamplePositionWS = clippedLightCenterWS
            + centeredSample.x * clippedLightSize.x * rightWS
            + centeredSample.y * clippedLightSize.y * upWS;

        float3 surfaceToLight = lightSamplePositionWS - positionWS;
        float distanceSquared = dot(surfaceToLight, surfaceToLight);
        if (distanceSquared <= 1e-8)
            return false;

        float inverseDistance = rsqrt(distanceSquared);
        lightDistance = distanceSquared * inverseDistance;
        lightDirectionWS = surfaceToLight * inverseDistance;
        float lightFacingCosine = saturate(dot(-lightDirectionWS, forwardWS));
        if (lightFacingCosine <= 0.0)
            return false;

        // The barn door maps the presampled random pair to the point-dependent visible
        // sub-rectangle. Its clipped area is therefore the exact reciprocal continuous PDF.
        shapeJacobian = lightFacingCosine
            * (clippedLightSize.x * clippedLightSize.y)
            / distanceSquared;
    }
    else if (light.lightType == VIVID_REGIR_LIGHT_TYPE_TUBE)
    {
        float length = max(light.areaSize.x, 0.0);
        float rightLengthSquared = dot(light.rightWS, light.rightWS);
        if (length <= 1e-6 || rightLengthSquared <= 1e-8)
            return false;

        float3 rightWS = light.rightWS * rsqrt(rightLengthSquared);
        float centeredSample = saturate(shapeSample.x) - 0.5;
        lightSamplePositionWS = light.positionWS + centeredSample * length * rightWS;

        float3 surfaceToLight = lightSamplePositionWS - positionWS;
        float distanceSquared = dot(surfaceToLight, surfaceToLight);
        if (distanceSquared <= 1e-8)
            return false;

        float inverseDistance = rsqrt(distanceSquared);
        lightDistance = distanceSquared * inverseDistance;
        lightDirectionWS = surfaceToLight * inverseDistance;
        float axialCosine = abs(dot(lightDirectionWS, rightWS));
        float radialCosine = sqrt(saturate(1.0 - axialCosine * axialCosine));
        if (radialCosine <= 0.0)
            return false;

        // Vivid/HDRP models tube lights as a zero-radius line. Integrating the visible half
        // cylinder contributes the factor 2, while uniform line sampling contributes length.
        shapeJacobian = 2.0 * radialCosine * length / distanceSquared;
    }
    else
    {
        return false;
    }

    float rangeWindow = SmoothDistanceWindowing(
        lightDistance * lightDistance,
        rcp(max(light.range * light.range, 1e-6)),
        1.0);
    weightedIncidentRadiance =
        max(light.color, 0.0) * rangeWindow * shapeJacobian * sampleWeight;
    return rangeWindow > 0.0
        && shapeJacobian > 0.0
        && VividReferencedPathtracingIsFinite(weightedIncidentRadiance)
        && any(weightedIncidentRadiance > 0.0);
}

bool VividReferencedPathtracingEvaluateReGIRLocalLight(
    float3 positionWS,
    float3 randomSample,
    out float3 lightDirectionWS,
    out float lightDistance,
    out float3 weightedIncidentRadiance)
{
    lightDirectionWS = 0.0;
    lightDistance = 0.0;
    weightedIncidentRadiance = 0.0;

    VividReGIRLightData light;
    float sampleWeight;
    float2 shapeSample;
    if (!VividReferencedPathtracingSelectReGIRLight(
            positionWS,
            randomSample,
            light,
            sampleWeight,
            shapeSample))
    {
        return false;
    }

    if (light.lightType == VIVID_REGIR_LIGHT_TYPE_POINT
        || light.lightType == VIVID_REGIR_LIGHT_TYPE_SPOT)
    {
        return VividReferencedPathtracingEvaluateReGIRPunctual(
            positionWS,
            light,
            sampleWeight,
            lightDirectionWS,
            lightDistance,
            weightedIncidentRadiance);
    }

    return VividReferencedPathtracingEvaluateReGIRArea(
        positionWS,
        light,
        sampleWeight,
        shapeSample,
        lightDirectionWS,
        lightDistance,
        weightedIncidentRadiance);
}

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
    VividReferencedPathtracingMaterial material = VividReferencedPathtracingResolveStandardLitOpenPBR(
        geometry,
        textureBaseLambda,
        baseTextureLod,
        normalTextureLod);

    float3 viewDirectionWS = normalize(-WorldRayDirection());
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
    payload.mainLightDiffuseBsdf = 0.0;
    payload.mainLightSpecularBsdf = 0.0;
    payload.mainLightDirectionWS = 0.0;
    payload.mainLightLightPdf = 0.0;
    payload.mainLightBsdfPdf = 0.0;
    payload.mainLightIsDelta = 1u;
    payload.reGIRLocalDiffuseRadiance = 0.0;
    payload.reGIRLocalSpecularRadiance = 0.0;
    payload.reGIRLocalDirectionWS = 0.0;
    payload.reGIRLocalDistance = 0.0;
    payload.environmentDirectDiffuseRadiance = 0.0;
    payload.environmentDirectSpecularRadiance = 0.0;
    payload.environmentDirectionWS = 0.0;
    payload.environmentLightPdf = 0.0;
    payload.environmentBsdfPdf = 0.0;
    payload.nextDirectionWS = 0.0;
    payload.nextThroughputWeight = 0.0;
    payload.nextPdf = 0.0;
    payload.linearRoughness = material.openPbrInputs.specular_roughness;
    payload.hitDistance = geometry.hitDistance;
    payload.nextLobeClass = 0u;

    float3 mainLightDirectionWS;
    float mainLightLightPdf;
    uint mainLightIsDelta;
    ReferencedPathtracingSampleMainDirectionalLight(
        normalize(_ReferencedMainLightDirectionWS.xyz),
        payload.mainLightRandom,
        mainLightDirectionWS,
        mainLightLightPdf,
        mainLightIsDelta);
    if (dot(mainLightDirectionWS, geometry.faceNormalWS) > 0.0
        && any(_ReferencedMainLightColor.rgb > 0.0))
    {
        // openpbr_eval returns f(wo, wi) * abs(NdotL). The ray-generation stage multiplies
        // this response by _ReferencedMainLightColor, whose RGB value is illuminance in lux.
        OpenPBR_DiffuseSpecular mainLightResponse = openpbr_eval(
            preparedBsdf,
            mainLightDirectionWS);
        float mainLightBsdfPdf = openpbr_pdf(preparedBsdf, mainLightDirectionWS);
        float3 mainLightDiffuseBsdf = openpbr_extract_diffuse_from_diffuse_specular(mainLightResponse);
        float3 mainLightSpecularBsdf = openpbr_extract_specular_from_diffuse_specular(mainLightResponse);
        if (VividReferencedPathtracingIsFinite(mainLightDiffuseBsdf)
            && VividReferencedPathtracingIsFinite(mainLightSpecularBsdf)
            && VividReferencedPathtracingIsFinite(mainLightBsdfPdf)
            && mainLightBsdfPdf >= 0.0)
        {
            payload.mainLightDiffuseBsdf = max(mainLightDiffuseBsdf, 0.0);
            payload.mainLightSpecularBsdf = max(mainLightSpecularBsdf, 0.0);
            payload.mainLightDirectionWS = mainLightDirectionWS;
            payload.mainLightLightPdf = mainLightLightPdf;
            payload.mainLightBsdfPdf = mainLightBsdfPdf;
            payload.mainLightIsDelta = mainLightIsDelta;
        }
    }

    float3 reGIRLocalDirectionWS;
    float reGIRLocalDistance;
    float3 reGIRWeightedIncidentRadiance;
    if (VividReferencedPathtracingEvaluateReGIRLocalLight(
            geometry.positionWS,
            payload.directLightRandom,
            reGIRLocalDirectionWS,
            reGIRLocalDistance,
            reGIRWeightedIncidentRadiance)
        && dot(reGIRLocalDirectionWS, geometry.faceNormalWS) > 0.0)
    {
        OpenPBR_DiffuseSpecular localLightResponse = openpbr_eval(
            preparedBsdf,
            reGIRLocalDirectionWS);
        float3 localDiffuseBsdf =
            openpbr_extract_diffuse_from_diffuse_specular(localLightResponse);
        float3 localSpecularBsdf =
            openpbr_extract_specular_from_diffuse_specular(localLightResponse);
        if (VividReferencedPathtracingIsFinite(localDiffuseBsdf)
            && VividReferencedPathtracingIsFinite(localSpecularBsdf))
        {
            payload.reGIRLocalDiffuseRadiance =
                max(localDiffuseBsdf, 0.0) * reGIRWeightedIncidentRadiance;
            payload.reGIRLocalSpecularRadiance =
                max(localSpecularBsdf, 0.0) * reGIRWeightedIncidentRadiance;
            payload.reGIRLocalDirectionWS = reGIRLocalDirectionWS;
            payload.reGIRLocalDistance = reGIRLocalDistance;
        }
    }

    float3 environmentDirectionWS;
    float3 environmentRadiance;
    float environmentLightPdf;
    // The returned PDF already contains the discrete environment-light selection factor.
    if (ReferencedPathtracingSampleEnvironmentLight(
            payload.environmentRandom,
            environmentDirectionWS,
            environmentRadiance,
            environmentLightPdf)
        && dot(environmentDirectionWS, geometry.faceNormalWS) > 0.0)
    {
        OpenPBR_DiffuseSpecular environmentResponse = openpbr_eval(
            preparedBsdf,
            environmentDirectionWS);
        float environmentBsdfPdf = openpbr_pdf(
            preparedBsdf,
            environmentDirectionWS);
        float3 environmentDiffuseBsdf =
            openpbr_extract_diffuse_from_diffuse_specular(environmentResponse);
        float3 environmentSpecularBsdf =
            openpbr_extract_specular_from_diffuse_specular(environmentResponse);
        if (VividReferencedPathtracingIsFinite(environmentRadiance)
            && VividReferencedPathtracingIsFinite(environmentDiffuseBsdf)
            && VividReferencedPathtracingIsFinite(environmentSpecularBsdf)
            && VividReferencedPathtracingIsFinite(environmentLightPdf)
            && VividReferencedPathtracingIsFinite(environmentBsdfPdf)
            && environmentLightPdf > 0.0
            && environmentBsdfPdf >= 0.0)
        {
            float3 weightedEnvironmentRadiance =
                max(environmentRadiance, 0.0) / environmentLightPdf;
            payload.environmentDirectDiffuseRadiance =
                max(environmentDiffuseBsdf, 0.0)
                * weightedEnvironmentRadiance;
            payload.environmentDirectSpecularRadiance =
                max(environmentSpecularBsdf, 0.0)
                * weightedEnvironmentRadiance;
            payload.environmentDirectionWS = normalize(environmentDirectionWS);
            payload.environmentLightPdf = environmentLightPdf;
            payload.environmentBsdfPdf = environmentBsdfPdf;
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
        && VividReferencedPathtracingIsFinite(sampledDirectionWS))
    {
        float3 nextThroughputWeight = openpbr_get_sum_of_diffuse_specular(sampledWeight);
        if (VividReferencedPathtracingIsFinite(nextThroughputWeight)
            && dot(sampledDirectionWS, geometry.faceNormalWS) > 0.000001)
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
