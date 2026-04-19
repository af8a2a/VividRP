Shader "VividRP/Material/SkyTextureVisualization"
{
    Properties
    {
        [Main(Display, _, on, off)] _Display("Display", Float) = 1
        [MainColor] [Sub(Display)] _Tint("Tint", Color) = (1, 1, 1, 1)
        [Sub(Display)] _Intensity("Intensity", Range(0.0, 8.0)) = 1.0
        [Sub(Display)] _DisplayGamma("Display Gamma", Range(0.25, 4.0)) = 1.0
        [Sub(Display)] _MipLevel("Mip Level", Range(0.0, 8.0)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "VividRenderPipeline"
        }

        HLSLINCLUDE
        #pragma target 4.5
        #pragma multi_compile_instancing

        #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
        #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/AutoExposure.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _Tint;
            float _Intensity;
            float _DisplayGamma;
            float _MipLevel;
        CBUFFER_END

        TEXTURECUBE(_SkyTexture);
        SAMPLER(sampler_SkyTexture);
        float4 _SkyTextureTint;
        float4 _SkyTextureParams;

        #define _SkyTextureExposure _SkyTextureParams.x
        #define _SkyTextureRotation _SkyTextureParams.y
        #define _SkyTextureMaxMip   _SkyTextureParams.z
        #define _SkyTextureEnabled  _SkyTextureParams.w

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 normalWS : TEXCOORD0;
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
            return output;
        }

        float3 RotateSkyDirectionAroundYAxis(float3 directionWS, float rotationDegrees)
        {
            float rotationRadians = radians(rotationDegrees);
            float s = 0.0;
            float c = 1.0;
            sincos(rotationRadians, s, c);

            return float3(
                c * directionWS.x - s * directionWS.z,
                directionWS.y,
                s * directionWS.x + c * directionWS.z);
        }

        float3 SampleDebugSkyTexture(float3 directionWS, float mipLevel)
        {
            if (_SkyTextureEnabled <= 0.5)
                return 0.0;

            float clampedMipLevel = min(max(mipLevel, 0.0), max(_SkyTextureMaxMip, 0.0));
            float3 rotatedDirectionWS = RotateSkyDirectionAroundYAxis(directionWS, _SkyTextureRotation);
            float3 skyRadiance = SAMPLE_TEXTURECUBE_LOD(_SkyTexture, sampler_TrilinearRepeat, rotatedDirectionWS, clampedMipLevel).rgb;
            return skyRadiance * _SkyTextureTint.rgb * _SkyTextureExposure;
        }

        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float3 directionWS = SafeNormalize(input.normalWS);
            float3 skyLighting = max(SampleDebugSkyTexture(directionWS, _MipLevel), 0.0);
            skyLighting *= _Tint.rgb * _Intensity;
            skyLighting = pow(max(skyLighting, 0.0), rcp(max(_DisplayGamma, 1e-4)));
            return half4(VividApplyPreExposure(skyLighting), 1.0);
        }
        ENDHLSL

        Pass
        {
            Name "VividForward"
            Tags
            {
                "LightMode" = "VividForward"
            }

            Blend One Zero
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "SRPDefaultUnlit"
            Tags
            {
                "LightMode" = "SRPDefaultUnlit"
            }

            Blend One Zero
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }

    CustomEditor "LWGUI.LWGUI"

    FallBack Off
}
