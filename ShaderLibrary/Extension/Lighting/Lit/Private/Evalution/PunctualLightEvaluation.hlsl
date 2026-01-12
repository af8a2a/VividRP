#ifndef LIT_PUNCTUAL_LIGHT_EVALUATION_INCLUDED
#define LIT_PUNCTUAL_LIGHT_EVALUATION_INCLUDED


DirectLighting EvaluatePunctual(PositionInputs posInput, PreLightData preLightData, ShadingData shadingData, float3 V)
{
    DirectLighting lighting;
    ZERO_INITIALIZE(DirectLighting, lighting);


    ClusteredLightingGridCell lightingGrid = ClusteredLighting::LoadPunctualLightCell(posInput);
    
    
    for (uint i = 0; i < lightingGrid.Count; ++i)
    {
        GPULightData gpuLight = lightingGrid.LoadLight(i);
        #ifdef _LIGHT_COOKIES
        if (gpuLight.cookieLightIndex >= 0)
        {
            float4 cookieUvRect = GetLightCookieAtlasUVRect(gpuLight.cookieLightIndex);
            float4x4 worldToLight = GetLightCookieWorldToLightMatrix(gpuLight.cookieLightIndex);
            float2 cookieUv = float2(0, 0);
            cookieUv = ComputeLightCookieUVSpot(worldToLight, gpuLight.positionWS.xyz, cookieUvRect);
            cookieUv = ComputeLightCookieUVPoint(worldToLight, gpuLight.positionWS.xyz, cookieUvRect);
            half4 cookieColor = SampleAdditionalLightsCookieAtlasTexture(cookieUv);
            cookieColor = half4(IsAdditionalLightsCookieAtlasTextureRGBFormat()
                                    ? cookieColor.rgb
                                    : IsAdditionalLightsCookieAtlasTextureAlphaFormat()
                                    ? cookieColor.aaa
                                    : cookieColor.rrr, 1);
            gpuLight.color *= cookieColor.rgb;
        }
        #endif
    
        float3 lightVector = gpuLight.positionWS - posInput.positionWS.xyz;
        float distanceSqr = max(dot(lightVector, lightVector), FLT_MIN);
        float3 lightDirection = float3(lightVector * rsqrt(distanceSqr));
        float shadowMask = 1;
    
    
        float distanceAtten = DistanceAttenuation(distanceSqr, gpuLight.lightAttenuation.xy) * AngleAttenuation(
            gpuLight.dir.xyz, lightDirection, gpuLight.lightAttenuation.zw);
        float shadowAtten = gpuLight.shadowType == 0
                                ? 1
                                : AdditionalLightShadow(gpuLight.shadowLightIndex, posInput.positionWS, lightDirection,
                                                        shadowMask,
                                                        gpuLight.lightOcclusionProbInfo);
        float attenuation = distanceAtten * shadowAtten * gpuLight.baseContribution;
    
        float3 lightDirWS = lightDirection;
        float NdotL = dot(shadingData.normalWS, lightDirWS);
    
        float clampedNdotL = saturate(NdotL);
        float clampedRoughness = max(shadingData.roughness, gpuLight.minRoughness);
    
        float LdotV, NdotH, LdotH, invLenLV;
        GetBSDFAngle(V, lightDirWS, NdotL, preLightData.NdotV, LdotV, NdotH, LdotH, invLenLV);
        float clampedNdotV = ClampNdotV(preLightData.NdotV);
    
    
        float3 F = F_Schlick(shadingData.fresnel0, LdotH);
        float DV = DV_SmithJointGGX(NdotH, abs(NdotL), clampedNdotV, clampedRoughness);
        float3 specTerm = F * DV;
        float diffTerm = DisneyDiffuse(clampedNdotV, abs(NdotL), LdotV, shadingData.perceptualRoughness);
    
        diffTerm *= clampedNdotL;
        specTerm *= clampedNdotL;
    
        lighting.diffuse += shadingData.diffuseColor * diffTerm * gpuLight.color * attenuation;
        lighting.specular += specTerm * gpuLight.color * attenuation;
    }


    // // Punctual Lights
    // uint lightCategory = LIGHTCATEGORY_PUNCTUAL;
    // uint lightStart;
    // uint lightCount;
    // GetCountAndStart(posInput, lightCategory, lightStart, lightCount);
    // uint v_lightListOffset = 0;
    // uint v_lightIdx = lightStart;
    //
    // if (lightCount > 0) // avoid 0 iteration warning.
    // {
    //     while (v_lightListOffset < lightCount)
    //     {
    //         v_lightIdx = FetchIndex(lightStart, v_lightListOffset);
    //         if (v_lightIdx == -1)
    //             break;
    //
    //         // #ifdef _LIGHT_LAYERS
    //         //     if (IsMatchingLightLayer(gpuLight.lightLayerMask, shadingData.meshRenderingLayers))
    //         // #endif
    //         {
    //             GPULightData gpuLight = FetchLight(v_lightIdx);
    //             #ifdef _LIGHT_COOKIES
    //             if (gpuLight.cookieLightIndex >= 0)
    //             {
    //                 float4 cookieUvRect = GetLightCookieAtlasUVRect(gpuLight.cookieLightIndex);
    //                 float4x4 worldToLight = GetLightCookieWorldToLightMatrix(gpuLight.cookieLightIndex);
    //                 float2 cookieUv = float2(0, 0);
    //                 cookieUv = ComputeLightCookieUVSpot(worldToLight, positionWS.xyz, cookieUvRect);
    //                 cookieUv = ComputeLightCookieUVPoint(worldToLight, positionWS.xyz, cookieUvRect);
    //                 half4 cookieColor = SampleAdditionalLightsCookieAtlasTexture(cookieUv);
    //                 cookieColor = half4(IsAdditionalLightsCookieAtlasTextureRGBFormat()
    //                                         ? cookieColor.rgb
    //                                         : IsAdditionalLightsCookieAtlasTextureAlphaFormat()
    //                                         ? cookieColor.aaa
    //                                         : cookieColor.rrr, 1);
    //                 gpuLight.color *= cookieColor.rgb;
    //             }
    //             #endif
    //
    //             float3 lightVector = gpuLight.positionWS - posInput.positionWS.xyz;
    //             float distanceSqr = max(dot(lightVector, lightVector), FLT_MIN);
    //             float3 lightDirection = float3(lightVector * rsqrt(distanceSqr));
    //             float shadowMask = 1;
    //
    //
    //             float distanceAtten = DistanceAttenuation(distanceSqr, gpuLight.lightAttenuation.xy) * AngleAttenuation(
    //                 gpuLight.dir.xyz, lightDirection, gpuLight.lightAttenuation.zw);
    //             float shadowAtten = gpuLight.shadowType == 0
    //                                     ? 1
    //                                     : AdditionalLightShadow(gpuLight.shadowLightIndex, posInput.positionWS, lightDirection,
    //                                                             shadowMask,
    //                                                             gpuLight.lightOcclusionProbInfo);
    //             float attenuation = distanceAtten * shadowAtten * gpuLight.baseContribution;
    //
    //             float3 lightDirWS = lightDirection;
    //             float NdotL = dot(shadingData.normalWS, lightDirWS);
    //
    //             float clampedNdotL = saturate(NdotL);
    //             float clampedRoughness = max(shadingData.roughness, gpuLight.minRoughness);
    //
    //             float LdotV, NdotH, LdotH, invLenLV;
    //             GetBSDFAngle(V, lightDirWS, NdotL, preLightData.NdotV, LdotV, NdotH, LdotH, invLenLV);
    //             float clampedNdotV = ClampNdotV(preLightData.NdotV);
    //
    //
    //             float3 F = F_Schlick(shadingData.fresnel0, LdotH);
    //             float DV = DV_SmithJointGGX(NdotH, abs(NdotL), clampedNdotV, clampedRoughness);
    //             float3 specTerm = F * DV;
    //             float diffTerm = DisneyDiffuse(clampedNdotV, abs(NdotL), LdotV, shadingData.perceptualRoughness);
    //
    //             diffTerm *= clampedNdotL;
    //             specTerm *= clampedNdotL;
    //
    //             lighting.diffuse += shadingData.diffuseColor * diffTerm * gpuLight.color * attenuation;
    //             lighting.specular += specTerm * gpuLight.color * attenuation;
    //         }
    //
    //         v_lightListOffset++;
    //     }
    // }

    return lighting;
}


#endif
