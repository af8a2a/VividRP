Shader "Hidden/VividRP/Editor/DDGIProbePreview"
{
    Properties
    {
        [HideInInspector] _ProbeColor("Probe Color", Color) = (0.28, 0.72, 1.0, 0.32)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "ProbePreview"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"

            float4 _ProbeColor;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                Varyings output;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = normalWS;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float lighting = saturate(dot(normalWS, normalize(float3(0.35, 0.75, 0.55)))) * 0.45 + 0.55;
                return float4(_ProbeColor.rgb * lighting, _ProbeColor.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
