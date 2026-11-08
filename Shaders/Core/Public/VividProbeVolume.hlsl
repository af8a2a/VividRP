#ifndef VIVIDRP_PROBE_VOLUME_INCLUDED
#define VIVIDRP_PROBE_VOLUME_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/BakedGI.hlsl"

#ifndef __AMBIENTPROBE_HLSL__
#define __AMBIENTPROBE_HLSL__
float3 EvaluateAmbientProbe(float3 normalWS)
{
    return VividSampleAmbientProbe(normalWS);
}
#endif

uint _EnableProbeVolumes;

#if defined(VIVID_REALTIME_DIFFUSE_GI)
uint _RealtimeDiffuseSkyHandled;
#endif

bool VividAllowUnoccludedSkyFallback()
{
#if defined(VIVID_REALTIME_DIFFUSE_GI)
    return _RealtimeDiffuseSkyHandled == 0u;
#else
    return true;
#endif
}

#if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
#include "Packages/com.unity.render-pipelines.core/Runtime/Lighting/ProbeVolume/ProbeVolume.hlsl"
#endif

bool VividHasProbeVolumeGI()
{
#if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
    return _EnableProbeVolumes != 0;
#else
    return false;
#endif
}

float3 SampleVividProbeVolume(
    float3 positionWS,
    float3 normalWS,
    float3 viewDirectionWS,
    uint renderingLayers,
    bool allowAmbientFallback = true)
{
#if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
    if (_EnableProbeVolumes != 0)
    {
        float3 bakeDiffuseLighting = 0.0;
        APVSample sample = SampleAPV(GetAbsolutePositionWS(positionWS), normalWS,
            renderingLayers, SafeNormalize(viewDirectionWS));
        if (allowAmbientFallback || sample.status != APV_SAMPLE_STATUS_INVALID)
            EvaluateAdaptiveProbeVolume(sample, normalWS, bakeDiffuseLighting);
        return bakeDiffuseLighting;
    }
#endif

    return 0.0;
}

#endif
