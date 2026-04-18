#ifndef VIVIDRP_PROBE_VOLUME_INCLUDED
#define VIVIDRP_PROBE_VOLUME_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/BakedGI.hlsl"

#ifndef __AMBIENTPROBE_HLSL__
#define __AMBIENTPROBE_HLSL__
float3 EvaluateAmbientProbe(float3 normalWS)
{
    return VividSampleAmbientProbe(normalWS);
}
#endif

uint _EnableProbeVolumes;

#if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
#include "Packages/com.unity.render-pipelines.core/Runtime/Lighting/ProbeVolume/ProbeVolume.hlsl"
#endif

#endif
