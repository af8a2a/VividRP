#include "Plugin.h"
#pragma comment(lib, "dxgi")
#pragma comment(lib, "d3d12")

#include <atomic>
#include <d3d12.h>

#include <dxgi.h>
#include <dxgi1_4.h>
#include <wrl/client.h>

#include "HookWrapper.hpp"
#include "IUnityInterface.h"
#include "IUnityGraphics.h"
#include "IUnityGraphicsD3D12.h"
#include "IUnityLog.h"
#include "MinHook.h"


//-------------------------------------------------------
//Global Unity interfaces
static IUnityInterfaces *g_unityInterfaces = nullptr;
static IUnityGraphics *g_unityGraphics = nullptr;
static IUnityLog *g_Log = nullptr;
static IUnityGraphicsD3D12v8 *g_unityGraphics_D3D12 = nullptr;


typedef decltype(&D3D12SerializeRootSignature) t_D3D12SerializeRootSignature;

typedef void (*t_CreateShaderResourceView)(
    ID3D12Device *pThis,
    ID3D12Resource *pResource,
    const D3D12_SHADER_RESOURCE_VIEW_DESC *pDesc,
    D3D12_CPU_DESCRIPTOR_HANDLE DestDescriptor);

typedef HRESULT (*t_CreateDescriptorHeap)(
    ID3D12Device *pThis,
    const D3D12_DESCRIPTOR_HEAP_DESC *pDescriptorHeapDesc,
    REFIID riid,
    void **ppvHeap);

typedef void (*t_CopyDescriptorsSimple)(
    ID3D12Device *pThis,
    UINT NumDescriptors,
    D3D12_CPU_DESCRIPTOR_HANDLE DestDescriptorRangeStart,
    D3D12_CPU_DESCRIPTOR_HANDLE SrcDescriptorRangeStart,
    D3D12_DESCRIPTOR_HEAP_TYPE DescriptorHeapsType);


static HookWrapper<t_D3D12SerializeRootSignature> *s_pSerializeRootSignatureHook = nullptr;
static HookWrapper<t_CreateDescriptorHeap> *s_pCreateDescriptorHeapHook = nullptr;

static HRESULT WINAPI DetourD3D12SerializeRootSignature(
    _In_ const D3D12_ROOT_SIGNATURE_DESC *pRootSignature,
    _In_ D3D_ROOT_SIGNATURE_VERSION Version,
    _Out_ ID3DBlob **ppBlob,
    _Always_(_Outptr_opt_result_maybenull_) ID3DBlob **ppErrorBlob) {
    D3D12_ROOT_SIGNATURE_DESC desc = *pRootSignature;
    // Local Root Signature does not support any other flags.
    // Source: https://github.com/microsoft/DirectX-Specs/blob/master/d3d/Raytracing.md#additional-root-signature-flags
    if (!(desc.Flags & D3D12_ROOT_SIGNATURE_FLAG_LOCAL_ROOT_SIGNATURE)) {
        desc.Flags |= D3D12_ROOT_SIGNATURE_FLAG_CBV_SRV_UAV_HEAP_DIRECTLY_INDEXED | D3D12_ROOT_SIGNATURE_FLAG_SAMPLER_HEAP_DIRECTLY_INDEXED;
    }
    const HRESULT result = s_pSerializeRootSignatureHook->GetOriginalPtr()(&desc, Version, ppBlob, ppErrorBlob);
    if (FAILED(result)) {
        UNITY_LOG_ERROR(g_Log, "Serializing root signature failure");
    }
    return result;
}

ID3D12DescriptorHeap *s_pDescriptorHeap_CBV_SRV_UAV = nullptr;
UINT s_descriptorHeap_CBV_SRV_UAV_IncrementSize = 0;
SIZE_T s_descriptorHeap_CBV_SRV_UAV_CPUDescriptorHandleForHeapStart = 0;

static HRESULT DetourCreateDescriptorHeap(
    ID3D12Device *pThis,
    const D3D12_DESCRIPTOR_HEAP_DESC *pDescriptorHeapDesc,
    REFIID riid,
    void **ppvHeap) {
    const HRESULT result = s_pCreateDescriptorHeapHook->GetOriginalPtr()(pThis, pDescriptorHeapDesc, riid, ppvHeap);

    if (SUCCEEDED(result) && pDescriptorHeapDesc->Type == D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV) {
        s_pDescriptorHeap_CBV_SRV_UAV = static_cast<ID3D12DescriptorHeap *>(*ppvHeap);
        s_descriptorHeap_CBV_SRV_UAV_IncrementSize = pThis->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);
        s_descriptorHeap_CBV_SRV_UAV_CPUDescriptorHandleForHeapStart = s_pDescriptorHeap_CBV_SRV_UAV->GetCPUDescriptorHandleForHeapStart().ptr;
        UNITY_LOG(g_Log, "Created a CBV/SRV/UAV descriptor heap.");
    }

    return result;
}

