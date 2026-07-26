#include "Plugin.h"
#pragma comment(lib, "dxgi")
#pragma comment(lib, "d3d12")

#include <atomic>
#include <d3d12.h>
#include <dxgi.h>
#include <dxgi1_4.h>
#include <wrl/client.h>

#include "IUnityInterface.h"
#include "IUnityGraphics.h"
#include "IUnityGraphicsD3D12.h"
#include "IUnityLog.h"

#include "nvapi.h"
#include "nvShaderExtnEnums.h"

//-------------------------------------------------------
// Global Unity interfaces
static IUnityInterfaces *g_unityInterfaces = nullptr;
static IUnityGraphics *g_unityGraphics = nullptr;
static IUnityLog *g_unityLog = nullptr;
static std::atomic<UnityGfxRenderer> g_renderer{kUnityGfxRendererNull};
static IUnityGraphicsD3D12v8 *g_unityGraphics_D3D12 = nullptr;

static void Log(const char *msg) {
    if (g_unityLog) {
        UNITY_LOG(g_unityLog, msg);
    }
}

extern "C" {
// Forward declarations
static void UNITY_INTERFACE_API OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType);

static void HandleDeviceEvent(UnityGfxDeviceEventType eventType) {
    switch (eventType) {
        case kUnityGfxDeviceEventInitialize:
            if (g_unityGraphics) {
                g_renderer = g_unityGraphics->GetRenderer();
            }
            if (g_renderer == kUnityGfxRendererD3D12) {
                NvAPI_Status status = NvAPI_Initialize();
                if (status == NVAPI_OK) {
                    Log("[NVAPI] Initialized successfully");
                } else {
                    Log("[NVAPI] Failed to initialize");
                }
            }
            break;
        case kUnityGfxDeviceEventShutdown:
            NvAPI_Unload();
            g_renderer = kUnityGfxRendererNull;
            Log("[NVAPI] Unloaded");
            break;
        case kUnityGfxDeviceEventBeforeReset:
        case kUnityGfxDeviceEventAfterReset:
        default:
            break;
    }
}

//-------------------------------------------------------
// Unity interfaces

static void UNITY_INTERFACE_API OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType) {
    HandleDeviceEvent(eventType);
}

// Called by Unity to load the plugin and provide the interfaces pointer
UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces *unityInterfaces) {
    g_unityInterfaces = unityInterfaces;
    g_unityGraphics = g_unityInterfaces->Get<IUnityGraphics>();
    g_unityLog = g_unityInterfaces->Get<IUnityLog>();
    g_unityGraphics->RegisterDeviceEventCallback(OnGraphicsDeviceEvent);
    g_unityGraphics_D3D12 = g_unityInterfaces->Get<IUnityGraphicsD3D12v8>();

    // Initialize now (in case the graphics device is already initialized)
    OnGraphicsDeviceEvent(kUnityGfxDeviceEventInitialize);
}

// Called by Unity when the plugin is unloaded
UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API UnityPluginUnload() {
    g_unityGraphics->UnregisterDeviceEventCallback(OnGraphicsDeviceEvent);
}

//-------------------------------------------------------
// NVAPI SER interfaces

UNITY_INTERFACE_EXPORT bool NvAPI_IsShaderExecutionReorderingAPISupported() {
    if (!g_unityGraphics_D3D12) return false;

    ID3D12Device *device = g_unityGraphics_D3D12->GetDevice();
    if (!device) return false;

    bool supported = false;
    NvAPI_Status status = NvAPI_D3D12_IsNvShaderExtnOpCodeSupported(
        device, NV_EXTN_OP_HIT_OBJECT_REORDER_THREAD, &supported);

    return status == NVAPI_OK && supported;
}

UNITY_INTERFACE_EXPORT bool NvAPI_IsShaderExecutionReorderingSupportedByGPU() {
    if (!g_unityGraphics_D3D12) return false;

    ID3D12Device *device = g_unityGraphics_D3D12->GetDevice();
    if (!device) return false;

    NVAPI_D3D12_RAYTRACING_THREAD_REORDERING_CAPS caps = NVAPI_D3D12_RAYTRACING_THREAD_REORDERING_CAP_NONE;
    NvAPI_Status status = NvAPI_D3D12_GetRaytracingCaps(
        device, NVAPI_D3D12_RAYTRACING_CAPS_TYPE_THREAD_REORDERING,
        &caps, sizeof(caps));

    return status == NVAPI_OK && caps == NVAPI_D3D12_RAYTRACING_THREAD_REORDERING_CAP_STANDARD;
}

UNITY_INTERFACE_EXPORT bool NvAPI_SetNvShaderExtnSlot(unsigned int uavSlot) {
    if (!g_unityGraphics_D3D12) return false;

    ID3D12Device *device = g_unityGraphics_D3D12->GetDevice();
    if (!device) return false;

    NvAPI_Status status = NvAPI_D3D12_SetNvShaderExtnSlotSpace(device, uavSlot, 0);
    return status == NVAPI_OK;
}
}
