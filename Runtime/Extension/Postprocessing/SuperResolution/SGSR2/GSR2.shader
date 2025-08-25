Shader "PostProcessing/GSR2"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"

    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

    TEXTURE2D_X(_MotionVectorTexture);
    float2 jitterOffset;
    float Biasmax_viewportXScale;
    TEXTURE2D(MotionDepthClipAlphaBuffer);
    TEXTURE2D(_PrevHistory);


    CBUFFER_START(Params)
        // float4x4 _ClipToPrevClip;
        float2 _RenderSize;
        float2 _OutputSize;
        float2 _RenderSizeRcp;
        float2 _OutputSizeRcp;
        float4 _JitterOffset;
        float2 _ScaleRatio;
        float _CameraFovAngleHor;
        float _MinLerpContribution;
        float _Reset;
    CBUFFER_END


    float2 DecodeVelocityFromTexture(float2 ev)
    {
        const float inv_div = 1.0f / (0.499f);
        float2 dv;
        dv.xy = ev.xy * inv_div - 32767.0f / 65535.0f * inv_div;
        return dv;
    }


    float FastLanczos(float base)
    {
        float y = base - 1.0f;
        float y2 = y * y;
        float y_temp = 0.75f * y + y2;
        return y_temp * y2;
    }
    ENDHLSL




    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100


        Pass
        {
            Name "Convert"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment convert_frag
            #pragma target 5.0

            void convert_frag(Varyings input,
                              out float depthclip:SV_Target0
            )
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);

                float2 texCoord = uv;


                float2 gatherCoord = texCoord - float2(0.5, 0.5) * _BlitTexture_TexelSize.xy;

                // Gather depth samples in a 4x4 grid
                // a  b  c  d
                // e  f  g  h
                // i  j  k  l
                // m  n  o  p
                float4 btmLeft = _CameraDepthTexture.Gather(sampler_LinearClamp, gatherCoord);
                float4 btmRight = _CameraDepthTexture.Gather(sampler_LinearClamp,
                                                             gatherCoord + float2(2.0, 0.0) * _RenderSizeRcp);
                float4 topLeft = _CameraDepthTexture.Gather(sampler_LinearClamp,
                                                            gatherCoord + float2(0.0, 2.0) * _RenderSizeRcp);
                float4 topRight = _CameraDepthTexture.Gather(sampler_LinearClamp,
                                                             gatherCoord + float2(2.0, 2.0) *
                                                             _RenderSizeRcp);

                #if UNITY_REVERSED_Z
                btmLeft = 1.0 - btmLeft;
                btmRight = 1.0 - btmRight;
                topLeft = 1.0 - topLeft;
                topRight = 1.0 - topRight;
                #endif

                // Find nearest depth (min for Unity's reversed Z-buffer)
                float maxC = min(min(min(btmLeft.z, btmRight.w), topLeft.y), topRight.x);
                float btmLeft4 = min(min(min(btmLeft.y, btmLeft.x), btmLeft.z), btmLeft.w);
                float btmLeftmin9 = min(topLeft.x, min(min(maxC, btmLeft4), btmRight.x));

                depthclip = 0.0;
                if (maxC < 1.0 - 1.0e-05f) // Reversed Z-buffer check
                {
                    float btmRight4 = min(min(min(btmRight.y, btmRight.x), btmRight.z), btmRight.w);
                    float topLeft4 = min(min(min(topLeft.y, topLeft.x), topLeft.z), topLeft.w);
                    float topRight4 = min(min(min(topRight.y, topRight.x), topRight.z), topRight.w);

                    float Wdepth = 0.0;
                    float Ksep = 1.37e-05f;
                    float Kfov = _CameraFovAngleHor;
                    float diagonal_length = length(_RenderSize);
                    float Ksep_Kfov_diagonal = Ksep * Kfov * diagonal_length;

                    float Depthsep = Ksep_Kfov_diagonal * maxC;
                    float EPSILON = 1.19e-07f;

                    // Calculate depth weights for each quadrant
                    Wdepth += saturate((Depthsep / (abs(maxC - btmLeft4) + EPSILON)));
                    Wdepth += saturate((Depthsep / (abs(maxC - btmRight4) + EPSILON)));
                    Wdepth += saturate((Depthsep / (abs(maxC - topLeft4) + EPSILON)));
                    Wdepth += saturate((Depthsep / (abs(maxC - topRight4) + EPSILON)));

                    depthclip = saturate(1.0f - Wdepth * 0.25);
                }

                // float2 motion = SAMPLE_TEXTURE2D(_MotionVectorTexture, sampler_LinearClamp, texCoord).xy;

                // if (motion.x > 0.0001f)
                // {
                //     motion.y = -motion.y;
                // }
                // else
                // {
                //     float3 Position = float3(texCoord, 1.0);
                //     float4 PreClip = mul(_ClipToPrevClip, float4(Position, 1.0));
                //     float2 PreScreen = PreClip.xy / PreClip.w;
                //     motion = Position.xy - PreScreen;
                // }


                // return float4(depthclip, 1.0);
            }
            ENDHLSL
        }
        Pass
        {
            Name "Upscale"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment upscale_frag
            #pragma target 5.0
            float4 upscale_frag(Varyings input) : SV_Target0
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);
                
                float2 texCoord =uv ;
                float Biasmax_viewportXScale = _ScaleRatio.x;
                float scalefactor = _ScaleRatio.y;

                float2 Hruv = texCoord;


                float2 Jitteruv = float2(
                    saturate(Hruv.x + (_JitterOffset.x * _OutputSizeRcp.x)),
                    saturate(Hruv.y + (_JitterOffset.y * _OutputSizeRcp.y))
                );

                int2 InputPos = int2(Jitteruv * _OutputSize);

                
                float2 motion = SAMPLE_TEXTURE2D(_MotionVectorTexture, sampler_LinearClamp,
                                                 float4(Jitteruv, 0, 0)).xy;

                float2 PrevUV = float2(
                    saturate(-0.5 * motion.x + Hruv.x),
                    saturate(-0.5 * motion.y + Hruv.y)
                );

                half depthfactor = SAMPLE_TEXTURE2D(MotionDepthClipAlphaBuffer, sampler_LinearClamp,
                                             float4(Jitteruv, 0, 0)).r;
                half3 HistoryColor = SAMPLE_TEXTURE2D(_PrevHistory, sampler_LinearClamp, float4(PrevUV, 0, 0)).xyz;
                // Upsampling with Lanczos filter
                float4 Upsampledcw = float4(0.0, 0.0, 0.0, 0.0);
                half kernelfactor = _Reset;
                half biasmax = Biasmax_viewportXScale;
                half biasmin = max(1.0f, 0.3 + 0.3 * biasmax);
                half biasfactor = max(0.25f * depthfactor, kernelfactor);
                half kernelbias = lerp(biasmax, biasmin, biasfactor);
                half motion_viewport_len = length(motion * _OutputSize.xy);
                half curvebias = lerp(-2.0, -3.0, saturate(motion_viewport_len * 0.02));

                float3 rectboxcenter = float3(0.0, 0.0, 0.0);
                float3 rectboxvar = float3(0.0, 0.0, 0.0);
                float rectboxweight = 0.0;
                float2 srcpos = float2(InputPos) + float2(0.5, 0.5) - _JitterOffset;


                kernelbias *= 0.5f;
                float kernelbias2 = kernelbias * kernelbias;
                float2 srcpos_srcOutputPos = srcpos - Hruv * _RenderSize;


                // Sample and process 9 points
                half3 rectboxmin, rectboxmax;
                half3 centerColor = _BlitTexture.Load(int3(InputPos, 0)).xyz;
                // Center sample
                {
                    half2 baseoffset = srcpos_srcOutputPos;
                    half baseoffset_dot = dot(baseoffset, baseoffset);
                    half base = clamp(baseoffset_dot * kernelbias2, 0.0f, 1.0f);
                    half weight = FastLanczos(base);
                    Upsampledcw += half4(centerColor * weight, weight);
                    half boxweight = exp(baseoffset_dot * curvebias);
                    rectboxmin = centerColor;
                    rectboxmax = centerColor;
                    half3 wsample = centerColor * boxweight;
                    rectboxcenter += wsample;
                    rectboxvar += (centerColor * wsample);
                    rectboxweight += boxweight;
                }


                // Sample surrounding pixels
                int2 offsets[8] = {
                    int2(-1, 0), int2(1, 0), int2(0, -1), int2(0, 1),
                    int2(-1, -1), int2(1, -1), int2(-1, 1), int2(1, 1)
                };


                for (int i = 0; i < 8; i++)
                {
                    half3 sampleColor = _BlitTexture.Load(int3(InputPos + offsets[i], 0)).xyz;
                    half2 baseoffset = srcpos_srcOutputPos + half2(offsets[i]);
                    half baseoffset_dot = dot(baseoffset, baseoffset);
                    half base = clamp(baseoffset_dot * kernelbias2, 0.0f, 1.0f);
                    half weight = FastLanczos(base);
                    Upsampledcw += half4(sampleColor * weight, weight);
                    half boxweight = exp(baseoffset_dot * curvebias);
                    rectboxmin = min(rectboxmin, sampleColor);
                    rectboxmax = max(rectboxmax, sampleColor);
                    half3 wsample = sampleColor * boxweight;
                    rectboxcenter += wsample;
                    rectboxvar += (sampleColor * wsample);
                    rectboxweight += boxweight;
                }


                // Normalize box statistics
                rectboxweight = 1.0f / rectboxweight;
                rectboxcenter *= rectboxweight;
                rectboxvar *= rectboxweight;
                rectboxvar = sqrt(abs(rectboxvar - rectboxcenter * rectboxcenter));

                half3 bias = half3(0.05f, 0.05f, 0.05f);

                Upsampledcw.xyz = clamp(Upsampledcw.xyz / Upsampledcw.w, rectboxmin - bias, rectboxmax + bias);
                Upsampledcw.w = Upsampledcw.w * (1.0f / 3.0f);


                half baseupdate = 1.0f - depthfactor;
                baseupdate = min(baseupdate, lerp(baseupdate, Upsampledcw.w * 10.0f,
                                                  clamp(10.0f * motion_viewport_len, 0.0, 1.0)));
                baseupdate = min(baseupdate, lerp(baseupdate, Upsampledcw.w,
                                                                  clamp(motion_viewport_len * 0.05f, 0.0, 1.0)));
                half basealpha = baseupdate;


                const float EPSILON = 1.192e-07f;
                half boxscale = max(depthfactor, clamp(motion_viewport_len * 0.05f, 0.0, 1.0));
                half boxsize = lerp(scalefactor, 1.0f, boxscale);
                half3 sboxvar = rectboxvar * boxsize;
                half3 boxmin = rectboxcenter - sboxvar;
                half3 boxmax = rectboxcenter + sboxvar;
                rectboxmax = min(rectboxmax, boxmax);
                rectboxmin = max(rectboxmin, boxmin);


                half3 clampedcolor = clamp(HistoryColor, rectboxmin, rectboxmax);
                half startLerpValue = _MinLerpContribution;
                if ((abs(motion.x) + abs(motion.y)) > 0.000001) startLerpValue = 0.0;
                half lerpcontribution = (any(rectboxmin > HistoryColor) || any(HistoryColor > rectboxmax))
                        ? startLerpValue
                        : 1.0f;

                HistoryColor = lerp(clampedcolor, HistoryColor, saturate(lerpcontribution));
                half basemin = min(basealpha, 0.1f);
                basealpha = lerp(basemin, basealpha, saturate(lerpcontribution));


                ////blend color
                half alphasum = max(EPSILON, basealpha + Upsampledcw.w);
                half alpha = saturate(Upsampledcw.w / alphasum + _Reset);


                Upsampledcw.xyz = lerp(HistoryColor, Upsampledcw.xyz, alpha);

                return float4(Upsampledcw.xyz, 1.0);
            }
            ENDHLSL
        }


    }
}