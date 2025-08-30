#include "AreaLight.hlsl"

DeferredLightingOutput DeferredLit(PositionInputs posInput, ShadingData shadingData)
{
    DeferredLightingOutput lightOutput;
    ZERO_INITIALIZE(DeferredLightingOutput, lightOutput);

    float3 positionWS = posInput.positionWS;
    float3 normalWS = shadingData.normalWS;
    float3 viewDirWS = GetWorldSpaceNormalizeViewDir(positionWS);


    float NdotV = dot(normalWS, viewDirWS);
    float clampedNdotV = ClampNdotV(NdotV);
    float3 specularFGD;
    float diffuseFGD;
    float reflectivity;
    GetPreIntegratedFGDGGXAndDisneyDiffuse(clampedNdotV, shadingData.perceptualRoughness, shadingData.fresnel0, specularFGD, diffuseFGD, reflectivity);
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

    // Accumulate Direct (Directional Lights, Punctual Lights, TODO: Area Lights)
    uint dirLightIndex = 0;
    // bool materialReceiveShadowsOff = (shadingData.materialFlags & kMaterialFlagReceiveShadowsOff) != 0;
    UNITY_LOOP
    for (dirLightIndex = 0; dirLightIndex < _DirectionalLightCount; dirLightIndex++)
    {
        DirectionalLightData dirLight = g_DirectionalLightDatas[dirLightIndex];
        // #ifdef _LIGHT_LAYERS
        //     if (IsMatchingLightLayer(dirLight.lightLayerMask, shadingData.meshRenderingLayers))
        // #endif
        {
            float3 lightDirWS = dirLight.dir;
            float NdotL = dot(normalWS, lightDirWS);

            float clampedNdotL = saturate(NdotL);
            float clampedRoughness = max(shadingData.roughness, dirLight.minRoughness);

            float LdotV, NdotH, LdotH, invLenLV;
            GetBSDFAngle(viewDirWS, lightDirWS, NdotL, NdotV, LdotV, NdotH, LdotH, invLenLV);


            float3 F = F_Schlick(shadingData.fresnel0, LdotH);
            float DV = DV_SmithJointGGX(NdotH, abs(NdotL), clampedNdotV, clampedRoughness);
            float3 specTerm = F * DV;
            float diffTerm = DisneyDiffuse(clampedNdotV, abs(NdotL), LdotV, shadingData.perceptualRoughness);

            diffTerm *= clampedNdotL;
            specTerm *= clampedNdotL;

            directDiffuse += shadingData.diffuseColor * diffTerm * dirLight.color;
            directSpecular += specTerm * dirLight.color;
        }
    }
    float shadowAttenuation = LoadScreenSpaceShadowmap(posInput.positionSS).x;

    half3 shadowScatter = EvaluateShadowScatter(shadowAttenuation);

    directDiffuse *= shadowAttenuation * shadowScatter;
    directSpecular *= shadowAttenuation * shadowScatter;


    // Punctual Lights
    uint lightCategory = LIGHTCATEGORY_PUNCTUAL;
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

            // #ifdef _LIGHT_LAYERS
            //     if (IsMatchingLightLayer(gpuLight.lightLayerMask, shadingData.meshRenderingLayers))
            // #endif
            {
                GPULightData gpuLight = FetchLight(v_lightIdx);
                #ifdef _LIGHT_COOKIES
                    if(gpuLight.cookieLightIndex >= 0)
                    {
                        float4 cookieUvRect = GetLightCookieAtlasUVRect(gpuLight.cookieLightIndex);
                        float4x4 worldToLight = GetLightCookieWorldToLightMatrix(gpuLight.cookieLightIndex);
                        float2 cookieUv = float2(0,0);
                        cookieUv = ComputeLightCookieUVSpot(worldToLight, positionWS.xyz, cookieUvRect);
                        cookieUv = ComputeLightCookieUVPoint(worldToLight, positionWS.xyz, cookieUvRect);
                        half4 cookieColor = SampleAdditionalLightsCookieAtlasTexture(cookieUv);
                            cookieColor = half4(IsAdditionalLightsCookieAtlasTextureRGBFormat() ? cookieColor.rgb
                            : IsAdditionalLightsCookieAtlasTextureAlphaFormat() ? cookieColor.aaa
                            : cookieColor.rrr, 1);
                        gpuLight.color *= cookieColor.rgb;
                    }
                #endif

                float3 lightVector = gpuLight.positionWS - positionWS.xyz;
                float distanceSqr = max(dot(lightVector, lightVector), FLT_MIN);
                float3 lightDirection = float3(lightVector * rsqrt(distanceSqr));
                float shadowMask = 1;


                float distanceAtten = DistanceAttenuation(distanceSqr, gpuLight.lightAttenuation.xy) * AngleAttenuation(
                    gpuLight.dir.xyz, lightDirection, gpuLight.lightAttenuation.zw);
                float shadowAtten = gpuLight.shadowType == 0
                                        ? 1
                                        : AdditionalLightShadow(gpuLight.shadowLightIndex, positionWS, lightDirection, shadowMask,
                                                                gpuLight.lightOcclusionProbInfo);
                float attenuation = distanceAtten * shadowAtten * gpuLight.baseContribution;

                float3 lightDirWS = lightDirection;
                float NdotL = dot(normalWS, lightDirWS);

                float clampedNdotL = saturate(NdotL);
                float clampedRoughness = max(shadingData.roughness, gpuLight.minRoughness);

                float LdotV, NdotH, LdotH, invLenLV;
                GetBSDFAngle(viewDirWS, lightDirWS, NdotL, NdotV, LdotV, NdotH, LdotH, invLenLV);


                float3 F = F_Schlick(shadingData.fresnel0, LdotH);
                float DV = DV_SmithJointGGX(NdotH, abs(NdotL), clampedNdotV, clampedRoughness);
                float3 specTerm = F * DV;
                float diffTerm = DisneyDiffuse(clampedNdotV, abs(NdotL), LdotV, shadingData.perceptualRoughness);

                diffTerm *= clampedNdotL;
                specTerm *= clampedNdotL;

                directDiffuse += shadingData.diffuseColor * diffTerm * gpuLight.color * attenuation;
                directSpecular += specTerm * gpuLight.color * attenuation;
            }

            v_lightListOffset++;
        }
    }

    AreaLighting areaLighting = EvaluateAreaHDRP(posInput, shadingData, viewDirWS);
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

    float3 SHColor = SampleSH9(_AmbientProbeData, normalWS);
    indirectDiffuse += diffuseFGD * SHColor * shadingData.diffuseColor;
    if (materialUseBakedGI)
    {
        // GI is from geometry's lightmap, stores in lighting buffer.
        indirectDiffuse = 0;
    }
    // TODO: ModifyBakedDiffuseLighting Function


    float3 reflectDirWS = reflect(-viewDirWS, normalWS);
    // Env is cubemap
    {
        float3 specDominantDirWS = GetSpecularDominantDir(normalWS, reflectDirWS, shadingData.perceptualRoughness, clampedNdotV);
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
        float3 envReflection = EvaluateEnvProbes(posInput, reflectDirWS, shadingData.perceptualRoughness, reflectionHierarchyWeight);
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
    lightOutput.diffuseLighting = directDiffuse + indirectDiffuse;
    lightOutput.specularLighting = directSpecular + indirectSpecular;
    lightOutput.specularLighting *= 1.0 + shadingData.fresnel0 * energyCompensation;

    return lightOutput;
}


DeferredLightingOutput Lightloop(PositionInputs posInput, ShadingData shadingData)
{
    return DeferredLit(posInput, shadingData);
}
