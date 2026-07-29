#ifndef VIVIDRP_INDIRECT_DIFFUSE_PASS_INCLUDED
#define VIVIDRP_INDIRECT_DIFFUSE_PASS_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GBuffer.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/BakedGI.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Lighting.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/PunctualLightCommon.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividProbeVolume.hlsl"

#define ATTRIBUTES_NEED_TEXCOORD0
#if defined(LIGHTMAP_ON)
    #define ATTRIBUTES_NEED_TEXCOORD1
#endif
#if defined(_NORMALMAP)
    #define ATTRIBUTES_NEED_TANGENT
#endif
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Raytracing/RayTracingCommon.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Raytracing/RaytracingIntersection.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"

#ifndef VIVIDRP_INDIRECT_DIFFUSE_DEFINE_RAYTRACING_SHADERS
    #define VIVIDRP_INDIRECT_DIFFUSE_DEFINE_RAYTRACING_SHADERS 1
#endif

#ifndef VIVIDRP_INDIRECT_DIFFUSE_CLOSEST_HIT_NAME
    #define VIVIDRP_INDIRECT_DIFFUSE_CLOSEST_HIT_NAME ClosestHitMain
#endif

#ifndef VIVIDRP_INDIRECT_DIFFUSE_ANY_HIT_NAME
    #define VIVIDRP_INDIRECT_DIFFUSE_ANY_HIT_NAME AnyHitMain
#endif

CBUFFER_START(UnityPerMaterial)
    float4 _BaseColor;
    float4 _BaseMap_ST;
    float4 _EmissionColor;
    float4 _TransmissionColor;
    float4 _OpacityColor;
    float _Cutoff;
    float _Smoothness;
    float _SmoothnessTextureChannel;
    float _Metallic;
    float _MetallicRemapMin;
    float _MetallicRemapMax;
    float _SmoothnessRemapMin;
    float _SmoothnessRemapMax;
    float _AORemapMin;
    float _AORemapMax;
    float _BumpScale;
    float _OcclusionStrength;
    float _ClearCoatMask;
    float _ClearCoatSmoothness;
    float _AlphaClip;
    float _WorkflowMode;
    float _ReceiveSSR;
    float _ReceiveDecals;
    float _ThinWalledTransmission;
    float _TransmissionWeight;
    float _TransmissionDepth;
    float _SpecularIOR;
CBUFFER_END

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
TEXTURE2D(_OpacityMap);
SAMPLER(sampler_OpacityMap);
TEXTURE2D(_MetallicGlossMap);
SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_RoughnessMap);
SAMPLER(sampler_RoughnessMap);
TEXTURE2D(_BumpMap);
SAMPLER(sampler_BumpMap);
TEXTURE2D(_OcclusionMap);
SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_EmissionMap);
SAMPLER(sampler_EmissionMap);

struct VividIndirectDiffuseHitGeometry
{
    float3 positionWS;
    float3 normalWS;
    float3 faceNormalWS;
    float3 tangentWS;
    float tangentSign;
    float2 uv;
    float2 lightmapUV;
    float hitDistance;
    bool isFrontFace;
};

struct VividIndirectDiffusePayload
{
    uint traceKind;
    uint hit;
    float signedHitDistance;
    float padding0;
    float4 lightingRadiance;
    float4 emissionRadiance;
    float4 mainDirectionalRadiance;
    float4 shadowNormalWS;
};

static const uint kVividIndirectDiffuseTraceKindRadiance = 0u;
static const uint kVividIndirectDiffuseTraceKindVisibility = 1u;

void VividIndirectDiffuseInitializePayload(out VividIndirectDiffusePayload payload, uint traceKind)
{
    payload.traceKind = traceKind;
    payload.hit = 0u;
    payload.signedHitDistance = -1.0;
    payload.padding0 = 0.0;
    payload.lightingRadiance = 0.0;
    payload.emissionRadiance = 0.0;
    payload.mainDirectionalRadiance = 0.0;
    payload.shadowNormalWS = 0.0;
}

void VividIndirectDiffuseInitializeRadiancePayload(out VividIndirectDiffusePayload payload)
{
    VividIndirectDiffuseInitializePayload(payload, kVividIndirectDiffuseTraceKindRadiance);
}

