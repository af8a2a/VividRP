#ifndef AAAA_XE_GTAO_COMMON_INCLUDED
#define AAAA_XE_GTAO_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

#define XE_GTAO_FP32_DEPTHS 1
#define XE_GTAO_USE_HALF_FLOAT_PRECISION 0
#define XE_GTAO_DEFAULT_THIN_OBJECT_HEURISTIC 1
#define XE_GTAO_USE_DEFAULT_CONSTANTS 1

#define VA_SATURATE(x) saturate(x)
#include "XeGTAO.h.hlsl"
#include "XeGTAO.h.cs.hlsl"
#include "XeGTAO.hlsl"

GTAOConstants LoadGTAOConstants()
{
    GTAOConstants gtaoConstants;

    gtaoConstants.ViewportSize = ViewportSize;
    gtaoConstants.ViewportPixelSize = ViewportPixelSize;
    gtaoConstants.DepthUnpackConsts = DepthUnpackConsts;
    gtaoConstants.CameraTanHalfFOV = CameraTanHalfFOV;
    gtaoConstants.NDCToViewMul = NDCToViewMul;
    gtaoConstants.NDCToViewAdd = NDCToViewAdd;
    gtaoConstants.NDCToViewMul_x_PixelSize = NDCToViewMul_x_PixelSize;
    gtaoConstants.EffectRadius = EffectRadius;
    gtaoConstants.EffectFalloffRange = EffectFalloffRange;
    gtaoConstants.RadiusMultiplier = RadiusMultiplier;
    gtaoConstants.Padding0 = Padding0;
    gtaoConstants.FinalValuePower = FinalValuePower;
    gtaoConstants.DenoiseBlurBeta = DenoiseBlurBeta;
    gtaoConstants.SampleDistributionPower = SampleDistributionPower;
    gtaoConstants.ThinOccluderCompensation = ThinOccluderCompensation;
    gtaoConstants.DepthMIPSamplingOffset = DepthMIPSamplingOffset;
    gtaoConstants.NoiseIndex = NoiseIndex;

    return gtaoConstants;
}

#endif // AAAA_XE_GTAO_COMMON_INCLUDED
