// Scalar translation of UE 5.7 TSRShadingAnalysis::ComputeMoireError.
// Spatial stages use a six-texel halo; no cross-wave assumptions are required.
Texture2D<float4> _PreviousFlickerHistory;
Texture2D<float2> _FlickerPreviousDepth;
Texture2D<float4> _FlickerInput;
Texture2D<float4> _ReprojectedFlickerHistory;
Texture2D<float4> _FlickerGradient;
RWTexture2D<float4> _OutputFlickerInput;
RWTexture2D<float4> _OutputReprojectedFlickerHistory;
RWTexture2D<float4> _OutputFlickerGradient;
RWTexture2D<float4> _CurrentFlickerHistory;
RWTexture2D<float> _OutputFlickerError;
float4 _FlickerParams; // period, inverse max parallax velocity, frame index, reserved
float4x4 _FlickerClipToPrevClip;
float4x4 _FlickerRotationalClipToPrevClip;
float4x4 _FlickerInvViewProjection;
float4x4 _FlickerPrevInvViewProjection;

float3 TSR_FlickerSMCS(float3 color)
{
    float3 g = max(color, 0.0) / (max(color, 0.0) + 0.17);
    return g * g;
}

float2 TSR_FlickerScreen(float2 uv)
{
    float2 p = uv * 2.0 - 1.0;
#if UNITY_UV_STARTS_AT_TOP
    p.y = -p.y;
#endif
    return p;
}
float3 TSR_FlickerWorld(float2 uv, float depth, float4x4 invVP)
{
    float4 p = mul(invVP, float4(TSR_FlickerScreen(uv), depth, 1.0));
    return p.xyz / (abs(p.w) > 1e-8 ? p.w : 1e-8);
}

float2 TSR_FlickerPointOffset(int2 pixel)
{
    uint3 v=uint3(pixel,(uint)_FlickerParams.z & 7u)*1664525u+1013904223u;
    v.x+=v.y*v.z;v.y+=v.z*v.x;v.z+=v.x*v.y;
    v.x+=v.y*v.z;v.y+=v.z*v.x;v.z+=v.x*v.y;
    return float2(v.xy>>16u)*(1.0/65536.0)-0.5;
}

[numthreads(8, 8, 1)]
void CSPrepareFlicker(uint3 id : SV_DispatchThreadID)
{
    int2 p = id.xy, size = int2(_RenderSize.xy);
    if (any(p >= size)) return;
    float2 uv = (float2(p) + 0.5) * _RenderSize.zw;
    float2 delta = (_Jitter.zw - _Jitter.xy) * 0.5;
#if UNITY_UV_STARTS_AT_TOP
    delta.y = -delta.y;
#endif
    float2 motion = _DilatedMotion[p];
    float2 previousUV = uv - motion + delta;
    float depth = _DilatedDepth[p];
    float2 previousMeta = _FlickerPreviousDepth.SampleLevel(sampler_PointClamp, previousUV, 0);
    float2 currentJitterUV = _Jitter.xy * 0.5;
#if UNITY_UV_STARTS_AT_TOP
    currentJitterUV.y = -currentJitterUV.y;
#endif
    float2 unjitteredUV = uv - currentJitterUV;
    float4 clip = float4(TSR_FlickerScreen(unjitteredUV), depth, 1.0);
    float4 previousClip = mul(_FlickerClipToPrevClip, clip);
    float4 rotationalClip = mul(_FlickerRotationalClipToPrevClip, clip);
    float invPrevW = rcp(max(abs(previousClip.w), 1e-8));
    float predictedDepth = previousClip.z * invPrevW;
    bool valid = _TSRParams.x > 0.5 && all(previousUV >= 0.0) && all(previousUV <= 1.0)
        && previousMeta.x > 0.0 && previousClip.w > 0.0
        && abs(predictedDepth - previousMeta.y) <= _TSRRejectionParams.x + _DepthError[p] * 2.0;
    float4 history = _PreviousFlickerHistory.SampleLevel(sampler_PointClamp, previousUV + TSR_FlickerPointOffset(p) * _RenderSize.zw, 0);
    // Only GCS luminance is exposure-dependent. Signed gradient/count are state.
    float linearHistory = 0.17 * history.x / max(1.0 - history.x, 1e-6);
    linearHistory *= TSR_HistoryExposureCorrection();
    history.x = max(linearHistory / (linearHistory + 0.17), 0.0);
    float inputLuma = dot(TSR_FlickerSMCS(_InputColor[p].rgb), (1.0 / 3.0).xxx);
    if (!valid) history = float4(sqrt(inputLuma), 127.0 / 255.0, 0.0, 0.0);
    float2 rotationalScreen = rotationalClip.xy / max(abs(rotationalClip.w), 1e-8);
    float parallax = 0.5 * length((rotationalScreen - previousClip.xy * invPrevW) * _RenderSize.xy);
    float3 world = TSR_FlickerWorld(unjitteredUV, depth, _FlickerInvViewProjection);
    float2 previousUnjitteredUV = unjitteredUV - motion;
    float3 previousWorld = TSR_FlickerWorld(previousUnjitteredUV, previousMeta.y, _FlickerPrevInvViewProjection);
    float radius = 2.0 * length(TSR_FlickerWorld(unjitteredUV + float2(_RenderSize.z, 0), depth, _FlickerInvViewProjection) - world);
    float moving = max(saturate(length(world - previousWorld) / max(radius, 1e-6) - 1.0),
        saturate(parallax * _FlickerParams.y - 0.5));
    _OutputFlickerInput[p] = float4(inputLuma, 1.0 - moving, valid ? 1.0 : 0.0, 0.0);
    _OutputReprojectedFlickerHistory[p] = history;
}

