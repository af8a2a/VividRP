#ifndef VIVIDRP_SIMPLE_LIT_GBUFFER_PASS_INCLUDED
#define VIVIDRP_SIMPLE_LIT_GBUFFER_PASS_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/GBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseColor;
    float4 _BaseMap_ST;
    float4 _EmissiveColor;
    float _LinearRoughness;
    float _Metallic;
    float _Occlusion;
    float _CustomData;
    float _MaterialId;
CBUFFER_END

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 normalWS : TEXCOORD0;
    float2 uv : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings Vert(Attributes input)
{
    Varyings output;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
    return output;
}

float3 SampleBaseColor(float2 uv)
{
    return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb * _BaseColor.rgb;
}

uint GetMaterialId()
{
    return (uint)min(max(round(_MaterialId), 0.0), 255.0);
}

VividGBufferSurfaceData BuildSimpleLitSurfaceData(Varyings input)
{
    VividGBufferSurfaceData surfaceData;
    surfaceData.baseColor = SampleBaseColor(input.uv);
    surfaceData.normalWS = normalize(input.normalWS);
    surfaceData.linearRoughness = _LinearRoughness;
    surfaceData.metallic = _Metallic;
    surfaceData.ambientOcclusion = _Occlusion;
    surfaceData.customData = _CustomData;
    surfaceData.materialId = GetMaterialId();
    surfaceData.emissive = _EmissiveColor.rgb;
    return surfaceData;
}

VividGBufferFragmentOutput FragGBuffer(Varyings input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    return PackVividGBufferSurfaceData(BuildSimpleLitSurfaceData(input));
}

half4 FragPreDepth(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    return 0.0;
}

half4 FragDebug(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    VividGBufferSurfaceData surfaceData = BuildSimpleLitSurfaceData(input);
    float3 debugColor = surfaceData.baseColor + surfaceData.emissive;
    return half4(debugColor, 1.0);
}

#endif
