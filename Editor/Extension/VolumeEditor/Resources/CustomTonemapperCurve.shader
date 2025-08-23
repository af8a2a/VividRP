Shader "Hidden/HD PostProcessing/Editor/Custom Tonemapper Curve"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/Postprocessing/ColorGrading/Shader/ToneMapping.hlsl"

    #pragma editor_sync_compilation
    #pragma target 3.5

    #pragma multi_compile_local_fragment _ _TONEMAP_GT _TONEMAP_ACES _TONEMAP_NEUTRAL _TONEMAP_AGX
    float4 _GTToneMap_Params0;
    float4 _GTToneMap_Params1;

    struct Attributes
    {
        float4 positionOS : POSITION;
        float2 texcoord : TEXCOORD0;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);


        output.positionCS = vertexInput.positionCS;
        output.uv = input.texcoord;

        return output;
    }


    float4 DrawCurve(Varyings i, float3 background, float3 curveColor)
    {
        #ifdef _TONEMAP_GT
        float y = GranTurismoTonemap(i.uv.x, _GTToneMap_Params0.x, _GTToneMap_Params0.y, _GTToneMap_Params0.z,
                                     _GTToneMap_Params0.w,
                                     _GTToneMap_Params1.x, _GTToneMap_Params1.y);
        #elif _TONEMAP_ACES
        float3 aces = unity_to_ACES(i.uv.xxx);
        float y = AcesTonemap(aces);
        #elif  _TONEMAP_NEUTRAL
        float y = NeutralTonemap(i.uv.xxx);
        #elif _TONEMAP_AGX
        float y = TonemapAgx(i.uv.xxx);
        #else
        float y = i.uv.x;
        #endif
        float aa = fwidth(i.uv.y - y);
        float curve = smoothstep(y - aa, y, i.uv.y) - smoothstep(y, y + aa, i.uv.y);
        float3 color = lerp(background, curveColor, curve);

        return float4(color, 1.0);
    }

    float4 FragCurveDark(Varyings i) : SV_Target
    {
        return DrawCurve(i, (pow(0.196, 2.2)).xxx, (pow(0.7, 2.2)).xxx);
    }

    float4 FragCurveLight(Varyings i) : SV_Target
    {
        return DrawCurve(i, (pow(0.635, 2.2)).xxx, (pow(0.2, 2.2)).xxx);
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
        }
        Cull Off ZWrite Off ZTest Always

        // (0) Dark skin
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCurveDark
            ENDHLSL
        }

        // (1) Light skin
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCurveLight
            ENDHLSL

        }
    }
}