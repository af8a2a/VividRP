#ifndef NS_INCLUDE
#define NS_INCLUDE
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"


#ifndef dx
#define dx 1.0
#endif

#ifndef dx2
#define dx2 (dx * dx)
#endif

#ifndef dt
#define dt unity_DeltaTime.z
#endif

#ifndef halfrdx
#define halfrdx (1 / dx * 0.5)
#endif


float _AdvectSpeed;
float _Viscosity;

Texture2D _FinalCTex;
Texture2D _Tex0, _Tex1;
float4 _Tex0_TexelSize, _Tex1_TexelSize;

int _InteractorCount;

struct InteractorData
{
    float2 PositionOS;
    float2 Force;
    float Radius;
};

StructuredBuffer<InteractorData> _InteractorData;


struct appdata
{
    uint vertexID : SV_VertexID;
    float4 vertex : POSITION;
    float2 texcoord : TEXCOORD0;
};

struct v2f
{
    float2 uv : TEXCOORD0;
    float4 pos : SV_POSITION;
    float2 uvL : TEXCOORD1;
    float2 uvR : TEXCOORD2;
    float2 uvT : TEXCOORD3;
    float2 uvB : TEXCOORD4;
};

////////////////////////////////////////////////////////////////////////


v2f vert_common(appdata v)
{
    v2f output;
    ZERO_INITIALIZE(v2f, output);
    float4 pos = GetFullScreenTriangleVertexPosition(v.vertexID);
    float2 uv = GetFullScreenTriangleTexCoord(v.vertexID);
    output.pos = pos;
    output.uv = uv;

    return output;
}

v2f vert_neighbor(appdata v)
{
    v2f output;

    ZERO_INITIALIZE(v2f, output);
    float4 pos = GetFullScreenTriangleVertexPosition(v.vertexID);
    float2 uv = GetFullScreenTriangleTexCoord(v.vertexID);

    output.pos = pos;
    output.uv = uv;
    float2 size = _Tex0_TexelSize.xy;
    output.uvL = output.uv - float2(1, 0) * size;
    output.uvR = output.uv + float2(1, 0) * size;
    output.uvT = output.uv + float2(0, 1) * size;
    output.uvB = output.uv - float2(0, 1) * size;
    return output;
}

////////////////////////////////////////////////////////////////////////

//advect
float4 frag_advect(v2f i) : SV_Target
{
    float2 vel = _Tex0.Sample(sampler_LinearClamp, i.uv).xy;
    float2 newUV = i.uv - vel * dt * _AdvectSpeed * 0.2;
    return _Tex1.Sample(sampler_LinearClamp, newUV);
}

//diffusion
float4 frag_diffusion(v2f i) : SV_Target
{
    float4 L = _Tex0.Sample(sampler_LinearClamp, i.uvL);
    float4 R = _Tex0.Sample(sampler_LinearClamp, i.uvR);
    float4 T = _Tex0.Sample(sampler_LinearClamp, i.uvT);
    float4 B = _Tex0.Sample(sampler_LinearClamp, i.uvB);

    float4 bC = _Tex1.Sample(sampler_LinearClamp, i.uv);
    float alpha = dx2 / (_Viscosity * dt);
    float beta = 4 + alpha;

    return (L + R + T + B + alpha * bC) / beta;
}

//force
float4 frag_force(v2f i) : SV_Target
{
    float2 velocity = _Tex0.SampleLevel(sampler_LinearClamp, i.uv, 0).xy;

    for (int index = 0; index < _InteractorCount; index++)
    {
        InteractorData data = _InteractorData[index];
        float2 dir = data.PositionOS - i.uv;
        velocity += data.Force * exp(-dot(dir, dir) / (data.Radius * 0.001)) * dt * 200;
    }
    return float4(velocity, 0, 1);
}

//divergence
float4 frag_divergence(v2f i) : SV_Target
{
    float4 L = _Tex0.Sample(sampler_LinearClamp, i.uvL);
    float4 R = _Tex0.Sample(sampler_LinearClamp, i.uvR);
    float4 T = _Tex0.Sample(sampler_LinearClamp, i.uvT);
    float4 B = _Tex0.Sample(sampler_LinearClamp, i.uvB);

    float4 C = _Tex0.Sample(sampler_LinearClamp, i.uv);
    //边界处理
    if (i.uvL.x <= 0) L = -C;
    if (i.uvR.x >= 1) R = -C;
    if (i.uvT.y >= 1) T = -C;
    if (i.uvB.y <= 0) B = -C;

    return halfrdx * (R.x - L.x + T.y - B.y);
}
//pressure
float4 frag_pressure(v2f i) : SV_Target
{
    float L = _Tex0.Sample(sampler_LinearClamp, i.uvL).x;
    float R = _Tex0.Sample(sampler_LinearClamp, i.uvR).x;
    float T = _Tex0.Sample(sampler_LinearClamp, i.uvT).x;
    float B = _Tex0.Sample(sampler_LinearClamp, i.uvB).x;

    float4 bC = _Tex1.Sample(sampler_LinearClamp, i.uv);
    float alpha = -dx2;
    float beta = 4;

    return (L + R + T + B + alpha * bC) / beta;
}

//gradient
float4 frag_gradient(v2f i) : SV_Target
{
    float L = _Tex0.Sample(sampler_LinearClamp, i.uvL).x;
    float R = _Tex0.Sample(sampler_LinearClamp, i.uvR).x;
    float T = _Tex0.Sample(sampler_LinearClamp, i.uvT).x;
    float B = _Tex0.Sample(sampler_LinearClamp, i.uvB).x;

    float4 bC = _Tex1.Sample(sampler_LinearClamp, i.uv);
    bC.xy -= halfrdx * float2(R - L, T - B);
    return bC;
}

#endif
