#ifndef CHARACTER_FORWARD_LIT_PASS_INCLUDED
#define CHARACTER_FORWARD_LIT_PASS_INCLUDED

#include "Evalution/DirectionalLightEvalution.hlsl"
#include "Evalution/PunctualLightEvaluation.hlsl"
#include "Evalution/AreaLightEvaluation.hlsl"

///////////////////////////////////////////////////////////////////////////////
//                  Vertex and Fragment functions                            //
///////////////////////////////////////////////////////////////////////////////

// Used in Standard (Physically Based) shader
Varyings ToonLitPassVertex(Attributes input)
{
    Varyings output;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);


    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);

    // already normalized from normal transform to WS.
    output.normalWS = normalInput.normalWS;
    real sign = input.tangentOS.w * GetOddNegativeScale();
    half4 tangentWS = half4(normalInput.tangentWS.xyz, sign);
    output.tangentWS = tangentWS;


    output.positionWS = vertexInput.positionWS;
    output.positionCS = vertexInput.positionCS;

    return output;
}


DirectLighting Lightloop(Varyings input, ShadingData shadingData)
{
    float4 positionSS = input.positionCS;
    uint2 tileIndex = uint2(positionSS.xy) / GetTileSize();

    PositionInputs posInput = GetPositionInput(positionSS.xy, _ScreenSize.zw, positionSS.z, positionSS.w, input.positionWS.xyz, tileIndex);


    DirectLighting lightOutput;

    float3 N = shadingData.normalWS;
    float3 V = GetWorldSpaceNormalizeViewDir(input.positionWS);

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


    float2 shadowCoord = TransformWorldToScreenShadowCoord(input.positionWS);
    float shadowAttenuation = SampleVividScreenSpaceShadowmap(shadowCoord).x;

    half3 shadowScatter = EvaluateShadowScatter(shadowAttenuation);
    
    directDiffuse *= shadowAttenuation * shadowScatter;
    directSpecular *= shadowAttenuation * shadowScatter;

    DirectLighting punctualLight = EvaluatePunctual(posInput, preLightData, shadingData, V);

    directDiffuse += punctualLight.diffuse;
    directSpecular += punctualLight.specular;

    DirectLighting areaLighting = EvaluateAreaHDRP(posInput, shadingData, V);
    directDiffuse += areaLighting.diffuse;
    directSpecular += areaLighting.specular;


    indirectDiffuse *= shadingData.occlusion;
    indirectSpecular *= shadingData.occlusion;
    lightOutput.diffuse = directDiffuse + indirectDiffuse;
    lightOutput.specular = directSpecular + indirectSpecular;
    lightOutput.specular *= 1.0 + shadingData.fresnel0 * energyCompensation;


    // lightOutput.specular = shadowAttenuation;
    return lightOutput;
}

half3 PackGBufferNormal(half3 normalWS)
{
    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);           // values between [-1, +1], must use fp32 on some platforms.
    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);   // values between [ 0, +1]
    return half3(PackFloat2To888(remappedOctNormalWS));               // values between [ 0, +1]
}


// Used in Standard (Physically Based) shader
half4 ToonLitPassFragment(Varyings input): SV_Target0
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);


    ShadingData shadingData = GetShadingData(input);


    DirectLighting lightOutput = Lightloop(input, shadingData);


    float3 diffuseLighting = lightOutput.diffuse;
    float3 specularLighting = lightOutput.specular;


    // return float4(specularLighting, 1.0);

    return float4(diffuseLighting + specularLighting + shadingData.emissive, 1.0);
}
#endif
