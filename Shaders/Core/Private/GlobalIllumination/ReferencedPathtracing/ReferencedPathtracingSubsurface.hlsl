#ifndef VIVIDRP_REFERENCED_PATH_TRACING_SUBSURFACE_INCLUDED
#define VIVIDRP_REFERENCED_PATH_TRACING_SUBSURFACE_INCLUDED

// Burley normalized diffusion sampling for the Face SSS V1 hybrid. Radius is
// expressed in world units and the returned RGB weight includes the diffusion
// albedo and the channel-mixture correction.
struct ReferencedPathtracingSubsurfaceSample
{
    float radius;
    float3 weight;
};

float3 ReferencedPathtracingBurleyDiffusionShape(float3 albedo)
{
    float3 centeredAlbedo = saturate(albedo) - 0.33;
    float3 squared = centeredAlbedo * centeredAlbedo;
    return 3.5 + 100.0 * squared * squared;
}

bool ReferencedPathtracingSampleBurleySubsurface(
    float3 albedo,
    float3 meanFreePath,
    float randomValue,
    out ReferencedPathtracingSubsurfaceSample sample)
{
    sample = (ReferencedPathtracingSubsurfaceSample)0;
    albedo = saturate(albedo);
    float albedoSum = albedo.x + albedo.y + albedo.z;
    if (albedoSum <= 0.000001)
        return false;

    float3 channelProbability = albedo / albedoSum;
    float channelRandom = min(saturate(randomValue), 0.99999994);
    uint channelIndex;
    float channelMinimum;
    float selectedChannelProbability;
    if (channelRandom < channelProbability.x)
    {
        channelIndex = 0u;
        channelMinimum = 0.0;
        selectedChannelProbability = channelProbability.x;
    }
    else if (channelRandom
        < channelProbability.x + channelProbability.y)
    {
        channelIndex = 1u;
        channelMinimum = channelProbability.x;
        selectedChannelProbability = channelProbability.y;
    }
    else
    {
        channelIndex = 2u;
        channelMinimum = channelProbability.x + channelProbability.y;
        selectedChannelProbability = channelProbability.z;
    }

    float radialRandom = saturate(
        (channelRandom - channelMinimum)
        / max(selectedChannelProbability, 0.000001));
    float3 inverseMeanFreePath = rcp(max(meanFreePath, 0.000001));
    float3 diffusionRate = inverseMeanFreePath
        * ReferencedPathtracingBurleyDiffusionShape(albedo);
    float selectedDiffusionRate = diffusionRate[channelIndex];
    if (radialRandom < 0.25)
    {
        float exponentialRandom = min(radialRandom * 4.0, 0.99999994);
        sample.radius = -log(max(1.0 - exponentialRandom, 0.00000006))
            / selectedDiffusionRate;
    }
    else
    {
        float exponentialRandom = min(
            (radialRandom - 0.25) * (1.0 / 0.75),
            0.99999994);
        sample.radius = -3.0
            * log(max(1.0 - exponentialRandom, 0.00000006))
            / selectedDiffusionRate;
    }

    float3 exponential = exp(-sample.radius * diffusionRate);
    float3 radialPdf = 0.25 * diffusionRate
        * (exponential + exp(-sample.radius * diffusionRate / 3.0));
    float mixturePdf = dot(channelProbability, radialPdf);
    if (mixturePdf <= 0.00000001
        || any(isnan(radialPdf))
        || any(isinf(radialPdf)))
    {
        sample = (ReferencedPathtracingSubsurfaceSample)0;
        return false;
    }

    sample.weight = albedo * radialPdf / mixturePdf;
    return all(sample.weight >= 0.0)
        && !any(isnan(sample.weight))
        && !any(isinf(sample.weight));
}

void ReferencedPathtracingBuildSubsurfaceProjectionFrame(
    float3 normalWS,
    float3 preferredTangentWS,
    out float3 tangentWS,
    out float3 bitangentWS)
{
    normalWS = normalize(normalWS);
    tangentWS = preferredTangentWS
        - normalWS * dot(preferredTangentWS, normalWS);
    float tangentLengthSquared = dot(tangentWS, tangentWS);
    if (tangentLengthSquared <= 0.00000001)
    {
        float3 fallbackAxis = abs(normalWS.z) < 0.999
            ? float3(0.0, 0.0, 1.0)
            : float3(0.0, 1.0, 0.0);
        tangentWS = cross(fallbackAxis, normalWS);
        tangentLengthSquared = dot(tangentWS, tangentWS);
    }
    tangentWS *= rsqrt(max(tangentLengthSquared, 0.00000001));
    bitangentWS = normalize(cross(normalWS, tangentWS));
}

#endif
