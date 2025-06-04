//copy from Mobile Bloom
static const float Weights[9] =
{
    0.5352615, 0.7035879, 0.8553453, 0.9616906, 1, 0.9616906, 0.8553453, 0.7035879, 0.5352615
};

half4 EncodeHDR(half3 color)
{
    #if _USE_RGBM
    half4 outColor = EncodeRGBM(color);
    #else
    half4 outColor = half4(color, 1.0);
    #endif

    return outColor;
}

half3 DecodeHDR(half4 color)
{
    #if _USE_RGBM
    return DecodeRGBM(color);
    #else
    return color.xyz;
    #endif
}

//------------------------------------------------//
TEXTURE2D_X(_MipDown0);
TEXTURE2D_X(_MipDown1);
TEXTURE2D_X(_MipDown2);
TEXTURE2D_X(_CharacterMaskTexture);

TEXTURE2D_X(_Bloom_Texture);

float4 _RimParams;
float4 _Params;
float4 _BlurCompositeWeight;
float4 _ColorTint;
float2 _BlurScaler;
float _RimIntensity;

#define RimWidth        _RimParams.x
#define RimSpread       _RimParams.y
#define RimMinRange     _RimParams.z
#define RimMaxRange     _RimParams.w
#define Threshold       _Params.x
#define LumRangeScale   _Params.y
#define PreFilterScale  _Params.z
#define Intensity       _Params.w

#if SHADER_API_GLES
struct a2v_preFilter
{
    float4 positionOS       : POSITION;
    float2 uv               : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
#else
struct a2v_preFilter
{
    uint vertexID : SV_VertexID;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
#endif
struct v2f_preFilter
{
    float4 positionHCS : SV_POSITION;
    float4 uv0 : TEXCOORD0;
    float4 uv1 : TEXCOORD1;
};

v2f_preFilter VertPreFilter_v2(a2v_preFilter v)
{
    v2f_preFilter o;
    UNITY_SETUP_INSTANCE_ID(v);

    #if SHADER_API_GLES
    float4 pos = v.positionOS;
    float2 uv = v.uv;
    #else
    float4 pos = GetFullScreenTriangleVertexPosition(v.vertexID);
    float2 uv = GetFullScreenTriangleTexCoord(v.vertexID);
    #endif

    o.positionHCS = pos;
    uv = uv * _BlitScaleBias.xy + _BlitScaleBias.zw;

    o.uv0.xy = uv + half2(-1, -1) * _BlitTexture_TexelSize.xy;
    o.uv0.zw = uv + half2(1, -1) * _BlitTexture_TexelSize.xy;
    o.uv1.xy = uv + half2(-1, 1) * _BlitTexture_TexelSize.xy;
    o.uv1.zw = uv + half2(1, 1) * _BlitTexture_TexelSize.xy;

    return o;
}

half4 FragPreFilter_v2(v2f_preFilter i) : SV_Target
{
    half3 mainCol = 0;

    mainCol += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.uv0.xy).xyz;
    mainCol += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.uv0.zw).xyz;
    mainCol += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.uv1.xy).xyz;
    mainCol += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.uv1.zw).xyz;
    mainCol /= 4;

    mainCol *= 1 / (1 + LumRangeScale * Luminance(mainCol.rgb));

    half brightness = Max3(mainCol.r, mainCol.g, mainCol.b);
    float thresholdKnee = Threshold * 0.5f;
    half softness = clamp(brightness - Threshold + thresholdKnee, 0.0, 2.0 * thresholdKnee);
    softness = (softness * softness) / (4.0 * thresholdKnee + 1e-4);
    half multiplier = max(brightness - Threshold, softness) / max(brightness, 1e-4);

    mainCol *= multiplier;
    mainCol = max(0, mainCol.rgb);
    mainCol *= PreFilterScale;
    mainCol = lerp(mainCol, _ColorTint.rgb * Luminance(mainCol.rgb), _ColorTint.a);

    return EncodeHDR(mainCol);
}


half4 FragBlur_pre(Varyings i) : SV_Target
{
    float2 scaler = _BlurScaler * _BlitTexture_TexelSize.xy;
    half3 s = 0;

    float2 offsetUV0 = i.texcoord.xy + scaler.xy * 5.307122000;
    float2 offsetUV1 = i.texcoord.xy - scaler.xy * 5.307122000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.035270680;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.035270680;

    offsetUV0 = i.texcoord.xy + scaler.xy * 3.373378000;
    offsetUV1 = i.texcoord.xy - scaler.xy * 3.373378000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.127357100;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.127357100;

    offsetUV0 = i.texcoord.xy + scaler.xy * 1.444753000;
    offsetUV1 = i.texcoord.xy - scaler.xy * 1.444753000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.259729700;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.259729700;

    offsetUV0 = i.texcoord.xy + scaler.xy * 0;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.155285200;

    return EncodeHDR(s);
}