#define NAMEOF(x) #x

FARPROC GetD3D12SerializeRootSignatureTargetFunction() {
    return GetProcAddress(GetModuleHandle("d3d12.dll"), NAMEOF(D3D12SerializeRootSignature));
}


extern "C" {
//-------------------------------------------------------
//Unity interfaces

static void UNITY_INTERFACE_API OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType) {
    switch (eventType) {
        case kUnityGfxDeviceEventInitialize: {
            IUnityGraphicsD3D12v8 *pD3d12 = g_unityInterfaces->Get<IUnityGraphicsD3D12v8>();
            ID3D12Device *pDevice = pD3d12->GetDevice();
            if (pDevice != nullptr) {
                void **pDeviceVTable = *reinterpret_cast<void ***>(pDevice);

                void *fnCreateDescriptorHeap = pDeviceVTable[14];
                s_pCreateDescriptorHeapHook = new HookWrapper<t_CreateDescriptorHeap>(fnCreateDescriptorHeap);
                s_pCreateDescriptorHeapHook->CreateAndEnable(&DetourCreateDescriptorHeap);
                UNITY_LOG(g_Log, "Hooked CreateDescriptorHeap");
            }

            break;
        }
        case kUnityGfxDeviceEventShutdown: {
            if (s_pCreateDescriptorHeapHook != nullptr) {
                s_pCreateDescriptorHeapHook->Disable();
                delete s_pCreateDescriptorHeapHook;
                s_pCreateDescriptorHeapHook = nullptr;
            }
        }
        case kUnityGfxDeviceEventBeforeReset:
        case kUnityGfxDeviceEventAfterReset:
        default:
            break;
    }
}

// Called by Unity to load the plugin and provide the interfaces pointer
UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces *unityInterfaces) {
    g_unityInterfaces = unityInterfaces;
    g_Log = g_unityInterfaces->Get<IUnityLog>();
    g_unityGraphics = g_unityInterfaces->Get<IUnityGraphics>();

    // Make sure the dll is loaded before creating the hooks.
    LoadLibraryW(L"d3d12.dll");
    g_unityGraphics->RegisterDeviceEventCallback(OnGraphicsDeviceEvent);


    if (MH_Initialize() == MH_OK) {
        UNITY_LOG(g_Log, "MH_Initialize success");
    } else {
        UNITY_LOG_ERROR(g_Log, "MH_Initialize failure");
    }

    s_pSerializeRootSignatureHook = new HookWrapper<t_D3D12SerializeRootSignature>(
        reinterpret_cast<LPVOID>(GetD3D12SerializeRootSignatureTargetFunction())
    );
    s_pSerializeRootSignatureHook->CreateAndEnable(&DetourD3D12SerializeRootSignature);


    // Run OnGraphicsDeviceEvent(initialize) manually on plugin load
    OnGraphicsDeviceEvent(kUnityGfxDeviceEventInitialize);
}

// Called by Unity when the plugin is unloaded
UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API UnityPluginUnload() {
    g_unityGraphics->UnregisterDeviceEventCallback(OnGraphicsDeviceEvent);
    g_unityGraphics = nullptr;
    g_Log = nullptr;

    s_pSerializeRootSignatureHook->Disable();
    delete s_pSerializeRootSignatureHook;
    s_pSerializeRootSignatureHook = nullptr;

    if (MH_Uninitialize() == MH_OK) {
        UNITY_LOG(g_Log, "MH_Uninitialize success");
    } else {
        UNITY_LOG_ERROR(g_Log, "MH_Uninitialize failure");
    }
}

UNITY_INTERFACE_EXPORT uint32_t UNITY_INTERFACE_API GetSRVDescriptorHeapCount() {
    if (s_pDescriptorHeap_CBV_SRV_UAV == nullptr) {
        UNITY_LOG_ERROR(g_Log, "Failed to get descriptor heap");
        return 0;
    }

    return s_pDescriptorHeap_CBV_SRV_UAV->GetDesc().NumDescriptors;
}

