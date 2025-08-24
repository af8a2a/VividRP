
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


float3 AgXLook(float3 val)
{
    // Default
    float3 offset = 0;
    float3 slope = 1;
    float3 power = 1;
    float sat = 1.0;


    power = float3(1.35, 1.35, 1.35);
    sat = 1.4;

    // ASC CDL
    val = pow(val * slope + offset, power);

    const float3 lw = float3(0.2126, 0.7152, 0.0722);
    float luma = dot(val, lw);

    return luma + sat * (val - luma);
}


float3 AgXDefaultContrastApprox(float3 x)
{
    float3 x2 = x * x;
    float3 x4 = x2 * x2;

    return +15.5 * x4 * x2
        - 40.14 * x4 * x
        + 31.96 * x4
        - 6.868 * x2 * x
        + 0.4298 * x2
        + 0.1191 * x
        - 0.00232;
}


float3 AgX(float3 val)
{
    const float3x3 agx_mat = float3x3(
        0.842479062253094, 0.0423282422610123, 0.0423756549057051,
        0.0784335999999992, 0.878468636469772, 0.0784336,
        0.0792237451477643, 0.0791661274605434, 0.879142973793104);

    const float min_ev = -12.47393f;
    const float max_ev = 4.026069f;

    // Input transform
    val = mul(agx_mat, val);

    // Log2 space encoding
    val = clamp(log2(val), min_ev, max_ev);
    val = (val - min_ev) / (max_ev - min_ev);

    // Apply sigmoid function approximation
    val = AgXDefaultContrastApprox(val);

    return val;
}

float3 AgXEotf(float3 val)
{
    const float3x3 agx_mat_inv = float3x3(
        1.19687900512017, -0.0528968517574562, -0.0529716355144438,
        -0.0980208811401368, 1.15190312990417, -0.0980434501171241,
        -0.0990297440797205, -0.0989611768448433, 1.15107367264116);

    // Undo input transform
    val = mul(agx_mat_inv, val);

    // sRGB IEC 61966-2-1 2.2 Exponent Reference EOTF Display
    //val = pow(val, vec3(2.2));

    return val;
}


/**
 * Tonemap an input colour using Agx.
 * @param color Input colour value to tonemap.
 * @return The tonemapped value.
 */
float3 TonemapAgx(float3 color)
{
    color = AgX(color);
    color = AgXLook(color);
    color = AgXEotf(color);
    return color;
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


half3 VividTonemap(half3 input)
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

