#ifndef VIVID_OUTLINE_INCLUDED
#define VIVID_OUTLINE_INCLUDED


// -------------------------------------
// Structs
struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    //smooth normal for outline
    float2 uv7 : TEXCOORD7;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 color : TEXCOORD1;
    float3 normalWS : TEXCOORD2;

    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};






// -------------------------------------
// Vertex
Varyings OutlineVert(Attributes input)
{
    Varyings output;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);


    {
        real sign = input.tangentOS.w * GetOddNegativeScale();
        float3 bitangent = cross(input.normalOS.xyz, input.tangentOS.xyz).xyz * sign;
        float3 normalTS = OctahedronToUnitVector(input.uv7.xy * 2.0 - 1.0);
        input.normalOS = mul(normalTS.xyz, float3x3(input.tangentOS.xyz, bitangent.xyz, input.normalOS.xyz)).xyz;
    }



    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 positionVS = TransformWorldToView(positionWS);
    positionVS.z -= _OutlineZBias * 0.001;


    float4 positionOffsetCS = TransformWViewToHClip(TransformWorldToViewDir(normalInput.normalWS) / 1920.0 + positionVS);
    float4 positionCS = TransformWViewToHClip(positionVS);
    float2 offsetCS = positionOffsetCS.xy - positionCS.xy;
    offsetCS = offsetCS * _OutlineWidth * 1.3;
    float3 normalCS = TransformWorldToHClipDir(normalInput.normalWS, true);
    float2 minOffsetDir = (_ScreenParams.zw - 1) * normalCS.xy * 2;
    minOffsetDir *= positionCS.w * 1.2;

    float2 maxOffsetDir = minOffsetDir * (_OutlineMaxOffsetMultiplier + 1);

    //offset
    float2 offsetDiffer = abs(offsetCS);
    offsetCS = clamp(offsetDiffer, abs(minOffsetDir), abs(maxOffsetDir));
    offsetDiffer = saturate(offsetCS - offsetDiffer);
    float offsetDifferLength = saturate(SafeSqrt(dot(offsetDiffer, offsetDiffer)) * 75);

    positionCS.xy += offsetCS * sign(normalCS.xy);


    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    half4 outlineColor = _OutlineColor;
    outlineColor *= _OutlineIntensity;
    output.positionCS = positionCS;
    output.uv = input.uv;
    output.color = outlineColor;
    output.color.a = offsetDifferLength;

    return output;
}

///////////////////////////////////////////////////////////////////////////////
//                      Material Property Helpers                            //
///////////////////////////////////////////////////////////////////////////////
half Alpha(half albedoAlpha, half4 color, half cutoff)
{
    #if !defined(_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A) && !defined(_GLOSSINESS_FROM_BASE_ALPHA)
    half alpha = albedoAlpha * color.a;
    #else
    half alpha = color.a;
    #endif
    
    #if defined(_ALPHATEST_ON)
    clip(alpha - cutoff);
    #endif
    
    return alpha;
}


float4 OutlineFrag(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float2 uv = input.uv;
    half4 baseColor = _BaseMap.Sample(sampler_LinearRepeat, uv);
    half4 metallic = SAMPLE_TEXTURE2D_X(_PBRMap, sampler_LinearRepeat, uv).r;

    half4 finalColor = input.color;

    finalColor.a = Alpha(baseColor.a, _BaseColor, _Cutoff);

    finalColor.xyz *= baseColor.xyz * _BaseColor.xyz;
    finalColor.xyz *= 1 - metallic * 0.75;

    finalColor.w *= saturate(1 - input.color.a);

    return finalColor;
}


#endif // UNIVERSAL_OBJECT_MOTION_VECTORS_INCLUDED