UNITY_INTERFACE_EXPORT bool UNITY_INTERFACE_API CreateSRVDescriptor(ID3D12Resource *pTexture, uint32_t index) {
    if (s_pDescriptorHeap_CBV_SRV_UAV == nullptr) {
        UNITY_LOG_ERROR(g_Log, "Failed to get descriptor heap");
        return false;
    }

    const IUnityGraphicsD3D12v8 *pD3d12 = g_unityInterfaces->Get<IUnityGraphicsD3D12v8>();
    ID3D12Device *pDevice = pD3d12->GetDevice();
    if (pDevice == nullptr) {
        UNITY_LOG_ERROR(g_Log, "Failed to get D3D12 device");
        return false;
    }

    const SIZE_T ptrOffset = static_cast<SIZE_T>(index) * s_descriptorHeap_CBV_SRV_UAV_IncrementSize;
    D3D12_CPU_DESCRIPTOR_HANDLE descriptorHandle =
    {
        s_descriptorHeap_CBV_SRV_UAV_CPUDescriptorHandleForHeapStart + ptrOffset
    };

    D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
    srvDesc.Format = pTexture->GetDesc().Format;

    //for DepthStencil format
    switch (srvDesc.Format) {
        case DXGI_FORMAT_D16_UNORM:
            srvDesc.Format = DXGI_FORMAT_R16_UNORM;
            break;
        case DXGI_FORMAT_D24_UNORM_S8_UINT:
            srvDesc.Format = DXGI_FORMAT_R24_UNORM_X8_TYPELESS;
            break;
        case DXGI_FORMAT_D32_FLOAT:
            srvDesc.Format = DXGI_FORMAT_R32_FLOAT;
            break;
        case DXGI_FORMAT_D32_FLOAT_S8X24_UINT:
            srvDesc.Format = DXGI_FORMAT_R32_FLOAT_X8X24_TYPELESS;
            break;
        default:
            break;
    }

    srvDesc.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
    srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
    srvDesc.Texture2D.MipLevels = UINT_MAX;
    srvDesc.Texture2D.PlaneSlice = 0;
    srvDesc.Texture2D.MostDetailedMip = 0;
    srvDesc.Texture2D.ResourceMinLODClamp = 0.0f;
    pDevice->CreateShaderResourceView(pTexture, &srvDesc, descriptorHandle);

    return true;
}


UNITY_INTERFACE_EXPORT bool UNITY_INTERFACE_API CreateUAVDescriptor(ID3D12Resource *pTexture, uint32_t index) {
    if (s_pDescriptorHeap_CBV_SRV_UAV == nullptr) {
        UNITY_LOG_ERROR(g_Log, "Failed to get descriptor heap");
        return false;
    }

    const IUnityGraphicsD3D12v8 *pD3d12 = g_unityInterfaces->Get<IUnityGraphicsD3D12v8>();
    ID3D12Device *pDevice = pD3d12->GetDevice();
    if (pDevice == nullptr) {
        UNITY_LOG_ERROR(g_Log, "Failed to get D3D12 device");
        return false;
    }

    const SIZE_T ptrOffset = static_cast<SIZE_T>(index) * s_descriptorHeap_CBV_SRV_UAV_IncrementSize;
    D3D12_CPU_DESCRIPTOR_HANDLE descriptorHandle =
    {
        s_descriptorHeap_CBV_SRV_UAV_CPUDescriptorHandleForHeapStart + ptrOffset
    };

    D3D12_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
    uavDesc.Format = pTexture->GetDesc().Format;
    switch (uavDesc.Format) {
        // Override TYPELESS resources to prevent device removed.
        case DXGI_FORMAT_D32_FLOAT:
        case DXGI_FORMAT_R32_TYPELESS: uavDesc.Format = DXGI_FORMAT_R32_UINT;
            break;
        case DXGI_FORMAT_R16_TYPELESS: uavDesc.Format = DXGI_FORMAT_R16_UINT;
            break;
        case DXGI_FORMAT_D16_UNORM: uavDesc.Format = DXGI_FORMAT_R16_UINT;
            break;
        default:
            break;
    }


    uavDesc.ViewDimension = D3D12_UAV_DIMENSION_TEXTURE2D;
    uavDesc.Texture2D.MipSlice = UINT_MAX;
    uavDesc.Texture2D.PlaneSlice = 0;
    //not support counter resouce yet
    pDevice->CreateUnorderedAccessView(pTexture, NULL, &uavDesc, descriptorHandle);

    return true;
}

UNITY_INTERFACE_EXPORT bool UNITY_INTERFACE_API CheckBindlessSupport() {
    if (g_unityGraphics_D3D12) {
        ID3D12Device *device = g_unityGraphics_D3D12->GetDevice();
        D3D12_FEATURE_DATA_SHADER_MODEL shaderModel = {D3D_SHADER_MODEL_6_6};
        HRESULT hr = device->CheckFeatureSupport(
            D3D12_FEATURE_SHADER_MODEL,
            &shaderModel, sizeof(shaderModel));

        if (SUCCEEDED(hr) && shaderModel.HighestShaderModel >= D3D_SHADER_MODEL_6_6) {
            return true;
        }
    }
    UNITY_LOG_ERROR(g_Log, "Current GPU not support SM6.6");

    return false;
}
}
