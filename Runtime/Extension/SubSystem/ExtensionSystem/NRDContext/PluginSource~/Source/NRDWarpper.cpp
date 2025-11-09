#include "NRDWarpper.h"
#include <stdio.h>
#include <IUnityLog.h>
using namespace nrd;


static IUnityLog *s_UnityLog = nullptr;


inline uint16_t DivideUp(uint32_t x, uint16_t y) {
    return uint16_t((x + y - 1) / y);
}

#define NRD_ASSERT(CONDITION_,MSG_) \
    if(!CONDITION_)\
        UNITY_LOG_ERROR(s_UnityLog,MSG_);


extern "C" {
UNITY_INTERFACE_EXPORT int UNITY_INTERFACE_API NRD_Test() {
    return 43;
}


UNITY_INTERFACE_EXPORT NRDContext * UNITY_INTERFACE_API NRD_GetContext() {
    auto *ctx = new NRDContext();
    return ctx;
}

UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API NRD_ReleaseContext(
    NRDContext *context) {
    delete context;
}


UNITY_INTERFACE_EXPORT Result UNITY_INTERFACE_API NRD_SetCommonSettings(
    NRDContext *nrdContext,
    const nrd::CommonSettings *commonSettings) {

    memcpy(&nrdContext->m_CommonSettings, commonSettings, sizeof(CommonSettings));
    // Silently fix settings for known cases
    if (nrdContext->m_IsFirstUse) {
        nrdContext->m_CommonSettings.accumulationMode = AccumulationMode::CLEAR_AND_RESTART;
        nrdContext->m_IsFirstUse = false;
    }
    if (nrdContext->m_CommonSettings.accumulationMode != AccumulationMode::CONTINUE) {
        nrdContext->m_SplitScreenPrev = 0.0f;

        nrdContext->m_WorldToViewPrev = nrdContext->m_WorldToView;
        nrdContext->m_ViewToClipPrev = nrdContext->m_ViewToClip;

        nrdContext->m_CommonSettings.resourceSizePrev[0] = nrdContext->m_CommonSettings.resourceSize[0];
        nrdContext->m_CommonSettings.resourceSizePrev[1] = nrdContext->m_CommonSettings.resourceSize[1];

        nrdContext->m_CommonSettings.rectSizePrev[0] = nrdContext->m_CommonSettings.rectSize[0];
        nrdContext->m_CommonSettings.rectSizePrev[1] = nrdContext->m_CommonSettings.rectSize[1];

        nrdContext->m_CommonSettings.cameraJitterPrev[0] = nrdContext->m_CommonSettings.cameraJitter[0];
        nrdContext->m_CommonSettings.cameraJitterPrev[1] = nrdContext->m_CommonSettings.cameraJitter[1];
    }


    // TODO: matrix verifications?
    bool isValid = nrdContext->m_CommonSettings.viewZScale > 0.0f;

    NRD_ASSERT(isValid,"'viewZScale' can't be <= 0");

    isValid &= nrdContext->m_CommonSettings.resourceSize[0] != 0 && nrdContext->m_CommonSettings.resourceSize[1] != 0;
    NRD_ASSERT(isValid, "'resourceSize' can't be 0");

    isValid &= nrdContext->m_CommonSettings.resourceSizePrev[0] != 0 && nrdContext->m_CommonSettings.resourceSizePrev[1]
            != 0;
    NRD_ASSERT(isValid, "'resourceSizePrev' can't be 0");

    isValid &= nrdContext->m_CommonSettings.rectSize[0] != 0 && nrdContext->m_CommonSettings.rectSize[1] != 0;
    NRD_ASSERT(isValid, "'rectSize' can't be 0");


    isValid &= nrdContext->m_CommonSettings.rectSizePrev[0] != 0 && nrdContext->m_CommonSettings.rectSizePrev[1] != 0;
    NRD_ASSERT(isValid, "'rectSizePrev' can't be 0");


    isValid &= ((nrdContext->m_CommonSettings.motionVectorScale[0] != 0.0f && nrdContext->m_CommonSettings.
                 motionVectorScale[1] != 0.0f) || nrdContext->m_CommonSettings.isMotionVectorInWorldSpace);
    NRD_ASSERT(isValid, "'mvScale.xy' can't be 0");


    isValid &= nrdContext->m_CommonSettings.cameraJitter[0] >= -0.5f && nrdContext->m_CommonSettings.cameraJitter[0] <=
            0.5f && nrdContext->m_CommonSettings.cameraJitter[1] >= -0.5f && nrdContext->m_CommonSettings.cameraJitter[
                1] <= 0.5f;
    NRD_ASSERT(isValid, "'cameraJitter' must be in range [-0.5; 0.5]");

    isValid &= nrdContext->m_CommonSettings.cameraJitterPrev[0] >= -0.5f && nrdContext->m_CommonSettings.
            cameraJitterPrev[0] <= 0.5f && nrdContext->m_CommonSettings.cameraJitterPrev[1] >= -0.5f && nrdContext->
            m_CommonSettings.cameraJitterPrev[1] <= 0.5f;
    NRD_ASSERT(isValid, "'cameraJitterPrev' must be in range [-0.5; 0.5]");

    isValid &= nrdContext->m_CommonSettings.denoisingRange > 0.0f;
    NRD_ASSERT(isValid, "'denoisingRange' must be >= 0");

    isValid &= nrdContext->m_CommonSettings.disocclusionThreshold > 0.0f;
    NRD_ASSERT(isValid, "'disocclusionThreshold' must be > 0");

    isValid &= nrdContext->m_CommonSettings.disocclusionThresholdAlternate > 0.0f;
    NRD_ASSERT(isValid, "'disocclusionThresholdAlternate' must be > 0");


    // Rotators (respecting sample patterns symmetry)
    float angle1 = Sequence::Weyl1D(0.5f, nrdContext->m_CommonSettings.frameIndex) * radians(90.0f);
    nrdContext->m_RotatorPre = Geometry::GetRotator(angle1);

    float a0 = Sequence::Weyl1D(0.0f, nrdContext->m_CommonSettings.frameIndex * 2) * radians(90.0f);
    float a1 = Sequence::Bayer4x4(uint2(0, 0), nrdContext->m_CommonSettings.frameIndex * 2) * radians(360.0f);
    nrdContext->m_Rotator = Geometry::CombineRotators(Geometry::GetRotator(a0), Geometry::GetRotator(a1));

    float a2 = Sequence::Weyl1D(0.0f, nrdContext->m_CommonSettings.frameIndex * 2 + 1) * radians(90.0f);
    float a3 = Sequence::Bayer4x4(uint2(0, 0), nrdContext->m_CommonSettings.frameIndex * 2 + 1) * radians(360.0f);
    nrdContext->m_RotatorPost = Geometry::CombineRotators(Geometry::GetRotator(a2), Geometry::GetRotator(a3));


    // Main matrices
    nrdContext->m_ViewToClip = float4x4(
        float4(nrdContext->m_CommonSettings.viewToClipMatrix),
        float4(nrdContext->m_CommonSettings.viewToClipMatrix + 4),
        float4(nrdContext->m_CommonSettings.viewToClipMatrix + 8),
        float4(nrdContext->m_CommonSettings.viewToClipMatrix + 12));

    nrdContext->m_ViewToClipPrev = float4x4(
        float4(nrdContext->m_CommonSettings.viewToClipMatrixPrev),
        float4(nrdContext->m_CommonSettings.viewToClipMatrixPrev + 4),
        float4(nrdContext->m_CommonSettings.viewToClipMatrixPrev + 8),
        float4(nrdContext->m_CommonSettings.viewToClipMatrixPrev + 12));

    nrdContext->m_WorldToView = float4x4(
        float4(nrdContext->m_CommonSettings.worldToViewMatrix),
        float4(nrdContext->m_CommonSettings.worldToViewMatrix + 4),
        float4(nrdContext->m_CommonSettings.worldToViewMatrix + 8),
        float4(nrdContext->m_CommonSettings.worldToViewMatrix + 12));

    nrdContext->m_WorldToViewPrev = float4x4(
        float4(nrdContext->m_CommonSettings.worldToViewMatrixPrev),
        float4(nrdContext->m_CommonSettings.worldToViewMatrixPrev + 4),
        float4(nrdContext->m_CommonSettings.worldToViewMatrixPrev + 8),
        float4(nrdContext->m_CommonSettings.worldToViewMatrixPrev + 12));

    nrdContext->m_WorldPrevToWorld = float4x4(
        float4(nrdContext->m_CommonSettings.worldPrevToWorldMatrix),
        float4(nrdContext->m_CommonSettings.worldPrevToWorldMatrix + 4),
        float4(nrdContext->m_CommonSettings.worldPrevToWorldMatrix + 8),
        float4(nrdContext->m_CommonSettings.worldPrevToWorldMatrix + 12));

    // Convert to LH
    uint32_t flags = 0;
    DecomposeProjection(STYLE_D3D, STYLE_D3D, nrdContext->m_ViewToClip, &flags, nullptr, nullptr,
                        nrdContext->m_Frustum.a, nullptr, nullptr);

    if (!(flags & PROJ_LEFT_HANDED)) {
        nrdContext->m_ViewToClip[2] = -nrdContext->m_ViewToClip[2];
        nrdContext->m_ViewToClipPrev[2] = -nrdContext->m_ViewToClipPrev[2];

        nrdContext->m_WorldToView.Transpose();
        nrdContext->m_WorldToView[2] = -nrdContext->m_WorldToView[2];
        nrdContext->m_WorldToView.Transpose();

        nrdContext->m_WorldToViewPrev.Transpose();
        nrdContext->m_WorldToViewPrev[2] = -nrdContext->m_WorldToViewPrev[2];
        nrdContext->m_WorldToViewPrev.Transpose();
    }

    // Compute other matrices
    nrdContext->m_ViewToWorld = nrdContext->m_WorldToView;
    nrdContext->m_ViewToWorld.InvertOrtho();

    nrdContext->m_ViewToWorldPrev = nrdContext->m_WorldToViewPrev;
    nrdContext->m_ViewToWorldPrev.InvertOrtho();

    const float3 &cameraPosition = nrdContext->m_ViewToWorld[3].xyz;
    const float3 &cameraPositionPrev = nrdContext->m_ViewToWorldPrev[3].xyz;
    float3 translationDelta = cameraPositionPrev - cameraPosition;

    // IMPORTANT: this part is mandatory needed to preserve precision by making matrices camera relative
    nrdContext->m_ViewToWorld.SetTranslation(float3::Zero());
    nrdContext->m_WorldToView = nrdContext->m_ViewToWorld;
    nrdContext->m_WorldToView.InvertOrtho();

    nrdContext->m_ViewToWorldPrev.SetTranslation(translationDelta);
    nrdContext->m_WorldToViewPrev = nrdContext->m_ViewToWorldPrev;
    nrdContext->m_WorldToViewPrev.InvertOrtho();

    nrdContext->m_WorldToClip = nrdContext->m_ViewToClip * nrdContext->m_WorldToView;
    nrdContext->m_WorldToClipPrev = nrdContext->m_ViewToClipPrev * nrdContext->m_WorldToViewPrev;

    nrdContext->m_ClipToWorldPrev = nrdContext->m_WorldToClipPrev;
    nrdContext->m_ClipToWorldPrev.Invert();

    nrdContext->m_ClipToView = nrdContext->m_ViewToClip;
    nrdContext->m_ClipToView.Invert();

    nrdContext->m_ClipToViewPrev = nrdContext->m_ViewToClipPrev;
    nrdContext->m_ClipToViewPrev.Invert();

    nrdContext->m_ClipToWorld = nrdContext->m_WorldToClip;
    nrdContext->m_ClipToWorld.Invert();

    float project[3];
    DecomposeProjection(STYLE_D3D, STYLE_D3D, nrdContext->m_ViewToClip, &flags, nullptr, nullptr,
                        nrdContext->m_Frustum.a, project, nullptr);

    nrdContext->m_ProjectY = project[1];
    nrdContext->m_OrthoMode = (flags & PROJ_ORTHO) ? -1.0f : 0.0f;

    DecomposeProjection(STYLE_D3D, STYLE_D3D, nrdContext->m_ViewToClipPrev, &flags, nullptr, nullptr,
                        nrdContext->m_FrustumPrev.a, nullptr, nullptr);

    nrdContext->m_ViewDirection = -float3(nrdContext->m_ViewToWorld[2]);
    nrdContext->m_ViewDirectionPrev = -float3(nrdContext->m_ViewToWorldPrev[2]);

    nrdContext->m_CameraDelta = float3(translationDelta.x, translationDelta.y, translationDelta.z);

    nrdContext->m_Timer.UpdateElapsedTimeSinceLastSave();
    nrdContext->m_Timer.SaveCurrentTime();

    nrdContext->m_TimeDelta = nrdContext->m_CommonSettings.timeDeltaBetweenFrames > 0.0f
                                  ? nrdContext->m_CommonSettings.timeDeltaBetweenFrames
                                  : nrdContext->m_Timer.GetSmoothedElapsedTime();
    nrdContext->m_FrameRateScale = max(33.333f / nrdContext->m_TimeDelta, 1.0f);

    float dx = abs(nrdContext->m_CommonSettings.cameraJitter[0] - nrdContext->m_CommonSettings.cameraJitterPrev[0]);
    float dy = abs(nrdContext->m_CommonSettings.cameraJitter[1] - nrdContext->m_CommonSettings.cameraJitterPrev[1]);
    nrdContext->m_JitterDelta = max(dx, dy);

    float FPS = nrdContext->m_FrameRateScale * 30.0f;
    float nonLinearAccumSpeed = FPS * 0.25f / (1.0f + FPS * 0.25f);
    nrdContext->m_CheckerboardResolveAccumSpeed = lerp(nonLinearAccumSpeed, 0.5f, nrdContext->m_JitterDelta);

    return isValid ? Result::SUCCESS : Result::INVALID_ARGUMENT;
}


// Create an NRD integration using D3D12. Returns nullptr on failure.
UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API NRD_SetupSigmaConstBuffer(
    const NRDContext *nrdContext,
    const nrd::CommonSettings *commonSettings,
    const nrd::SigmaSettings *settings,
    void *data
) {
    struct SharedConstants {
        float4x4 gWorldToView;
        float4x4 gViewToClip;
        float4x4 gWorldToClipPrev;
        float4x4 gWorldToViewPrev;
        float4 gRotator;
        float4 gRotatorPost;
        float4 gViewVectorWorld;
        float4 gLightDirectionView;
        float4 gFrustum;
        float4 gFrustumPrev;
        float4 gCameraDelta;
        float4 gMvScale;
        float2 gResourceSizeInv;
        float2 gResourceSizeInvPrev;
        float2 gRectSize;
        float2 gRectSizeInv;
        float2 gRectSizePrev;
        float2 gResolutionScale;
        float2 gRectOffset;
        uint2 gPrintfAt;
        uint2 gRectOrigin;
        int2 gRectSizeMinusOne;
        int2 gTilesSizeMinusOne;
        float gOrthoMode;
        float gUnproject;
        float gDenoisingRange;
        float gPlaneDistSensitivity;
        float gStabilizationStrength;
        float gDebug;
        float gSplitScreen;
        float gViewZScale;
        float gMinRectDimMulUnproject;
        uint gFrameIndex;
        uint gIsRectChanged;
    };

    [[maybe_unused]] uint16_t resourceW = commonSettings->resourceSize[0];
    [[maybe_unused]] uint16_t resourceH = commonSettings->resourceSize[1];
    [[maybe_unused]] uint16_t resourceWprev = commonSettings->resourceSizePrev[0];
    [[maybe_unused]] uint16_t resourceHprev = commonSettings->resourceSizePrev[1];
    [[maybe_unused]] uint16_t rectW = commonSettings->rectSize[0];
    [[maybe_unused]] uint16_t rectH = commonSettings->rectSize[1];
    [[maybe_unused]] uint16_t rectWprev = commonSettings->rectSizePrev[0];
    [[maybe_unused]] uint16_t rectHprev = commonSettings->rectSizePrev[1];


    float unproject = 1.0f / (0.5f * rectH * nrdContext->m_ProjectY);
    uint16_t tilesW = DivideUp(rectW, 16);
    uint16_t tilesH = DivideUp(rectH, 16);

    bool isRectChanged = rectW != rectWprev || rectH != rectHprev;
    uint32_t frameNum = min(settings->maxStabilizedFrameNum, SIGMA_MAX_HISTORY_FRAME_NUM);
    float3 lightDirectionView = Rotate(nrdContext->m_WorldToView,
                                       float3(settings->lightDirection[0], settings->lightDirection[1],
                                              settings->lightDirection[2]));
    float stabilizationStrength = frameNum / (1.0f + frameNum);


    SharedConstants *consts = (SharedConstants *) data;

    consts->gWorldToView = nrdContext->m_WorldToView;
    consts->gViewToClip = nrdContext->m_ViewToClip;
    consts->gWorldToClipPrev = nrdContext->m_WorldToClipPrev;
    consts->gWorldToViewPrev = nrdContext->m_WorldToViewPrev;
    consts->gRotator = nrdContext->m_Rotator;
    consts->gRotatorPost = nrdContext->m_RotatorPost;
    consts->gViewVectorWorld = nrdContext->m_ViewDirection.xmm;
    consts->gLightDirectionView = float4(lightDirectionView.x, lightDirectionView.y, lightDirectionView.z, 0.0f);
    consts->gFrustum = nrdContext->m_Frustum;
    consts->gFrustumPrev = nrdContext->m_FrustumPrev;
    consts->gCameraDelta = nrdContext->m_CameraDelta.xmm;
    consts->gMvScale = float4(nrdContext->m_CommonSettings.motionVectorScale[0],
                              nrdContext->m_CommonSettings.motionVectorScale[1],
                              nrdContext->m_CommonSettings.motionVectorScale[2],
                              nrdContext->m_CommonSettings.isMotionVectorInWorldSpace ? 1.0f : 0.0f);
    consts->gResourceSizeInv = float2(1.0f / float(resourceW), 1.0f / float(resourceH));
    consts->gResourceSizeInvPrev = float2(1.0f / float(resourceWprev), 1.0f / float(resourceHprev));
    consts->gRectSize = float2(float(rectW), float(rectH));
    consts->gRectSizeInv = float2(1.0f / float(rectW), 1.0f / float(rectH));
    consts->gRectSizePrev = float2(float(rectWprev), float(rectHprev));
    consts->gResolutionScale = float2(float(rectW) / float(resourceW), float(rectH) / float(resourceH));
    consts->gRectOffset = float2(float(nrdContext->m_CommonSettings.rectOrigin[0]) / float(resourceW),
                                 float(nrdContext->m_CommonSettings.rectOrigin[1]) / float(resourceH));
    consts->gPrintfAt = uint2(nrdContext->m_CommonSettings.printfAt[0], nrdContext->m_CommonSettings.printfAt[1]);
    consts->gRectOrigin = uint2(nrdContext->m_CommonSettings.rectOrigin[0], nrdContext->m_CommonSettings.rectOrigin[1]);
    consts->gRectSizeMinusOne = int2(rectW - 1, rectH - 1);
    consts->gTilesSizeMinusOne = int2(tilesW - 1, tilesH - 1);
    consts->gOrthoMode = nrdContext->m_OrthoMode;
    consts->gUnproject = unproject;
    consts->gDenoisingRange = nrdContext->m_CommonSettings.denoisingRange;
    consts->gPlaneDistSensitivity = settings->planeDistanceSensitivity;
    consts->gStabilizationStrength = nrdContext->m_CommonSettings.accumulationMode == AccumulationMode::CONTINUE
                                         ? stabilizationStrength
                                         : 0.0f;
    consts->gDebug = nrdContext->m_CommonSettings.debug;
    consts->gSplitScreen = nrdContext->m_CommonSettings.splitScreen;
    consts->gViewZScale = nrdContext->m_CommonSettings.viewZScale;
    consts->gMinRectDimMulUnproject = (float) min(rectW, rectH) * unproject;
    consts->gFrameIndex = nrdContext->m_CommonSettings.frameIndex;
    consts->gIsRectChanged = isRectChanged ? 1 : 0;
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces *unityInterfacesPtr) {
    s_UnityLog = unityInterfacesPtr->Get<IUnityLog>();
    UNITY_LOG(s_UnityLog, "Load NRD Plugin");
}

// Unity plugin unload event
extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload() {
    s_UnityLog = nullptr;
}
}
