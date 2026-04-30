Shader "Hidden/VividRP/FinalBlit"
{
    SubShader
    {
        Tags { "RenderPipeline" = "VividRenderPipeline" }

        Pass
        {
            Name "Blit"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ _FILM_GRAIN
            #pragma multi_compile_local _ _VIGNETTE
            #pragma multi_compile_local _ _BLOOM
            #pragma multi_compile_local _ _BLOOM_HQ
            #pragma multi_compile_local _ _BLOOM_DIRT

            #include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"

            TEXTURE2D(_BlitTexture);
            SAMPLER(sampler_BlitTexture);
            TEXTURE3D(_VividColorGradingLut);
            SAMPLER(sampler_VividColorGradingLut);
            float4 _BlitScaleBias;
            float4 _VividColorGradingParams;
            StructuredBuffer<float4> _VividAutoExposureBuffer;
            StructuredBuffer<float4> _VividAutoExposurePreExposureBuffer;
            float4 _VividAutoExposureParams;

            #if defined(_VIGNETTE)
            TEXTURE2D(_VividVignetteMask);
            SAMPLER(sampler_VividVignetteMask);
            float4 _VividVignetteParams1; // xy: center, z: mode (0 procedural, 1 masked)
            float4 _VividVignetteParams2; // x: intensity, y: smoothness, z: roundness, w: rounded
            float4 _VividVignetteColor;   // xyz: color, w: masked opacity
            float4 _VividVignetteScreenParams;

            #define VividVignetteCenter     _VividVignetteParams1.xy
            #define VividVignetteMode       _VividVignetteParams1.z
            #define VividVignetteIntensity  _VividVignetteParams2.x
            #define VividVignetteSmoothness _VividVignetteParams2.y
            #define VividVignetteRoundness  _VividVignetteParams2.z
            #define VividVignetteRounded    _VividVignetteParams2.w
            #define VividVignetteColor      _VividVignetteColor.xyz
            #define VividVignetteOpacity    _VividVignetteColor.w
            #endif

            #if defined(_BLOOM)
            TEXTURE2D(_VividBloomTexture);
            SAMPLER(sampler_VividBloomTexture);
            float4 _VividBloomParams;   // x: intensity, y: dirtIntensity, z: bloomEnabled, w: dirtEnabled
            float4 _VividBloomTint;     // xyz: tint color
            float4 _VividBloomDirtScale; // xy: scale, zw: offset
            #if defined(_BLOOM_DIRT)
            TEXTURE2D(_VividBloomDirtTexture);
            SAMPLER(sampler_VividBloomDirtTexture);
            #endif
            #endif

            #if defined(_FILM_GRAIN)
            TEXTURE2D(_VividFilmGrainTexture);
            float4 _VividFilmGrainParams;   // x: intensity, y: response
            float4 _VividFilmGrainTexParams; // x: scaleX, y: scaleY, z: offsetX, w: offsetY
            #endif


            #define DYNAMIC_SCALING_APPLY_SCALEBIAS(uv)  DynamicScalingApplyScaleBias(uv, _BlitScaleBias)
            #define DYNAMIC_SCALING_REMOVE_SCALEBIAS(uv) DynamicScalingRemoveScaleBias(uv, _BlitScaleBias)

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float2 screenUV   : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float4 pos = GetFullScreenTriangleVertexPosition(input.vertexID);
                float2 uv = GetFullScreenTriangleTexCoord(input.vertexID);

                output.positionCS = pos;
                output.uv = DYNAMIC_SCALING_APPLY_SCALEBIAS(uv);
                output.screenUV = uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, input.uv);
                float oneOverPreExposure = rcp(max(_VividAutoExposurePreExposureBuffer[0].x, 1e-4));
                float exposureScale = _VividAutoExposureParams.x > 0.5
                    ? _VividAutoExposureBuffer[0].x * oneOverPreExposure
                    : 1.0;
                float3 postProcessed = color.rgb * _VividColorGradingParams.w * exposureScale;

                if (_VividColorGradingParams.z > 0.5)
                {
                    float3 lutSpace = saturate(LinearToLogC(max(postProcessed, 0.0)));
                    postProcessed = ApplyLut3D(
                        TEXTURE3D_ARGS(_VividColorGradingLut, sampler_VividColorGradingLut),
                        lutSpace,
                        _VividColorGradingParams.xy);
                }

                #if defined(_BLOOM)
                {
                    #if defined(_BLOOM_HQ)
                    float4 bloomBicubicParams = float4(
                        _ScreenParams.xy,
                        1.0 / _ScreenParams.x,
                        1.0 / _ScreenParams.y);
                    float2 maxCoord = 1.0 - bloomBicubicParams.zw;
                    float3 bloom = SampleTexture2DBicubic(
                        TEXTURE2D_ARGS(_VividBloomTexture, sampler_VividBloomTexture),
                        input.uv, bloomBicubicParams, maxCoord, 0).xyz;
                    #else
                    float3 bloom = SAMPLE_TEXTURE2D(_VividBloomTexture, sampler_VividBloomTexture, input.uv).xyz;
                    #endif

                    bloom *= _VividBloomTint.xyz * _VividBloomParams.x;

                    #if defined(_BLOOM_DIRT)
                    float2 dirtUV = input.uv * _VividBloomDirtScale.xy + _VividBloomDirtScale.zw;
                    float3 dirt = SAMPLE_TEXTURE2D(_VividBloomDirtTexture, sampler_VividBloomDirtTexture, dirtUV).xyz;
                    bloom += dirt * _VividBloomParams.y;
                    #endif

                    postProcessed += bloom;
                }
                #endif

                #if defined(_VIGNETTE)
                {
                    if (VividVignetteMode < 0.5)
                    {
                        float2 d = abs(input.screenUV - VividVignetteCenter) * VividVignetteIntensity;
                        d.x *= lerp(1.0, _VividVignetteScreenParams.x / _VividVignetteScreenParams.y, VividVignetteRounded);
                        d = pow(saturate(d), VividVignetteRoundness);
                        float vfactor = pow(saturate(1.0 - dot(d, d)), VividVignetteSmoothness);
                        postProcessed *= lerp(VividVignetteColor, (1.0).xxx, vfactor);
                    }
                    else
                    {
                        float vfactor = SAMPLE_TEXTURE2D(_VividVignetteMask, sampler_VividVignetteMask, input.screenUV).w;
                        vfactor = FastSRGBToLinear(vfactor);
                        float3 newColor = postProcessed * lerp(VividVignetteColor, (1.0).xxx, vfactor);
                        postProcessed = lerp(postProcessed, newColor, VividVignetteOpacity);
                    }
                }
                #endif

                #if defined(_FILM_GRAIN)
                {
                    postProcessed = saturate(postProcessed);

                    float2 grainUV = input.uv * _VividFilmGrainTexParams.xy + _VividFilmGrainTexParams.zw;
                    float grain = SAMPLE_TEXTURE2D(_VividFilmGrainTexture, sampler_LinearRepeat, grainUV).w;

                    // Remap from [0, 1] to [-1, 1]
                    grain = (grain - 0.5) * 2.0;

                    // Match HDRP's noisiness response curve based on scene luminance.
                    float lum = Luminance(postProcessed);
                    lum = 1.0 - sqrt(lum);
                    lum = lerp(1.0, lum, _VividFilmGrainParams.y);

                    postProcessed += postProcessed * grain * _VividFilmGrainParams.x * lum;
                }
                #endif

                return float4(postProcessed, color.a);
            }
            ENDHLSL
        }
    }
}
