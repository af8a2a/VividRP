Shader "VividRP/Material/ReflectionProbeUsageDebug"
{
    Properties
    {
        [Main(Display, _, on, off)] _Display("Display", Float) = 1
        [SubEnum(Display, WeightedRadiance, 0, AverageRadiance, 1, Weight, 2, MipLevel, 3, ReflectionDirection, 4, ProbeCount, 5)] _DebugMode("Mode", Float) = 0
        [MainColor] [Sub(Display)] _Tint("Tint", Color) = (1, 1, 1, 1)
        [Sub(Display)] [HDR] _MissingColor("Missing Probe Color", Color) = (1, 0, 1, 1)
        [Sub(Display)] _Intensity("Intensity", Range(0.0, 16.0)) = 1.0
        [Sub(Display)] _DisplayGamma("Display Gamma", Range(0.25, 4.0)) = 1.0
        [Sub(Display)] _PerceptualRoughness("Perceptual Roughness", Range(0.0, 1.0)) = 0.35
        [Sub(Display)] _ProbeCountScale("Probe Count Scale", Range(1.0, 32.0)) = 8.0
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
        #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/LightingLoop.hlsl"

        #define VIVID_REFLECTION_PROBE_USAGE_DEBUG_WEIGHTED_RADIANCE 0
        #define VIVID_REFLECTION_PROBE_USAGE_DEBUG_AVERAGE_RADIANCE 1
        #define VIVID_REFLECTION_PROBE_USAGE_DEBUG_WEIGHT 2
        #define VIVID_REFLECTION_PROBE_USAGE_DEBUG_MIP_LEVEL 3
        #define VIVID_REFLECTION_PROBE_USAGE_DEBUG_REFLECTION_DIRECTION 4
        #define VIVID_REFLECTION_PROBE_USAGE_DEBUG_PROBE_COUNT 5

        CBUFFER_START(UnityPerMaterial)
            float4 _Tint;
            float4 _MissingColor;
            float _DebugMode;
            float _Intensity;
            float _DisplayGamma;
            float _PerceptualRoughness;
            float _ProbeCountScale;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
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
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            return output;
        }

        float3 GetDebugViewDirectionWS(float3 positionWS)
        {
            if (unity_OrthoParams.w > 0.5)
                return SafeNormalize(TransformViewToWorldDir(float3(0.0, 0.0, -1.0), true));

            return SafeNormalize(_WorldSpaceCameraPos.xyz - positionWS);
        }

        float3 GetDebugHeatColor(float value)
        {
            float t = saturate(value);
            float3 cold = lerp(float3(0.0, 0.0, 1.0), float3(0.0, 1.0, 1.0), saturate(t * 2.0));
            float3 hot = lerp(float3(1.0, 1.0, 0.0), float3(1.0, 0.0, 0.0), saturate(t * 2.0 - 1.0));
            return lerp(cold, hot, step(0.5, t));
        }

        float3 ApplyDebugDisplayScale(float3 color)
        {
            color *= _Tint.rgb * _Intensity;
            return pow(max(color, 0.0), rcp(max(_DisplayGamma, 1e-4)));
        }

        float3 EvaluateReflectionProbeUsageDebugColor(Varyings input)
        {
            uint2 pixelCoord = uint2(input.positionCS.xy);
            float3 normalWS = SafeNormalize(input.normalWS);
            float3 viewDirectionWS = GetDebugViewDirectionWS(input.positionWS);
            float3 reflectionDirectionWS = SafeNormalize(reflect(-viewDirectionWS, normalWS));
            float perceptualRoughness = saturate(_PerceptualRoughness);

            VividLightingLoopContext lightLoop = VividLightingLoop::Create(pixelCoord, input.positionWS);
            uint reflectionProbeCount = VividLightingLoop::GetReflectionProbeCount(lightLoop);

            float3 weightedRadiance = 0.0;
            float reflectionProbeWeight = 0.0;
            bool hasReflectionProbe = VividLightingLoop::TryEvaluateReflectionProbes(
                lightLoop,
                input.positionWS,
                normalWS,
                reflectionDirectionWS,
                perceptualRoughness,
                weightedRadiance,
                reflectionProbeWeight);


            int debugMode = (int)_DebugMode;
            if (debugMode == VIVID_REFLECTION_PROBE_USAGE_DEBUG_MIP_LEVEL)
            {
                float maxMipLevel = max((float)_ReflectionAtlasMipCount - 1.0, 1.0);
                return GetDebugHeatColor(GetReflectionProbeAtlasMipLevel(perceptualRoughness) / maxMipLevel);
            }

            if (debugMode == VIVID_REFLECTION_PROBE_USAGE_DEBUG_REFLECTION_DIRECTION)
                return reflectionDirectionWS * 0.5 + 0.5;

            if (debugMode == VIVID_REFLECTION_PROBE_USAGE_DEBUG_PROBE_COUNT)
                return GetDebugHeatColor((float)reflectionProbeCount / max(_ProbeCountScale, 1.0));

            if (!hasReflectionProbe)
                return _MissingColor.rgb;

            if (debugMode == VIVID_REFLECTION_PROBE_USAGE_DEBUG_AVERAGE_RADIANCE)
                return weightedRadiance / max(reflectionProbeWeight, 1e-4);

            if (debugMode == VIVID_REFLECTION_PROBE_USAGE_DEBUG_WEIGHT)
                return GetDebugHeatColor(reflectionProbeWeight);

            return weightedRadiance;
        }

        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float3 debugColor = EvaluateReflectionProbeUsageDebugColor(input);
            // return half4(debugColor,1);
            return half4(VividApplyPreExposure(ApplyDebugDisplayScale(debugColor)), 1.0);
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

    }

    CustomEditor "LWGUI.LWGUI"

    FallBack Off
}
