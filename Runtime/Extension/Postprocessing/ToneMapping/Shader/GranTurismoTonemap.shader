Shader "PostProcessing/CustomToneMapping"
{
    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
        }
        LOD 100

        Pass
        {
            
            Name "GranTurismoTonemap"
            HLSLPROGRAM
            #include "ToneMapping.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag


            half3 ApplyTonemap(half3 input, float4 tonemapParams0, float4 tonemapParams1)
            {
                input.r = GranTurismoTonemap(input.r, tonemapParams0.x, tonemapParams0.y, tonemapParams0.z, tonemapParams0.w, tonemapParams1.x,
                                             tonemapParams1.y);
                input.g = GranTurismoTonemap(input.g, tonemapParams0.x, tonemapParams0.y, tonemapParams0.z, tonemapParams0.w, tonemapParams1.x,
                                               tonemapParams1.y);
                input.b = GranTurismoTonemap(input.b, tonemapParams0.x, tonemapParams0.y, tonemapParams0.z, tonemapParams0.w, tonemapParams1.x,
                                                   tonemapParams1.y);
                return saturate(input);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.texcoord.xy;
                half4 col = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
                col.rgb = ApplyTonemap(col.rgb, _GTToneMap_Params0, _GTToneMap_Params1);

                return col;
            }
            ENDHLSL
        }
    }
}