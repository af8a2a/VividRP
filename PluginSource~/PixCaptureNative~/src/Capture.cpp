#include <windows.h>
#include <d3d12.h>
#include <dxgi.h>
#include <wrl/client.h>
#include <pix3.h>
#include <IUnityGraphics.h>
#include <IUnityGraphicsD3D12.h>
#include <chrono>
#include <future>
#include <mutex>
#include <string>

using Microsoft::WRL::ComPtr;
namespace
{
    // ABI: keep in sync with PixNativeState in PixCaptureSession.cs.
    enum State { Idle, Preparing, Armed, Capturing, WaitingForGpu, Finalizing, Captured, Failed };
    std::mutex gate;
    IUnityInterfaces* interfaces = nullptr;
    IUnityGraphics* graphics = nullptr;
    IUnityGraphicsD3D12v7* d3d = nullptr;
    ComPtr<ID3D12Fence> fence;
    std::future<HRESULT> completion;
    State state = Idle;
    HRESULT lastHr = S_OK;
    UINT64 generation = 0;
    UINT64 fenceValue = 0;
    int eventBase = 0;
    bool ownsCapture = false;
    bool cancelled = false;
    std::wstring capturePath; // Outlives Begin's marshalled C# string and End's worker.

    HRESULT Fail(HRESULT hr) { lastHr = hr; state = Failed; return hr; }

    void ReapCompletion()
    {
        if (!completion.valid() || completion.wait_for(std::chrono::seconds(0)) != std::future_status::ready) return;
        HRESULT result = completion.get();
        ownsCapture = false;
        if (FAILED(result)) Fail(result);
        else state = cancelled ? Failed : Captured;
    }

    void EndOwnedCapture()
    {
        // Never end a capture owned by PIX UI or another plugin. EndCapture can
        // serialize a large file: keep it off Unity's main/submission threads.
        if (!ownsCapture || completion.valid()) return;
        state = Finalizing;
        try { completion = std::async(std::launch::async, [] { return PIXEndCapture(FALSE); }); }
        catch (...)
        {
            // If a worker cannot be created, still close our capture. This rare
            // fallback may block, but must not leak an owned PIX session.
            HRESULT hr = PIXEndCapture(FALSE);
            ownsCapture = false;
            Fail(FAILED(hr) ? hr : E_OUTOFMEMORY);
        }
    }

    HRESULT Availability()
    {
        if (!graphics || graphics->GetRenderer() != kUnityGfxRendererD3D12 || !d3d)
            return E_NOINTERFACE;
        // No late LoadLibrary: launch Unity under PIX before D3D12CreateDevice.
        if (!GetModuleHandleW(L"WinPixGpuCapturer.dll"))
            return HRESULT_FROM_WIN32(ERROR_MOD_NOT_FOUND);
        return S_OK;
    }

    void UNITY_INTERFACE_API OnRenderEvent(int eventId, void* data)
    {
        std::lock_guard<std::mutex> lock(gate);
        if (reinterpret_cast<UINT64>(data) != generation || cancelled || !d3d) return;
        bool prepare = eventId == eventBase && state == Preparing;
        bool finish = eventId == eventBase + 1 && state == Capturing;
        if (!prepare && !finish) return;
        // ConfigureEvent guarantees worker recording is joined and queued lists
        // are submitted before this callback. Only the submission thread touches
        // Unity's queue. The fence is polled from C#, never CPU-waited here.
        if (prepare)
        {
            fence.Reset();
            HRESULT hr = d3d->GetDevice()->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&fence));
            if (FAILED(hr)) { Fail(hr); return; }
            fenceValue = 1;
        }
        else ++fenceValue;
        HRESULT hr = d3d->GetCommandQueue()->Signal(fence.Get(), fenceValue);
        if (FAILED(hr)) { Fail(hr); return; }
        if (finish) state = WaitingForGpu;
    }

    void UNITY_INTERFACE_API OnDeviceEvent(UnityGfxDeviceEventType event)
    {
        std::lock_guard<std::mutex> lock(gate);
        if (event == kUnityGfxDeviceEventInitialize || event == kUnityGfxDeviceEventAfterReset)
        {
            if (graphics->GetRenderer() != kUnityGfxRendererD3D12) return;
            d3d = interfaces->Get<IUnityGraphicsD3D12v7>();
            if (!d3d) return;
            UnityD3D12PluginEventConfig config{};
            config.graphicsQueueAccess = kUnityD3D12GraphicsQueueAccess_Allow;
            config.flags = kUnityD3D12EventConfigFlag_FlushCommandBuffers |
                kUnityD3D12EventConfigFlag_SyncWorkerThreads;
            d3d->ConfigureEvent(eventBase, &config);
            d3d->ConfigureEvent(eventBase + 1, &config);
        }
        else if (event == kUnityGfxDeviceEventShutdown || event == kUnityGfxDeviceEventBeforeReset)
        {
            cancelled = true;
            lastHr = DXGI_ERROR_DEVICE_REMOVED;
            if (ownsCapture) EndOwnedCapture(); else state = Failed;
            fence.Reset();
            d3d = nullptr;
        }
    }
}