groupshared float2 FlickerC0[400];
groupshared float2 FlickerC1[324];
groupshared float2 FlickerC2[256];
groupshared float3 FlickerFiltered[196]; // blurred input/history, variation error
groupshared float3 FlickerRaw[144]; // energy, confidence, denominator
groupshared float2 FlickerMedian[100];

float TSR_FlickerBlurWeight(int x, int y)
{
    return (x == 0 ? 0.5 : 0.25) * (y == 0 ? 0.5 : 0.25);
}

[numthreads(8, 8, 1)]
void CSAnalyzeFlicker(uint3 group : SV_GroupID, uint3 tid : SV_GroupThreadID, uint lane : SV_GroupIndex)
{
    int2 origin = int2(group.xy) * 8, size = int2(_RenderSize.xy);
    for (uint stageIndex1 = lane; stageIndex1 < 400; stageIndex1 += 64)
    {
        int2 p = clamp(origin + int2(stageIndex1 % 20, stageIndex1 / 20) - 6, 0, size - 1);
        float g = _ReprojectedFlickerHistory[p].x;
        FlickerC0[stageIndex1] = float2(_FlickerInput[p].x, g * g);
    }
    GroupMemoryBarrierWithGroupSync();
    for (uint stageIndex2 = lane; stageIndex2 < 324; stageIndex2 += 64)
    {
        int2 p = clamp(origin + int2(stageIndex2 % 18, stageIndex2 / 18) - 5, 0, size - 1) - origin + 6;
        float2 lo = 1.0, hi = 0.0;
        [unroll] for (int y = -1; y <= 1; y++) [unroll] for (int x = -1; x <= 1; x++)
        { float2 v = FlickerC0[(p.y + y) * 20 + p.x + x]; lo = min(lo, v); hi = max(hi, v); }
        FlickerC1[stageIndex2] = clamp(FlickerC0[p.y * 20 + p.x], lo.yx, hi.yx);
    }
    GroupMemoryBarrierWithGroupSync();
    for (uint stageIndex3 = lane; stageIndex3 < 256; stageIndex3 += 64)
    {
        int2 p = clamp(origin + int2(stageIndex3 % 16, stageIndex3 / 16) - 4, 0, size - 1) - origin + 5;
        float2 lo = 1.0, hi = 0.0;
        [unroll] for (int y = -1; y <= 1; y++) [unroll] for (int x = -1; x <= 1; x++)
        { float2 v = FlickerC1[(p.y + y) * 18 + p.x + x]; lo = min(lo, v); hi = max(hi, v); }
        FlickerC2[stageIndex3] = clamp(FlickerC0[(p.y + 1) * 20 + p.x + 1], lo.yx, hi.yx);
    }
    GroupMemoryBarrierWithGroupSync();
    for (uint stageIndex4 = lane; stageIndex4 < 196; stageIndex4 += 64)
    {
        int2 p = clamp(origin + int2(stageIndex4 % 14, stageIndex4 / 14) - 3, 0, size - 1) - origin + 4;
        float2 filtered = 0.0, variationSum = 0.0;
        [unroll] for (int y = -1; y <= 1; y++) [unroll] for (int x = -1; x <= 1; x++)
        {
            int2 q = p + int2(x,y); float2 v = FlickerC2[q.y * 16 + q.x];
            filtered += v * TSR_FlickerBlurWeight(x,y);
            variationSum += float2(abs(FlickerC0[(q.y+2)*20+q.x+2].x-v.x),v.x) * 0.125;
        }
        float c2 = FlickerC2[p.y * 16 + p.x].x;
        float2 variation = abs(1.125 * float2(abs(FlickerC0[(p.y+2)*20+p.x+2].x-c2),c2) - variationSum);
        FlickerFiltered[stageIndex4] = float3(filtered, min(variation.x,variation.y));
    }
    GroupMemoryBarrierWithGroupSync();
    for (uint stageIndex5 = lane; stageIndex5 < 144; stageIndex5 += 64)
    {
        int2 p = clamp(origin + int2(stageIndex5 % 12, stageIndex5 / 12) - 2, 0, size - 1) - origin + 3;
        float lo = 1.0, hi = 0.0, c2lo = 1.0, c2hi = 0.0, error = 0.0;
        [unroll] for (int y = -1; y <= 1; y++) [unroll] for (int x = -1; x <= 1; x++)
        {
            int2 q = p + int2(x,y); float3 v = FlickerFiltered[q.y*14+q.x];
            lo=min(lo,v.x);hi=max(hi,v.x);error+=v.z*TSR_FlickerBlurWeight(x,y);
            float c=FlickerC2[(q.y+1)*16+q.x+1].x; c2lo=min(c2lo,c);c2hi=max(c2hi,c);
        }
        const float encodingError = 0.5 / 1024.0;
        error=max(max(error,encodingError),(c2hi-c2lo)*0.0625)+encodingError;
        float2 filtered=FlickerFiltered[p.y*14+p.x].xy;
        float denominator=max(abs(filtered.x-filtered.y),(c2hi-c2lo)*0.25+encodingError*0.5);
        float energy=abs(clamp(filtered.y,lo-error,hi+error)-filtered.y);
        FlickerRaw[stageIndex5]=float3(energy,saturate(1.0-energy/max(denominator,1e-8)),denominator);
    }
    GroupMemoryBarrierWithGroupSync();
    for (uint stageIndex6 = lane; stageIndex6 < 100; stageIndex6 += 64)
    {
        int2 p = clamp(origin + int2(stageIndex6 % 10, stageIndex6 / 10) - 1, 0, size - 1) - origin + 2;
        float2 values[9]; uint index=0;
        [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++)
            values[index++]=FlickerRaw[(p.y+y)*12+p.x+x].xy;
        [unroll] for(uint a=1;a<9;a++) [unroll] for(uint b=a;b>0;b--)
        { float2 lo=min(values[b-1],values[b]),hi=max(values[b-1],values[b]);values[b-1]=lo;values[b]=hi; }
        FlickerMedian[stageIndex6]=values[4];
    }
    GroupMemoryBarrierWithGroupSync();
    int2 coord=origin+int2(tid.xy);if(any(coord>=size))return;
    float energy=0.0,clampBlend=1.0;
    [unroll] for(uint medianY=0;medianY<3;medianY++) [unroll] for(uint medianX=0;medianX<3;medianX++)
    { float2 v=FlickerMedian[(tid.y+medianY)*10+tid.x+medianX];energy=max(energy,v.x);clampBlend=min(clampBlend,v.y); }
    int2 p=int2(tid.xy)+6;float2 center=FlickerC0[p.y*20+p.x];
    float lo=center.x,hi=center.x;
    [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++) if(abs(x)+abs(y)==1)
    {float v=FlickerC0[(p.y+y)*20+p.x+x].x;lo=min(lo,v);hi=max(hi,v);}
    float rejection=saturate(1.0-energy/max(FlickerRaw[(tid.y+2)*12+tid.x+2].z,1e-8));
    float finalHistory=lerp(lerp(clamp(center.y,lo,hi),center.y,clampBlend),center.x,max(1.0-rejection,0.05));
    float gradient=finalHistory-lerp(center.y,center.x,0.05);
    float previousGradient=_ReprojectedFlickerHistory[coord].y*(255.0/127.0)-1.0;
    bool flicker=gradient*previousGradient<=0.0 && abs(gradient)>=1.0/127.0
        && abs(previousGradient)>=1.0/127.0 && _TSRParams.x>0.5;
    _OutputFlickerGradient[coord]=float4(sqrt(max(finalHistory,0.0)),gradient,
        flicker?min(abs(gradient),abs(previousGradient)):0.0,flicker?1.0:0.0);
}

