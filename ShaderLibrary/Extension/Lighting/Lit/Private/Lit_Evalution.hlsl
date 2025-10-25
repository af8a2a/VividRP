#ifndef LIT_EVALUTION_INCLUDED
#define LIT_EVALUTION_INCLUDED
#include "Evalution/DirectionalLightEvalution.hlsl"
#include "Evalution/PunctualLightEvaluation.hlsl"
#include "Evalution/AreaLightEvaluation.hlsl"

DirectLighting Lightloop(float3 V, PositionInputs posInput, ShadingData shadingData)
{
    DirectLighting lightOutput;

    float3 positionWS = posInput.positionWS;
    float3 N = shadingData.normalWS;

    PreLightData preLightData = PreLightData::Init(N, V, shadingData);
    float clampedNdotV = ClampNdotV(preLightData.NdotV);


    float3 specularFGD;
    float diffuseFGD;
    float reflectivity;
    GetPreIntegratedFGDGGXAndDisneyDiffuse(clampedNdotV, shadingData.perceptualRoughness, shadingData.fresnel0,
                                           specularFGD, diffuseFGD, reflectivity);
    shadingData.diffuseFGD = diffuseFGD;
    shadingData.specularFGD = specularFGD;
    // Ref: Practical multiple scattering compensation for microfacet models.
    // We only apply the formulation for metals.
    // For dielectrics, the change of reflectance is negligible.
    // We deem the intensity difference of a couple of percent for high values of roughness
    // to not be worth the cost of another precomputed table.
    // Note: this formulation bakes the BSDF non-symmetric!
    float energyCompensation = 1.0 / reflectivity - 1.0;

    float3 directDiffuse = 0;
    float3 directSpecular = 0;
    float3 indirectDiffuse = 0;
    float3 indirectSpecular = 0;

    // Shading


    DirectLighting dirLight = EvaluateDirectional(preLightData, shadingData, V);

    directDiffuse += dirLight.diffuse;
    directSpecular += dirLight.specular;


    float shadowAttenuation =saturate( LoadScreenSpaceShadowmap(posInput.positionSS).x);

    half3 shadowScatter = EvaluateShadowScatter(shadowAttenuation);

    directDiffuse *= shadowAttenuation * shadowScatter;
    directSpecular *= shadowAttenuation * shadowScatter;



    DirectLighting punctualLight = EvaluatePunctual(posInput,preLightData, shadingData, V);

    directDiffuse  += punctualLight.diffuse;
    directSpecular += punctualLight.specular;

    DirectLighting areaLighting = EvaluateAreaHDRP(posInput, shadingData, V);
    directDiffuse += areaLighting.diffuse;
    directSpecular += areaLighting.specular;
    //     
    //
    // Accumulate Indirect (Reflection probe, ScreenSpace Reflection/Refraction)
    // Reflection / Refraction hierarchy is
    //  1. Screen Space Refraction / Reflection
    //  2. Environment Reflection / Refraction
    //  3. Sky Reflection / Refraction
    bool materialUseBakedGI = (shadingData.materialFlags & kMaterialFlagSubtractiveMixedLighting) != 0;

    float3 SHColor = SampleSH9(_AmbientProbeData, N);
    indirectDiffuse += diffuseFGD * SHColor * shadingData.diffuseColor;
    if (materialUseBakedGI)
    {
        // GI is from geometry's lightmap, stores in lighting buffer.
        indirectDiffuse = 0;
    }
    // TODO: ModifyBakedDiffuseLighting Function


    float3 reflectDirWS = reflect(-V, N);
    // Env is cubemap
    {
        float3 specDominantDirWS = GetSpecularDominantDir(N, reflectDirWS, shadingData.perceptualRoughness,
                                                          clampedNdotV);
        // When we are rough, we tend to see outward shifting of the reflection when at the boundary of the projection volume
        // Also it appear like more sharp. To avoid these artifact and at the same time get better match to reference we lerp to original unmodified reflection.
        // Formula is empirical.
        reflectDirWS = lerp(specDominantDirWS, reflectDirWS, saturate(smoothstep(0, 1, shadingData.roughness2)));
    }

    float reflectionHierarchyWeight = 0.0; // Max: 1.0

    #if defined(_SCREEN_SPACE_REFLECTION)
    // Evaluate ScreenSpaceReflection.
    {
        float4 ssrLighting = LOAD_TEXTURE2D(_SSRLightingTexture, posInput.positionSS);
        UpdateLightingHierarchyWeights(reflectionHierarchyWeight, ssrLighting.a);
        indirectSpecular += specularFGD * ssrLighting.rgb * ssrLighting.a;
    }
    #endif


    // Evaluate Environment probes
    if (reflectionHierarchyWeight < 1.0)
    {
        float3 envReflection = EvaluateEnvProbes(posInput, reflectDirWS, shadingData.perceptualRoughness,
                                                 reflectionHierarchyWeight);
        indirectSpecular += specularFGD * envReflection;
    }


    // Evaluate SkyEnvironment
    if (reflectionHierarchyWeight < 1.0)
    {
        float weight = 1.0;
        UpdateLightingHierarchyWeights(reflectionHierarchyWeight, weight);
        float3 skyReflection = SampleSkyEnvironment(reflectDirWS, shadingData.perceptualRoughness);
        indirectSpecular += specularFGD * skyReflection * weight;
    }

    // Post evaluate indirect diffuse or energy.
    indirectDiffuse *= shadingData.occlusion;
    indirectSpecular *= shadingData.occlusion;
    lightOutput.diffuse = directDiffuse + indirectDiffuse;
    lightOutput.specular = directSpecular + indirectSpecular;
    lightOutput.specular *= 1.0 + shadingData.fresnel0 * energyCompensation;

    return lightOutput;
}
#endif