void VividIndirectDiffuseInitializeVisibilityPayload(out VividIndirectDiffusePayload payload)
{
    VividIndirectDiffuseInitializePayload(payload, kVividIndirectDiffuseTraceKindVisibility);
}

bool VividIndirectDiffuseIsVisibilityTrace(VividIndirectDiffusePayload payload)
{
    return payload.traceKind == kVividIndirectDiffuseTraceKindVisibility;
}

float3 UnpackVividNormalScale(float4 packedNormal, float scale)
{
    float3 normalTS;
    normalTS.xy = packedNormal.wy * 2.0 - 1.0;
    normalTS.xy *= scale;
    normalTS.z = sqrt(saturate(1.0 - dot(normalTS.xy, normalTS.xy)));
    return normalTS;
}

float2 VividIndirectDiffuseTransformUV(float2 uv)
{
    return uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
}

float4 SampleBase(float2 uv, float textureLod)
{
    float4 baseSample = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uv, textureLod) * _BaseColor;
#if defined(_OPACITYMAP)
    baseSample.a *= SAMPLE_TEXTURE2D_LOD(_OpacityMap, sampler_OpacityMap, uv, textureLod).r;
#endif
    return baseSample;
}

float4 SampleBase(float2 uv)
{
    return SampleBase(uv, 0.0);
}

float3 SampleOpenPbrGeometryOpacity(float2 uv, float textureLod)
{
    float3 opacity =
        saturate(_OpacityColor.rgb)
        * saturate(
            SAMPLE_TEXTURE2D_LOD(
                _BaseMap,
                sampler_BaseMap,
                uv,
                textureLod).a
            * _BaseColor.a);
#if defined(_OPACITYMAP)
    opacity *= saturate(SAMPLE_TEXTURE2D_LOD(
        _OpacityMap,
        sampler_OpacityMap,
        uv,
        textureLod).rgb);
#endif
    return saturate(opacity);
}

float3 SampleOpenPbrGeometryOpacity(float2 uv)
{
    return SampleOpenPbrGeometryOpacity(uv, 0.0);
}

float ResolveOpenPbrGeometryOpacityBranchProbability(float3 opacity)
{
    opacity = saturate(opacity);
    // The channel mean preserves p = opacity for grayscale input and bounds
    // every RGB importance weight to three for saturated colored opacity.
    return (opacity.r + opacity.g + opacity.b) * (1.0 / 3.0);
}

float3 ResolveOpenPbrGeometryOpacityBranchWeight(
    float3 opacity,
    float branchProbability,
    bool surfaceBranch)
{
    opacity = saturate(opacity);
    branchProbability = saturate(branchProbability);
    if (surfaceBranch)
    {
        return branchProbability > 0.0
            ? opacity / branchProbability
            : 0.0;
    }

    float transmissionProbability = 1.0 - branchProbability;
    return transmissionProbability > 0.0
        ? (1.0 - opacity) / transmissionProbability
        : 0.0;
}

bool VividIndirectDiffuseIsAlphaClipped(float alpha)
{
#if defined(_ALPHATEST_ON)
    return alpha < _Cutoff;
#else
    return false;
#endif
}

float2 SampleMetallicSmoothness(float2 uv, float baseAlpha, float textureLod)
{
    float metallic = saturate(_Metallic);
    float smoothness = saturate(_Smoothness);

#if defined(_METALLICSPECGLOSSMAP)
    float4 metallicGlossSample = SAMPLE_TEXTURE2D_LOD(_MetallicGlossMap, sampler_MetallicGlossMap, uv, textureLod);
    metallic = lerp(_MetallicRemapMin, _MetallicRemapMax, saturate(metallicGlossSample.r));

    #if defined(_ROUGHNESSMAP)
        float roughness = SAMPLE_TEXTURE2D_LOD(_RoughnessMap, sampler_RoughnessMap, uv, textureLod).r;
        smoothness = lerp(_SmoothnessRemapMin, _SmoothnessRemapMax, saturate(1.0 - roughness));
    #elif defined(_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A)
        smoothness = lerp(_SmoothnessRemapMin, _SmoothnessRemapMax, saturate(baseAlpha));
    #else
        smoothness = lerp(_SmoothnessRemapMin, _SmoothnessRemapMax, saturate(metallicGlossSample.a));
    #endif
#elif defined(_ROUGHNESSMAP)
    float roughness = SAMPLE_TEXTURE2D_LOD(_RoughnessMap, sampler_RoughnessMap, uv, textureLod).r;
    smoothness = lerp(_SmoothnessRemapMin, _SmoothnessRemapMax, saturate(1.0 - roughness));
#elif defined(_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A)
    smoothness = lerp(_SmoothnessRemapMin, _SmoothnessRemapMax, saturate(baseAlpha));
#endif

    return float2(metallic, saturate(smoothness));
}