[numthreads(8, 8, 1)]
void CSUpdateFlicker(uint3 id : SV_DispatchThreadID)
{
    int2 p=id.xy,size=int2(_RenderSize.xy);if(any(p>=size))return;
    float4 input=_FlickerInput[p],previous=_ReprojectedFlickerHistory[p],g=_FlickerGradient[p];
    float2 contribution=0.0;
    [unroll] for(int y=-2;y<=2;y++) [unroll] for(int x=-2;x<=2;x++)
        contribution=max(contribution,_FlickerGradient[clamp(p+int2(x,y),0,size-1)].zw);
    float gradient=(previous.y*(255.0/127.0)-1.0)*0.95*(1.0-g.w)+g.y;
    float variation=previous.z*0.95*input.y+contribution.x;
    float count=previous.w*20.0*0.95*input.y+contribution.y;
    if(input.z<0.5){gradient=0.0;variation=0.0;count=0.0;}
    float quantizedCount=floor(count*(255.0/20.0))*(20.0/255.0);
    variation*=quantizedCount/max(count,1e-8);
    float threshold=rcp(max(1.0-pow(0.95,max(_FlickerParams.x,0.001)),1e-8));
    float fade=saturate(count/threshold-0.5);
    float error=(abs(variation/max(count,1e-8))+count/127.0)*fade;
    _CurrentFlickerHistory[p]=saturate(float4(g.x,gradient*(127.0/255.0)+127.0/255.0,variation,quantizedCount/20.0));
    _OutputFlickerError[p]=saturate(error*input.y-input.w);
}
