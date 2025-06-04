Shader "Hidden/DanbaidongRP/Sky/HDRISky"
{
    Properties
    {   
    }

    HLSLINCLUDE

    #pragma editor_sync_compilation
    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
    #include "SkyUtils.hlsl"

    float4 _GradientBottom;
    float4 _GradientMiddle;
    float4 _GradientTop;
    float _GradientDiffusion;
    float _SkyIntensity;

    struct Attributes
    {
        uint vertexID : SV_VertexID;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID, UNITY_RAW_FAR_CLIP_VALUE);
        return output;
    }
    

    float4 RenderSky(Varyings input, float exposure)
    {
        float3 viewDirWS = GetSkyViewDirWS(input.positionCS.xy);
        float verticalGradient = viewDirWS.y * _GradientDiffusion;
        float topLerpFactor = saturate(-verticalGradient);
        float bottomLerpFactor = saturate(verticalGradient);
        float3 color = lerp(_GradientMiddle.xyz, _GradientBottom.xyz, bottomLerpFactor);
        color = lerp(color, _GradientTop.xyz, topLerpFactor);
        return float4(color * _SkyIntensity, 1.0);
    }

    float4 FragBaking(Varyings input) : SV_Target
    {
        return RenderSky(input, 1.0);
    }

    float4 FragRender(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        // alpha used to be exposureMultiplier
        return RenderSky(input, 1.0);
    }


    ENDHLSL

    SubShader
    {
        Tags{ "RenderPipeline" = "UniversalPipeline" }

        // For cubemap
        Pass
        {
            Name "FragBaking"
            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragBaking
            ENDHLSL
        }

        // For fullscreen Sky
        Pass
        {
            Name "FragRender"
            ZWrite Off
            ZTest LEqual
            Blend Off
            Cull Off

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragRender
            ENDHLSL
        }
    }
    Fallback Off
}