float2 SampleMetallicSmoothness(float2 uv, float baseAlpha)
{
    return SampleMetallicSmoothness(uv, baseAlpha, 0.0);
}

float SampleAmbientOcclusion(float2 uv)
{
#if defined(_OCCLUSIONMAP)
    float occlusion = SAMPLE_TEXTURE2D_LOD(_OcclusionMap, sampler_OcclusionMap, uv, 0.0).g;
    return saturate(lerp(_AORemapMin, _AORemapMax, occlusion));
#else
    return 1.0;
#endif
}

float3 SampleEmission(float2 uv, float textureLod)
{
#if defined(_EMISSION)
    return max(SAMPLE_TEXTURE2D_LOD(_EmissionMap, sampler_EmissionMap, uv, textureLod).rgb * _EmissionColor.rgb, 0.0);
#else
    return float3(0.0, 0.0, 0.0);
#endif
}

float3 SampleEmission(float2 uv)
{
    return SampleEmission(uv, 0.0);
}

float3 VividIndirectDiffuseTransformNormalToWorld(float3 normalOS)
{
    return normalize(mul(normalOS, (float3x3)WorldToObject3x4()));
}

float3 VividIndirectDiffuseTransformDirToWorld(float3 directionOS)
{
    return normalize(mul(directionOS, (float3x3)WorldToObject3x4()));
}

float2 VividIndirectDiffuseFetchUV(AttributeData attributeData)
{
    IntersectionVertex currentVertex;
    GetCurrentIntersectionVertex(attributeData, currentVertex);
    return VividIndirectDiffuseTransformUV(currentVertex.texCoord0.xy);
}

float2 VividIndirectDiffuseFetchLightmapUV(IntersectionVertex currentVertex)
{
#if defined(LIGHTMAP_ON)
    return TransformVividLightmapUV(currentVertex.texCoord1.xy);
#else
    return 0.0;
#endif
}

VividIndirectDiffuseHitGeometry VividIndirectDiffuseBuildHitGeometry(AttributeData attributeData)
{
    IntersectionVertex currentVertex;
    GetCurrentIntersectionVertex(attributeData, currentVertex);

    VividIndirectDiffuseHitGeometry geometry;
    geometry.positionWS = WorldRayOrigin() + WorldRayDirection() * RayTCurrent();
    GetCurrentIntersectionGeometricNormal(attributeData, geometry.faceNormalWS);
    geometry.normalWS = VividIndirectDiffuseTransformNormalToWorld(currentVertex.normalOS);
    geometry.tangentWS = VividIndirectDiffuseTransformDirToWorld(currentVertex.tangentOS.xyz);
    geometry.tangentSign = sign(currentVertex.tangentOS.w);
    geometry.uv = VividIndirectDiffuseTransformUV(currentVertex.texCoord0.xy);
    geometry.lightmapUV = VividIndirectDiffuseFetchLightmapUV(currentVertex);
    geometry.hitDistance = RayTCurrent();
    geometry.isFrontFace = HitKind() == HIT_KIND_TRIANGLE_FRONT_FACE;

    if (dot(geometry.normalWS, geometry.faceNormalWS) < 0.0)
    {
        geometry.normalWS = -geometry.normalWS;
    }

    if (!geometry.isFrontFace)
    {
        geometry.faceNormalWS = -geometry.faceNormalWS;
        geometry.normalWS = -geometry.normalWS;
    }

    return geometry;
}

