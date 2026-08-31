#pragma once

#include <cstddef>
#include <cstdint>

#include "IUnityInterface.h"
#include "IUnityGraphics.h"

#if defined(_WIN32)
#define VMS_EXPORT UNITY_INTERFACE_EXPORT
#else
#define VMS_EXPORT
#endif

enum VMS_SupportStatus : std::uint32_t
{
    VMS_SUPPORT_UNKNOWN = 0,
    VMS_SUPPORT_SUPPORTED = 1,
    VMS_SUPPORT_NOT_D3D12 = 2,
    VMS_SUPPORT_NO_DEVICE = 3,
    VMS_SUPPORT_SHADER_MODEL_6_5_UNAVAILABLE = 4,
    VMS_SUPPORT_MESH_SHADER_UNAVAILABLE = 5,
    VMS_SUPPORT_UNITY_D3D12_INTERFACE_UNAVAILABLE = 6,
};

enum VMS_CullMode : std::uint32_t
{
    VMS_CULL_NONE = 1,
    VMS_CULL_FRONT = 2,
    VMS_CULL_BACK = 3,
};

enum VMS_CompareFunction : std::uint32_t
{
    VMS_COMPARE_NEVER = 1,
    VMS_COMPARE_LESS = 2,
    VMS_COMPARE_EQUAL = 3,
    VMS_COMPARE_LESS_EQUAL = 4,
    VMS_COMPARE_GREATER = 5,
    VMS_COMPARE_NOT_EQUAL = 6,
    VMS_COMPARE_GREATER_EQUAL = 7,
    VMS_COMPARE_ALWAYS = 8,
};

struct VMS_RenderStateDesc
{
    std::uint32_t cullMode;
    std::uint32_t frontCounterClockwise;
    std::uint32_t depthEnable;
    std::uint32_t depthWrite;
    std::uint32_t depthCompare;
    std::uint32_t renderTargetCount;
    std::uint32_t renderTargetFormats[4];
    std::uint32_t depthStencilFormat;
    std::uint32_t sampleCount;
    std::uint32_t sampleQuality;
};

struct VMS_ShaderObjectDesc
{
    std::uint32_t structSize;
    std::uint32_t abiVersion;
    const char* sourceUtf8;
    std::uint32_t sourceLength;
    std::uint32_t reserved0;
    const char* amplificationEntryUtf8;
    const char* meshEntryUtf8;
    const char* pixelEntryUtf8;
    VMS_RenderStateDesc renderState;
};

struct VMS_ShaderBytecode
{
    const void* data;
    std::uint64_t size;
};

struct VMS_ShaderObjectDxilDesc
{
    std::uint32_t structSize;
    std::uint32_t abiVersion;
    VMS_ShaderBytecode amplificationShader;
    VMS_ShaderBytecode meshShader;
    VMS_ShaderBytecode pixelShader;
    VMS_RenderStateDesc renderState;
};

struct VMS_DispatchDesc
{
    std::uint32_t structSize;
    std::uint32_t abiVersion;
    std::uint64_t shaderHandle;

    // Native pointers returned by GraphicsBuffer.GetNativeBufferPtr on D3D12.
    // Bound as root SRVs t0..t5 in this exact order.
    void* visibleRequests;
    void* indirectArgs;
    void* instances;
    void* meshlets;
    void* vertices;
    void* indices;

    std::uint32_t rendererListIndex;
    std::uint32_t maxRequestCount;
    float viewProjectionColumnMajor[16];
};

static_assert(sizeof(void*) == 8, "The VMS ABI is 64-bit only.");
static_assert(sizeof(VMS_RenderStateDesc) == 52);
static_assert(sizeof(VMS_ShaderObjectDesc) == 104);
static_assert(offsetof(VMS_ShaderObjectDesc, sourceUtf8) == 8);
static_assert(offsetof(VMS_ShaderObjectDesc, renderState) == 48);
static_assert(sizeof(VMS_ShaderBytecode) == 16);
static_assert(offsetof(VMS_ShaderBytecode, size) == 8);
static_assert(sizeof(VMS_ShaderObjectDxilDesc) == 112);
static_assert(offsetof(VMS_ShaderObjectDxilDesc, amplificationShader) == 8);
static_assert(offsetof(VMS_ShaderObjectDxilDesc, meshShader) == 24);
static_assert(offsetof(VMS_ShaderObjectDxilDesc, pixelShader) == 40);
static_assert(offsetof(VMS_ShaderObjectDxilDesc, renderState) == 56);
static_assert(sizeof(VMS_DispatchDesc) == 136);
static_assert(offsetof(VMS_DispatchDesc, shaderHandle) == 8);
static_assert(offsetof(VMS_DispatchDesc, visibleRequests) == 16);
static_assert(offsetof(VMS_DispatchDesc, rendererListIndex) == 64);
static_assert(offsetof(VMS_DispatchDesc, viewProjectionColumnMajor) == 72);

inline constexpr std::uint32_t VMS_ABI_VERSION = 2;

extern "C"
{
VMS_EXPORT std::uint32_t UNITY_INTERFACE_API VMS_GetAbiVersion();
VMS_EXPORT std::uint32_t UNITY_INTERFACE_API VMS_GetSupportStatus();
VMS_EXPORT std::uint64_t UNITY_INTERFACE_API VMS_GetDispatchFailureCount();
VMS_EXPORT const char* UNITY_INTERFACE_API VMS_GetLastError();

VMS_EXPORT std::uint64_t UNITY_INTERFACE_API VMS_CreateShaderObject(const VMS_ShaderObjectDesc* desc);
VMS_EXPORT std::uint64_t UNITY_INTERFACE_API VMS_CreateShaderObjectFromDxil(
    const VMS_ShaderObjectDxilDesc* desc);
VMS_EXPORT void UNITY_INTERFACE_API VMS_DestroyShaderObject(std::uint64_t handle);

VMS_EXPORT void* UNITY_INTERFACE_API VMS_CreateDispatchRequest(const VMS_DispatchDesc* desc);
VMS_EXPORT void* UNITY_INTERFACE_API VMS_CreateDispatchBatchRequest(
    const VMS_DispatchDesc* descs,
    std::uint32_t dispatchCount);
VMS_EXPORT void UNITY_INTERFACE_API VMS_DestroyDispatchRequest(void* request);

VMS_EXPORT UnityRenderingEventAndData UNITY_INTERFACE_API VMS_GetRenderEventFunc();
VMS_EXPORT int UNITY_INTERFACE_API VMS_GetDispatchEventId();
VMS_EXPORT int UNITY_INTERFACE_API VMS_GetStateBoundaryEventId();
}
