Shader "Volumetric/Final"
{
    HLSLINCLUDE

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "VolumetricLighting.hlsl"

    struct Attributes
    {
        uint vertexID : SV_VertexID;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
        return output;
    }

    void VolumetricScatteringCompute(float2 positionCS, float depth, out float3 color, out float opacity)
    {
        PositionInputs posInput = GetPositionInput(positionCS, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);

        // if (depth == UNITY_RAW_FAR_CLIP_VALUE) {
        //     posInput.positionWS = GetCurrentViewPosition() - 
        // }
        EvaluateScattering(posInput, color, opacity);
    }

    float4 Frag(Varyings input) : SV_Target
    {
        float3 volColor;
        float volOpacity;
        float depth = LoadSceneDepth(input.positionCS.xy);
        VolumetricScatteringCompute(input.positionCS.xy, depth, volColor, volOpacity);
        return float4(volColor, 1 - volOpacity);
    }
    ENDHLSL
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off
            Blend One SrcAlpha, Zero One
            ZTest Less

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}