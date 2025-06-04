Shader "PostProcessing/BackgroundLighting"
{

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
        }
        LOD 100

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ScreenCoordOverride.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "BackgroundLightScatter.hlsl"
        //#pragma enable_d3d11_debug_symbols
        ENDHLSL

        ZTest Always
        ZWrite Off
        Cull Off

        Pass//0
        {
            Name "Prefilter"

            HLSLPROGRAM
            #pragma vertex   VertPreFilter_v2
            #pragma fragment FragPreFilter_v2
            ENDHLSL
        }

        // first pre blur, sigma = 2.6, 加速高斯模糊, 半径5, 7次采样
        /*
        *  [0]offset: 5.307122000, weight: 0.035270680
        *  [1]offset: 3.373378000, weight: 0.127357100
        *  [2]offset: 1.444753000, weight: 0.259729700
        *  [3]offset: 0.000000000, weight: 0.155285200
        */
        Pass//1
        {
            Name "Preblur"

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment FragBlur_pre
            ENDHLSL
        }

        Pass//2
        {
            Name "DownSample"

            HLSLPROGRAM
            #pragma vertex   VertDownSample_v2
            #pragma fragment FragDownSample_v2
            ENDHLSL
        }

        //mip 1st blur, sigma = 3.2, 加速高斯模糊, 半径8, 9次采样
        /*
        *    [0]offset: 7.324664000, weight: 0.017001690
        *    [1]offset: 5.368860000, weight: 0.058725350
        *    [2]offset: 3.415373000, weight: 0.138472900
        *    [3]offset: 1.463444000, weight: 0.222984700
        *    [4]offset: 0.000000000, weight: 0.125630700
        */
        Pass//3
        {
            Name "Blur1"

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment FragBlur_first
            ENDHLSL
        }

        //mip 2nd blur, sigma = 5.3, 加速高斯模糊，半径16, 17次采样
        /*
        *    [0]offset: 15.365450000, weight: 0.002165789
        *    [1]offset: 13.382110000, weight: 0.006026655
        *    [2]offset: 11.399060000, weight: 0.014561720
        *    [3]offset: 9.416246000,  weight: 0.030551590
        *    [4]offset: 7.433644000,  weight: 0.055660430
        *    [5]offset: 5.451206000,  weight: 0.088055510
        *    [6]offset: 3.468890000,  weight: 0.120967400
        *    [7]offset: 1.486653000,  weight: 0.144306200
        *    [8]offset: 0.000000000,  weight: 0.075409520
        */
        Pass//4
        {
            Name "Blur2"

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment FragBlur_second
            ENDHLSL
        }

        //mip 3rd blur, sigma = 6.65, 加速高斯模糊，半径20, 21次采样
        /*
        *    [0]offset: 19.391510000, weight: 0.001667595
        *    [1]offset: 17.402340000, weight: 0.003832045
        *    [2]offset: 15.413260000, weight: 0.008048251
        *    [3]offset: 13.424270000, weight: 0.015449170
        *    [4]offset: 11.435350000, weight: 0.027104610
        *    [5]offset: 9.446500000,  weight: 0.043462710
        *    [6]offset: 7.457702000,  weight: 0.063698220
        *    [7]offset: 5.468947000,  weight: 0.085324850
        *    [8]offset: 3.480224000,  weight: 0.104463000
        *    [9]offset: 1.491521000,  weight: 0.116892900
        *    [10]offset: 0.000000000, weight: 0.060113440
        */
        Pass//5
        {
            Name "Blur3"

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment FragBlur_third
            ENDHLSL
        }

        Pass//6
        {
            Name "upsampler"

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment FragUpSample_v2
            ENDHLSL
        }

        Pass
        {
            Name "Apply Scatter"


            HLSLPROGRAM
            #pragma multi_compile _RIM_LIGHT
            #pragma vertex   Vert
            #pragma fragment frag
            ENDHLSL
        }
    }
}