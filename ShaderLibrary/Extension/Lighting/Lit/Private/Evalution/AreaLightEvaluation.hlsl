#ifndef LIT_AREA_LIGHT_EVALUATION_INCLUDED
#define LIT_AREA_LIGHT_EVALUATION_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/Lighting/Common/AreaLightCommon.hlsl"


float4 EvaluateLTC_Area(bool isRectLight, float3 center, float3 right, float3 up, float halfLength, float halfHeight,
                        float3x3 invM, float perceptualRoughness
                        #if 0
                        ,int cookieMode, float4 cookieScaleOffset
                        #endif
)
{
    float3 ortho = cross(center, right);
    float orthoSq = dot(ortho, ortho);

    // Check whether the light is in a vertical orientation.
    bool quit = (orthoSq == 0);

    // Check whether the light is entirely below the surface.
    // We must test twice, since a linear transformation
    // may bring the light above the surface (a side-effect).
    quit = quit || (center.z + halfLength * abs(right.z) + halfHeight * abs(up.z) <= 0);

    float4 ltcValue = float4(1, 1, 1, 0);

    if (!quit)
    {
        // Perform a sparse matrix multiplication.
        float3 C = mul(invM, center);
        float3 A = mul(invM, right);
        float3 B = mul(invM, up);

        // Check whether the light is entirely below the surface.
        // We must test twice, since a linear transformation
        // may bring the light below the surface (as expected).
        if (C.z + halfLength * abs(A.z) + halfHeight * abs(B.z) > 0)
        {
            if (isRectLight)
            {
                float4x3 lightVerts;

                lightVerts[0] = C - halfLength * A - halfHeight * B; // LL
                lightVerts[1] = lightVerts[0] + (2 * halfHeight) * B; // UL
                lightVerts[2] = lightVerts[1] + (2 * halfLength) * A; // UR
                lightVerts[3] = lightVerts[2] - (2 * halfHeight) * B; // LR

                float3 formFactor;

                // Polygon irradiance in the transformed configuration.
                ltcValue.a = PolygonIrradiance(lightVerts, formFactor);

                // if (cookieMode != COOKIEMODE_NONE)
                // {
                //     ltcValue.rgb = SampleAreaLightCookie(cookieScaleOffset, lightVerts, formFactor, perceptualRoughness);
                // }
            }
            else // Line light
            {
                float w = ComputeLineWidthFactor(invM, ortho, orthoSq);

                ltcValue.a = I_diffuse_line(C, A, halfLength) * w;
            }
        }
    }

    return ltcValue;
}


DirectLighting EvaluateAreaHDRP(PositionInputs posInput, ShadingData shadingData, float3 V)
{
    DirectLighting lighting;
    lighting.diffuse = 0;
    lighting.specular = 0;
    float3 positionWS = posInput.positionWS;

    ClusteredLightingGridCell lightingGrid = ClusteredLighting::LoadAreaLightCell(posInput);
    for (uint i = 0; i < lightingGrid.Count; ++i)
    {
        GPULightData lightData = lightingGrid.LoadLight(i);


        float3 unL = lightData.positionWS - posInput.positionWS;

        float halfLength = lightData.size.x * 0.5;
        float halfHeight = lightData.size.y * 0.5; // = 0 for a line light

        float intensity = PillowWindowing(unL, lightData.right, lightData.up, halfLength, halfHeight,
                                          lightData.rangeAttenuationScale, lightData.rangeAttenuationBias);

        // Make sure the light is front-facing (and has a non-zero effective area).
        intensity *= (dot(unL, lightData.forward) >= 0) ? 0 : 1;

        if (intensity > 0)
        {
            float nov = dot(shadingData.normalWS, V);
            float3x3 orthoBasisViewNormal = GetOrthoBasisViewNormal(V, shadingData.normalWS, nov);

            // Rotate the light vectors into the local coordinate system.
            float3 center = mul(orthoBasisViewNormal, unL);
            float3 right = mul(orthoBasisViewNormal, lightData.right);
            float3 up = mul(orthoBasisViewNormal, lightData.up);


            float4 ltcValue;

            // ----- 1. Evaluate the diffuse part -----
            float clampedNdotV = ClampNdotV(nov);

            float3x3 ltcTransformDiffuse = SampleLtcMatrix(shadingData.perceptualRoughness, clampedNdotV,
                                                           LTCLIGHTINGMODEL_DISNEY_DIFFUSE);
            float3x3 ltcTransformSpecular = SampleLtcMatrix(shadingData.perceptualRoughness, clampedNdotV,
                                                            LTCLIGHTINGMODEL_GGX);


            ltcValue = EvaluateLTC_Area(true, center, right, up, halfLength, halfHeight,
                                        ltcTransformDiffuse, 1.0f
                                        #if 0
                                                ,
                                        // LTC light cookies appear broken unless diffuse roughness is set to 1.
                                        transpose(preLightData.ltcTransformDiffuse),
                                            /*bsdfData.perceptualRoughness*/ 1.0f,
                                            lightData.cookieMode, lightData.cookieScaleOffset
                                        #endif
            );
            ltcValue.a *= intensity;
            lighting.diffuse += ltcValue.rgb * ltcValue.a;

            // ----- 2. Evaluate the specular part -----

            ltcValue = EvaluateLTC_Area(true, center, right, up, halfLength, halfHeight,
                                        transpose(ltcTransformSpecular), shadingData.perceptualRoughness
                                        #if 0
                                                ,lightData.cookieMode, lightData.cookieScaleOffset
                                        #endif
            );
            ltcValue.a *= intensity;
            lighting.specular += ltcValue.rgb * ltcValue.a;
            lighting.diffuse *= lightData.color * shadingData.diffuseFGD;
            lighting.specular *= lightData.color * shadingData.specularFGD;

            // Add the foam and surface diffuse
            lighting.diffuse *= shadingData.diffuseColor; // + bsdfData.foamColor;
        }
    }
    return lighting;
}


