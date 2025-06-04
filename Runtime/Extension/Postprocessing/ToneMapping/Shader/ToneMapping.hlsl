float4 _GTToneMap_Params0;
float4 _GTToneMap_Params1;

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
