
float3 AcesFilm(float3 x)
{
    float a = 2.51f;
    float b = 0.03f;
    float c = 2.43f;
    float d = 0.59f;
    float e = 0.14f;
    return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
}

float W_f(float x, float e0, float e1)
{
    if (x <= e0)
        return 0;
    if (x >= e1)
        return 1;
    float a = (x - e0) / (e1 - e0);
    return a * a * (3 - 2 * a);
}

float H_f(float x, float e0, float e1)
{
    if (x <= e0)
        return 0;
    if (x >= e1)
        return 1;
    return (x - e0) / (e1 - e0);
}

//ref
//https://www.desmos.com/calculator/gslcdxvipg?lang=zh-CN
//see also:http://cdn2.gran-turismo.com/data/www/pdi_publications/PracticalHDRandWCGinGTS_20181222.pdf
//see also:https://www.slideshare.net/nikuque/hdr-theory-and-practicce-jp#87
float GranTurismoTonemap(float x, float P, float a, float m, float l, float c, float b)
{
    float l0 = (P - m) * l / a;
    float L0 = m - m / a;
    float L1 = m + (1 - m) / a;
    float L_x = m + a * (x - m);
    float T_x = m * pow(x / m, c) + b;
    float S0 = m + l0;
    float S1 = m + a * l0;
    float C2 = a * P / (P - S1);
    float S_x = P - (P - S1) * exp(-(C2 * (x - S0) / P));
    float w0_x = 1 - W_f(x, 0, m);
    float w2_x = H_f(x, m + l0, m + l0);
    float w1_x = 1 - w0_x - w2_x;
    float f_x = T_x * w0_x + L_x * w1_x + S_x * w2_x;
    return f_x;
}


float3 AgxApproximate(float3 x)
{
    float3 x2 = x * x;
    float3 x4 = x2 * x2;
    float3 x6 = x4 * x2;

    return -17.86 * x6 * x
        + 78.01 * x6
        - 126.7 * x4 * x
        + 92.06 * x4
        - 28.72 * x2 * x
        + 4.361 * x2
        - 0.1718 * x
        + 0.002857;
}


/*-------------------------------------------------------------------------------------------------
# Function `tonemapAgX`
> Benjamin Wrensch's approximation to the AgX tone mapping curve by Troy Sobotka.

From https://iolite-engine.com/blog_posts/minimal_agx_implementation
-------------------------------------------------------------------------------------------------*/
inline float3 tonemapAgX(float3 color)
{
    // Input transform
    const float3x3 agx_mat = float3x3(0.842479062253094F, 0.0423282422610123F, 0.0423756549057051F,  //
                                      0.0784335999999992F, 0.878468636469772F, 0.0784336F,           //
                                      0.0792237451477643F, 0.0791661274605434F, 0.879142973793104F);
    color                  = mul(color, agx_mat);

    // Log2 space encoding
    const float min_ev = -12.47393f;
    const float max_ev = 4.026069f;
    color              = clamp(log2(color), min_ev, max_ev);
    color              = (color - min_ev) / (max_ev - min_ev);

    // Apply 6th-order sigmoid function approximation
    float3 v =((15.5f)* color+ (-40.14f));
    v        =(color  *   v  + (31.96f));
    v        =(color  *   v  + (-6.868f));
    v        =(color  *   v  + (0.4298f));
    v        =(color  *   v  + (0.1191f));
    v        =(color  *   v  + (-0.0023f));

    // Output transform
    const float3x3 agx_mat_inv = float3x3(1.19687900512017F, -0.0528968517574562F, -0.0529716355144438F,  //
                                          -0.0980208811401368F, 1.15190312990417F, -0.0980434501171241F,  //
                                          -0.0990297440797205F, -0.0989611768448433F, 1.15107367264116F);
    v                          = mul(v, agx_mat_inv);


    return pow(v, 2.2);
}


/*-------------------------------------------------------------------------------------------------
# Function `tonemapKhronosPBR`
> The Khronos PBR neutral tone mapper.

Adapted from https://github.com/KhronosGroup/ToneMapping/blob/main/PBR_Neutral/pbrNeutral.glsl
-------------------------------------------------------------------------------------------------*/
inline float3 tonemapKhronosPBR(float3 color)
{
    const float startCompression = 0.8F - 0.04F;
    const float desaturation     = 0.15F;

    float x    = min(color.x, min(color.y, color.z));
    float peak = max(color.x, max(color.y, color.z));

    float offset = x < 0.08F ? x * (-6.25F * x + 1.F) : 0.04F;
    color -= offset;

    if(peak >= startCompression)
    {
        const float d       = 1.F - startCompression;
        float       newPeak = 1.F - d * d / (peak + d - startCompression);
        color *= newPeak / peak;

        float g = 1.F - 1.F / (desaturation * (peak - newPeak) + 1.F);
        color   = lerp(color, newPeak, g);
    }
    return color;
}


float3 VividTonemap(float3 input)
{
    #if _TONEMAP_ACES
    float3 aces = unity_to_ACES(input);
    input = AcesTonemap(aces);
    #elif _TONEMAP_NEUTRAL
    input = NeutralTonemap(input);
    #elif  _TONEMAP_GT
    input.r = GranTurismoTonemap(input.r, GT_PARAM0.x, GT_PARAM0.y, GT_PARAM0.z, GT_PARAM0.w, GT_PARAM1.x, GT_PARAM1.y);
    input.g = GranTurismoTonemap(input.g, GT_PARAM0.x, GT_PARAM0.y, GT_PARAM0.z, GT_PARAM0.w, GT_PARAM1.x, GT_PARAM1.y);
    input.b = GranTurismoTonemap(input.b, GT_PARAM0.x, GT_PARAM0.y, GT_PARAM0.z, GT_PARAM0.w, GT_PARAM1.x, GT_PARAM1.y);
    #elif  _TONEMAP_AGX
    input = tonemapAgX(input);
    #elif  _TONEMAP_KHRONOSPBR
    input = tonemapKhronosPBR(input);
    #endif

    return saturate(input);
}

