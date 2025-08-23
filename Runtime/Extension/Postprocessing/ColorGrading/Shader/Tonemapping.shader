Shader "PostProcessing/ToneMapping"
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

            Name "CustomToneMapping"
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #pragma multi_compile_fragment _ _TONEMAP_NEUTRAL _TONEMAP_ACES _TONEMAP_GT _TONEMAP_AGX  _TONEMAP_AGX_APPROX
            #pragma multi_compile_fragment _ _HDR_GRADING

            #include "ToneMapping.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag

            TEXTURE2D(_InternalLut);
            TEXTURE2D(_UserLut);

            float4 _GTToneMap_Params0;
            float4 _GTToneMap_Params1;
            float4 _Lut_Params;
            float4 _UserLut_Params;


            #define LutParams               _Lut_Params.xyz
            #define PostExposure            _Lut_Params.w
            #define UserLutParams           _UserLut_Params.xyz
            #define UserLutContribution     _UserLut_Params.w
            #define GT_PARAM0               _GTToneMap_Params0
            #define GT_PARAM1               _GTToneMap_Params1


            real3 GetSRGBToLinear(real3 c)
            {
                #if _USE_FAST_SRGB_LINEAR_CONVERSION
    return FastSRGBToLinear(c);
                #else
                return SRGBToLinear(c);
                #endif
            }

            real4 GetSRGBToLinear(real4 c)
            {
                #if _USE_FAST_SRGB_LINEAR_CONVERSION
    return FastSRGBToLinear(c);
                #else
                return SRGBToLinear(c);
                #endif
            }

            real3 GetLinearToSRGB(real3 c)
            {
                #if _USE_FAST_SRGB_LINEAR_CONVERSION
    return FastLinearToSRGB(c);
                #else
                return LinearToSRGB(c);
                #endif
            }

            real4 GetLinearToSRGB(real4 c)
            {
                #if _USE_FAST_SRGB_LINEAR_CONVERSION
    return FastLinearToSRGB(c);
                #else
                return LinearToSRGB(c);
                #endif
            }


            half3 ApplyTonemap(half3 input)
            {
                #if _TONEMAP_ACES
                float3 aces = unity_to_ACES(input);
                input = AcesTonemap(aces);
                #elif _TONEMAP_NEUTRAL
                input = NeutralTonemap(input);
                #elif  _TONEMAP_GT
                    input.r = GranTurismoTonemap(input.r, GT_PARAM0.x, GT_PARAM0.y, GT_PARAM0.z, GT_PARAM0.w, GT_PARAM1.x, GT_PARAM0.y);
                    input.g = GranTurismoTonemap(input.g, GT_PARAM0.x, GT_PARAM0.y, GT_PARAM0.z, GT_PARAM0.w, GT_PARAM1.x, GT_PARAM0.y);
                    input.b = GranTurismoTonemap(input.b, GT_PARAM0.x, GT_PARAM0.y, GT_PARAM0.z, GT_PARAM0.w, GT_PARAM1.x, GT_PARAM0.y);
                #elif  _TONEMAP_AGX
                input = AgX(input);
                #elif  _TONEMAP_AGX_APPROX
                input = AgxApproximate(input);
                #endif

                return saturate(input);
            }

            half3 ApplyColorGrading(half3 input, float postExposure, TEXTURE2D_PARAM(lutTex, lutSampler), float3 lutParams,
                        TEXTURE2D_PARAM(userLutTex, userLutSampler), float3 userLutParams, float userLutContrib)
            {
                // Artist request to fine tune exposure in post without affecting bloom, dof etc
                input *= postExposure;

                // HDR Grading:
                //   - Apply internal LogC LUT
                //   - (optional) Clamp result & apply user LUT
                #if _HDR_GRADING
                {
                    float3 inputLutSpace = saturate(LinearToLogC(input)); // LUT space is in LogC
                    input = ApplyLut2D(TEXTURE2D_ARGS(lutTex, lutSampler), inputLutSpace, lutParams);

                    UNITY_BRANCH
                    if (userLutContrib > 0.0)
                    {
                        input = saturate(input);
                        input.rgb = GetLinearToSRGB(input.rgb); // In LDR do the lookup in sRGB for the user LUT
                        half3 outLut = ApplyLut2D(TEXTURE2D_ARGS(userLutTex, userLutSampler), input, userLutParams);
                        input = lerp(input, outLut, userLutContrib);
                        input.rgb = GetSRGBToLinear(input.rgb);
                    }
                }

                // LDR Grading:
                //   - Apply tonemapping (result is clamped)
                //   - (optional) Apply user LUT
                //   - Apply internal linear LUT
                #else
                {
                    input = ApplyTonemap(input);

                        UNITY_BRANCH
                    if (userLutContrib > 0.0)
                    {
                        input.rgb = GetLinearToSRGB(input.rgb); // In LDR do the lookup in sRGB for the user LUT
                        half3 outLut = ApplyLut2D(TEXTURE2D_ARGS(userLutTex, userLutSampler), input, userLutParams);
                        input = lerp(input, outLut, userLutContrib);
                        input.rgb = GetSRGBToLinear(input.rgb);
                    }

                    input = ApplyLut2D(TEXTURE2D_ARGS(lutTex, lutSampler), input, lutParams);
                }
                #endif

                return input;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.texcoord.xy;
                half4 inputColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);

                half3 color = inputColor.rgb;

                color = ApplyColorGrading(color, PostExposure, TEXTURE2D_ARGS(_InternalLut, sampler_LinearClamp), LutParams,
                      TEXTURE2D_ARGS(_UserLut, sampler_LinearClamp), UserLutParams, UserLutContribution);

                return half4(color, inputColor.a);
            }
            ENDHLSL
        }
    }
}