float3 VividIndirectDiffuseSampleNormalWS(VividIndirectDiffuseHitGeometry geometry, float textureLod)
{
    float3 normalWS = SafeNormalize(geometry.normalWS);

#if defined(_NORMALMAP)
    float tangentLengthSquared = dot(geometry.tangentWS, geometry.tangentWS);
    if (tangentLengthSquared > 1e-8)
    {
        float3 tangentWS = geometry.tangentWS * rsqrt(tangentLengthSquared);
        float3 bitangentWS = SafeNormalize(cross(normalWS, tangentWS) * geometry.tangentSign);
        float3 normalTS = UnpackVividNormalScale(
            SAMPLE_TEXTURE2D_LOD(_BumpMap, sampler_BumpMap, geometry.uv, textureLod), _BumpScale);
        normalWS = SafeNormalize(normalTS.x * tangentWS + normalTS.y * bitangentWS + normalTS.z * normalWS);
    }
#endif

    return normalWS;
}

float3 VividIndirectDiffuseSampleNormalWS(VividIndirectDiffuseHitGeometry geometry)
{
    return VividIndirectDiffuseSampleNormalWS(geometry, 0.0);
}

float3 SampleStandardLitIndirectDiffuseBakedGI(VividIndirectDiffuseHitGeometry geometry, float3 normalWS)
{
#if defined(LIGHTMAP_ON)
    return SampleVividBakedGI(geometry.lightmapUV, normalWS);
#else
    return SampleVividProbeVolume(
        geometry.positionWS,
        normalWS,
        GetWorldSpaceNormalizeViewDir(geometry.positionWS),
        GetMeshRenderingLayerMask());
#endif
}

float HasStandardLitIndirectDiffuseBakedGI()
{
#if defined(LIGHTMAP_ON)
    return 1.0;
#else
    return VividHasProbeVolumeGI() ? 1.0 : 0.0;
#endif
}

VividGBufferSurfaceData BuildStandardLitSurfaceData(VividIndirectDiffuseHitGeometry geometry, out float4 baseSample)
{
    baseSample = SampleBase(geometry.uv);

    float2 metallicSmoothness = SampleMetallicSmoothness(geometry.uv, baseSample.a);

    VividGBufferSurfaceData surfaceData;
    surfaceData.baseColor = baseSample.rgb;
    surfaceData.normalWS = VividIndirectDiffuseSampleNormalWS(geometry);
    surfaceData.linearRoughness = (1.0 - metallicSmoothness.y) * (1.0 - metallicSmoothness.y);
    surfaceData.metallic = metallicSmoothness.x;
    surfaceData.ambientOcclusion = SampleAmbientOcclusion(geometry.uv);
    surfaceData.customData1 = 0.0;

    uint materialFeatures = VIVID_MATERIALFEATURE_LIT;

    if (_ReceiveSSR > 0.5)
        materialFeatures |= VIVID_MATERIALFEATURE_SSR_RECEIVE;

    if (_ReceiveDecals > 0.5)
        materialFeatures |= VIVID_MATERIALFEATURE_DECAL_RECEIVE;

#if defined(_CLEARCOAT)
    float clearCoatMask = saturate(_ClearCoatMask);
    surfaceData.customData = clearCoatMask;
    if (clearCoatMask > 0.0)
        materialFeatures |= VIVID_MATERIALFEATURE_CLEAR_COAT;
#else
    surfaceData.customData = 0.0;
#endif
    surfaceData.materialFeatures = materialFeatures;

    surfaceData.emissive = SampleEmission(geometry.uv);
    surfaceData.builtinData = BuildVividBuiltinData(
        SampleStandardLitIndirectDiffuseBakedGI(geometry, surfaceData.normalWS),
        HasStandardLitIndirectDiffuseBakedGI(),
        geometry.lightmapUV,
        geometry.positionWS);
    return surfaceData;
}

float3 VividIndirectDiffuseGetDiffuseColor(VividGBufferSurfaceData surfaceData)
{
    return saturate(surfaceData.baseColor) * (1.0 - saturate(surfaceData.metallic));
}

