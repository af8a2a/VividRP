Shader "PostProcessing/Diffusion"
{

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
    #include "Diffusion.hlsl"
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100



        Pass//0
        {
            Name "BlurH"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag_Blur1
            ENDHLSL
        }

        Pass//1
        {
            Name "BlurV"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag_Blur2
            ENDHLSL
        }


        // 变亮
        Pass//2
        {
            Name "Max"

            Blend One One
            BlendOp Max

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag_Max
            ENDHLSL
        }

        // 正片叠底
        Pass//3
        {
            Name "Multiply"

            Blend One Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag_Multiply
            ENDHLSL
        }


        // 滤色
        Pass//4
        {
            Name "Filter"


            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag_Filter
            ENDHLSL
        }

    }
}