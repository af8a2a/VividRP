#ifndef LIT_DIRECTIONAL_LIGHT_EVALUATION_INCLUDED
#define LIT_DIRECTIONAL_LIGHT_EVALUATION_INCLUDED


DirectLighting EvaluateDirectional(PreLightData preLightData, ShadingData shadingData, float3 V)
{
    DirectLighting lighting;
    ZERO_INITIALIZE(DirectLighting, lighting);

    // Accumulate Direct (Directional Lights, Punctual Lights, TODO: Area Lights)
    uint dirLightIndex = 0;
    // bool materialReceiveShadowsOff = (shadingData.materialFlags & kMaterialFlagReceiveShadowsOff) != 0;
    UNITY_LOOP
    for (dirLightIndex = 0; dirLightIndex < _DirectionalLightCount; dirLightIndex++)
    {
        // #ifdef _LIGHT_LAYERS
        //     if (IsMatchingLightLayer(dirLight.lightLayerMask, shadingData.meshRenderingLayers))
        // #endif
        {
            DirectionalLightData dirLight = g_DirectionalLightDatas[dirLightIndex];
            float3 lightDirWS = dirLight.dir;
            float NdotL = dot(shadingData.normalWS, lightDirWS);
            float NdotV = preLightData.NdotV;
            float clampedNdotL = saturate(NdotL);
            float clampedNdotV = ClampNdotV(NdotV);
            float clampedRoughness = max(shadingData.roughness, dirLight.minRoughness);

            float LdotV, NdotH, LdotH, invLenLV;
            GetBSDFAngle(V, lightDirWS, NdotL, NdotV, LdotV, NdotH, LdotH, invLenLV);


            float3 F = F_Schlick(shadingData.fresnel0, LdotH);
            float DV = DV_SmithJointGGX(NdotH, abs(NdotL), clampedNdotV, clampedRoughness);
            float3 specTerm = F * DV;
            float diffTerm = DisneyDiffuse(clampedNdotV, abs(NdotL), LdotV, shadingData.perceptualRoughness);

            diffTerm *= clampedNdotL;
            specTerm *= clampedNdotL;

            lighting.diffuse += shadingData.diffuseColor * diffTerm * dirLight.color;
            lighting.specular += specTerm * dirLight.color;
        }
    }
    return lighting;
}


#endif
