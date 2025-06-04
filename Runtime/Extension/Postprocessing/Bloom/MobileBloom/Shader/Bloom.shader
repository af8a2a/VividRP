Shader "PostProcessing/MobileBloom"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ScreenCoordOverride.hlsl"

    #define BLOOM_CUSTOM


    TEXTURE2D_X(_SourceTexLowMip);
    float4 _SourceTexLowMip_TexelSize;


    float4 _Bloom_Custom_Params; // threshold, lumRnageScale, preFilterScale, intensity
    float4 _Bloom_Custom_ColorTint;
    float2 _Bloom_Custom_BlurScaler;

    #define BloomCustomThreshold      _Bloom_Custom_Params.x
    #define BloomCustomLumRangeScale  _Bloom_Custom_Params.y
    #define BloomCustomPreFilterScale _Bloom_Custom_Params.z
    #define BloomCustomIntensity      _Bloom_Custom_Params.w


    half4 EncodeHDR(half3 color)
    {
        #if _USE_RGBM
            half4 outColor = EncodeRGBM(color);
        #else
        half4 outColor = half4(color, 1.0);
        #endif

        #if UNITY_COLORSPACE_GAMMA
            return half4(sqrt(outColor.xyz), outColor.w); // linear to γ
        #else
        return outColor;
        #endif
    }

    half3 DecodeHDR(half4 color)
    {
        #if UNITY_COLORSPACE_GAMMA
            color.xyz *= color.xyz; // γ to linear
        #endif

        #if _USE_RGBM
            return DecodeRGBM(color);
        #else
        return color.xyz;
        #endif
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100
        ZTest Always ZWrite Off Cull Off


        // 0: Bloom ver2 preFilter

        Pass
        {

            Name "Custom Bloom Prefilter"

            HLSLPROGRAM
            #pragma multi_compile_local _ _USE_RGBM
            #pragma vertex   VertPreFilter_v2
            #pragma fragment FragPreFilter_v2


            #if SHADER_API_GLES
            struct a2v_preFilter
            {
                float4 positionOS       : POSITION;
                float2 uv               : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            #else
            struct a2v_preFilter
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            #endif
            struct v2f_preFilter
            {
                float4 positionHCS : SV_POSITION;
                float4 uv0: TEXCOORD0;
                float4 uv1: TEXCOORD1;
            };

            v2f_preFilter VertPreFilter_v2(a2v_preFilter v)
            {
                v2f_preFilter o;
                UNITY_SETUP_INSTANCE_ID(v);

                #if SHADER_API_GLES
                float4 pos = v.positionOS;
                float2 uv  = v.uv;
                #else
                float4 pos = GetFullScreenTriangleVertexPosition(v.vertexID);
                float2 uv = GetFullScreenTriangleTexCoord(v.vertexID);
                #endif

                o.positionHCS = pos;
                uv = uv * _BlitScaleBias.xy + _BlitScaleBias.zw;

                o.uv0.xy = uv + half2(-1, -1) * _BlitTexture_TexelSize.xy;
                o.uv0.zw = uv + half2(1, -1) * _BlitTexture_TexelSize.xy;
                o.uv1.xy = uv + half2(-1, 1) * _BlitTexture_TexelSize.xy;
                o.uv1.zw = uv + half2(1, 1) * _BlitTexture_TexelSize.xy;

                return o;
            }

            half4 FragPreFilter_v2(v2f_preFilter i) : SV_Target
            {
                half3 mainCol = 0;

                mainCol += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.uv0.xy);
                mainCol += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.uv0.zw);
                mainCol += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.uv1.xy);
                mainCol += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.uv1.zw);
                mainCol /= 4;

                mainCol *= 1 / (1 + BloomCustomLumRangeScale * Luminance(mainCol.rgb));

                half brightness = Max3(mainCol.r, mainCol.g, mainCol.b);
                float thresholdKnee = BloomCustomThreshold * 0.5f;
                half softness = clamp(brightness - BloomCustomThreshold + thresholdKnee, 0.0, 2.0 * thresholdKnee);
                softness = (softness * softness) / (4.0 * thresholdKnee + 1e-4);
                half multiplier = max(brightness - BloomCustomThreshold, softness) / max(brightness, 1e-4);
                mainCol *= multiplier;

                // mainCol -= BloomCustomThreshold;
                mainCol = max(0, mainCol.rgb);

                mainCol *= BloomCustomPreFilterScale;

                mainCol = lerp(mainCol, _Bloom_Custom_ColorTint.rgb * Luminance(mainCol.rgb),
                               _Bloom_Custom_ColorTint.a);
                return EncodeHDR(mainCol);
            }
            ENDHLSL
        }


        // 1: Bloom ver2 downsampler

        Pass
        {
            Name "Custom Bloom DownSample"

            HLSLPROGRAM
            #pragma vertex   VertDownSample_v2
            #pragma fragment FragDownSample_v2

            #if SHADER_API_GLES
            struct a2v_downsampler
            {
                float4 positionOS       : POSITION;
                float2 uv               : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            #else
            struct a2v_downsampler
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            #endif
            struct v2f_downsampler
            {
                float4 positionHCS : SV_POSITION;
                float4 uv0: TEXCOORD0;
                float4 uv1: TEXCOORD1;
            };

            v2f_downsampler VertDownSample_v2(a2v_downsampler v)
            {
                v2f_downsampler o;
                UNITY_SETUP_INSTANCE_ID(v);

                #if SHADER_API_GLES
                float4 pos = v.positionOS;
                float2 uv  = v.uv;
                #else
                float4 pos = GetFullScreenTriangleVertexPosition(v.vertexID);
                float2 uv = GetFullScreenTriangleTexCoord(v.vertexID);
                #endif
                o.positionHCS = pos;
                uv = uv * _BlitScaleBias.xy + _BlitScaleBias.zw;

                o.uv0.xy = uv + half2(0.95999998, 0.25) * _BlitTexture_TexelSize.xy;
                o.uv0.zw = uv + half2(0.25, -0.95999998) * _BlitTexture_TexelSize.xy;
                o.uv1.xy = uv + half2(-0.95999998, -0.25) * _BlitTexture_TexelSize.xy;
                o.uv1.zw = uv + half2(-0.25, 0.95999998) * _BlitTexture_TexelSize.xy;

                return o;
            }

            half4 FragDownSample_v2(v2f_downsampler i) : SV_Target
            {
                half3 s;
                s = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.uv0.xy));
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.uv0.zw));
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.uv1.xy));
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.uv1.zw));

                return EncodeHDR(s * 0.25);
            }
            ENDHLSL
        }

        // 2: first pre blur, sigma = 2.6, 加速高斯模糊, 半径5, 7次采样
        /*
        *  [0]offset: 5.307122000, weight: 0.035270680
        *  [1]offset: 3.373378000, weight: 0.127357100
        *  [2]offset: 1.444753000, weight: 0.259729700
        *  [3]offset: 0.000000000, weight: 0.155285200
        */

        Pass
        {
            Name "Custom Bloom Pre blur"

            HLSLPROGRAM
            #pragma multi_compile_local _ _USE_RGBM

            #pragma vertex   Vert
            #pragma fragment FragBlur_pre


            half4 FragBlur_pre(Varyings i) : SV_Target
            {
                float2 scaler = _Bloom_Custom_BlurScaler * _BlitTexture_TexelSize.xy;
                half3 s = 0;

                float2 offsetUV0 = i.texcoord.xy + scaler.xy * 5.307122000;
                float2 offsetUV1 = i.texcoord.xy - scaler.xy * 5.307122000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.035270680;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.035270680;

                offsetUV0 = i.texcoord.xy + scaler.xy * 3.373378000;
                offsetUV1 = i.texcoord.xy - scaler.xy * 3.373378000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.127357100;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.127357100;

                offsetUV0 = i.texcoord.xy + scaler.xy * 1.444753000;
                offsetUV1 = i.texcoord.xy - scaler.xy * 1.444753000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.259729700;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.259729700;

                offsetUV0 = i.texcoord.xy + scaler.xy * 0;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.155285200;

                return EncodeHDR(s);
            }
            ENDHLSL
        }


        // 3: mip 1st blur, sigma = 3.2, 加速高斯模糊, 半径8, 9次采样
        /*
        *    [0]offset: 7.324664000, weight: 0.017001690
        *    [1]offset: 5.368860000, weight: 0.058725350
        *    [2]offset: 3.415373000, weight: 0.138472900
        *    [3]offset: 1.463444000, weight: 0.222984700
        *    [4]offset: 0.000000000, weight: 0.125630700
        */

        Pass
        {
            Name "Custom Bloom Blur1"

            HLSLPROGRAM
            #pragma multi_compile_local _ _USE_RGBM

            #pragma vertex   Vert
            #pragma fragment FragBlur_first


            half4 FragBlur_first(Varyings i) : SV_Target
            {
                float2 scaler = _Bloom_Custom_BlurScaler * _BlitTexture_TexelSize.xy;
                half3 s = 0;

                float2 offsetUV0 = i.texcoord.xy + scaler.xy * 7.324664000;
                float2 offsetUV1 = i.texcoord.xy - scaler.xy * 7.324664000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.017001690;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.017001690;

                offsetUV0 = i.texcoord.xy + scaler.xy * 5.368860000;
                offsetUV1 = i.texcoord.xy - scaler.xy * 5.368860000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.058725350;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.058725350;

                offsetUV0 = i.texcoord.xy + scaler.xy * 3.415373000;
                offsetUV1 = i.texcoord.xy - scaler.xy * 3.415373000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.138472900;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.138472900;

                offsetUV0 = i.texcoord.xy + scaler.xy * 1.463444000;
                offsetUV1 = i.texcoord.xy - scaler.xy * 1.463444000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.222984700;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.222984700;

                offsetUV0 = i.texcoord.xy + scaler.xy * 0;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.125630700;

                return EncodeHDR(s);
            }
            ENDHLSL
        }

        // 4: mip 2nd blur, sigma = 5.3, 加速高斯模糊，半径16, 17次采样
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

        Pass
        {
            Name "Custom Bloom Blur2"

            HLSLPROGRAM
            #pragma multi_compile_local _ _USE_RGBM

            #pragma vertex   Vert
            #pragma fragment FragBlur_second


            half4 FragBlur_second(Varyings i) : SV_Target
            {
                float2 scaler = _Bloom_Custom_BlurScaler * _BlitTexture_TexelSize.xy;
                half3 s = 0;

                float2 offsetUV0 = i.texcoord.xy + scaler.xy * 15.365450000;
                float2 offsetUV1 = i.texcoord.xy - scaler.xy * 15.365450000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.002165789;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.002165789;

                offsetUV0 = i.texcoord.xy + scaler.xy * 13.382110000;
                offsetUV1 = i.texcoord.xy - scaler.xy * 13.382110000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.006026655;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.006026655;

                offsetUV0 = i.texcoord.xy + scaler.xy * 11.399060000;
                offsetUV1 = i.texcoord.xy - scaler.xy * 11.399060000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.014561720;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.014561720;

                offsetUV0 = i.texcoord.xy + scaler.xy * 9.416246000;
                offsetUV1 = i.texcoord.xy - scaler.xy * 9.416246000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.030551590;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.030551590;

                offsetUV0 = i.texcoord.xy + scaler.xy * 7.433644000;
                offsetUV1 = i.texcoord.xy - scaler.xy * 7.433644000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.055660430;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.055660430;

                offsetUV0 = i.texcoord.xy + scaler.xy * 5.451206000;
                offsetUV1 = i.texcoord.xy - scaler.xy * 5.451206000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.088055510;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.088055510;

                offsetUV0 = i.texcoord.xy + scaler.xy * 3.468890000;
                offsetUV1 = i.texcoord.xy - scaler.xy * 3.468890000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.120967400;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.120967400;

                offsetUV0 = i.texcoord.xy + scaler.xy * 1.486653000;
                offsetUV1 = i.texcoord.xy - scaler.xy * 1.486653000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.144306200;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.144306200;

                offsetUV0 = i.texcoord.xy + scaler.xy * 0;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.075409520;

                return EncodeHDR(s);
            }
            ENDHLSL
        }

        // 5: mip 3rd blur, sigma = 6.65, 加速高斯模糊，半径20, 21次采样
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

        Pass
        {
            Name "Custom Bloom Blur3"

            HLSLPROGRAM
            #pragma multi_compile_local _ _USE_RGBM

            #pragma vertex   Vert
            #pragma fragment FragBlur_third


            half4 FragBlur_third(Varyings i) : SV_Target
            {
                float2 scaler = _Bloom_Custom_BlurScaler * _BlitTexture_TexelSize.xy;
                half3 s = 0;

                float2 offsetUV0 = i.texcoord.xy + scaler.xy * 19.391510000;
                float2 offsetUV1 = i.texcoord.xy - scaler.xy * 19.391510000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.001667595;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.001667595;

                offsetUV0 = i.texcoord.xy + scaler.xy * 17.402340000;
                offsetUV1 = i.texcoord.xy - scaler.xy * 17.402340000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.003832045;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.003832045;

                offsetUV0 = i.texcoord.xy + scaler.xy * 15.413260000;
                offsetUV1 = i.texcoord.xy - scaler.xy * 15.413260000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.008048251;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.008048251;

                offsetUV0 = i.texcoord.xy + scaler.xy * 13.424270000;
                offsetUV1 = i.texcoord.xy - scaler.xy * 13.424270000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.015449170;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.015449170;

                offsetUV0 = i.texcoord.xy + scaler.xy * 11.435350000;
                offsetUV1 = i.texcoord.xy - scaler.xy * 11.435350000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.027104610;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.027104610;

                offsetUV0 = i.texcoord.xy + scaler.xy * 9.446500000;
                offsetUV1 = i.texcoord.xy - scaler.xy * 9.446500000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.043462710;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.043462710;

                offsetUV0 = i.texcoord.xy + scaler.xy * 7.457702000;
                offsetUV1 = i.texcoord.xy - scaler.xy * 7.457702000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.063698220;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.063698220;

                offsetUV0 = i.texcoord.xy + scaler.xy * 5.468947000;
                offsetUV1 = i.texcoord.xy - scaler.xy * 5.468947000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.085324850;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.085324850;

                offsetUV0 = i.texcoord.xy + scaler.xy * 3.480224000;
                offsetUV1 = i.texcoord.xy - scaler.xy * 3.480224000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.104463000;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.104463000;

                offsetUV0 = i.texcoord.xy + scaler.xy * 1.491521000;
                offsetUV1 = i.texcoord.xy - scaler.xy * 1.491521000;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.116892900;
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.116892900;

                offsetUV0 = i.texcoord.xy + scaler.xy * 0;
                offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
                s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.060113440;

                return EncodeHDR(s);
            }
            ENDHLSL
        }

        // 6: ver2 upsampler

        Pass
        {
            Name "Custom Bloom Upsample"

            HLSLPROGRAM
            #pragma multi_compile_local _ _USE_RGBM

            #pragma vertex   Vert
            #pragma fragment FragUpSample_v2

            float4 _Bloom_Custom_BlurCompositeWeight;

            TEXTURE2D_X(_BloomMipDown0);
            TEXTURE2D_X(_BloomMipDown1);
            TEXTURE2D_X(_BloomMipDown2);

            half4 FragUpSample_v2(Varyings i) : SV_Target
            {
                // half4 combineScale = half4(0.3,0.3,0.26,0.15);
                float4 combineScale = _Bloom_Custom_BlurCompositeWeight;
                half3 main = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.texcoord)) * combineScale
                    .x;
                half3 mip0 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BloomMipDown0, sampler_LinearClamp, i.texcoord)) *
                    combineScale.y;
                half3 mip1 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BloomMipDown1, sampler_LinearClamp, i.texcoord)) *
                    combineScale.z;
                half3 mip2 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BloomMipDown2, sampler_LinearClamp, i.texcoord)) *
                    combineScale.w;


                return EncodeHDR(main + mip0 + mip1 + mip2);
            }
            ENDHLSL
        }

        //todo
        //integrate in uberPost?
        //7 apply bloom 
        Pass
        {
            Name "Apply Bloom"

            HLSLPROGRAM
            #pragma multi_compile_local _ _USE_RGBM

            #pragma vertex   Vert
            #pragma fragment frag


            //tex
            TEXTURE2D_X(_Bloom_Texture);
            float4 _Bloom_Texture_TexelSize;


            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = SCREEN_COORD_APPLY_SCALEBIAS(UnityStereoTransformScreenSpaceTex(input.texcoord));

                float2 uvBloom = uv;
                half3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, SCREEN_COORD_REMOVE_SCALEBIAS(uvBloom)).xyz;

                half4 bloom = SAMPLE_TEXTURE2D_X(_Bloom_Texture, sampler_LinearClamp,
                                     SCREEN_COORD_REMOVE_SCALEBIAS(uvBloom));


                #if defined(_USE_RGBM)
                     bloom.xyz = DecodeRGBM(bloom);
                #endif

                half3 bloomedCol = bloom.xyz * _Bloom_Custom_Params.w + color.xyz;

                // Expossure (Tonemapping)
                // half3 expossuredCol = bloomedCol;
                // half3 temp1 = expossuredCol * (expossuredCol * 1.36 + 0.047);
                // half3 temp2 = expossuredCol * (expossuredCol * 0.93 + 0.56) + 0.14;
                // half3 tonemappedCol = temp1 / temp2;
                // tonemappedCol = clamp(tonemappedCol, 0.0, 1.0);

                color = bloomedCol;


                return half4(color, 1);
            }
            ENDHLSL
        }


    }
}