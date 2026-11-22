#ifndef VIVID_LUMEN_SCREEN_TRACE_HZB_INCLUDED
#define VIVID_LUMEN_SCREEN_TRACE_HZB_INCLUDED

// Verbatim port of UE 5.7.4 HZBTracing.ush::TraceHZB (same as SdfScreenProbeTracing.compute),
// adapted to VividRP's packed single-UAV ClosestHZB layout (_SdfClosestHzb / _SdfClosestHzbSize).

Texture2D<float> _SdfClosestHzb;
float4 _SdfClosestHzbSize; // base width, height, mip count, log2(height)
float _SdfDepthThicknessScale;

uint2 LumenClosestMipSize(uint mip)
{
    return max(uint2(_SdfClosestHzbSize.xy) >> mip, 1u);
}

uint2 LumenClosestMipOffset(uint mip)
{
    return uint2(0u, 2u * ((uint)_SdfClosestHzbSize.y - LumenClosestMipSize(mip).y)
        + (uint)max(0, (int)mip - (int)_SdfClosestHzbSize.w));
}

float LumenLoadClosestHzb(float2 uv, uint mip)
{
    mip = min(mip, (uint)_SdfClosestHzbSize.z - 1u);
    uint2 size = LumenClosestMipSize(mip);
    return _SdfClosestHzb.Load(int3(LumenClosestMipOffset(mip) + min((uint2)(saturate(uv) * size), size - 1u), 0));
}

float2 LumenScreenTraceLineBoxIntersect(float3 RayOrigin, float3 RayEnd, float3 BoxMin, float3 BoxMax)
{
    float3 InvRayDir = 1.0f / (RayEnd - RayOrigin);
    float3 FirstPlaneIntersections = (BoxMin - RayOrigin) * InvRayDir;
    float3 SecondPlaneIntersections = (BoxMax - RayOrigin) * InvRayDir;
    float3 ClosestPlaneIntersections = min(FirstPlaneIntersections, SecondPlaneIntersections);
    float3 FurthestPlaneIntersections = max(FirstPlaneIntersections, SecondPlaneIntersections);

    float2 BoxIntersections;
    BoxIntersections.x = max(ClosestPlaneIntersections.x, max(ClosestPlaneIntersections.y, ClosestPlaneIntersections.z));
    BoxIntersections.y = min(FurthestPlaneIntersections.x, min(FurthestPlaneIntersections.y, FurthestPlaneIntersections.z));
    return saturate(BoxIntersections);
}

float LumenComputeHitVignetteFromScreenPos(float2 ScreenPosition)
{
    float2 Vignette = saturate(abs(ScreenPosition) * 5.0f - 4.0f);
    return saturate(1.0f - dot(Vignette, Vignette));
}