#define EXPORT extern "C" __declspec(dllexport)
EXPORT HRESULT UNITY_INTERFACE_API VividPixAvailable()
{
    std::lock_guard<std::mutex> lock(gate);
    return Availability();
}

EXPORT HRESULT UNITY_INTERFACE_API VividPixPrepare(UINT64* token, int* firstEvent)
{
    std::lock_guard<std::mutex> lock(gate);
    if (!token || !firstEvent) return E_POINTER;
    *token = 0;
    *firstEvent = 0;
    ReapCompletion(); // Also recovers a cancellation from the previous C# domain.
    if (ownsCapture || completion.valid() || (state >= Preparing && state <= Finalizing)) return E_PENDING;
    HRESULT hr = Availability();
    if (FAILED(hr)) return hr;
    if (PIXGetCaptureState() != 0) return E_PENDING;
    *token = ++generation;
    *firstEvent = eventBase;
    cancelled = false;
    lastHr = S_OK;
    fence.Reset();
    state = Preparing;
    return S_OK;
}

EXPORT UnityRenderingEventAndData UNITY_INTERFACE_API VividPixGetRenderEvent() { return OnRenderEvent; }

EXPORT HRESULT UNITY_INTERFACE_API VividPixBegin(UINT64 token, const wchar_t* path)
{
    std::lock_guard<std::mutex> lock(gate);
    if (token != generation || state != Armed) return E_UNEXPECTED;
    if (!path || !*path) return E_INVALIDARG;
    // Recheck immediately before begin; a user may have started another capture.
    if (PIXGetCaptureState() != 0) return Fail(E_PENDING);
    if (GetFileAttributesW(path) != INVALID_FILE_ATTRIBUTES) return Fail(HRESULT_FROM_WIN32(ERROR_FILE_EXISTS));
    try { capturePath = path; }
    catch (...) { return Fail(E_OUTOFMEMORY); }
    PIXCaptureParameters parameters{};
    parameters.GpuCaptureParameters.FileName = capturePath.c_str();
    HRESULT hr = PIXBeginCapture(PIX_CAPTURE_GPU, &parameters);
    lastHr = hr;
    if (FAILED(hr)) return Fail(hr);
    ownsCapture = true;
    state = Capturing;
    return hr;
}

EXPORT int UNITY_INTERFACE_API VividPixPoll(UINT64 token, HRESULT* hr)
{
    std::lock_guard<std::mutex> lock(gate);
    if (!hr) return Failed;
    if (token != generation) { *hr = E_INVALIDARG; return Failed; }
    ReapCompletion();
    if (!cancelled && fence && (state == Preparing || state == WaitingForGpu))
    {
        UINT64 completed = fence->GetCompletedValue();
        if (completed == UINT64_MAX) Fail(DXGI_ERROR_DEVICE_REMOVED);
        else if (completed >= fenceValue)
        {
            if (state == Preparing) state = Armed;
            else EndOwnedCapture();
        }
    }
    *hr = lastHr;
    return state;
}

EXPORT HRESULT UNITY_INTERFACE_API VividPixCancel(UINT64 token)
{
    std::lock_guard<std::mutex> lock(gate);
    if (token != generation) return E_INVALIDARG;
    cancelled = true;
    if (SUCCEEDED(lastHr)) lastHr = HRESULT_FROM_WIN32(ERROR_CANCELLED);
    if (ownsCapture) EndOwnedCapture(); else state = Failed;
    return S_OK;
}

EXPORT DWORD UNITY_INTERFACE_API VividPixRuntimePath(wchar_t* path, DWORD capacity)
{
    HMODULE module = GetModuleHandleW(L"WinPixGpuCapturer.dll");
    return module ? GetModuleFileNameW(module, path, capacity) : 0;
}

EXPORT void UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* unityInterfaces)
{
    interfaces = unityInterfaces;
    graphics = interfaces->Get<IUnityGraphics>();
    if (!graphics) return;
    eventBase = graphics->ReserveEventIDRange(2);
    graphics->RegisterDeviceEventCallback(OnDeviceEvent);
    OnDeviceEvent(kUnityGfxDeviceEventInitialize);
}

EXPORT void UNITY_INTERFACE_API UnityPluginUnload()
{
    if (graphics) graphics->UnregisterDeviceEventCallback(OnDeviceEvent);
    {
        std::lock_guard<std::mutex> lock(gate);
        cancelled = true;
        EndOwnedCapture();
    }
    if (completion.valid()) completion.wait(); // DLL code must outlive the worker.
    fence.Reset();
    d3d = nullptr;
    graphics = nullptr;
    interfaces = nullptr;
}
