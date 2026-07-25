#ifndef VIVIDRP_REBLUR_SIGNAL_ENCODING_INCLUDED
#define VIVIDRP_REBLUR_SIGNAL_ENCODING_INCLUDED

// VividRP keeps REBLUR radiance in linear RGB. The YCoCg representation contains signed
// chroma; losing either negative component in an intermediate filter/history path changes
// saturated red and blue lights into unrelated hues.
#define VIVID_REBLUR_SIGNAL_USE_YCOCG 0

float3 VividReblurEncodeRadiance(float3 radiance)
{
#if VIVID_REBLUR_SIGNAL_USE_YCOCG
    float y = dot(radiance, float3(0.25, 0.5, 0.25));
    float co = dot(radiance, float3(0.5, 0.0, -0.5));
    float cg = dot(radiance, float3(-0.25, 0.5, -0.25));
    return float3(y, co, cg);
#else
    return radiance;
#endif
}

float3 VividReblurDecodeRadiance(float3 signal)
{
#if VIVID_REBLUR_SIGNAL_USE_YCOCG
    float t = signal.x - signal.z;
    return max(float3(t + signal.y, signal.x + signal.z, t - signal.y), 0.0);
#else
    return max(signal, 0.0);
#endif
}

#endif