void LumenTraceHZB(
    float2 InHZBBaseTexelSize,
    float4 InHZBUVToScreenUVScaleBias,
    float3 RayTranslatedWorldOrigin,
    float3 RayWorldDirection,
    float MaxWorldTraceDistance,
    float4 InHZBUvFactorAndInvFactor,
    float MaxIterations,
    float RelativeDepthThickness,
    float NumThicknessStepsToDetermineCertainty,
    inout bool bHit,
    inout bool bUncertain,
    inout float3 OutScreenUV,
    inout float3 OutLastVisibleScreenUV,
    inout float OutHitTileZ)
{
    float3 RayStartScreenUV;
    {
        float4 RayStartClip = mul(_SdfDiffuseVP, float4(RayTranslatedWorldOrigin, 1.0f));
        float3 RayStartScreenPosition = RayStartClip.xyz / max(RayStartClip.w, 0.01f);
        RayStartScreenUV = float3((RayStartScreenPosition.xy * LumenNdcToUvScale() + 0.5f) * InHZBUvFactorAndInvFactor.xy, RayStartScreenPosition.z);
    }

    float3 RayEndScreenUV;
    {
        float ViewRayDirectionZ = dot(RayWorldDirection, _SdfProbeCameraForward.xyz);
        float SceneDepth = dot(float4(RayTranslatedWorldOrigin, 1.0f), _SdfProbeCameraForward);
        float RayEndWorldDistance = ViewRayDirectionZ < 0.0f
            ? min(-0.99f * SceneDepth / ViewRayDirectionZ, MaxWorldTraceDistance)
            : MaxWorldTraceDistance;

        float3 RayWorldEnd = RayTranslatedWorldOrigin + RayWorldDirection * RayEndWorldDistance;
        float4 RayEndClip = mul(_SdfDiffuseVP, float4(RayWorldEnd, 1.0f));
        float3 RayEndScreenPosition = RayEndClip.xyz / RayEndClip.w;
        RayEndScreenUV = float3((RayEndScreenPosition.xy * LumenNdcToUvScale() + 0.5f) * InHZBUvFactorAndInvFactor.xy, RayEndScreenPosition.z);

        float2 ScreenEdgeIntersections = LumenScreenTraceLineBoxIntersect(
            RayStartScreenUV, RayEndScreenUV, float3(0, 0, 0), float3(InHZBUvFactorAndInvFactor.xy, 1));
        RayEndScreenUV = RayStartScreenUV + (RayEndScreenUV - RayStartScreenUV) * ScreenEdgeIntersections.y;
    }

    float BaseMipLevel = -1.0f;
    float MipLevel = BaseMipLevel;

    float3 RayDirectionScreenUV = RayEndScreenUV - RayStartScreenUV;
    float CurrentIntersectionTime = 0.0f;
    float LastAboveSurfaceTime = 0.0f;

    float2 FloorOffset = float2(RayDirectionScreenUV.x < 0.0f ? 0.0f : 1.0f, RayDirectionScreenUV.y < 0.0f ? 0.0f : 1.0f);
    float3 RayScreenUV = RayStartScreenUV;

    // Step out of current tile without hit test to avoid self-intersection
    {
        float2 CurrentMipTexelSize = exp2(MipLevel) * InHZBBaseTexelSize;
        float2 CurrentMipResolution = 1.0f / CurrentMipTexelSize;

        float2 UVOffset = 0.005f * CurrentMipTexelSize;
        UVOffset = float2(RayDirectionScreenUV.x < 0.0f ? -UVOffset.x : UVOffset.x, RayDirectionScreenUV.y < 0.0f ? -UVOffset.y : UVOffset.y);

        float2 XYPlane = floor(RayScreenUV.xy * CurrentMipResolution) + FloorOffset;
        XYPlane = XYPlane * CurrentMipTexelSize + UVOffset;

        float2 PlaneIntersectionTimes = (XYPlane - RayStartScreenUV.xy) / RayDirectionScreenUV.xy;
        float IntersectionTime = min(PlaneIntersectionTimes.x, PlaneIntersectionTimes.y);
        CurrentIntersectionTime = IntersectionTime;
        RayScreenUV = RayStartScreenUV + CurrentIntersectionTime * RayDirectionScreenUV;
    }

    if (CurrentIntersectionTime >= 1.0f)
    {
        RayScreenUV = RayStartScreenUV;
    }

    float NumIterations = 0.0f;
    bHit = false;
    bUncertain = false;
    OutHitTileZ = 0.0f;

    while (MipLevel >= BaseMipLevel && NumIterations < MaxIterations && CurrentIntersectionTime < 1.0f)
    {
        float2 CurrentMipTexelSize = exp2(MipLevel) * InHZBBaseTexelSize;
        float2 CurrentMipResolution = 1.0f / CurrentMipTexelSize;

        float2 UVOffset = 0.005f * CurrentMipTexelSize;
        UVOffset = float2(RayDirectionScreenUV.x < 0.0f ? -UVOffset.x : UVOffset.x, RayDirectionScreenUV.y < 0.0f ? -UVOffset.y : UVOffset.y);

        float2 XYPlane = floor(RayScreenUV.xy * CurrentMipResolution) + FloorOffset;
        XYPlane = XYPlane * CurrentMipTexelSize + UVOffset;

        float TileZ;
        if (MipLevel < 0.0f)
        {
            TileZ = _SdfDiffuseDepth.SampleLevel(sampler_PointClamp, RayScreenUV.xy * InHZBUVToScreenUVScaleBias.xy + InHZBUVToScreenUVScaleBias.zw, 0).r;
        }
        else
        {
            TileZ = LumenLoadClosestHzb(RayScreenUV.xy, (uint)MipLevel);
        }

        float3 BoundaryPlanes = float3(XYPlane, TileZ);
        float3 PlaneIntersectionTimes = (BoundaryPlanes - RayStartScreenUV) / RayDirectionScreenUV;
        PlaneIntersectionTimes.z = RayDirectionScreenUV.z < 0.0f ? PlaneIntersectionTimes.z : 1.0f;
        float IntersectionTime = min(PlaneIntersectionTimes.x, PlaneIntersectionTimes.y);

        bool bAboveSurface = RayScreenUV.z > TileZ;
        bool bSkippedTile = bAboveSurface;

        IntersectionTime = min(IntersectionTime, PlaneIntersectionTimes.z);
        bSkippedTile = bSkippedTile && (IntersectionTime != PlaneIntersectionTimes.z);

        if (bSkippedTile)
        {
            LastAboveSurfaceTime = IntersectionTime;
        }

        CurrentIntersectionTime = bAboveSurface ? IntersectionTime : CurrentIntersectionTime;
        RayScreenUV = RayStartScreenUV + min(CurrentIntersectionTime, 1.0f) * RayDirectionScreenUV;
        MipLevel += bSkippedTile ? 1.0f : -1.0f;
        NumIterations++;
    }

    if (MipLevel < BaseMipLevel && CurrentIntersectionTime < 1.0f)
    {
        float TileZ = _SdfDiffuseDepth.SampleLevel(sampler_PointClamp, RayScreenUV.xy * InHZBUVToScreenUVScaleBias.xy + InHZBUVToScreenUVScaleBias.zw, 0).r;
        OutHitTileZ = TileZ;

        float HitSceneDepth = LumenReflectionEyeDepth(TileZ);
        float RaySceneDepth = LumenReflectionEyeDepth(RayScreenUV.z);

        bHit = (RaySceneDepth - HitSceneDepth) < RelativeDepthThickness * max(HitSceneDepth, 0.0000001f);

        if (!bHit)
        {
            RayScreenUV = RayStartScreenUV + LastAboveSurfaceTime * RayDirectionScreenUV;
        }
    }

    if (bHit && !bUncertain && NumThicknessStepsToDetermineCertainty > 0.0f)
    {
        float ThicknessSearchMipLevel = 0.0f;
        float MipNumTexels = exp2(ThicknessSearchMipLevel);
        float2 HZBTileSize = MipNumTexels * InHZBBaseTexelSize;
        float NumSteps = NumThicknessStepsToDetermineCertainty / MipNumTexels;
        float ThicknessSearchEndTime = min(length(RayDirectionScreenUV.xy * HZBTileSize * NumSteps) / max(length(RayEndScreenUV.xy - RayScreenUV.xy), 1e-6f), 1.0f);

        for (float I = 0.0f; I < NumSteps; I++)
        {
            float3 SampleUV = RayScreenUV + (I / NumSteps) * ThicknessSearchEndTime * (RayEndScreenUV - RayScreenUV);
            if (all(SampleUV.xy > 0.0f) && all(SampleUV.xy < InHZBUvFactorAndInvFactor.xy))
            {
                float SampleTileZ = LumenLoadClosestHzb(SampleUV.xy, (uint)ThicknessSearchMipLevel);
                if (SampleUV.z > SampleTileZ)
                {
                    bUncertain = true;
                }
            }
        }
    }

    OutScreenUV.xy = RayScreenUV.xy * InHZBUVToScreenUVScaleBias.xy + InHZBUVToScreenUVScaleBias.zw;
    OutScreenUV.z = RayScreenUV.z;

    float3 LastVisibleScreenUV = RayStartScreenUV + LastAboveSurfaceTime * RayDirectionScreenUV;
    OutLastVisibleScreenUV.xy = LastVisibleScreenUV.xy * InHZBUVToScreenUVScaleBias.xy + InHZBUVToScreenUVScaleBias.zw;
    OutLastVisibleScreenUV.z = LastVisibleScreenUV.z;
}

#endif
