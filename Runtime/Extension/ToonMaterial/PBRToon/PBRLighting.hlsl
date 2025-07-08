#ifndef CHARACTER_FORWARD_LIT_PASS_INCLUDED
#define CHARACTER_FORWARD_LIT_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "PBRLightingUtil.hlsl"


///////////////////////////////////////////////////////////////////////////////
//                  Vertex and Fragment functions                            //
///////////////////////////////////////////////////////////////////////////////

// Used in Standard (Physically Based) shader
Varyings LitPassVertex(Attributes input)
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);

    // normalWS and tangentWS already normalize.
    // this is required to avoid skewing the direction during interpolation
    // also required for per-vertex lighting and SH evaluation
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


half3 CharacterSpecular(BRDFData brdfData, half3 normalWS, half3 lightDirectionWS, half3 viewDirectionWS)
{
    float3 lightDirectionWSFloat3 = float3(lightDirectionWS);
    float3 halfDir = SafeNormalize(lightDirectionWSFloat3 + float3(viewDirectionWS));
    half LoH = half(saturate(dot(lightDirectionWSFloat3, halfDir)));
    float NoH = saturate(dot(float3(normalWS), halfDir));
    float nol = saturate(dot(float3(normalWS), lightDirectionWSFloat3));
    float nov = saturate(dot(float3(normalWS), viewDirectionWS));

    float roughness = brdfData.roughness;
    float metalic = MetallicFromReflectivity(brdfData.reflectivity);
    float3 fresnel0 = ComputeFresnel0(brdfData.albedo, metalic, DEFAULT_SPECULAR_VALUE);

    float3 F = F_Schlick(fresnel0, LoH);
    float DV = DV_SmithJointGGX(NoH, nol, nov, roughness);
    float3 specTerm = F * DV;
    return specTerm;
}


half3 CharacterLightingDirect(BRDFData brdfData, Light light, half3 normalWS, half3 viewDirectionWS)
{
    half NdotL = saturate(dot(normalWS, light.direction));
    half3 radiance = light.color * (light.shadowScatter * NdotL);
    half3 brdf = brdfData.diffuse;
    brdf += brdfData.specular * CharacterSpecular(brdfData, normalWS, light.direction, viewDirectionWS);
    return brdf * radiance;
}

half3 CharacterLightingIndirect(BRDFData brdfData, half3 positionWS, half3 normalWS, half3 viewDirWS, float2 normalizedScreenSpaceUV)
{
    float roughness = brdfData.roughness;

    float metalic = MetallicFromReflectivity(brdfData.reflectivity);
    float3 fresnel0 = ComputeFresnel0(brdfData.albedo, metalic, DEFAULT_SPECULAR_VALUE);


    const float3 F = F_Schlick(max(dot(normalWS, viewDirWS), 0.0), fresnel0, roughness);
    const float3 kS = F;
    float3 kD = 1.0 - kS;
    kD *= 1.0 - metalic;
    float nov = saturate(dot(float3(normalWS), viewDirWS));

    float3 specularFGD = 0;
    float diffuseFGD = 0;
    float reflectivity = 0;

    GetPreIntegratedFGDGGXAndDisneyDiffuse(nov, brdfData.perceptualRoughness, fresnel0,
                                           specularFGD, //out
                                           diffuseFGD, //out
                                           reflectivity //out
    );
    
    float3 SHColor = SampleSH(normalWS);
    float3 indirectDiffuse = diffuseFGD * SHColor * brdfData.diffuse;

    half3 reflectVector = reflect(-viewDirWS, normalWS);
    float3 indirectSpecular = specularFGD * GlossyEnvironmentReflection(reflectVector, positionWS, brdfData.perceptualRoughness, 1.0h, normalizedScreenSpaceUV);

    return indirectDiffuse + indirectSpecular;
}


half4 CharacterRendering(InputData inputData, SurfaceData surfaceData)
{
    BRDFData brdfData;

    // NOTE: can modify "surfaceData"...
    InitializeBRDFData(surfaceData, brdfData);


    float4 shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
    Light mainLight = GetMainLight(shadowCoord);


    half3 mainLightColor = CharacterLightingDirect(brdfData, mainLight, inputData.normalWS, inputData.viewDirectionWS);

    half3 indirectLighting = CharacterLightingIndirect(brdfData, inputData.positionWS, inputData.normalWS, inputData.viewDirectionWS,
                                                       inputData.normalizedScreenSpaceUV);
    return (mainLightColor+indirectLighting).xyzz;
}


// Used in Standard (Physically Based) shader
half4 LitPassFragment(Varyings input): SV_Target0
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);


    SurfaceData surfaceData;
    InitializeCharacterSurfaceData(input.uv, surfaceData);

    InputData inputData;
    InitializeCharacterInputData(input, surfaceData.normalTS, inputData);

    half4 color = CharacterRendering(inputData, surfaceData);

    return color;
}
#endif
