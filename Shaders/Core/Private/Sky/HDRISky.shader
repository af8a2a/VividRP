Shader "Hidden/VividRP/HDRISky"
{
    Properties
    {
        [NoScaleOffset] _DepthTexture("Depth", 2D) = "white" {}
        [NoScaleOffset] _SkyCubemap("Sky Cubemap", Cube) = "" {}
        [HDR] _SkyTint("Sky Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _SkyParam("Sky Param", Vector) = (0, 1, 0, 0)
    }

    HLSLINCLUDE
    #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
    #include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/Sky/SkyUtils.hlsl"
    TEXTURE2D_X_FLOAT(_DepthTexture);
    SAMPLER(sampler_DepthTexture);
    TEXTURECUBE(_SkyCubemap);
    SAMPLER(sampler_SkyCubemap);
    float4 _SkyCubemap_HDR;
    float4 _SkyTint;
    float4 _SkyParam;
    
    #define _Intensity          _SkyParam.x
    #define _CosPhi             _SkyParam.z
    #define _SinPhi             _SkyParam.w
    #define _CosSinPhi          _SkyParam.zw

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


    float3 GetSkyColor(float3 dir)
    {
        #if defined(DISTORTION_PROCEDURAL) || defined(DISTORTION_FLOWMAP)
        if (dir.y >= 0 || !_UpperHemisphere)
        {
            float2 alpha = frac(float2(_ScrollFactor, _ScrollFactor + 0.5)) - 0.5;

        #ifdef DISTORTION_FLOWMAP
        float3 tangent = normalize(cross(dir, float3(0.0, 1.0, 0.0)));
        float3 bitangent = cross(tangent, dir);

        float3 windDir = RotationUp(dir, _ScrollDirection);
        float2 flow = SAMPLE_TEXTURE2D_LOD(_Flowmap, sampler_Flowmap, GetLatLongCoords(windDir, _UpperHemisphere), 0).rg * 2.0 - 1.0;

        float3 dd = flow.x * tangent + flow.y * bitangent;
        #else
        float3 windDir = float3(_ScrollDirection.x, 0.0f, _ScrollDirection.y);
        float3 dd = windDir * sin(dir.y * PI * 0.5);
        #endif

        // Sample twice
        float3 color1 = DecodeHDREnvironment(SAMPLE_TEXTURECUBE_LOD(_Cubemap, sampler_Cubemap, dir + alpha.x * dd, 0), _Cubemap_HDR);
        float3 color2 = DecodeHDREnvironment(SAMPLE_TEXTURECUBE_LOD(_Cubemap, sampler_Cubemap, dir + alpha.y * dd, 0), _Cubemap_HDR);

        // Blend color samples
        return lerp(color1, color2, abs(2.0 * alpha.x));
        }
        else
        #endif

        return DecodeHDREnvironment(SAMPLE_TEXTURECUBE_LOD(_SkyCubemap, sampler_SkyCubemap, dir, 0), _SkyCubemap_HDR);
    }


    float4 GetColorWithRotation(float3 dir, float exposure, float2 cos_sin)
    {
        dir = RotationUp(dir, cos_sin);

        float3 skyColor = GetSkyColor(dir)*_Intensity*exposure;
        skyColor = ClampToFloat16Max(skyColor);

        return float4(skyColor, 1.0);
    }


    float4 RenderSky(Varyings input, float exposure)
    {
        float3 viewDirWS = GetSkyViewDirWS(input.positionCS.xy);

        // Reverse it to point into the scene
        float3 dir = -viewDirWS;

        return GetColorWithRotation(dir, exposure, _CosSinPhi);
    }
    
    
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "VividRenderPipeline"
        }

        Pass
        {
            Name "HDRISky"
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragRender

            #include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/AutoExposure.hlsl"
            float4 FragRender(Varyings input) : SV_Target
            {
                return float4(RenderSky(input,VividGetPreExposure()));
            }
            ENDHLSL
        }

        Pass
        {
            Name "HDRISkyBaking"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragBaking

            float4 FragBaking(Varyings input) : SV_Target
            {
                return float4(RenderSky(input, 1));
            }
            ENDHLSL
        }
    }

    FallBack Off
}