DirectLighting EvaluateArea(PositionInputs posInput, ShadingData shadingData, float3 V)
{
    DirectLighting lighting;
    lighting.diffuse = 0;
    lighting.specular = 0;
    float3 positionWS = posInput.positionWS;

    ClusteredLightingGridCell lightingGrid = ClusteredLighting::LoadAreaLightCell(posInput);
    for (uint i = 0; i < lightingGrid.Count; ++i)
    {
        GPULightData lightData = lightingGrid.LoadLight(i);

        float nov = dot(shadingData.normalWS, V);

        //
        float3x3 orthoBasisViewNormal = GetOrthoBasisViewNormal(V, shadingData.normalWS, nov);

        float2 areaExtent = lightData.size.xy;
        float3 center = GetCameraRelativePositionWS(lightData.positionWS) - positionWS;

        center = mul(orthoBasisViewNormal, center);
        float3 right = mul(orthoBasisViewNormal, lightData.right) * areaExtent.x;
        float3 up = mul(orthoBasisViewNormal, lightData.up) * areaExtent.y;
        bool quit = (center.z + abs(right.z) + abs(up.z)) <= 0;
        if (quit) return lighting;

        //
        float3x3 invM = SampleLtcMatrix(shadingData.perceptualRoughness, ClampNdotV(nov), 0);

        center = mul(center, invM);
        right = mul(right, invM);
        up = mul(up, invM);
        quit = (center.z + abs(right.z) + abs(up.z)) <= 0;
        if (quit) return lighting;
        //
        float4x3 verts;

        verts[0] = center - right - up; // DL
        verts[1] = center - right + up; // UL
        verts[2] = center + right + up; // UR
        verts[3] = center + right - up; // DR
        float3 formFactor = RectFormFactor(verts);


        float3 lightVector = lightData.positionWS - positionWS.xyz;
        float distanceSqr = max(dot(lightVector, lightVector), FLT_MIN);
        float3 lightDirection = float3(lightVector * rsqrt(distanceSqr));
        float shadowMask = 1;

        float scale = 0.25 / (areaExtent.x * areaExtent.y) * TWO_PI;

        float distanceAtten = DistanceAttenuation(distanceSqr, lightData.lightAttenuation.xy) * AngleAttenuation(
            lightData.dir.xyz, lightDirection, lightData.lightAttenuation.zw);
        float shadowAtten = lightData.shadowType == 0
                                ? 1
                                : AdditionalLightShadow(lightData.shadowLightIndex, positionWS, lightDirection,
                                                        shadowMask,
                                                        lightData.lightOcclusionProbInfo);
        float attenuation = distanceAtten * shadowAtten * lightData.baseContribution;


        lighting.diffuse = lightData.color * PolygonIrradianceFromVectorFormFactor(formFactor) * scale *
            attenuation;
        lighting.specular = lightData.color * PolygonIrradianceFromVectorFormFactor(formFactor) * scale *
            attenuation;
    }
    return lighting;
}

#endif
