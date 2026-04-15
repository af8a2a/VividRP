#ifndef EXPOSURE_COMMON_INCLUDED
#define EXPOSURE_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/PhysicalCamera.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"


TEXTURE2D(_ExposureWeightMask);
TEXTURE2D_X(_SourceTexture);
TEXTURE2D(_PreviousExposureTexture);
StructuredBuffer<float4> _PreviousExposureBuffer;
RW_TEXTURE2D(float2, _OutputTexture);
TEXTURE2D(_ExposureCurveTexture);
RWStructuredBuffer<uint> _HistogramBuffer;
RWStructuredBuffer<float4> _CurrentExposureBuffer;

CBUFFER_START(cb)
float4 _ExposureParams;
float4 _ExposureParams2;
float4 _HistogramRangeParams;
float4 _AutoExposureScreenSize;
float4 _ProceduralMaskParams;
float4 _ProceduralMaskParams2;
float4 _HistogramExposureParams;
float4 _AdaptationParams;
uint4 _Variants;
CBUFFER_END

#define ParamEV100                      _ExposureParams.y
#define ParamExposureCompensation       _ExposureParams.x
#define ParamAperture                   _ExposureParams.y
#define ParamShutterSpeed               _ExposureParams.z
#define ParamISO                        _ExposureParams.w
#define ParamSpeedLightToDark           _AdaptationParams.x
#define ParamSpeedDarkToLight           _AdaptationParams.y
#define ParamExposureLimitMin           _ExposureParams.y
#define ParamExposureLimitMax           _ExposureParams.z
#define ParamCurveMin                   _ExposureParams2.x
#define ParamCurveMax                   _ExposureParams2.y
#define LensImperfectionExposureScale   _ExposureParams2.z
#define MeterCalibrationConstant        _ExposureParams2.w
#define ParamHistogramScale             _HistogramRangeParams.x
#define ParamHistogramBias              _HistogramRangeParams.y
#define ParamHistogramLowPercent        _HistogramRangeParams.z
#define ParamHistogramHighPercent       _HistogramRangeParams.w
#define ParamScreenSize                 _AutoExposureScreenSize.xy
#define ParamInvScreenSize              _AutoExposureScreenSize.zw
#define ParamSourceBuffer               _Variants.x
#define ParamMeteringMode               _Variants.y
#define ParamAdaptationMode             _Variants.z
#define ParamEvaluateMode               _Variants.w
#define ParamCurveMinEV100              _HistogramExposureParams.x
#define ParamCurveInvRange              _HistogramExposureParams.y
#define ParamCurveEnabled               _HistogramExposureParams.z
#define ParamMiddleGrey                 _HistogramExposureParams.w

#define ProceduralCenter           _ProceduralMaskParams.xy     // Transformed in screen space on CPU
#define ProceduralRadii            _ProceduralMaskParams.zw
#define ProceduralSoftness         _ProceduralMaskParams2.x
#define ProceduralMin              _ProceduralMaskParams2.y
#define ProceduralMax              _ProceduralMaskParams2.z

float GetPreviousExposureEV100()
{
    return _PreviousExposureTexture[uint2(0u, 0u)].y;
}

float GetFractionWithinHistogram(float averageLuminance)
{
    return ComputeEV100FromAvgLuminance(max(averageLuminance, 1e-4), MeterCalibrationConstant)
        * ParamHistogramScale
        + ParamHistogramBias;
}

uint GetHistogramBinLocation(float averageLuminance)
{
    return min(
        (uint)(saturate(GetFractionWithinHistogram(averageLuminance)) * (HISTOGRAM_BINS - 1)),
        (uint)(HISTOGRAM_BINS - 1));
}

float BinLocationToEV(uint binIdx)
{
    return (binIdx * rcp(float(HISTOGRAM_BINS - 1)) - ParamHistogramBias) / max(ParamHistogramScale, 1e-4);
}

float ConvertAvgEV100ToAvgLuminance(float averageSceneEV100, float calibrationConstant)
{
    return max(calibrationConstant * exp2(averageSceneEV100) * 0.01, 1e-6);
}

float SampleVividExposureCompensationCurveStops(float averageSceneEV100)
{
    if (ParamCurveEnabled < 0.5)
        return 0.0;

    float curveU = saturate((averageSceneEV100 - ParamCurveMinEV100) * ParamCurveInvRange);
    return SAMPLE_TEXTURE2D_LOD(_ExposureCurveTexture, sampler_LinearClamp, float2(curveU, 0.5), 0.0).x;
}

void WriteExposureBuffer(float currentExposureScale, float targetExposureScale, float averageSceneLuminance, float middleGreyCompensation)
{
    _CurrentExposureBuffer[0] = float4(currentExposureScale, targetExposureScale, averageSceneLuminance, middleGreyCompensation);
}

float WeightSample(uint2 pixel, float2 sourceSize, float luminance)
{
    UNITY_BRANCH
        switch (ParamMeteringMode)
        {
        case 1u:
        {
            // Spot metering
            float screenDiagonal = 0.5f * (sourceSize.x + sourceSize.y);
            const float kRadius = 0.075 * screenDiagonal;
            const float2 kCenter = sourceSize * 0.5f;
            float d = length(kCenter - pixel) - kRadius;
            return 1.0 - saturate(d);
        }
        case 2u:
        {
            // Center-weighted
            float screenDiagonal = 0.5f * (sourceSize.x + sourceSize.y);
            const float2 kCenter = sourceSize * 0.5f;
            return 1.0 - saturate(pow(length(kCenter - pixel) / screenDiagonal, 1.0));
        }
        case 3u:
        {
            // Mask weigthing
            return SAMPLE_TEXTURE2D_LOD(_ExposureWeightMask, sampler_LinearClamp, pixel * rcp(sourceSize), 0.0).x;
        }
        case 4u:
        {
            // Procedural.
            float radius = max(ProceduralRadii.x, ProceduralRadii.y);
            float2 ellipseScale = float2(radius / ProceduralRadii.x, radius / ProceduralRadii.y);

            float dist = length(ProceduralCenter * ellipseScale - pixel * ellipseScale);
            return (luminance > ProceduralMin && luminance < ProceduralMax) ? saturate(1.0 - PositivePow((dist / radius), ProceduralSoftness)) : 0.0f;
        }
        default:
        {
            // Global average
            return 1.0;
        }
        }
}

float SampleLuminance(float2 uv)
{
    if (ParamSourceBuffer == 1)
    {
        // Color buffer
        float prevExposure = ConvertEV100ToExposure(GetPreviousExposureEV100(), LensImperfectionExposureScale);
        float3 color = SAMPLE_TEXTURE2D_X_LOD(_SourceTexture, sampler_LinearClamp, uv, 0.0).xyz;
        return Luminance(color / prevExposure);
    }
    else
    {
        return 1.0f;
    }
}

float AdaptExposure(float exposure)
{
    if (ParamAdaptationMode == 1)
    {
        return ComputeLuminanceAdaptation(GetPreviousExposureEV100(), exposure, ParamSpeedDarkToLight, ParamSpeedLightToDark, unity_DeltaTime.x);
    }
    else
    {
        return exposure;
    }
}

float CurveRemap(float inEV, out float limitMin, out float limitMax)
{
    float remap = saturate((inEV - ParamCurveMin) / (ParamCurveMax - ParamCurveMin));
    float3 curveSample = SAMPLE_TEXTURE2D_LOD(_ExposureCurveTexture, sampler_LinearClamp, float2(remap, 0.0), 0.0).xyz;
    limitMin = curveSample.y;
    limitMax = curveSample.z;
    return curveSample.x;
}

#endif