#if SHADER_API_GLES
    struct a2v_downsampler
    {
        float4 positionOS       : POSITION;
        float2 uv               : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };
#else
struct a2v_downsampler
{
    uint vertexID : SV_VertexID;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
#endif
struct v2f_downsampler
{
    float4 positionHCS : SV_POSITION;
    float4 uv0 : TEXCOORD0;
    float4 uv1 : TEXCOORD1;
};

v2f_downsampler VertDownSample_v2(a2v_downsampler v)
{
    v2f_downsampler o;
    UNITY_SETUP_INSTANCE_ID(v);

    #if SHADER_API_GLES
                float4 pos = v.positionOS;
                float2 uv = v.uv;
    #else
    float4 pos = GetFullScreenTriangleVertexPosition(v.vertexID);
    float2 uv = GetFullScreenTriangleTexCoord(v.vertexID);
    #endif
    o.positionHCS = pos;
    uv = uv * _BlitScaleBias.xy + _BlitScaleBias.zw;

    o.uv0.xy = uv + half2(0.95999998, 0.25) * _BlitTexture_TexelSize.xy;
    o.uv0.zw = uv + half2(0.25, -0.95999998) * _BlitTexture_TexelSize.xy;
    o.uv1.xy = uv + half2(-0.95999998, -0.25) * _BlitTexture_TexelSize.xy;
    o.uv1.zw = uv + half2(-0.25, 0.95999998) * _BlitTexture_TexelSize.xy;

    return o;
}

half4 FragDownSample_v2(v2f_downsampler i) : SV_Target
{
    half3 s;
    s = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.uv0.xy));
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.uv0.zw));
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.uv1.xy));
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.uv1.zw));

    return EncodeHDR(s * 0.25);
}


half4 FragBlur_first(Varyings i) : SV_Target
{
    float2 scaler = _BlurScaler * _BlitTexture_TexelSize.xy;
    half3 s = 0;

    float2 offsetUV0 = i.texcoord.xy + scaler.xy * 7.324664000;
    float2 offsetUV1 = i.texcoord.xy - scaler.xy * 7.324664000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.017001690;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.017001690;

    offsetUV0 = i.texcoord.xy + scaler.xy * 5.368860000;
    offsetUV1 = i.texcoord.xy - scaler.xy * 5.368860000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.058725350;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.058725350;

    offsetUV0 = i.texcoord.xy + scaler.xy * 3.415373000;
    offsetUV1 = i.texcoord.xy - scaler.xy * 3.415373000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.138472900;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.138472900;

    offsetUV0 = i.texcoord.xy + scaler.xy * 1.463444000;
    offsetUV1 = i.texcoord.xy - scaler.xy * 1.463444000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.222984700;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.222984700;

    offsetUV0 = i.texcoord.xy + scaler.xy * 0;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.125630700;

    return EncodeHDR(s);
}


half4 FragBlur_second(Varyings i) : SV_Target
{
    float2 scaler = _BlurScaler * _BlitTexture_TexelSize.xy;
    half3 s = 0;

    float2 offsetUV0 = i.texcoord.xy + scaler.xy * 15.365450000;
    float2 offsetUV1 = i.texcoord.xy - scaler.xy * 15.365450000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.002165789;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.002165789;

    offsetUV0 = i.texcoord.xy + scaler.xy * 13.382110000;
    offsetUV1 = i.texcoord.xy - scaler.xy * 13.382110000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.006026655;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.006026655;

    offsetUV0 = i.texcoord.xy + scaler.xy * 11.399060000;
    offsetUV1 = i.texcoord.xy - scaler.xy * 11.399060000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.014561720;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.014561720;

    offsetUV0 = i.texcoord.xy + scaler.xy * 9.416246000;
    offsetUV1 = i.texcoord.xy - scaler.xy * 9.416246000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.030551590;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.030551590;

    offsetUV0 = i.texcoord.xy + scaler.xy * 7.433644000;
    offsetUV1 = i.texcoord.xy - scaler.xy * 7.433644000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.055660430;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.055660430;

    offsetUV0 = i.texcoord.xy + scaler.xy * 5.451206000;
    offsetUV1 = i.texcoord.xy - scaler.xy * 5.451206000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.088055510;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.088055510;

    offsetUV0 = i.texcoord.xy + scaler.xy * 3.468890000;
    offsetUV1 = i.texcoord.xy - scaler.xy * 3.468890000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.120967400;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.120967400;

    offsetUV0 = i.texcoord.xy + scaler.xy * 1.486653000;
    offsetUV1 = i.texcoord.xy - scaler.xy * 1.486653000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.144306200;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.144306200;

    offsetUV0 = i.texcoord.xy + scaler.xy * 0;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.075409520;

    return EncodeHDR(s);
}