float3 VividIndirectDiffuseEvaluateDirectional(
    VividGBufferSurfaceData surfaceData,
    float3 diffuseColor,
    DirectionalLightData directionalLight,
    out float3 lightDirectionWS)
{
    lightDirectionWS = SafeNormalize(directionalLight.directionWS);
    float nDotL = saturate(dot(surfaceData.normalWS, lightDirectionWS));
    return diffuseColor * (INV_PI * nDotL) * directionalLight.color;
}

float VividIndirectDiffuseEvaluatePunctualDistanceAttenuation(PunctualLightData punctualLight, float distanceSquared)
{
    float attenuation = DistanceWindowing(
        distanceSquared,
        punctualLight.rangeAttenuationScale,
        punctualLight.rangeAttenuationBias);
    return attenuation * attenuation;
}

float VividIndirectDiffuseEvaluatePunctualSpotAttenuation(PunctualLightData punctualLight, float3 lightDirectionWS)
{
    if (punctualLight.lightType != VIVID_PUNCTUAL_LIGHT_TYPE_SPOT)
    {
        return 1.0;
    }

    float spotCosine = saturate(dot(punctualLight.directionWS, -lightDirectionWS));
    float attenuation = saturate(spotCosine * punctualLight.angleScale + punctualLight.angleOffset);
    return attenuation * attenuation;
}

float3 VividIndirectDiffuseEvaluatePunctual(
    VividIndirectDiffuseHitGeometry geometry,
    VividGBufferSurfaceData surfaceData,
    float3 diffuseColor,
    PunctualLightData punctualLight,
    out float3 lightDirectionWS,
    out float lightDistance)
{
    float4 distances = 0.0;
    lightDirectionWS = float3(0.0, 0.0, 0.0);
    lightDistance = 0.0;
    GetVividPunctualLightVectors(geometry.positionWS, punctualLight, lightDirectionWS, distances);

    if (distances.y <= 1e-6)
    {
        return float3(0.0, 0.0, 0.0);
    }

    lightDistance = distances.x;
    float nDotL = saturate(dot(surfaceData.normalWS, lightDirectionWS));
    if (nDotL <= 0.0)
    {
        return float3(0.0, 0.0, 0.0);
    }

    float attenuation = VividPunctualLightAttenuationWithDistanceModification(
        punctualLight,
        geometry.positionWS - punctualLight.positionWS,
        distances);

    return diffuseColor * (INV_PI * nDotL) * punctualLight.color * attenuation;
}

void VividIndirectDiffuseEvaluateFrontFaceRadiance(
    VividIndirectDiffuseHitGeometry geometry,
    VividGBufferSurfaceData surfaceData,
    out float3 lightingRadiance,
    out float3 emissionRadiance,
    out float3 mainDirectionalDirectionWS,
    out float3 mainDirectionalRadiance)
{
    float3 diffuseColor = VividIndirectDiffuseGetDiffuseColor(surfaceData);
    lightingRadiance = float3(0.0, 0.0, 0.0);
    emissionRadiance = surfaceData.emissive;
    mainDirectionalDirectionWS = float3(0.0, 0.0, 0.0);
    mainDirectionalRadiance = float3(0.0, 0.0, 0.0);

    if (surfaceData.builtinData.hasBakedGI > 0.5)
    {
        lightingRadiance += surfaceData.builtinData.bakeDiffuseLighting * diffuseColor * INV_PI;
    }

    DirectionalLightData sunLight;
    if (TryGetMainDirectionalLight(sunLight))
    {
        mainDirectionalDirectionWS = sunLight.directionWS;
        float3 directionalRadiance = VividIndirectDiffuseEvaluateDirectional( surfaceData, diffuseColor, sunLight, mainDirectionalDirectionWS);

        mainDirectionalRadiance = directionalRadiance * saturate(sunLight.shadowStrength);
    }

    // for (uint lightIndex = 0u; lightIndex < punctualLightCount; lightIndex++)
    // {
    //     PunctualLightData punctualLight = _PunctualLights[lightIndex];
    //     float3 lightDirectionWS = float3(0.0, 1.0, 0.0);
    //     float lightDistance = 0.0;
    //     float3 punctualRadiance = VividIndirectDiffuseEvaluatePunctual(
    //         geometry,
    //         surfaceData,
    //         diffuseColor,
    //         punctualLight,
    //         lightDirectionWS,
    //         lightDistance);
    //     lightingRadiance += punctualRadiance;
    // }

    lightingRadiance *= surfaceData.ambientOcclusion;
    mainDirectionalRadiance *= surfaceData.ambientOcclusion;
}

