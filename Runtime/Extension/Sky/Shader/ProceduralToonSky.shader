//Shader "Hidden/DanbaidongRP/Sky/ProceduralToon"
//{
//    Properties
//    {
//        _StarTex("StarTex", 2D) = "black" {}
//        _BrightNoiseTex("BrightNoiseTex(xy:scale zw:speed)", 2D) = "white" {}
//
//        [Ramp]_SkyGradientColorTex("SkyGradientColorTex", 2D)  = "white" { }
//        [Ramp]_SkyGradientRangeTex("SkyGradientRangeTex", 2D)  = "black" { }
//
//
//        [HDR]_SunColor("SunColor", Color) = (1,1,1,1)
//        _SunSize("Sun Size", Range(0, 1)) = 0.04
//        _SunInnerBound("Inner Bound", Range(0, 1)) = 0.2
//        _SunOuterBound("Outer Bound", Range(0, 1)) = 0.8
//
//        _StarColor("StarColor", Color) = (1,1,1,1)
//        _RandomColor("RandomColor", Range(0,1)) = 0.358
//        _ColorIntensity("ColorIntensity", Range(0,100)) = 2.0
//    }
//
//    HLSLINCLUDE
//
//    #pragma editor_sync_compilation
//    #pragma target 4.5
//    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch
//
//    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
//    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
//    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
//    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"
//    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
//    #include "SkyUtils.hlsl"
//
//    int _RenderSunDisk;
//
//    float4 _StarTex_ST;
//    float4 _BrightNoiseTex_ST;
//    float _TimeOfDay;
//
//    float4 _SunColor;
//    float _SunSize;
//    float _SunInnerBound;
//    float _SunOuterBound;
//
//    float4 _StarColor;
//    float _RandomColor;
//    float _ColorIntensity;
//
//    TEXTURE2D(_StarTex);
//    SAMPLER(sampler_StarTex);
//
//    TEXTURE2D(_BrightNoiseTex);
//    SAMPLER(sampler_BrightNoiseTex);
//
//    TEXTURE2D(_SkyGradientColorTex);
//    TEXTURE2D(_SkyGradientRangeTex);
//
//    float3 palette(float t)
//    {
//        float3 phases  = float3(0.00, 0.19, 0.00);
//        float3 amplitudes = float3(0.62, 0.52, 0.21);
//        float3 frequencies = float3(1.19, 1.00, 2.57);
//        float3 offset = float3(0.5, 0.5, 1.05);
//
//        return (offset + amplitudes * cos((frequencies * t + phases) * 6.28318));
//    }
//
//    #if UNITY_UV_STARTS_AT_TOP
//    static const float rampIndices[5] = { 0.9, 0.7, 0.5, 0.3, 0.1 };
//    #else
//    static const float rampIndices[5] = { 0.1, 0.3, 0.5, 0.7, 0.9 };
//    #endif
//
//    struct Attributes
//    {
//        uint vertexID : SV_VertexID;
//        UNITY_VERTEX_INPUT_INSTANCE_ID
//    };
//
//    struct Varyings
//    {
//        float4 positionCS   : SV_POSITION;
//        float4 backgroundColor : TEXCOORD0;
//        float4 topColor     : TEXCOORD1;
//        float4 middleColor  : TEXCOORD2;
//        float4 bottomColor  : TEXCOORD3;
//        float4 horizonColor : TEXCOORD4;
//        UNITY_VERTEX_OUTPUT_STEREO
//    };
//
//    Varyings Vert(Attributes input)
//    {
//        Varyings output;
//        UNITY_SETUP_INSTANCE_ID(input);
//        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
//        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID, UNITY_RAW_FAR_CLIP_VALUE);
//
//        float timeline = _TimeOfDay;
//
//        output.backgroundColor  = SAMPLE_TEXTURE2D_LOD(_SkyGradientColorTex, sampler_LinearClamp, float2(timeline, rampIndices[0]), 0);
//        output.topColor         = SAMPLE_TEXTURE2D_LOD(_SkyGradientColorTex, sampler_LinearClamp, float2(timeline, rampIndices[1]), 0);
//        output.middleColor      = SAMPLE_TEXTURE2D_LOD(_SkyGradientColorTex, sampler_LinearClamp, float2(timeline, rampIndices[2]), 0);
//        output.bottomColor      = SAMPLE_TEXTURE2D_LOD(_SkyGradientColorTex, sampler_LinearClamp, float2(timeline, rampIndices[3]), 0);
//        output.horizonColor     = SAMPLE_TEXTURE2D_LOD(_SkyGradientColorTex, sampler_LinearClamp, float2(timeline, rampIndices[4]), 0);
//
//        return output;
//    }
//
//    float4 BakeSky(Varyings input, float exposure)
//    {
//        float3 viewDirWS = -GetSkyViewDirWS(input.positionCS.xy);
//        float vertical = viewDirWS.y * 0.5 + 0.5;
//
//        // Reconstruct projUV
//        float2 projUV = float2(viewDirWS.x, viewDirWS.z);
//        // Reconstruct sphereUV
//        float theta = atan2(viewDirWS.z, viewDirWS.x) * INV_PI * 0.5 + 0.5;
//        float phi = asin(viewDirWS.y) * INV_PI + 0.5;
//        float2 sphereUV = float2(theta, phi);
//
//
//
//        // SkyColor
//        float3 skyColor = input.backgroundColor.rgb;
//        float gradientRange = 0;
//        gradientRange = SAMPLE_TEXTURE2D(_SkyGradientRangeTex, sampler_LinearClamp, float2(vertical, rampIndices[1])).x;
//        skyColor = lerp(skyColor, input.topColor.rgb, gradientRange * input.topColor.a);
//        gradientRange = SAMPLE_TEXTURE2D(_SkyGradientRangeTex, sampler_LinearClamp, float2(vertical, rampIndices[2])).x;
//        skyColor = lerp(skyColor, input.middleColor.rgb, gradientRange * input.middleColor.a);
//        gradientRange = SAMPLE_TEXTURE2D(_SkyGradientRangeTex, sampler_LinearClamp, float2(vertical, rampIndices[3])).x;
//        skyColor = lerp(skyColor, input.bottomColor.rgb, gradientRange * input.bottomColor.a);
//        gradientRange = SAMPLE_TEXTURE2D(_SkyGradientRangeTex, sampler_LinearClamp, float2(vertical, rampIndices[4])).x;
//        skyColor = lerp(skyColor, input.horizonColor.rgb, gradientRange * input.horizonColor.a);
//
//        // Sun
//        Light mainlight = GetMainLight();
//        float3 sunPos = mainlight.direction;
//        float sunDist = distance(viewDirWS, sunPos);
//        float sunArea = 1 - sunDist/_SunSize;
//        sunArea = smoothstep(_SunInnerBound, _SunOuterBound, sunArea);
//        float sunTimeAtten =  saturate(1.1 - smoothstep(0.25, 0.27, abs(_TimeOfDay - 0.5)));
//        float3 sunResult = _SunColor.rgb * sunArea * sunTimeAtten;
//        sunResult = (_RenderSunDisk > 0) ? sunResult : 0;
//
//        float3 result = skyColor + sunResult;
//
//        return float4(result, 1.0);
//    }
//
//    float4 RenderSky(Varyings input, float exposure)
//    {
//        float3 viewDirWS = -GetSkyViewDirWS(input.positionCS.xy);
//        float vertical = viewDirWS.y * 0.5 + 0.5;
//
//        // Reconstruct projUV
//        float2 projUV = float2(viewDirWS.x, viewDirWS.z);
//        // Reconstruct sphereUV
//        float theta = atan2(viewDirWS.z, viewDirWS.x) * INV_PI * 0.5 + 0.5;
//        float phi = asin(viewDirWS.y) * INV_PI + 0.5;
//        float2 sphereUV = float2(theta, phi);
//
//
//
//        // SkyColor
//        float3 skyColor = input.backgroundColor.rgb;
//        float gradientRange = 0;
//        gradientRange = SAMPLE_TEXTURE2D(_SkyGradientRangeTex, sampler_LinearClamp, float2(vertical, rampIndices[1])).x;
//        skyColor = lerp(skyColor, input.topColor.rgb, gradientRange * input.topColor.a);
//        gradientRange = SAMPLE_TEXTURE2D(_SkyGradientRangeTex, sampler_LinearClamp, float2(vertical, rampIndices[2])).x;
//        skyColor = lerp(skyColor, input.middleColor.rgb, gradientRange * input.middleColor.a);
//        gradientRange = SAMPLE_TEXTURE2D(_SkyGradientRangeTex, sampler_LinearClamp, float2(vertical, rampIndices[3])).x;
//        skyColor = lerp(skyColor, input.bottomColor.rgb, gradientRange * input.bottomColor.a);
//        gradientRange = SAMPLE_TEXTURE2D(_SkyGradientRangeTex, sampler_LinearClamp, float2(vertical, rampIndices[4])).x;
//        skyColor = lerp(skyColor, input.horizonColor.rgb, gradientRange * input.horizonColor.a);
//
//        // Sun
//        Light mainlight = GetMainLight();
//        float3 sunPos = mainlight.direction;
//        float sunDist = distance(viewDirWS, sunPos);
//        float sunArea = 1 - sunDist/_SunSize;
//        sunArea = smoothstep(_SunInnerBound, _SunOuterBound, sunArea);
//        float sunTimeAtten =  saturate(1.1 - smoothstep(0.25, 0.27, abs(_TimeOfDay - 0.5)));
//        float3 sunResult = _SunColor.rgb * sunArea * sunTimeAtten;
//
//
//        // Star
//        // Must no lod
//        float4 topStarTex = SAMPLE_TEXTURE2D_LOD(_StarTex, sampler_StarTex, projUV * _StarTex_ST.xy, 0);
//        float4 bottomStarTex = SAMPLE_TEXTURE2D_LOD(_StarTex, sampler_StarTex, sphereUV * _StarTex_ST.zw, 0);
//        float starBrightTex = SAMPLE_TEXTURE2D(_BrightNoiseTex, sampler_BrightNoiseTex, projUV * _BrightNoiseTex_ST.xy + frac(_Time.y * _BrightNoiseTex_ST.zw)).r;
//
//        float4 stars = lerp(bottomStarTex, topStarTex, smoothstep(0.65, 0.66, viewDirWS.y));
//
//        float starPosMask = smoothstep(0.0, 0.9, viewDirWS.y);
//        float starTimeMask = smoothstep(0.2, 0.3, abs(_TimeOfDay - 0.5));
//        float starBrightNoise = smoothstep(0.7, 0.9, starBrightTex);
//
//        float3 starColor = lerp(_StarColor, palette(stars.y).rgb, _RandomColor) * _ColorIntensity;
//        float3 starResult = starColor * stars.x * starPosMask * starTimeMask * starBrightNoise;
//
//
//        float3 result = skyColor + sunResult + starResult;
//
//
//        return float4(result, 1.0);
//    }
//
//    float4 FragBaking(Varyings input) : SV_Target
//    {
//        return BakeSky(input, 1.0);
//    }
//
//    float4 FragRender(Varyings input) : SV_Target
//    {
//        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
//
//        // alpha used to be exposureMultiplier
//        return RenderSky(input, 1.0);
//    }
//
//
//    ENDHLSL
//
//    SubShader
//    {
//        Tags{ "RenderPipeline" = "UniversalPipeline" }
//
//        // For cubemap
//        Pass
//        {
//            Name "FragBaking"
//            ZWrite Off
//            ZTest Always
//            Blend Off
//            Cull Off
//
//            HLSLPROGRAM
//                #pragma vertex Vert
//                #pragma fragment FragBaking
//            ENDHLSL
//        }
//
//        // For fullscreen Sky
//        Pass
//        {
//            Name "FragRender"
//            ZWrite Off
//            ZTest LEqual
//            Blend Off
//            Cull Off
//
//            HLSLPROGRAM
//                #pragma vertex Vert
//                #pragma fragment FragRender
//            ENDHLSL
//        }
//    }
//
//    CustomEditor "UnityEditor.DanbaidongGUI.DanbaidongGUI"
//    Fallback Off
//}
