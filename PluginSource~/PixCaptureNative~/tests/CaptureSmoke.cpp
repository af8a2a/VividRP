// Real D3D12 + PIX, with only Unity's plugin-interface delivery simulated.
// Does not prove Unity's render/submission threading or a VividRP frame.
#include <windows.h>
#include <d3d12.h>
#include <dxgi.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <pix3.h>
#include <IUnityGraphics.h>
#include <IUnityGraphicsD3D12.h>
#include <filesystem>
#include <iostream>
#include <stdexcept>
#include <thread>
#include <chrono>
#include <array>

using Microsoft::WRL::ComPtr;
extern "C" HRESULT UNITY_INTERFACE_API VividPixPrepare(UINT64*, int*);
extern "C" HRESULT UNITY_INTERFACE_API VividPixBegin(UINT64, const wchar_t*);
extern "C" int UNITY_INTERFACE_API VividPixPoll(UINT64, HRESULT*);
extern "C" HRESULT UNITY_INTERFACE_API VividPixCancel(UINT64);
extern "C" UnityRenderingEventAndData UNITY_INTERFACE_API VividPixGetRenderEvent();
namespace
{
    ComPtr<ID3D12Device> device;
    ComPtr<ID3D12CommandQueue> queue;
    IUnityGraphics graphics{};
    IUnityGraphicsD3D12v7 d3d{};
    IUnityGraphicsDeviceEventCallback deviceCallback = nullptr;
    int configured = 0;
    void Check(HRESULT hr) { if (FAILED(hr)) { std::cerr << "HRESULT 0x" << std::hex << hr << '\n'; throw std::runtime_error("D3D12/PIX failure"); } }
    IUnityInterface* UNITY_INTERFACE_API GetInterface(UnityInterfaceGUID guid)
    {
        if (guid == GetUnityInterfaceGUID<IUnityGraphics>()) return &graphics;
        if (guid == GetUnityInterfaceGUID<IUnityGraphicsD3D12v7>()) return &d3d;
        return nullptr;
    }
    void WaitState(UINT64 token, int expected)
    {
        auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(45);
        for (;;)
        {
            HRESULT hr;
            int state = VividPixPoll(token, &hr);
            Check(hr);
            if (state == expected) return;
            if (state == 7 || std::chrono::steady_clock::now() > deadline) throw std::runtime_error("capture state timeout/failure");
            // Poll the actual fence/completion, never use elapsed time as success.
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
        }
    }
}