void VividIndirectDiffuseWritePayload(
    VividIndirectDiffuseHitGeometry geometry,
    VividGBufferSurfaceData surfaceData,
    inout VividIndirectDiffusePayload payload)
{
    payload.hit = 1u;
    payload.lightingRadiance = 0.0;
    payload.emissionRadiance = 0.0;
    payload.mainDirectionalRadiance = 0.0;

    if (geometry.isFrontFace)
    {
        float3 lightingRadiance = float3(0.0, 0.0, 0.0);
        float3 emissionRadiance = float3(0.0, 0.0, 0.0);
        float3 mainDirectionalDirectionWS = float3(0.0, 0.0, 0.0);
        float3 mainDirectionalRadiance = float3(0.0, 0.0, 0.0);
        VividIndirectDiffuseEvaluateFrontFaceRadiance(
            geometry,
            surfaceData,
            lightingRadiance,
            emissionRadiance,
            mainDirectionalDirectionWS,
            mainDirectionalRadiance);
        payload.lightingRadiance = float4(lightingRadiance, 0.0);
        payload.emissionRadiance = float4(emissionRadiance, 0.0);
        payload.mainDirectionalRadiance = float4(mainDirectionalRadiance, 0.0);
        payload.shadowNormalWS = float4(SafeNormalize(surfaceData.normalWS), 0.0);
    }

    payload.signedHitDistance = geometry.isFrontFace ? geometry.hitDistance : -geometry.hitDistance;
}

bool VividIndirectDiffuseAnyHit(AttributeData attributeData)
{
    float2 uv = VividIndirectDiffuseFetchUV(attributeData);
    float alpha = SampleBase(uv).a;
    return VividIndirectDiffuseIsAlphaClipped(alpha) ? VIVID_RAYTRACING_HIT_IGNORE : VIVID_RAYTRACING_HIT_ACCEPT;
}

void VividIndirectDiffuseClosestHit(
    AttributeData attributeData,
    inout VividIndirectDiffusePayload payload)
{
    if (VividIndirectDiffuseIsVisibilityTrace(payload))
    {
        payload.hit = 1u;
        payload.signedHitDistance = RayTCurrent();
        return;
    }

    VividIndirectDiffuseHitGeometry geometry = VividIndirectDiffuseBuildHitGeometry(attributeData);
    float4 baseSample = float4(0.0, 0.0, 0.0, 0.0);
    VividGBufferSurfaceData surfaceData = BuildStandardLitSurfaceData(geometry, baseSample);
    VividIndirectDiffuseWritePayload(geometry, surfaceData, payload);
}
#if VIVIDRP_INDIRECT_DIFFUSE_DEFINE_RAYTRACING_SHADERS && !defined(SHADERSTAGE_RGS)
[shader("closesthit")]
void VIVIDRP_INDIRECT_DIFFUSE_CLOSEST_HIT_NAME(
    inout VividIndirectDiffusePayload payload : SV_RayPayload,
    AttributeData attributeData : SV_IntersectionAttributes)
{
    VividIndirectDiffuseClosestHit(attributeData, payload);
}

[shader("anyhit")]
void VIVIDRP_INDIRECT_DIFFUSE_ANY_HIT_NAME(
    inout VividIndirectDiffusePayload payload : SV_RayPayload,
    AttributeData attributeData : SV_IntersectionAttributes)
{
    uint result = VividIndirectDiffuseAnyHit(attributeData);

    if (result == VIVID_RAYTRACING_HIT_ACCEPT && VividIndirectDiffuseIsVisibilityTrace(payload))
    {
        payload.hit = 1u;
        payload.signedHitDistance = RayTCurrent();
        AcceptHitAndEndSearch();
        return;
    }

    if (result == VIVID_RAYTRACING_HIT_IGNORE)
    {
        IgnoreHit();
    }
    else if (result == VIVID_RAYTRACING_HIT_ACCEPT_AND_END_SEARCH)
    {
        AcceptHitAndEndSearch();
    }
}
#endif

#endif