half4 FragBlur_third(Varyings i) : SV_Target
{
    float2 scaler = _BlurScaler * _BlitTexture_TexelSize.xy;
    half3 s = 0;

    float2 offsetUV0 = i.texcoord.xy + scaler.xy * 19.391510000;
    float2 offsetUV1 = i.texcoord.xy - scaler.xy * 19.391510000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.001667595;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.001667595;

    offsetUV0 = i.texcoord.xy + scaler.xy * 17.402340000;
    offsetUV1 = i.texcoord.xy - scaler.xy * 17.402340000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.003832045;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.003832045;

    offsetUV0 = i.texcoord.xy + scaler.xy * 15.413260000;
    offsetUV1 = i.texcoord.xy - scaler.xy * 15.413260000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.008048251;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.008048251;

    offsetUV0 = i.texcoord.xy + scaler.xy * 13.424270000;
    offsetUV1 = i.texcoord.xy - scaler.xy * 13.424270000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.015449170;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.015449170;

    offsetUV0 = i.texcoord.xy + scaler.xy * 11.435350000;
    offsetUV1 = i.texcoord.xy - scaler.xy * 11.435350000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.027104610;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.027104610;

    offsetUV0 = i.texcoord.xy + scaler.xy * 9.446500000;
    offsetUV1 = i.texcoord.xy - scaler.xy * 9.446500000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.043462710;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.043462710;

    offsetUV0 = i.texcoord.xy + scaler.xy * 7.457702000;
    offsetUV1 = i.texcoord.xy - scaler.xy * 7.457702000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.063698220;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.063698220;

    offsetUV0 = i.texcoord.xy + scaler.xy * 5.468947000;
    offsetUV1 = i.texcoord.xy - scaler.xy * 5.468947000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.085324850;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.085324850;

    offsetUV0 = i.texcoord.xy + scaler.xy * 3.480224000;
    offsetUV1 = i.texcoord.xy - scaler.xy * 3.480224000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.104463000;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.104463000;

    offsetUV0 = i.texcoord.xy + scaler.xy * 1.491521000;
    offsetUV1 = i.texcoord.xy - scaler.xy * 1.491521000;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    offsetUV1.xy = clamp(offsetUV1.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.116892900;
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV1)) * 0.116892900;

    offsetUV0 = i.texcoord.xy + scaler.xy * 0;
    offsetUV0.xy = clamp(offsetUV0.xy, _BlitTexture_TexelSize.xy, 1 - _BlitTexture_TexelSize.xy);
    s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, offsetUV0)) * 0.060113440;

    return EncodeHDR(s);
}

half4 FragUpSample_v2(Varyings i) : SV_Target
{
    // half4 combineScale = half4(0.3,0.3,0.26,0.15);
    float4 combineScale = _BlurCompositeWeight;
    half3 main = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.texcoord)) * combineScale
        .x;
    half3 mip0 = DecodeHDR(SAMPLE_TEXTURE2D_X(_MipDown0, sampler_LinearClamp, i.texcoord)) *
        combineScale.y;
    half3 mip1 = DecodeHDR(SAMPLE_TEXTURE2D_X(_MipDown1, sampler_LinearClamp, i.texcoord)) *
        combineScale.z;
    half3 mip2 = DecodeHDR(SAMPLE_TEXTURE2D_X(_MipDown2, sampler_LinearClamp, i.texcoord)) *
        combineScale.w;


    return EncodeHDR(main + mip0 + mip1 + mip2);
}

float4 SampleSceneNormal(float2 uv)
{
    float4 normal = SAMPLE_TEXTURE2D_X(_CameraNormalsTexture, sampler_CameraNormalsTexture, UnityStereoTransformScreenSpaceTex(uv));

    #if defined(_GBUFFER_NORMALS_OCT)
                half2 remappedOctNormalWS = Unpack888ToFloat2(normal); // values between [ 0,  1]
                half2 octNormalWS = remappedOctNormalWS.xy * 2.0h - 1.0h;    // values between [-1, +1]
                normal.xyz = UnpackNormalOctQuadEncode(octNormalWS);
    #endif

    return normal;
}

half4 frag(Varyings input) : SV_Target
{
    float2 uv = SCREEN_COORD_APPLY_SCALEBIAS(UnityStereoTransformScreenSpaceTex(input.texcoord));
    float2 uvBloom = uv;
    half4 bloom = SAMPLE_TEXTURE2D_X(_Bloom_Texture, sampler_LinearClamp,
                                     SCREEN_COORD_REMOVE_SCALEBIAS(uvBloom));

    half charectorMask = SAMPLE_TEXTURE2D_X(_CharacterMaskTexture, sampler_LinearClamp,
                                            SCREEN_COORD_REMOVE_SCALEBIAS(uv)).r;
    half3 bloomedCol = bloom.xyz * _Params.w * charectorMask;
    half3 rawColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, SCREEN_COORD_REMOVE_SCALEBIAS(uv)).rgb;

    half3 finalCol = lerp(rawColor, bloomedCol + rawColor, charectorMask);

    return half4(finalCol, 1);
}
