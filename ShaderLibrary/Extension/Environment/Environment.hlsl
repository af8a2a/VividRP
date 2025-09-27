#ifndef VIVID_ENVIRONMENT_INCLUDED
#define VIVID_ENVIRONMENT_INCLUDED
TEXTURECUBE(_SkyTexture);
StructuredBuffer<float4> _AmbientProbeData;

float4 SampleSkyTexture(float3 texCoord, float lod, int sliceIndex = 0)
{
    return SAMPLE_TEXTURECUBE_LOD(_SkyTexture, sampler_TrilinearClamp, texCoord, lod);
}


float3 SampleSkyEnvironment(float3 reflectVector, float perceptualRoughness)
{
    float mip = PerceptualRoughnessToMipmapLevel(perceptualRoughness);
    return SampleSkyTexture(reflectVector, mip).rgb;
}

float3 EvaluateEnvProbes(PositionInputs posInput, float3 reflectDirWS, float perceptualRoughness, inout float hierarchyWeight)
{
    float3 irradiance = 0;
    float totalWeight = 0;
    float mip = PerceptualRoughnessToMipmapLevel(perceptualRoughness);

    uint lightCategory = LIGHTCATEGORY_ENV;
    uint lightStart;
    uint lightCount;
    GetCountAndStart(posInput, lightCategory, lightStart, lightCount);
    uint v_lightListOffset = 0;
    uint v_lightIdx = lightStart;

    if (lightCount > 0) // avoid 0 iteration warning.
    {
        while (v_lightListOffset < lightCount)
        {
            v_lightIdx = FetchIndex(lightStart, v_lightListOffset);
            if (v_lightIdx == -1)
                break;

            uint probeIndex = v_lightIdx;

            float weight = CalculateProbeWeight(posInput.positionWS, urp_ReflProbes_BoxMin[probeIndex], urp_ReflProbes_BoxMax[probeIndex]);
            weight = min(weight, 1.0f - totalWeight);

            uint maxMip = (uint)abs(urp_ReflProbes_ProbePosition[probeIndex].w) - 1;
            half probeMip = min(mip, maxMip);
            float2 uv = saturate(PackNormalOctQuadEncode(reflectDirWS) * 0.5 + 0.5);

            float mip0 = floor(probeMip);
            float mip1 = mip0 + 1;
            float mipBlend = probeMip - mip0;
            float4 scaleOffset0 = urp_ReflProbes_MipScaleOffset[probeIndex * 7 + (uint)mip0];
            float4 scaleOffset1 = urp_ReflProbes_MipScaleOffset[probeIndex * 7 + (uint)mip1];

            float3 irradiance0 = SAMPLE_TEXTURE2D_LOD(urp_ReflProbes_Atlas, sampler_LinearClamp, uv * scaleOffset0.xy + scaleOffset0.zw, 0.0).xyz;
            float3 irradiance1 = SAMPLE_TEXTURE2D_LOD(urp_ReflProbes_Atlas, sampler_LinearClamp, uv * scaleOffset1.xy + scaleOffset1.zw, 0.0).xyz;
            irradiance += weight * lerp(irradiance0, irradiance1, mipBlend);
            totalWeight += weight;

            v_lightListOffset++;
        }
    }

    // Apply the main lobe weight and update main reflection hierarchyWeight:
    UpdateLightingHierarchyWeights(hierarchyWeight, totalWeight);
    // irradiance *= totalWeight; // We have applied in the loop.

    return irradiance;
}
#endif