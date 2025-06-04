Texture2D _BlurTexture;

float _Contrast;
float _Intensity;
float _Filter;
float _Multiply;
float _BlurScale;

static const float Weights[9] = {
    0.5352615, 0.7035879, 0.8553453, 0.9616906, 1, 0.9616906, 0.8553453, 0.7035879, 0.5352615
};

float3 Contrast(float3 In, float Contrast)
{
    const float midpoint = pow(0.5, 2.2);
    return (In - midpoint) * Contrast + midpoint;
}

//------------------------------------------------//


half4 Frag_Contrast(Varyings i) : SV_Target
{
    half4 col = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, i.texcoord);

    col.rgb = Contrast(col.rgb, _Contrast);

    return col;
}

half4 frag_Blur1(Varyings i) : SV_Target
{
    float texelSize = _BlitTexture_TexelSize.x * _BlurScale;
    float2 uv = i.texcoord;

    half4 c0 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv - float2(texelSize * 3.23076923, 0));
    half4 c1 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv - float2(texelSize * 1.38461538, 0));
    half4 c2 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
    half4 c3 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2(texelSize * 1.38461538, 0));
    half4 c4 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2(texelSize * 3.23076923, 0));

    half4 color = c0 * 0.07027027 +
        c1 * 0.31621622 +
        c2 * 0.22702703 +
        c3 * 0.31621622 +
        c4 * 0.07027027;

    return color;
}

half4 frag_Blur2(Varyings i) : SV_Target
{
    float texelSize = _BlitTexture_TexelSize.y * _BlurScale;
    float2 uv = i.texcoord;

    half4 c0 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv - float2(0, texelSize * 3.23076923));
    half4 c1 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv - float2(0, texelSize * 1.38461538));
    half4 c2 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
    half4 c3 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2(0, texelSize * 1.38461538));
    half4 c4 = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2(0, texelSize * 3.23076923));

    half4 color = c0 * 0.07027027 +
        c1 * 0.31621622 +
        c2 * 0.22702703 +
        c3 * 0.31621622 +
        c4 * 0.07027027;

    return color;
}

half4 frag_Max(Varyings i) : SV_Target
{
    half4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, i.texcoord);

    color.rgb *= _Intensity;

    return color;
}

half4 frag_Multiply(Varyings i) : SV_Target
{
    half4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, i.texcoord);

    color.rgb = pow(color.rgb, _Multiply + 1.0f);

    return color;
}

//after tonemapping
half4 frag_Filter(Varyings i) : SV_Target
{
    half4 color =FastTonemap( SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, i.texcoord));
    half4 color2 = FastTonemap(SAMPLE_TEXTURE2D(_BlurTexture, sampler_LinearClamp, i.texcoord));

    color.rgb = 1 - (1 - color.rgb) * (1 - color2.rgb * _Filter);
    return FastTonemapInvert(color);
}
