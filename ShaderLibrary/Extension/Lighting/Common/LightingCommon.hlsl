#ifndef LIGHT_COMMON_INCLUDED
#define LIGHT_COMMON_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"

struct DirectLighting
{
    float3 diffuse;
    float3 specular;
};

struct IndirectLighting
{
    float3 specularReflected;
    float3 specularTransmitted;
};

struct AggregateLighting
{
    DirectLighting direct;
    IndirectLighting indirect;
};

void AccumulateDirectLighting(DirectLighting src, inout AggregateLighting dst)
{
    dst.direct.diffuse += src.diffuse;
    dst.direct.specular += src.specular;
}

void AccumulateIndirectLighting(IndirectLighting src, inout AggregateLighting dst)
{
    dst.indirect.specularReflected += src.specularReflected;
    dst.indirect.specularTransmitted += src.specularTransmitted;
}


//-----------------------------------------------------------------------------
// Ambient occlusion helper
//-----------------------------------------------------------------------------
// Ambient occlusion
struct VividAmbientOcclusionFactor
{
    float3 indirectAmbientOcclusion;
    float3 directAmbientOcclusion;
    float3 indirectSpecularOcclusion;
    float3 directSpecularOcclusion;

    // Get screen space ambient occlusion only:
    static float GetScreenSpaceDiffuseOcclusion(float2 positionSS)
    {
        #if defined(_SCREEN_SPACE_OCCLUSION) && !defined(_SURFACE_TYPE_TRANSPARENT)
        float indirectAmbientOcclusion = LOAD_TEXTURE2D_X(_ScreenSpaceOcclusionTexture, positionSS).x;
        #else
        float indirectAmbientOcclusion = 1.0;
        #endif

        return indirectAmbientOcclusion;
    }

    static float3 GetScreenSpaceAmbientOcclusion(float2 positionSS)
    {
        float indirectAmbientOcclusion = GetScreenSpaceDiffuseOcclusion(positionSS);
        return lerp(_AmbientOcclusionParam.rgb, float3(1.0, 1.0, 1.0), indirectAmbientOcclusion);
    }

    static VividAmbientOcclusionFactor GetScreenSpaceAmbientOcclusion(float2 positionSS, float NdotV, float perceptualRoughness, float ambientOcclusionFromData,
                                                                      float specularOcclusionFromData)
    {
        float indirectAmbientOcclusion = GetScreenSpaceDiffuseOcclusion(positionSS);
        float directAmbientOcclusion = lerp(1.0, indirectAmbientOcclusion, _AmbientOcclusionParam.w);

        float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
        float indirectSpecularOcclusion = GetSpecularOcclusionFromAmbientOcclusion(ClampNdotV(NdotV), indirectAmbientOcclusion, roughness);
        float directSpecularOcclusion = lerp(1.0, indirectSpecularOcclusion, _AmbientOcclusionParam.w);

        VividAmbientOcclusionFactor ambientOcclusion;

        ambientOcclusion.indirectSpecularOcclusion = lerp(_AmbientOcclusionParam.rgb, float3(1.0, 1.0, 1.0),
                                                          min(specularOcclusionFromData, indirectSpecularOcclusion));
        ambientOcclusion.indirectAmbientOcclusion = lerp(_AmbientOcclusionParam.rgb, float3(1.0, 1.0, 1.0),
                                                         min(ambientOcclusionFromData, directSpecularOcclusion));
        ambientOcclusion.directSpecularOcclusion = lerp(_AmbientOcclusionParam.rgb, float3(1.0, 1.0, 1.0), directAmbientOcclusion);
        ambientOcclusion.directAmbientOcclusion = lerp(_AmbientOcclusionParam.rgb, float3(1.0, 1.0, 1.0), directAmbientOcclusion);
        return ambientOcclusion;
    }

    // Use GTAOMultiBounce approximation for ambient occlusion (allow to get a tint from the diffuseColor)
    static VividAmbientOcclusionFactor GetScreenSpaceAmbientOcclusionMultibounce(float2 positionSS, float NdotV, float perceptualRoughness,
                                                                                 float ambientOcclusionFromData, float specularOcclusionFromData,
                                                                                 float3 diffuseColor, float3 fresnel0, float specularOcclusionBlend)
    {
        float indirectAmbientOcclusion = GetScreenSpaceDiffuseOcclusion(positionSS);
        float directAmbientOcclusion = lerp(1.0, indirectAmbientOcclusion, _AmbientOcclusionParam.w);

        float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
        // This specular occlusion formulation make sense only with SSAO. When we use Raytracing AO we support different range (local, medium, sky). When using medium or
        // sky occlusion, the result on specular occlusion can be a disaster (all is black). Thus we use _SpecularOcclusionBlend when using RTAO to disable this trick.
        float indirectSpecularOcclusion = lerp(1.0, GetSpecularOcclusionFromAmbientOcclusion(ClampNdotV(NdotV), indirectAmbientOcclusion, roughness),
                                               specularOcclusionBlend);
        float directSpecularOcclusion = lerp(1.0, indirectSpecularOcclusion, _AmbientOcclusionParam.w);

        VividAmbientOcclusionFactor aoFactor;

        aoFactor.indirectSpecularOcclusion = GTAOMultiBounce(min(specularOcclusionFromData, indirectSpecularOcclusion), fresnel0);
        aoFactor.indirectAmbientOcclusion = GTAOMultiBounce(min(ambientOcclusionFromData, indirectAmbientOcclusion), diffuseColor);
        // Note: when affecting direct lighting we don't used the fake bounce.
        aoFactor.directSpecularOcclusion = directSpecularOcclusion.xxx;
        aoFactor.directAmbientOcclusion = directAmbientOcclusion.xxx;
        return aoFactor;
    }
};
#endif
