#pragma once

#include <cstdint>
#include "NRD_SDK/Include/NRD.h"
#include "IUnityInterface.h"
#include <ml.h>
#include <ml.hlsli>

#include "Timer.h"


struct NRDContext {
    nrd::CommonSettings m_CommonSettings = {};
    Timer m_Timer;
    float4x4 m_ViewToClip = float4x4::Identity();
    float4x4 m_ViewToClipPrev = float4x4::Identity();
    float4x4 m_ClipToView = float4x4::Identity();
    float4x4 m_ClipToViewPrev = float4x4::Identity();
    float4x4 m_WorldToView = float4x4::Identity();
    float4x4 m_WorldToViewPrev = float4x4::Identity();
    float4x4 m_ViewToWorld = float4x4::Identity();
    float4x4 m_ViewToWorldPrev = float4x4::Identity();
    float4x4 m_WorldToClip = float4x4::Identity();
    float4x4 m_WorldToClipPrev = float4x4::Identity();
    float4x4 m_ClipToWorld = float4x4::Identity();
    float4x4 m_ClipToWorldPrev = float4x4::Identity();
    float4x4 m_WorldPrevToWorld = float4x4::Identity();
    float4 m_RotatorPre = float4::Zero();
    float4 m_Rotator = float4::Zero();
    float4 m_RotatorPost = float4::Zero();
    float4 m_Frustum = float4::Zero();
    float4 m_FrustumPrev = float4::Zero();
    float3 m_CameraDelta = float3::Zero();
    float3 m_ViewDirection = float3::Zero();
    float3 m_ViewDirectionPrev = float3::Zero();
    float m_SplitScreenPrev = 0.0f;
    const char *m_PassName = nullptr;
    uint8_t *m_ConstantDataUnaligned = nullptr;
    uint8_t *m_ConstantData = nullptr;
    size_t m_ConstantDataOffset = 0;
    size_t m_ResourceOffset = 0;
    size_t m_DispatchClearIndex[2] = {};
    float m_OrthoMode = 0.0f;
    float m_CheckerboardResolveAccumSpeed = 0.0f;
    float m_JitterDelta = 0.0f;
    float m_TimeDelta = 0.0f;
    float m_FrameRateScale = 0.0f;
    float m_ProjectY = 0.0f;
    uint32_t m_AccumulatedFrameNum = 0;
    uint16_t m_TransientPoolOffset = 0;
    uint16_t m_PermanentPoolOffset = 0;
    bool m_IsFirstUse = true;

    NRDContext() = default;

    ~NRDContext() = default;
};

extern "C" {
// Create an NRD integration using D3D12. Returns nullptr on failure.

UNITY_INTERFACE_EXPORT NRDContext * UNITY_INTERFACE_API NRD_GetContext();

UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API NRD_ReleaseContext(
    NRDContext *context);

UNITY_INTERFACE_EXPORT int UNITY_INTERFACE_API NRD_Test();


UNITY_INTERFACE_EXPORT nrd::Result UNITY_INTERFACE_API NRD_SetCommonSettings(
    NRDContext *nrdContext,
    const nrd::CommonSettings *commonSettings);


UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API NRD_SetupSigmaConstBuffer(
    const NRDContext *nrdContext,
    const nrd::CommonSettings *commonSettings,
    const nrd::SigmaSettings *settings,
    void *data
    );


}
