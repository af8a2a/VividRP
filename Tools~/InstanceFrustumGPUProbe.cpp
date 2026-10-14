// Standalone D3D12 runner for production culling predicates (64 threads/group).
#define NOMINMAX
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <wrl/client.h>
#include <algorithm>
#include <cmath>
#include <cstring>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>
using Microsoft::WRL::ComPtr;

static void check(HRESULT hr)
{
    if (FAILED(hr)) throw std::runtime_error("D3D12 HRESULT " + std::to_string(unsigned(hr)));
}
static std::vector<char> read(const char* path)
{
    std::ifstream file(path, std::ios::binary | std::ios::ate);
    if (!file) throw std::runtime_error(path);
    auto size = file.tellg();
    if (size <= 0) throw std::runtime_error("Empty probe input");
    std::vector<char> bytes(static_cast<size_t>(size));
    file.seekg(0); file.read(bytes.data(), bytes.size());
    if (!file) throw std::runtime_error("Truncated probe input");
    return bytes;
}
int main(int argc, char** argv)
{
    try
    {
        if (argc != 4 && argc != 6 && argc != 8)
            throw std::runtime_error("Usage: probe shader.dxil cases.bin results.bin [inputStride outputStride [srv1.bin srv2.bin]]");
        const size_t inputStride = argc >= 6 ? std::stoul(argv[4]) : 192;
        const size_t outputStride = argc >= 6 ? std::stoul(argv[5]) : 32;
        if (!inputStride || !outputStride) throw std::runtime_error("Zero probe stride");
        auto shader = read(argv[1]), vertices = read(argv[2]);
        size_t count = vertices.size() / inputStride;
        if (vertices.size() % inputStride || count % 64 || count / 64 > 65535)
            throw std::runtime_error("Probe data stride/count mismatch");
        ComPtr<IDXGIFactory6> factory;
        check(CreateDXGIFactory2(0, IID_PPV_ARGS(&factory)));
        ComPtr<IDXGIAdapter1> adapter;
        check(factory->EnumAdapterByGpuPreference(0, DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE, IID_PPV_ARGS(&adapter)));
        DXGI_ADAPTER_DESC1 adapterDesc{}; adapter->GetDesc1(&adapterDesc);
        std::wcout << L"GPU: " << adapterDesc.Description << L"\n";
        ComPtr<ID3D12Device> device;
        check(D3D12CreateDevice(adapter.Get(), D3D_FEATURE_LEVEL_12_0, IID_PPV_ARGS(&device)));
        D3D12_FEATURE_DATA_D3D12_OPTIONS4 options{};
        check(device->CheckFeatureSupport(D3D12_FEATURE_D3D12_OPTIONS4, &options, sizeof(options)));
        if (!options.Native16BitShaderOpsSupported) throw std::runtime_error("GPU does not support native 16-bit shader operations");
        std::cout << "Native 16-bit shader operations supported\n";
        auto buffer = [&](size_t size, D3D12_HEAP_TYPE heapType, D3D12_RESOURCE_STATES state, D3D12_RESOURCE_FLAGS flags)
        {
            D3D12_HEAP_PROPERTIES heap{}; heap.Type = heapType;
            D3D12_RESOURCE_DESC desc{};
            desc.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER; desc.Width = size;
            desc.Height = 1; desc.DepthOrArraySize = 1; desc.MipLevels = 1;
            desc.SampleDesc.Count = 1; desc.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR; desc.Flags = flags;
            ComPtr<ID3D12Resource> result;
            check(device->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &desc, state, nullptr, IID_PPV_ARGS(&result)));
            return result;
        };
        auto input = buffer(vertices.size(), D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_STATE_GENERIC_READ, D3D12_RESOURCE_FLAG_NONE);
        auto output = buffer(count * outputStride, D3D12_HEAP_TYPE_DEFAULT, D3D12_RESOURCE_STATE_UNORDERED_ACCESS, D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS);
        auto readback = buffer(count * outputStride, D3D12_HEAP_TYPE_READBACK, D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_FLAG_NONE);
        void* mapped = nullptr;
        D3D12_RANGE empty{};
        check(input->Map(0, &empty, &mapped)); std::memcpy(mapped, vertices.data(), vertices.size()); input->Unmap(0, nullptr);
        std::vector<ComPtr<ID3D12Resource>> extraInputs;
        if (argc == 8)
        {
            for (int i = 6; i < 8; ++i)
            {
                auto data = read(argv[i]);
                auto resource = buffer(data.size(), D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_STATE_GENERIC_READ, D3D12_RESOURCE_FLAG_NONE);
                check(resource->Map(0, &empty, &mapped));
                std::memcpy(mapped, data.data(), data.size()); resource->Unmap(0, nullptr);
                extraInputs.push_back(resource);
            }
        }
        std::vector<D3D12_ROOT_PARAMETER> params(2 + extraInputs.size());
        params[0].ParameterType = D3D12_ROOT_PARAMETER_TYPE_SRV;
        params[1].ParameterType = D3D12_ROOT_PARAMETER_TYPE_UAV;
        for (size_t i = 0; i < extraInputs.size(); ++i)
        {
            params[2 + i].ParameterType = D3D12_ROOT_PARAMETER_TYPE_SRV;
            params[2 + i].Descriptor.ShaderRegister = UINT(1 + i);
        }
        D3D12_ROOT_SIGNATURE_DESC rootDesc{}; rootDesc.NumParameters = UINT(params.size()); rootDesc.pParameters = params.data();
        ComPtr<ID3DBlob> rootBytes, errors;
        check(D3D12SerializeRootSignature(&rootDesc, D3D_ROOT_SIGNATURE_VERSION_1, &rootBytes, &errors));
        ComPtr<ID3D12RootSignature> root;
        check(device->CreateRootSignature(0, rootBytes->GetBufferPointer(), rootBytes->GetBufferSize(), IID_PPV_ARGS(&root)));
        D3D12_COMPUTE_PIPELINE_STATE_DESC psoDesc{};
        psoDesc.pRootSignature = root.Get(); psoDesc.CS = {shader.data(), shader.size()};
        ComPtr<ID3D12PipelineState> pso;
        check(device->CreateComputePipelineState(&psoDesc, IID_PPV_ARGS(&pso)));
        D3D12_COMMAND_QUEUE_DESC queueDesc{}; queueDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
        ComPtr<ID3D12CommandQueue> queue;
        check(device->CreateCommandQueue(&queueDesc, IID_PPV_ARGS(&queue)));
        ComPtr<ID3D12CommandAllocator> allocator;
        check(device->CreateCommandAllocator(queueDesc.Type, IID_PPV_ARGS(&allocator)));
        ComPtr<ID3D12GraphicsCommandList> commands;
        check(device->CreateCommandList(0, queueDesc.Type, allocator.Get(), pso.Get(), IID_PPV_ARGS(&commands)));
        commands->SetComputeRootSignature(root.Get());
        commands->SetComputeRootShaderResourceView(0, input->GetGPUVirtualAddress());
        commands->SetComputeRootUnorderedAccessView(1, output->GetGPUVirtualAddress());
        for (size_t i = 0; i < extraInputs.size(); ++i)
            commands->SetComputeRootShaderResourceView(UINT(2 + i), extraInputs[i]->GetGPUVirtualAddress());
        commands->Dispatch(UINT(count / 64), 1, 1);
        D3D12_RESOURCE_BARRIER barrier{};
        barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
        barrier.Transition = {output.Get(), D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES, D3D12_RESOURCE_STATE_UNORDERED_ACCESS, D3D12_RESOURCE_STATE_COPY_SOURCE};
        commands->ResourceBarrier(1, &barrier);
        commands->CopyResource(readback.Get(), output.Get());
        check(commands->Close());
        ID3D12CommandList* lists[] = {commands.Get()}; queue->ExecuteCommandLists(1, lists);
        ComPtr<ID3D12Fence> fence; check(device->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&fence)));
        check(queue->Signal(fence.Get(), 1));
        HANDLE event = CreateEvent(nullptr, FALSE, FALSE, nullptr);
        if (!event) throw std::runtime_error("CreateEvent failed");
        check(fence->SetEventOnCompletion(1, event));
        DWORD waited = WaitForSingleObject(event, 30000); CloseHandle(event);
        if (waited != WAIT_OBJECT_0) throw std::runtime_error("GPU probe timed out");
        D3D12_RANGE range{0, count * outputStride}; check(readback->Map(0, &range, &mapped));
        std::ofstream results(argv[3], std::ios::binary);
        results.write(static_cast<const char*>(mapped), count * outputStride);
        if (!results) throw std::runtime_error("Cannot write GPU results");
        readback->Unmap(0, &empty);
        std::cout << "Executed " << count << " culling cases\n";
        return 0;
    }
    catch (const std::exception& e) { std::cerr << e.what() << "\n"; return 1; }
}
