Shader "Hidden/VividRP/SDF Surface Card Capture"
{
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            TEXTURE2D(_CardBaseMap);
            SAMPLER(sampler_CardBaseMap);
            TEXTURE2D(_CardMetallicMap);
            SAMPLER(sampler_CardMetallicMap);
            float4 _CardBaseMapST;
            float4 _CardBaseColor;
            float4 _CardMetallic;
            float4 _CardMinAndFace;
            float4 _CardInverseExtent;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalLS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float depth : TEXCOORD2;
            };

            Varyings Vert(float3 positionLS : POSITION, float3 normalLS : NORMAL, float2 uv : TEXCOORD0)
            {
                uint face = (uint)_CardMinAndFace.w;
                uint axis = face >> 1u;
                float3 local = (positionLS - _CardMinAndFace.xyz) * _CardInverseExtent.xyz;
                float2 cardUV = axis == 0u ? local.yz : axis == 1u ? local.xz : local.xy;
                float clipDepth = (face & 1u) == 0u ? local[axis] : 1.0 - local[axis];
                #if UNITY_REVERSED_Z
                    clipDepth = 1.0 - clipDepth;
                #else
                    clipDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, clipDepth);
                #endif
                Varyings output;
                output.positionCS = float4(cardUV * 2.0 - 1.0, clipDepth, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                    output.positionCS.y = -output.positionCS.y;
                #endif
                output.normalLS = normalLS;
                output.uv = uv * _CardBaseMapST.xy + _CardBaseMapST.zw;
                output.depth = local[axis];
                return output;
            }

            void Frag(Varyings input, out float4 albedoDepth : SV_Target0, out float4 normalCoverage : SV_Target1)
            {
                float3 albedo = SAMPLE_TEXTURE2D(_CardBaseMap, sampler_CardBaseMap, input.uv).rgb * _CardBaseColor.rgb;
                float metallic = _CardMetallic.x;
                if (_CardMetallic.w > 0.0)
                {
                    float4 channels = SAMPLE_TEXTURE2D(_CardMetallicMap, sampler_CardMetallicMap, input.uv);
                    metallic = lerp(_CardMetallic.y, _CardMetallic.z, _CardMetallic.w == 1.0 ? channels.g : channels.r);
                }
                uint face = (uint)_CardMinAndFace.w;
                float3 normal = SafeNormalize(input.normalLS);
                float sign = (face & 1u) == 0u ? -1.0 : 1.0;
                bool flipped = normal[face >> 1u] * sign < 0.0;
                normal *= flipped ? -1.0 : 1.0;
                albedoDepth = float4(saturate(albedo) * (1.0 - saturate(metallic)), saturate(input.depth));
                // Nonzero still means covered. Preserve original mesh facing without
                // changing the face-oriented normal used by existing lighting filters.
                normalCoverage = float4(normal * 0.5 + 0.5, flipped ? 0.5 : 1.0);
            }
            ENDHLSL
        }
    }
}