int wmain(int argc, wchar_t** argv)
{
    if (argc != 3) { std::cerr << "PixCaptureSmoke PIX_INSTALL NEW_OUTPUT_DIRECTORY\n"; return 1; }
    UINT64 active = 0;
    bool loaded = false;
    try
    {
        std::filesystem::path pix(argv[1]), output(argv[2]);
        if (std::filesystem::exists(output)) throw std::runtime_error("Use a new output directory");
        std::filesystem::create_directories(output);
        if (!LoadLibraryExW((pix / L"WinPixGpuCapturer.dll").c_str(), nullptr,
            LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS))
            throw std::runtime_error("PIX load failed");
        Check(D3D12CreateDevice(nullptr, D3D_FEATURE_LEVEL_12_0, IID_PPV_ARGS(&device)));
        D3D12_COMMAND_QUEUE_DESC queueDesc{};
        Check(device->CreateCommandQueue(&queueDesc, IID_PPV_ARGS(&queue)));
        graphics.GetRenderer = []() { return kUnityGfxRendererD3D12; };
        graphics.RegisterDeviceEventCallback = [](IUnityGraphicsDeviceEventCallback callback) { deviceCallback = callback; };
        graphics.UnregisterDeviceEventCallback = [](IUnityGraphicsDeviceEventCallback callback) { if (callback == deviceCallback) deviceCallback = nullptr; };
        graphics.ReserveEventIDRange = [](int count) { return count == 2 ? 2000 : 0; };
        d3d.GetDevice = []() { return device.Get(); };
        d3d.GetCommandQueue = []() { return queue.Get(); };
        d3d.ConfigureEvent = [](int, const UnityD3D12PluginEventConfig* config)
        {
            if (config->graphicsQueueAccess != kUnityD3D12GraphicsQueueAccess_Allow ||
                config->flags != (kUnityD3D12EventConfigFlag_FlushCommandBuffers | kUnityD3D12EventConfigFlag_SyncWorkerThreads))
                throw std::runtime_error("Wrong Unity synchronization flags");
            ++configured;
        };
        IUnityInterfaces interfaces{};
        interfaces.GetInterface = GetInterface;
        UnityPluginLoad(&interfaces);
        loaded = true;
        if (configured != 2 || !deviceCallback) throw std::runtime_error("Missing lifecycle registration");

        ComPtr<ID3DBlob> shader, errors, rootBlob;
        const char* source = "RWByteAddressBuffer output : register(u0); [numthreads(1,1,1)] void CS() { output.Store(0, 42); }";
        Check(D3DCompile(source, strlen(source), nullptr, nullptr, nullptr, "CS", "cs_5_1", 0, 0, &shader, &errors));
        D3D12_ROOT_PARAMETER parameter{};
        parameter.ParameterType = D3D12_ROOT_PARAMETER_TYPE_UAV;
        D3D12_ROOT_SIGNATURE_DESC rootDesc{};
        rootDesc.NumParameters = 1; rootDesc.pParameters = &parameter;
        Check(D3D12SerializeRootSignature(&rootDesc, D3D_ROOT_SIGNATURE_VERSION_1, &rootBlob, &errors));
        ComPtr<ID3D12RootSignature> root;
        Check(device->CreateRootSignature(0, rootBlob->GetBufferPointer(), rootBlob->GetBufferSize(), IID_PPV_ARGS(&root)));
        D3D12_COMPUTE_PIPELINE_STATE_DESC psoDesc{};
        psoDesc.pRootSignature = root.Get();
        psoDesc.CS = {shader->GetBufferPointer(), shader->GetBufferSize()};
        ComPtr<ID3D12PipelineState> pso;
        Check(device->CreateComputePipelineState(&psoDesc, IID_PPV_ARGS(&pso)));
        D3D12_HEAP_PROPERTIES heap{}; heap.Type = D3D12_HEAP_TYPE_DEFAULT;
        D3D12_RESOURCE_DESC desc{};
        desc.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER; desc.Width = 256; desc.Height = 1;
        desc.DepthOrArraySize = 1; desc.MipLevels = 1; desc.SampleDesc.Count = 1;
        desc.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR; desc.Flags = D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
        ComPtr<ID3D12Resource> buffer;
        Check(device->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &desc, D3D12_RESOURCE_STATE_UNORDERED_ACCESS, nullptr, IID_PPV_ARGS(&buffer)));

        auto callback = VividPixGetRenderEvent();
        for (const char* kind : { "valid", "valid2", "marker_only", "missing_end" })
        {
            int eventBase;
            Check(VividPixPrepare(&active, &eventBase));
            callback(eventBase, reinterpret_cast<void*>(active));
            WaitState(active, 2);
            std::wstring name(kind, kind + strlen(kind));
            auto file = output / (name + L".wpix");
            Check(VividPixBegin(active, file.c_str()));
            ComPtr<ID3D12CommandAllocator> allocator;
            ComPtr<ID3D12GraphicsCommandList> list;
            Check(device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&allocator)));
            Check(device->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, allocator.Get(), pso.Get(), IID_PPV_ARGS(&list)));
            PIXBeginEvent(list.Get(), 0, "VividRP.AgentCapture/smoke/FrameBegin"); PIXEndEvent(list.Get());
            PIXBeginEvent(list.Get(), 0, "SmokePass");
            if (std::string(kind) != "marker_only")
            {
                list->SetComputeRootSignature(root.Get());
                list->SetComputeRootUnorderedAccessView(0, buffer->GetGPUVirtualAddress());
                list->Dispatch(1, 1, 1);
            }
            PIXEndEvent(list.Get());
            if (std::string(kind) != "missing_end")
            {
                PIXBeginEvent(list.Get(), 0, "VividRP.AgentCapture/smoke/FrameEnd"); PIXEndEvent(list.Get());
            }
            Check(list->Close());
            ID3D12CommandList* lists[] = { list.Get() };
            queue->ExecuteCommandLists(1, lists);
            callback(eventBase + 1, reinterpret_cast<void*>(active));
            WaitState(active, 6);
            std::cout << kind << " capture completed\n";
            active = 0;
        }
        // M1: the expected work exists ONLY on an async compute queue. The
        // graphics finish fence must wait for every compute tail first.
        std::array<ComPtr<ID3D12CommandQueue>, 3> computeQueues;
        queueDesc.Type = D3D12_COMMAND_LIST_TYPE_COMPUTE;
        for (auto& compute : computeQueues) Check(device->CreateCommandQueue(&queueDesc, IID_PPV_ARGS(&compute)));
        const char* queueNames[] = { "Default", "Background", "Urgent" };
        for (const char* kind : { "async_valid", "async_valid2", "async_marker_only", "async_missing_end" })
        {
            int base;
            Check(VividPixPrepare(&active, &base));
            callback(base, reinterpret_cast<void*>(active)); WaitState(active, 2);
            std::wstring name(kind, kind + strlen(kind));
            Check(VividPixBegin(active, (output / (name + L".wpix")).c_str()));
            std::array<ComPtr<ID3D12CommandAllocator>, 5> allocators;
            std::array<ComPtr<ID3D12GraphicsCommandList>, 5> lists;
            for (unsigned i = 0; i < lists.size(); ++i)
            {
                auto type = i < 2 ? D3D12_COMMAND_LIST_TYPE_DIRECT : D3D12_COMMAND_LIST_TYPE_COMPUTE;
                Check(device->CreateCommandAllocator(type, IID_PPV_ARGS(&allocators[i])));
                Check(device->CreateCommandList(0, type, allocators[i].Get(), pso.Get(), IID_PPV_ARGS(&lists[i])));
            }
            ComPtr<ID3D12Fence> start;
            std::array<ComPtr<ID3D12Fence>, 3> tails;
            Check(device->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&start)));
            PIXBeginEvent(lists[0].Get(), 0, "VividRP.AgentCapture/smoke/FrameBegin"); PIXEndEvent(lists[0].Get());
            Check(lists[0]->Close());
            ID3D12CommandList* first[] = {lists[0].Get()}; queue->ExecuteCommandLists(1, first);
            Check(queue->Signal(start.Get(), 1));
            for (unsigned i = 0; i < computeQueues.size(); ++i)
            {
                auto* list = lists[i + 2].Get();
                std::string begin = std::string("VividRP.AgentCapture/smoke/FrameBegin/") + queueNames[i];
                std::string end = std::string("VividRP.AgentCapture/smoke/FrameEnd/") + queueNames[i];
                PIXBeginEvent(list, 0, "%s", begin.c_str()); PIXEndEvent(list);
                if (i == 1)
                {
                    PIXBeginEvent(list, 0, "AsyncSmokePass");
                    if (std::string(kind) != "async_marker_only")
                    {
                        list->SetComputeRootSignature(root.Get());
                        list->SetComputeRootUnorderedAccessView(0, buffer->GetGPUVirtualAddress());
                        list->Dispatch(1, 1, 1);
                    }
                    PIXEndEvent(list);
                }
                if (i != 1 || std::string(kind) != "async_missing_end")
                { PIXBeginEvent(list, 0, "%s", end.c_str()); PIXEndEvent(list); }
                Check(list->Close());
                Check(computeQueues[i]->Wait(start.Get(), 1));
                ID3D12CommandList* compute[] = {list}; computeQueues[i]->ExecuteCommandLists(1, compute);
                Check(device->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&tails[i])));
                Check(computeQueues[i]->Signal(tails[i].Get(), 1));
                Check(queue->Wait(tails[i].Get(), 1));
            }
            PIXBeginEvent(lists[1].Get(), 0, "VividRP.AgentCapture/smoke/FrameEnd"); PIXEndEvent(lists[1].Get());
            Check(lists[1]->Close());
            ID3D12CommandList* last[] = {lists[1].Get()}; queue->ExecuteCommandLists(1, last);
            callback(base + 1, reinterpret_cast<void*>(active)); WaitState(active, 6);
            for (const auto& tail : tails)
                if (tail->GetCompletedValue() != 1) throw std::runtime_error("Native completion raced a compute queue tail");
            active = 0;
            std::cout << kind << " joined capture completed\n";
        }
        // A delayed callback from a released request cannot arm its successor.
        UINT64 stale = 0, next = 0;
        int eventBase;
        Check(VividPixPrepare(&stale, &eventBase));
        UINT64 rejected = 0;
        int rejectedEvent = 0;
        if (VividPixPrepare(&rejected, &rejectedEvent) != E_PENDING || rejected != 0)
            throw std::runtime_error("Busy capture ownership was replaced");
        Check(VividPixCancel(stale));
        Check(VividPixPrepare(&active, &eventBase));
        callback(eventBase, reinterpret_cast<void*>(stale));
        HRESULT statusHr;
        if (VividPixPoll(active, &statusHr) != 1) throw std::runtime_error("Stale callback changed successor");
        callback(eventBase, reinterpret_cast<void*>(active));
        WaitState(active, 2);
        Check(VividPixBegin(active, (output / L"cancelled.wpix").c_str()));
        Check(VividPixCancel(active));
        // Simulate loss of the old managed domain: no Poll(oldToken). Prepare
        // must reap the cancelled EndCapture once it completes.
        auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(45);
        for (;;)
        {
            HRESULT hr = VividPixPrepare(&next, &eventBase);
            if (hr != E_PENDING) { Check(hr); break; }
            if (std::chrono::steady_clock::now() > deadline) throw std::runtime_error("Cancelled domain recovery timed out");
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
        }
        active = next;
        Check(VividPixCancel(active));
        active = 0;
        std::cout << "Native busy/stale callback/cancelled-domain recovery checks passed\n";
        UnityPluginUnload(); loaded = false;
        if (deviceCallback) throw std::runtime_error("Lifecycle callback was not unregistered");
        std::cout << "Native capture protocol completed; validate files separately.\n";
        return 0;
    }
    catch (const std::exception& e)
    {
        if (active) VividPixCancel(active);
        if (loaded) UnityPluginUnload();
        std::cerr << e.what() << '\n';
        return 2;
    }
}
