TEXTURECUBE(_SkyTexture);
float4 SampleSkyTexture(float3 texCoord, float lod, int sliceIndex = 0)
{
    return SAMPLE_TEXTURECUBE_LOD(_SkyTexture, sampler_TrilinearClamp, texCoord, lod);
}


float3 SampleSkyEnvironment(float3 reflectVector, float perceptualRoughness)
{
    float mip = PerceptualRoughnessToMipmapLevel(perceptualRoughness);
    return SampleSkyTexture(reflectVector, mip).rgb;
}


