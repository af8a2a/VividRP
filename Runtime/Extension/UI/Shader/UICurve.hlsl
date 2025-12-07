#ifndef UI_CURVE
#define UI_CURVE

float4 _Speed;
float4 _SinCurveFactor;  // 频率
float4 _SinCurveFactor2; // 振幅
float4 _SinCurveFactor3; // x轴偏移
float4 _SinCurveFactor4; // y轴偏移

float _EndpointFixed;

sampler2D _NoiseTex;
half4 _NoiseTex_ST;

float Curve(float x)
{
    float t = _Time.y;
    float2 flow = float2(x + _NoiseTex_ST.z * _Time.y, 0);
    float noise = tex2D(_NoiseTex, flow);
    float4 sinValue = sin(x * _SinCurveFactor + _SinCurveFactor3 + t * _Speed);
    float4 amplitude =  lerp(1, sin(PI * x), _EndpointFixed); // 端点固定
    sinValue = _SinCurveFactor2 * amplitude * sinValue + _SinCurveFactor4;

    return dot(sinValue, noise); // 四条曲线波形相加
}

float CurveNormalize(float x)
{
    float y = Curve(x);
    // 使范围略小于(0,1)
    y *= 0.45;
    y += 0.5;
    return y;
}

float sdfCurve(float uvx, float uvy)
{
    float y = CurveNormalize(uvx);

    //float dy = ddx(y) / px;
    //float sdf = abs(i.uv.y - y) / sqrt(1.0 + dy * dy);
    // 等宽sin曲线：https://zhuanlan.zhihu.com/p/343538603
    return abs(uvy - y) * cos(atan(cos(uvx)));
}

#endif