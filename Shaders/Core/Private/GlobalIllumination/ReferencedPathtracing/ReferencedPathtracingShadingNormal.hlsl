#ifndef VIVIDRP_REFERENCED_PATH_TRACING_SHADING_NORMAL_INCLUDED
#define VIVIDRP_REFERENCED_PATH_TRACING_SHADING_NORMAL_INCLUDED

// Phase 4.9 contract for the currently supported opaque reflection subset.
// The geometric normal owns surface sidedness and ray offsets. The adjusted
// shading normal owns OpenPBR evaluation and sampling.
static const float kReferencedShadingNormalViewCosineThreshold = 0.1;
static const float kReferencedShadingNormalReflectionHorizonEpsilon = 0.001;
static const float kReferencedShadingNormalDirectionEpsilon = 0.000001;

float3 ReferencedPathtracingComputeConsistentShadingNormal(
    float3 viewDirectionWS,
    float3 geometricNormalWS,
    float3 shadingNormalWS)
{
    float3 viewDirection = SafeNormalize(viewDirectionWS);
    float3 geometricNormal = SafeNormalize(geometricNormalWS);
    float3 shadingNormal = SafeNormalize(shadingNormalWS);

    if (dot(shadingNormal, geometricNormal) < 0.0)
        shadingNormal = -shadingNormal;

    // RTXPT-style grazing blend prevents OpenPBR from classifying an opaque
    // front-face hit as back-facing after smooth-normal interpolation or a
    // normal-map perturbation.
    float viewCosine = dot(viewDirection, shadingNormal);
    if (viewCosine <= kReferencedShadingNormalViewCosineThreshold)
    {
        float blend = saturate(
            max(viewCosine, 0.0)
            / kReferencedShadingNormalViewCosineThreshold);
        shadingNormal = SafeNormalize(
            lerp(geometricNormal, shadingNormal, blend));
    }

    // Unreal/HDRP-style view-dependent clipping. If perfect reflection about
    // the shading normal crosses the geometric horizon, project it back just
    // above that horizon and reconstruct the corresponding half vector.
    float3 reflectedDirection = reflect(-viewDirection, shadingNormal);
    float reflectedGeometricCosine =
        dot(reflectedDirection, geometricNormal);
    if (reflectedGeometricCosine
        < kReferencedShadingNormalReflectionHorizonEpsilon)
    {
        reflectedDirection = SafeNormalize(
            reflectedDirection
            - reflectedGeometricCosine * geometricNormal
            + kReferencedShadingNormalReflectionHorizonEpsilon
                * geometricNormal);
        float3 consistentNormal = viewDirection + reflectedDirection;
        if (dot(consistentNormal, consistentNormal)
            > kReferencedShadingNormalDirectionEpsilon)
        {
            shadingNormal = SafeNormalize(consistentNormal);
        }
        else
        {
            shadingNormal = geometricNormal;
        }
    }

    if (dot(shadingNormal, viewDirection)
            <= kReferencedShadingNormalDirectionEpsilon
        || dot(shadingNormal, geometricNormal)
            <= kReferencedShadingNormalDirectionEpsilon)
    {
        return geometricNormal;
    }

    return shadingNormal;
}

bool ReferencedPathtracingIsValidOpaqueReflectionDirection(
    float3 directionWS,
    float3 geometricNormalWS,
    float3 shadingNormalWS)
{
    return dot(directionWS, geometricNormalWS)
            > kReferencedShadingNormalDirectionEpsilon
        && dot(directionWS, shadingNormalWS)
            > kReferencedShadingNormalDirectionEpsilon;
}

float ReferencedPathtracingEvaluateDiffuseShadowTerminator(
    float3 directionWS,
    float3 shadingNormalWS,
    float3 interpolatedNormalWS)
{
    // Imageworks' microfacet shadow-terminator softening, as used by Unreal's
    // path tracer. This modifies the diffuse value/weight only; the sampling
    // density remains the OpenPBR density.
    float3 direction = SafeNormalize(directionWS);
    float3 shadingNormal = SafeNormalize(shadingNormalWS);
    float3 interpolatedNormal = SafeNormalize(interpolatedNormalWS);
    if (dot(interpolatedNormal, shadingNormal) < 0.0)
        interpolatedNormal = -interpolatedNormal;

    float normalCosine =
        saturate(abs(dot(interpolatedNormal, shadingNormal)));
    float tangentSquared =
        (1.0 - normalCosine * normalCosine)
        / (normalCosine * normalCosine + 0.000001);
    float alphaSquared = saturate(0.125 * tangentSquared);
    float lightCosine = saturate(dot(interpolatedNormal, direction));
    if (lightCosine <= 0.0)
        return 0.0;

    float lightTangentSquared =
        (1.0 - lightCosine * lightCosine)
        / (lightCosine * lightCosine + 0.000001);
    return saturate(
        2.0
        / (1.0 + sqrt(1.0 + alphaSquared * lightTangentSquared)));
}

#endif
