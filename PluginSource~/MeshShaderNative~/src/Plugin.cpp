#include "Plugin.h"

#include <Windows.h>

#include <d3d12.h>
#include <dxgi.h>
#include <wrl/client.h>

#include <array>
#include <atomic>
#include <cstring>
#include <memory>
#include <mutex>
#include <sstream>
#include <string>
#include <unordered_map>
#include <vector>

#include "IUnityGraphicsD3D12.h"

#pragma comment(lib, "d3d12")

namespace
{
using Microsoft::WRL::ComPtr;

constexpr UINT kRootConstantCount = 20;
constexpr UINT kResourceCount = 6;
constexpr UINT kRootConstantParameter = 0;
constexpr UINT kFirstResourceParameter = 1;
constexpr std::uint32_t kMaxDispatchBatchCount = 64;

IUnityInterfaces* g_unityInterfaces = nullptr;
IUnityGraphics* g_unityGraphics = nullptr;
IUnityGraphicsD3D12v8* g_unityGraphicsD3D12v8 = nullptr;

std::atomic<std::uint32_t> g_supportStatus{VMS_SUPPORT_UNKNOWN};
std::atomic<int> g_dispatchEventId{-1};
std::atomic<int> g_stateBoundaryEventId{-1};
std::atomic<std::uint64_t> g_dispatchFailureCount{0};
std::atomic<std::uint64_t> g_deviceGeneration{0};

std::mutex g_deviceLifecycleMutex;
std::mutex g_deviceMutex;
ComPtr<ID3D12Device> g_device;

std::mutex g_errorMutex;
std::string g_lastError;
thread_local std::string g_lastErrorSnapshot;

struct ShaderObject
{
    VMS_RenderStateDesc renderState{};
    std::vector<std::uint8_t> amplificationShader;
    std::vector<std::uint8_t> meshShader;
    std::vector<std::uint8_t> pixelShader;
    ComPtr<ID3D12RootSignature> rootSignature;
    ComPtr<ID3D12PipelineState> pipelineState;
    std::mutex lifetimeMutex;
    std::size_t pendingDispatchCount = 0;
    bool gpuUseUntracked = false;
    ComPtr<ID3D12Fence> lastUseFence;
    UINT64 lastUseFenceValue = 0;
    std::uint64_t deviceGeneration = 0;
};

std::mutex g_shaderMutex;
std::unordered_map<std::uint64_t, std::shared_ptr<ShaderObject>> g_shaderObjects;
std::atomic<std::uint64_t> g_nextShaderHandle{1};

std::mutex g_retiredShaderMutex;
std::vector<std::shared_ptr<ShaderObject>> g_retiredShaderObjects;

template<typename T>
T* GetUnityInterface(IUnityInterfaces* interfaces)
{
    if (!interfaces)
        return nullptr;
    return reinterpret_cast<T*>(
        interfaces->GetInterface(GetUnityInterfaceGUID<T>()));
}

struct DispatchRequest
{
    std::shared_ptr<ShaderObject> shader;
    // Retain the resources from request creation until the frame fence proves
    // that every recorded DispatchMesh using them has completed on the GPU.
    std::array<ComPtr<ID3D12Resource>, kResourceCount> resources{};
    std::uint32_t rendererListIndex = 0;
    std::uint32_t maxRequestCount = 0;
    std::array<float, 16> viewProjection{};
};

struct DispatchBatchRequest
{
    std::vector<DispatchRequest> dispatches;
    std::uint64_t deviceGeneration = 0;
    ComPtr<ID3D12Fence> completionFence;
    UINT64 completionFenceValue = 0;
    DispatchBatchRequest* nextRetired = nullptr;
};

enum class DispatchBatchExecutionResult
{
    Failed,
    Submitted,
    DeviceGenerationChanged,
};

std::mutex g_pendingDispatchMutex;
std::unordered_map<
    std::uintptr_t,
    std::unique_ptr<DispatchBatchRequest>> g_pendingDispatchBatches;
std::uintptr_t g_nextDispatchToken = 1;

std::mutex g_retiredDispatchMutex;
DispatchBatchRequest* g_retiredDispatchBatches = nullptr;

template<typename T, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE Type>
struct alignas(void*) PipelineSubobject
{
    D3D12_PIPELINE_STATE_SUBOBJECT_TYPE type = Type;
    T value{};
};

struct MeshPipelineStream
{
    PipelineSubobject<ID3D12RootSignature*, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_ROOT_SIGNATURE> rootSignature;
    PipelineSubobject<D3D12_SHADER_BYTECODE, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_AS> amplificationShader;
    PipelineSubobject<D3D12_SHADER_BYTECODE, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_MS> meshShader;
    PipelineSubobject<D3D12_SHADER_BYTECODE, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_PS> pixelShader;
    PipelineSubobject<D3D12_BLEND_DESC, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_BLEND> blendState;
    PipelineSubobject<UINT, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_SAMPLE_MASK> sampleMask;
    PipelineSubobject<D3D12_RASTERIZER_DESC, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_RASTERIZER> rasterizerState;
    PipelineSubobject<D3D12_DEPTH_STENCIL_DESC, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_DEPTH_STENCIL> depthStencilState;
    PipelineSubobject<D3D12_PRIMITIVE_TOPOLOGY_TYPE, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_PRIMITIVE_TOPOLOGY> primitiveTopology;
    PipelineSubobject<D3D12_RT_FORMAT_ARRAY, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_RENDER_TARGET_FORMATS> renderTargetFormats;
    PipelineSubobject<DXGI_FORMAT, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_DEPTH_STENCIL_FORMAT> depthStencilFormat;
    PipelineSubobject<DXGI_SAMPLE_DESC, D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_SAMPLE_DESC> sampleDesc;
};

void SetLastError(std::string message)
{
    std::lock_guard lock(g_errorMutex);
    g_lastError = std::move(message);
}

std::string FormatHResult(const char* operation, HRESULT result)
{
    std::ostringstream stream;
    stream << operation << " failed with HRESULT 0x" << std::hex << std::uppercase
           << static_cast<std::uint32_t>(result) << '.';
    return stream.str();
}

std::uint64_t AdvanceDeviceGeneration()
{
    std::uint64_t generation =
        g_deviceGeneration.fetch_add(1, std::memory_order_acq_rel) + 1;
    if (generation == 0)
    {
        generation =
            g_deviceGeneration.fetch_add(1, std::memory_order_acq_rel) + 1;
    }
    return generation;
}

bool CreateRootSignature(ID3D12Device* device, ComPtr<ID3D12RootSignature>& rootSignature)
{
    std::array<D3D12_ROOT_PARAMETER, 1 + kResourceCount> parameters{};
    parameters[0].ParameterType = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
    parameters[0].Constants.ShaderRegister = 0;
    parameters[0].Constants.RegisterSpace = 0;
    parameters[0].Constants.Num32BitValues = kRootConstantCount;
    parameters[0].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

    for (UINT index = 0; index < kResourceCount; ++index)
    {
        D3D12_ROOT_PARAMETER& parameter = parameters[kFirstResourceParameter + index];
        parameter.ParameterType = D3D12_ROOT_PARAMETER_TYPE_SRV;
        parameter.Descriptor.ShaderRegister = index;
        parameter.Descriptor.RegisterSpace = 0;
        parameter.ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;
    }

    D3D12_ROOT_SIGNATURE_DESC desc{};
    desc.NumParameters = static_cast<UINT>(parameters.size());
    desc.pParameters = parameters.data();
    desc.Flags = D3D12_ROOT_SIGNATURE_FLAG_NONE;

    ComPtr<ID3DBlob> serialized;
    ComPtr<ID3DBlob> errors;
    HRESULT hr = D3D12SerializeRootSignature(
        &desc, D3D_ROOT_SIGNATURE_VERSION_1, &serialized, &errors);
    if (FAILED(hr))
    {
        if (errors && errors->GetBufferPointer() && errors->GetBufferSize() != 0)
        {
            SetLastError(std::string(
                static_cast<const char*>(errors->GetBufferPointer()), errors->GetBufferSize()));
        }
        else
        {
            SetLastError(FormatHResult("D3D12SerializeRootSignature", hr));
        }
        return false;
    }

    hr = device->CreateRootSignature(
        0, serialized->GetBufferPointer(), serialized->GetBufferSize(),
        IID_PPV_ARGS(&rootSignature));
    if (FAILED(hr))
    {
        SetLastError(FormatHResult("ID3D12Device::CreateRootSignature", hr));
        return false;
    }
    return true;
}

D3D12_BLEND_DESC CreateOpaqueBlendState()
{
    D3D12_BLEND_DESC result{};
    for (D3D12_RENDER_TARGET_BLEND_DESC& target : result.RenderTarget)
    {
        target.SrcBlend = D3D12_BLEND_ONE;
        target.DestBlend = D3D12_BLEND_ZERO;
        target.BlendOp = D3D12_BLEND_OP_ADD;
        target.SrcBlendAlpha = D3D12_BLEND_ONE;
        target.DestBlendAlpha = D3D12_BLEND_ZERO;
        target.BlendOpAlpha = D3D12_BLEND_OP_ADD;
        target.LogicOp = D3D12_LOGIC_OP_NOOP;
        target.RenderTargetWriteMask = D3D12_COLOR_WRITE_ENABLE_ALL;
    }
    return result;
}

D3D12_RASTERIZER_DESC CreateRasterizerState(const VMS_RenderStateDesc& desc)
{
    D3D12_RASTERIZER_DESC result{};
    result.FillMode = D3D12_FILL_MODE_SOLID;
    result.CullMode = static_cast<D3D12_CULL_MODE>(desc.cullMode);
    result.FrontCounterClockwise = desc.frontCounterClockwise != 0;
    result.DepthBias = D3D12_DEFAULT_DEPTH_BIAS;
    result.DepthBiasClamp = D3D12_DEFAULT_DEPTH_BIAS_CLAMP;
    result.SlopeScaledDepthBias = D3D12_DEFAULT_SLOPE_SCALED_DEPTH_BIAS;
    result.DepthClipEnable = TRUE;
    result.ConservativeRaster = D3D12_CONSERVATIVE_RASTERIZATION_MODE_OFF;
    return result;
}

D3D12_DEPTH_STENCIL_DESC CreateDepthStencilState(const VMS_RenderStateDesc& desc)
{
    D3D12_DEPTH_STENCIL_DESC result{};
    result.DepthEnable = desc.depthEnable != 0;
    result.DepthWriteMask = desc.depthWrite != 0
        ? D3D12_DEPTH_WRITE_MASK_ALL : D3D12_DEPTH_WRITE_MASK_ZERO;
    result.DepthFunc = static_cast<D3D12_COMPARISON_FUNC>(desc.depthCompare);
    result.StencilReadMask = D3D12_DEFAULT_STENCIL_READ_MASK;
    result.StencilWriteMask = D3D12_DEFAULT_STENCIL_WRITE_MASK;
    result.FrontFace.StencilFailOp = D3D12_STENCIL_OP_KEEP;
    result.FrontFace.StencilDepthFailOp = D3D12_STENCIL_OP_KEEP;
    result.FrontFace.StencilPassOp = D3D12_STENCIL_OP_KEEP;
    result.FrontFace.StencilFunc = D3D12_COMPARISON_FUNC_ALWAYS;
    result.BackFace = result.FrontFace;
    return result;
}

bool ValidateRenderState(const VMS_RenderStateDesc& desc)
{
    if (desc.cullMode < VMS_CULL_NONE || desc.cullMode > VMS_CULL_BACK)
    {
        SetLastError("RenderState.cullMode is invalid.");
        return false;
    }
    if (desc.depthCompare < VMS_COMPARE_NEVER || desc.depthCompare > VMS_COMPARE_ALWAYS)
    {
        SetLastError("RenderState.depthCompare is invalid.");
        return false;
    }
    if (desc.renderTargetCount > 4)
    {
        SetLastError("RenderState.renderTargetCount exceeds the ABI limit of four.");
        return false;
    }
    return true;
}

bool CreatePipelineState(
    ID3D12Device* device,
    const VMS_RenderStateDesc& renderState,
    ShaderObject& shader)
{
    ComPtr<ID3D12Device2> device2;
    HRESULT hr = device->QueryInterface(IID_PPV_ARGS(&device2));
    if (FAILED(hr))
    {
        SetLastError(FormatHResult("QueryInterface(ID3D12Device2)", hr));
        return false;
    }

    MeshPipelineStream stream{};
    stream.rootSignature.value = shader.rootSignature.Get();
    stream.amplificationShader.value = {
        shader.amplificationShader.data(),
        shader.amplificationShader.size()};
    stream.meshShader.value = {
        shader.meshShader.data(),
        shader.meshShader.size()};
    stream.pixelShader.value = {
        shader.pixelShader.data(),
        shader.pixelShader.size()};
    stream.blendState.value = CreateOpaqueBlendState();
    stream.sampleMask.value = UINT_MAX;
    stream.rasterizerState.value = CreateRasterizerState(renderState);
    stream.depthStencilState.value = CreateDepthStencilState(renderState);
    stream.primitiveTopology.value = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
    stream.renderTargetFormats.value.NumRenderTargets = renderState.renderTargetCount;
    for (UINT index = 0; index < renderState.renderTargetCount; ++index)
    {
        stream.renderTargetFormats.value.RTFormats[index] =
            static_cast<DXGI_FORMAT>(renderState.renderTargetFormats[index]);
    }
    stream.depthStencilFormat.value =
        static_cast<DXGI_FORMAT>(renderState.depthStencilFormat);
    stream.sampleDesc.value.Count =
        renderState.sampleCount == 0 ? 1 : renderState.sampleCount;
    stream.sampleDesc.value.Quality = renderState.sampleQuality;

    D3D12_PIPELINE_STATE_STREAM_DESC streamDesc{};
    streamDesc.SizeInBytes = sizeof(stream);
    streamDesc.pPipelineStateSubobjectStream = &stream;
    hr = device2->CreatePipelineState(&streamDesc, IID_PPV_ARGS(&shader.pipelineState));
    if (FAILED(hr))
    {
        SetLastError(FormatHResult("ID3D12Device2::CreatePipelineState", hr));
        return false;
    }
    return true;
}

void MarkDispatchBatchQueued(DispatchBatchRequest& batch)
{
    for (const DispatchRequest& request : batch.dispatches)
    {
        std::lock_guard lock(request.shader->lifetimeMutex);
        ++request.shader->pendingDispatchCount;
    }
}

void* RegisterPendingDispatchBatch(
    std::unique_ptr<DispatchBatchRequest> batch)
{
    std::lock_guard lock(g_pendingDispatchMutex);
    std::uintptr_t token = 0;
    do
    {
        token = g_nextDispatchToken++;
        if (g_nextDispatchToken == 0)
            g_nextDispatchToken = 1;
    }
    while (token == 0 || g_pendingDispatchBatches.contains(token));

    g_pendingDispatchBatches.emplace(token, std::move(batch));
    return reinterpret_cast<void*>(token);
}

std::unique_ptr<DispatchBatchRequest> TakePendingDispatchBatch(void* request)
{
    if (!request)
        return nullptr;

    const std::uintptr_t token = reinterpret_cast<std::uintptr_t>(request);
    std::lock_guard lock(g_pendingDispatchMutex);
    const auto found = g_pendingDispatchBatches.find(token);
    if (found == g_pendingDispatchBatches.end())
        return nullptr;

    std::unique_ptr<DispatchBatchRequest> batch = std::move(found->second);
    g_pendingDispatchBatches.erase(found);
    return batch;
}

void CompleteDispatchBatch(
    DispatchBatchRequest& batch,
    ID3D12Fence* frameFence,
    UINT64 frameFenceValue,
    bool submitted)
{
    for (const DispatchRequest& request : batch.dispatches)
    {
        std::lock_guard lock(request.shader->lifetimeMutex);
        if (submitted)
        {
            if (frameFence && frameFenceValue != 0)
            {
                request.shader->lastUseFence = frameFence;
                request.shader->lastUseFenceValue = frameFenceValue;
                request.shader->gpuUseUntracked = false;
            }
            else
            {
                request.shader->gpuUseUntracked = true;
            }
        }
        if (request.shader->pendingDispatchCount != 0)
            --request.shader->pendingDispatchCount;
    }
}

void CancelPendingDispatchBatches()
{
    decltype(g_pendingDispatchBatches) pending;
    {
        std::lock_guard lock(g_pendingDispatchMutex);
        pending.swap(g_pendingDispatchBatches);
    }

    for (auto& entry : pending)
        CompleteDispatchBatch(*entry.second, nullptr, 0, false);
}

void CancelPendingDispatchBatchesForShader(const ShaderObject* shader)
{
    std::vector<std::unique_ptr<DispatchBatchRequest>> canceled;
    {
        std::lock_guard lock(g_pendingDispatchMutex);
        for (auto entry = g_pendingDispatchBatches.begin();
             entry != g_pendingDispatchBatches.end();)
        {
            bool referencesShader = false;
            for (const DispatchRequest& request : entry->second->dispatches)
            {
                if (request.shader.get() == shader)
                {
                    referencesShader = true;
                    break;
                }
            }

            if (!referencesShader)
            {
                ++entry;
                continue;
            }

            canceled.emplace_back(std::move(entry->second));
            entry = g_pendingDispatchBatches.erase(entry);
        }
    }

    for (const std::unique_ptr<DispatchBatchRequest>& batch : canceled)
        CompleteDispatchBatch(*batch, nullptr, 0, false);
}

bool CanReleaseRetiredShaderObject(const std::shared_ptr<ShaderObject>& shader)
{
    std::lock_guard lock(shader->lifetimeMutex);
    if (shader->pendingDispatchCount != 0 || shader->gpuUseUntracked)
        return false;
    return !shader->lastUseFence ||
           shader->lastUseFence->GetCompletedValue() >= shader->lastUseFenceValue;
}

void CollectRetiredShaderObjects()
{
    std::lock_guard lock(g_retiredShaderMutex);
    auto write = g_retiredShaderObjects.begin();
    for (auto read = g_retiredShaderObjects.begin();
         read != g_retiredShaderObjects.end();
         ++read)
    {
        if (!CanReleaseRetiredShaderObject(*read))
        {
            if (write != read)
                *write = std::move(*read);
            ++write;
        }
    }
    g_retiredShaderObjects.erase(write, g_retiredShaderObjects.end());
}

void RetireShaderObject(std::shared_ptr<ShaderObject> shader)
{
    {
        std::lock_guard lock(g_retiredShaderMutex);
        g_retiredShaderObjects.emplace_back(std::move(shader));
    }
    CollectRetiredShaderObjects();
}

void CollectRetiredDispatchBatches()
{
    std::lock_guard lock(g_retiredDispatchMutex);
    DispatchBatchRequest** current = &g_retiredDispatchBatches;
    while (*current)
    {
        DispatchBatchRequest* batch = *current;
        // Without a usable Unity frame fence, keep the batch conservatively
        // until ReleaseDeviceObjects observes reset or shutdown.
        const bool completed = batch->completionFence &&
            batch->completionFenceValue != 0 &&
            batch->completionFence->GetCompletedValue() >=
                batch->completionFenceValue;
        if (!completed)
        {
            current = &batch->nextRetired;
            continue;
        }

        *current = batch->nextRetired;
        delete batch;
    }
}

void RetireSubmittedDispatchBatch(
    std::unique_ptr<DispatchBatchRequest> batch,
    ID3D12Fence* frameFence,
    UINT64 frameFenceValue)
{
    batch->completionFence = frameFence;
    batch->completionFenceValue = frameFenceValue;

    std::lock_guard lock(g_retiredDispatchMutex);
    batch->nextRetired = g_retiredDispatchBatches;
    g_retiredDispatchBatches = batch.release();
}

void ReleaseRetiredDispatchBatches()
{
    DispatchBatchRequest* batches = nullptr;
    {
        std::lock_guard lock(g_retiredDispatchMutex);
        batches = g_retiredDispatchBatches;
        g_retiredDispatchBatches = nullptr;
    }

    while (batches)
    {
        DispatchBatchRequest* next = batches->nextRetired;
        delete batches;
        batches = next;
    }
}

void ReleaseDeviceObjects(bool clearShaderObjects)
{
    CancelPendingDispatchBatches();
    ReleaseRetiredDispatchBatches();
    {
        std::lock_guard lock(g_shaderMutex);
        if (clearShaderObjects)
        {
            g_shaderObjects.clear();
        }
        else
        {
            for (auto& entry : g_shaderObjects)
            {
                std::shared_ptr<ShaderObject>& shader = entry.second;
                shader->pipelineState.Reset();
                shader->rootSignature.Reset();
                std::lock_guard lifetimeLock(shader->lifetimeMutex);
                shader->pendingDispatchCount = 0;
                shader->gpuUseUntracked = false;
                shader->lastUseFence.Reset();
                shader->lastUseFenceValue = 0;
                shader->deviceGeneration = 0;
            }
        }
    }
    {
        std::lock_guard lock(g_retiredShaderMutex);
        g_retiredShaderObjects.clear();
    }
    {
        std::lock_guard lock(g_deviceMutex);
        g_device.Reset();
    }
}

bool RebuildShaderDeviceObjects(
    ID3D12Device* device,
    std::uint64_t deviceGeneration)
{
    std::lock_guard lock(g_shaderMutex);
    for (auto& entry : g_shaderObjects)
    {
        std::shared_ptr<ShaderObject>& shader = entry.second;
        shader->pipelineState.Reset();
        shader->rootSignature.Reset();
        shader->deviceGeneration = 0;
        if (!CreateRootSignature(device, shader->rootSignature) ||
            !CreatePipelineState(device, shader->renderState, *shader))
        {
            for (auto& remainingEntry : g_shaderObjects)
            {
                std::shared_ptr<ShaderObject>& remainingShader = remainingEntry.second;
                remainingShader->pipelineState.Reset();
                remainingShader->rootSignature.Reset();
                remainingShader->deviceGeneration = 0;
            }
            return false;
        }
        shader->deviceGeneration = deviceGeneration;
    }
    return true;
}

std::uint32_t EvaluateSupport(ID3D12Device* device)
{
    D3D12_FEATURE_DATA_SHADER_MODEL shaderModel{D3D_SHADER_MODEL_6_5};
    if (FAILED(device->CheckFeatureSupport(
            D3D12_FEATURE_SHADER_MODEL, &shaderModel, sizeof(shaderModel))) ||
        shaderModel.HighestShaderModel < D3D_SHADER_MODEL_6_5)
    {
        return VMS_SUPPORT_SHADER_MODEL_6_5_UNAVAILABLE;
    }

    D3D12_FEATURE_DATA_D3D12_OPTIONS7 options{};
    if (FAILED(device->CheckFeatureSupport(
            D3D12_FEATURE_D3D12_OPTIONS7, &options, sizeof(options))) ||
        options.MeshShaderTier == D3D12_MESH_SHADER_TIER_NOT_SUPPORTED)
    {
        return VMS_SUPPORT_MESH_SHADER_UNAVAILABLE;
    }

    ComPtr<ID3D12CommandAllocator> allocator;
    if (FAILED(device->CreateCommandAllocator(
            D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&allocator))))
    {
        return VMS_SUPPORT_MESH_SHADER_UNAVAILABLE;
    }

    ComPtr<ID3D12GraphicsCommandList6> commandList;
    if (FAILED(device->CreateCommandList(
            0,
            D3D12_COMMAND_LIST_TYPE_DIRECT,
            allocator.Get(),
            nullptr,
            IID_PPV_ARGS(&commandList))))
    {
        return VMS_SUPPORT_MESH_SHADER_UNAVAILABLE;
    }
    commandList->Close();
    return VMS_SUPPORT_SUPPORTED;
}

void ConfigureRenderEvent();

void HandleGraphicsDeviceEvent(UnityGfxDeviceEventType eventType)
{
    std::lock_guard deviceLifecycleLock(g_deviceLifecycleMutex);
    if (eventType == kUnityGfxDeviceEventShutdown ||
        eventType == kUnityGfxDeviceEventBeforeReset)
    {
        g_supportStatus.store(VMS_SUPPORT_UNKNOWN, std::memory_order_release);
        AdvanceDeviceGeneration();
        ReleaseDeviceObjects(false);
        return;
    }
    if (eventType != kUnityGfxDeviceEventInitialize &&
        eventType != kUnityGfxDeviceEventAfterReset)
    {
        return;
    }

    g_supportStatus.store(VMS_SUPPORT_UNKNOWN, std::memory_order_release);
    const std::uint64_t deviceGeneration = AdvanceDeviceGeneration();
    if (!g_unityGraphics)
    {
        g_supportStatus.store(
            VMS_SUPPORT_UNITY_D3D12_INTERFACE_UNAVAILABLE, std::memory_order_release);
        return;
    }

    const UnityGfxRenderer renderer = g_unityGraphics->GetRenderer();
    if (renderer != kUnityGfxRendererD3D12)
    {
        g_supportStatus.store(VMS_SUPPORT_NOT_D3D12, std::memory_order_release);
        return;
    }
    if (!g_unityGraphicsD3D12v8)
    {
        g_supportStatus.store(
            VMS_SUPPORT_UNITY_D3D12_INTERFACE_UNAVAILABLE,
            std::memory_order_release);
        return;
    }

    ID3D12Device* rawDevice = g_unityGraphicsD3D12v8->GetDevice();
    if (!rawDevice)
    {
        g_supportStatus.store(VMS_SUPPORT_NO_DEVICE, std::memory_order_release);
        return;
    }

    // ConfigureEvent is only valid while Unity has an active D3D12 device.
    // In particular, the D3D12 interface may appear non-null under NullGfx.
    ConfigureRenderEvent();

    const std::uint32_t supportStatus = EvaluateSupport(rawDevice);
    if (supportStatus == VMS_SUPPORT_SUPPORTED &&
        !RebuildShaderDeviceObjects(rawDevice, deviceGeneration))
    {
        g_supportStatus.store(VMS_SUPPORT_UNKNOWN, std::memory_order_release);
        return;
    }

    {
        std::lock_guard lock(g_deviceMutex);
        g_device = rawDevice;
    }
    g_supportStatus.store(supportStatus, std::memory_order_release);
}

void UNITY_INTERFACE_API OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType)
{
    HandleGraphicsDeviceEvent(eventType);
}

bool GetRecordingState(UnityGraphicsD3D12RecordingState& state)
{
    if (g_unityGraphicsD3D12v8 &&
        g_unityGraphicsD3D12v8->CommandRecordingState(&state))
    {
        return state.commandList != nullptr;
    }
    return false;
}

DispatchBatchExecutionResult ExecuteDispatchBatch(DispatchBatchRequest& batch)
{
    if (batch.dispatches.empty())
    {
        SetLastError("The mesh-shader dispatch batch is empty.");
        return DispatchBatchExecutionResult::Failed;
    }

    const std::uint64_t deviceGeneration =
        g_deviceGeneration.load(std::memory_order_acquire);
    if (deviceGeneration == 0 ||
        batch.deviceGeneration != deviceGeneration ||
        g_supportStatus.load(std::memory_order_acquire) !=
            VMS_SUPPORT_SUPPORTED)
    {
        SetLastError(
            "The D3D12 device generation changed before the mesh-shader batch executed.");
        return DispatchBatchExecutionResult::DeviceGenerationChanged;
    }

    constexpr D3D12_RESOURCE_STATES shaderReadState =
        static_cast<D3D12_RESOURCE_STATES>(
            D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE |
            D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
    for (const DispatchRequest& request : batch.dispatches)
    {
        if (request.shader->deviceGeneration != deviceGeneration)
        {
            SetLastError(
                "A mesh-shader batch references a ShaderObject from another D3D12 device generation.");
            return DispatchBatchExecutionResult::DeviceGenerationChanged;
        }
        for (UINT resourceIndex = 0;
             resourceIndex < kResourceCount;
             ++resourceIndex)
        {
            const ComPtr<ID3D12Resource>& resource =
                request.resources[resourceIndex];
            if (!resource ||
                resource->GetDesc().Dimension != D3D12_RESOURCE_DIMENSION_BUFFER)
            {
                SetLastError(
                    "A mesh-shader dispatch resource is not a D3D12 buffer (index " +
                    std::to_string(resourceIndex) + ").");
                return DispatchBatchExecutionResult::Failed;
            }
            g_unityGraphicsD3D12v8->RequestResourceState(
                resource.Get(), shaderReadState);
        }
    }

    UnityGraphicsD3D12RecordingState recordingState{};
    if (!GetRecordingState(recordingState))
    {
        SetLastError("Unity did not provide an active D3D12 command recording state.");
        return DispatchBatchExecutionResult::Failed;
    }

    ComPtr<ID3D12GraphicsCommandList6> commandList;
    HRESULT hr = recordingState.commandList->QueryInterface(IID_PPV_ARGS(&commandList));
    if (FAILED(hr))
    {
        SetLastError(
            FormatHResult("QueryInterface(ID3D12GraphicsCommandList6)", hr));
        return DispatchBatchExecutionResult::Failed;
    }

    for (const DispatchRequest& request : batch.dispatches)
    {
        commandList->SetPipelineState(request.shader->pipelineState.Get());
        commandList->SetGraphicsRootSignature(request.shader->rootSignature.Get());

        std::array<std::uint32_t, kRootConstantCount> constants{};
        std::memcpy(
            constants.data(), request.viewProjection.data(), sizeof(float) * 16);
        constants[16] = request.rendererListIndex;
        constants[17] = request.maxRequestCount;
        commandList->SetGraphicsRoot32BitConstants(
            kRootConstantParameter, kRootConstantCount, constants.data(), 0);

        for (UINT index = 0; index < kResourceCount; ++index)
        {
            commandList->SetGraphicsRootShaderResourceView(
                kFirstResourceParameter + index,
                request.resources[index]->GetGPUVirtualAddress());
        }

        // The AS reads t1, clamps the count, and emits the 2D mesh dispatch.
        commandList->DispatchMesh(1, 1, 1);
    }

    constexpr UINT indirectArgsIndex = 1;
    for (const DispatchRequest& request : batch.dispatches)
    {
        g_unityGraphicsD3D12v8->RequestResourceState(
            request.resources[indirectArgsIndex].Get(),
            D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT);
        for (UINT index = 0; index < kResourceCount; ++index)
        {
            const D3D12_RESOURCE_STATES finalState =
                index == indirectArgsIndex
                    ? D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT
                    : shaderReadState;
            g_unityGraphicsD3D12v8->NotifyResourceState(
                request.resources[index].Get(), finalState, false);
        }
    }
    return DispatchBatchExecutionResult::Submitted;
}

void UNITY_INTERFACE_API OnRenderEvent(int eventId, void* data)
{
    if (eventId == g_stateBoundaryEventId.load(std::memory_order_acquire))
    {
        if (data)
        {
            SetLastError("The state-boundary render event received unexpected data.");
            g_dispatchFailureCount.fetch_add(1, std::memory_order_relaxed);
        }
        CollectRetiredDispatchBatches();
        CollectRetiredShaderObjects();
        return;
    }

    if (!data)
    {
        SetLastError("The render event received a null dispatch request.");
        g_dispatchFailureCount.fetch_add(1, std::memory_order_relaxed);
        return;
    }

    std::unique_ptr<DispatchBatchRequest> batch =
        TakePendingDispatchBatch(data);
    if (!batch)
    {
        // Reset/shutdown may have canceled this token before Unity reached the
        // queued event. The request has already been completed and released.
        CollectRetiredDispatchBatches();
        CollectRetiredShaderObjects();
        return;
    }
    if (eventId != g_dispatchEventId.load(std::memory_order_acquire))
    {
        SetLastError("The render event ID does not match the reserved event ID.");
        g_dispatchFailureCount.fetch_add(1, std::memory_order_relaxed);
        CompleteDispatchBatch(*batch, nullptr, 0, false);
        CollectRetiredShaderObjects();
        return;
    }

    std::lock_guard deviceLifecycleLock(g_deviceLifecycleMutex);
    bool submitted = false;
    try
    {
        const DispatchBatchExecutionResult executionResult =
            ExecuteDispatchBatch(*batch);
        submitted =
            executionResult == DispatchBatchExecutionResult::Submitted;
        if (executionResult == DispatchBatchExecutionResult::Failed)
            g_dispatchFailureCount.fetch_add(1, std::memory_order_relaxed);
    }
    catch (const std::exception& exception)
    {
        SetLastError(std::string("Unhandled dispatch exception: ") + exception.what());
        g_dispatchFailureCount.fetch_add(1, std::memory_order_relaxed);
    }
    catch (...)
    {
        SetLastError("Unhandled non-standard dispatch exception.");
        g_dispatchFailureCount.fetch_add(1, std::memory_order_relaxed);
    }

    ComPtr<ID3D12Fence> frameFence;
    UINT64 frameFenceValue = 0;
    if (submitted && g_unityGraphicsD3D12v8)
    {
        frameFence = g_unityGraphicsD3D12v8->GetFrameFence();
        frameFenceValue = g_unityGraphicsD3D12v8->GetNextFrameFenceValue();
    }
    CompleteDispatchBatch(
        *batch, frameFence.Get(), frameFenceValue, submitted);
    if (submitted)
    {
        RetireSubmittedDispatchBatch(
            std::move(batch), frameFence.Get(), frameFenceValue);
    }
    CollectRetiredDispatchBatches();
    CollectRetiredShaderObjects();
}

void ConfigureRenderEvent()
{
    const int dispatchEventId =
        g_dispatchEventId.load(std::memory_order_acquire);
    const int stateBoundaryEventId =
        g_stateBoundaryEventId.load(std::memory_order_acquire);
    if (dispatchEventId < 0 ||
        stateBoundaryEventId < 0 ||
        !g_unityGraphics ||
        g_unityGraphics->GetRenderer() != kUnityGfxRendererD3D12 ||
        !g_unityGraphicsD3D12v8 ||
        !g_unityGraphicsD3D12v8->GetDevice())
    {
        return;
    }

    UnityD3D12PluginEventConfig dispatchConfig{};
    dispatchConfig.graphicsQueueAccess = kUnityD3D12GraphicsQueueAccess_DontCare;
    // CommandRecordingState exposes Unity's active list. SplitJobs workers must
    // be quiesced before the plugin replaces its PSO and graphics root state.
    dispatchConfig.flags =
        kUnityD3D12EventConfigFlag_SyncWorkerThreads |
        kUnityD3D12EventConfigFlag_ModifiesCommandBuffersState;
    dispatchConfig.ensureActiveRenderTextureIsBound = true;

    UnityD3D12PluginEventConfig stateBoundaryConfig{};
    stateBoundaryConfig.graphicsQueueAccess =
        kUnityD3D12GraphicsQueueAccess_DontCare;
    // This no-op event only marks the end of native command-list state
    // changes. Unity can invalidate its cached bindings without submitting the
    // active command buffers or waiting for worker threads a second time.
    stateBoundaryConfig.flags =
        kUnityD3D12EventConfigFlag_ModifiesCommandBuffersState;
    stateBoundaryConfig.ensureActiveRenderTextureIsBound = false;

    if (g_unityGraphicsD3D12v8)
    {
        g_unityGraphicsD3D12v8->ConfigureEvent(
            dispatchEventId, &dispatchConfig);
        g_unityGraphicsD3D12v8->ConfigureEvent(
            stateBoundaryEventId, &stateBoundaryConfig);
    }
}
} // namespace

extern "C"
{
std::uint32_t UNITY_INTERFACE_API VMS_GetAbiVersion()
{
    return VMS_ABI_VERSION;
}

std::uint32_t UNITY_INTERFACE_API VMS_GetSupportStatus()
{
    return g_supportStatus.load(std::memory_order_acquire);
}

std::uint64_t UNITY_INTERFACE_API VMS_GetDispatchFailureCount()
{
    return g_dispatchFailureCount.load(std::memory_order_acquire);
}

const char* UNITY_INTERFACE_API VMS_GetLastError()
{
    std::lock_guard lock(g_errorMutex);
    g_lastErrorSnapshot = g_lastError;
    return g_lastErrorSnapshot.c_str();
}

std::uint64_t UNITY_INTERFACE_API VMS_CreateShaderObject(
    const VMS_ShaderObjectDesc* desc)
{
    (void)desc;
    SetLastError(
        "Runtime HLSL compilation is no longer supported. Compile AS/MS/PS "
        "with VividMeshShaderCompiler and call VMS_CreateShaderObjectFromDxil.");
    return 0;
}

std::uint64_t UNITY_INTERFACE_API VMS_CreateShaderObjectFromDxil(
    const VMS_ShaderObjectDxilDesc* desc)
{
    try
    {
        CollectRetiredDispatchBatches();
        CollectRetiredShaderObjects();
        if (!desc || desc->structSize < sizeof(VMS_ShaderObjectDxilDesc))
        {
            SetLastError(
                "VMS_ShaderObjectDxilDesc is null or has an incompatible size.");
            return 0;
        }
        if (desc->abiVersion != VMS_ABI_VERSION)
        {
            SetLastError(
                "VMS_ShaderObjectDxilDesc has an incompatible ABI version.");
            return 0;
        }
        const std::array<const VMS_ShaderBytecode*, 3> bytecodes{
            &desc->amplificationShader,
            &desc->meshShader,
            &desc->pixelShader,
        };
        constexpr std::array<const char*, 3> bytecodeNames{
            "Amplification", "Mesh", "Pixel"};
        for (std::size_t index = 0; index < bytecodes.size(); ++index)
        {
            if (!bytecodes[index]->data || bytecodes[index]->size == 0)
            {
                SetLastError(
                    std::string(bytecodeNames[index]) +
                    " shader DXIL is null or empty.");
                return 0;
            }
            if (bytecodes[index]->size > SIZE_MAX)
            {
                SetLastError(
                    std::string(bytecodeNames[index]) +
                    " shader DXIL exceeds the native address space.");
                return 0;
            }
        }
        if (!ValidateRenderState(desc->renderState))
            return 0;

        std::lock_guard deviceLifecycleLock(g_deviceLifecycleMutex);
        if (g_supportStatus.load(std::memory_order_acquire) !=
            VMS_SUPPORT_SUPPORTED)
        {
            SetLastError(
                "Mesh shaders are not supported by the active Unity D3D12 device.");
            return 0;
        }

        ComPtr<ID3D12Device> device;
        {
            std::lock_guard lock(g_deviceMutex);
            device = g_device;
        }
        if (!device)
        {
            SetLastError("The Unity D3D12 device is not initialized.");
            return 0;
        }
        const std::uint64_t deviceGeneration =
            g_deviceGeneration.load(std::memory_order_acquire);
        if (deviceGeneration == 0)
        {
            SetLastError("The Unity D3D12 device generation is invalid.");
            return 0;
        }

        auto shader = std::make_shared<ShaderObject>();
        shader->renderState = desc->renderState;
        const auto copyBytecode = [](const VMS_ShaderBytecode& source,
                                     std::vector<std::uint8_t>& destination)
        {
            const auto* begin = static_cast<const std::uint8_t*>(source.data);
            destination.assign(begin, begin + static_cast<std::size_t>(source.size));
        };
        copyBytecode(desc->amplificationShader, shader->amplificationShader);
        copyBytecode(desc->meshShader, shader->meshShader);
        copyBytecode(desc->pixelShader, shader->pixelShader);

        if (!CreateRootSignature(device.Get(), shader->rootSignature) ||
            !CreatePipelineState(device.Get(), shader->renderState, *shader))
        {
            return 0;
        }
        shader->deviceGeneration = deviceGeneration;

        std::uint64_t handle =
            g_nextShaderHandle.fetch_add(1, std::memory_order_relaxed);
        if (handle == 0)
            handle = g_nextShaderHandle.fetch_add(1, std::memory_order_relaxed);
        {
            std::lock_guard lock(g_shaderMutex);
            g_shaderObjects.emplace(handle, std::move(shader));
        }
        SetLastError({});
        return handle;
    }
    catch (const std::exception& exception)
    {
        SetLastError(
            std::string("DXIL shader object creation failed: ") +
            exception.what());
        return 0;
    }
    catch (...)
    {
        SetLastError(
            "DXIL shader object creation failed with a non-standard exception.");
        return 0;
    }
}

void UNITY_INTERFACE_API VMS_DestroyShaderObject(std::uint64_t handle)
{
    if (handle == 0)
        return;

    std::shared_ptr<ShaderObject> shader;
    {
        std::lock_guard lock(g_shaderMutex);
        const auto found = g_shaderObjects.find(handle);
        if (found == g_shaderObjects.end())
            return;
        shader = std::move(found->second);
        g_shaderObjects.erase(found);
    }
    CancelPendingDispatchBatchesForShader(shader.get());
    RetireShaderObject(std::move(shader));
    CollectRetiredDispatchBatches();
}

void* UNITY_INTERFACE_API VMS_CreateDispatchBatchRequest(
    const VMS_DispatchDesc* descs,
    std::uint32_t dispatchCount)
{
    try
    {
        CollectRetiredDispatchBatches();
        if (!descs || dispatchCount == 0 || dispatchCount > kMaxDispatchBatchCount)
        {
            SetLastError(
                "The dispatch batch is null, empty, or exceeds the batch limit.");
            return nullptr;
        }
        if (g_dispatchEventId.load(std::memory_order_acquire) < 0)
        {
            SetLastError(
                "Unity has not reserved a render event ID for the plugin.");
            return nullptr;
        }

        std::lock_guard deviceLifecycleLock(g_deviceLifecycleMutex);
        if (g_supportStatus.load(std::memory_order_acquire) !=
            VMS_SUPPORT_SUPPORTED)
        {
            SetLastError("The Unity D3D12 device is not ready for dispatch.");
            return nullptr;
        }
        const std::uint64_t deviceGeneration =
            g_deviceGeneration.load(std::memory_order_acquire);
        if (deviceGeneration == 0)
        {
            SetLastError("The Unity D3D12 device generation is invalid.");
            return nullptr;
        }

        auto batch = std::make_unique<DispatchBatchRequest>();
        batch->deviceGeneration = deviceGeneration;
        batch->dispatches.reserve(dispatchCount);
        void* eventData = nullptr;
        {
            // Keep lookup, registration, and pending-use accounting atomic
            // with handle destruction.
            std::lock_guard shaderLock(g_shaderMutex);
            for (std::uint32_t dispatchIndex = 0;
                 dispatchIndex < dispatchCount;
                 ++dispatchIndex)
            {
                const VMS_DispatchDesc& desc = descs[dispatchIndex];
                if (desc.structSize < sizeof(VMS_DispatchDesc))
                {
                    SetLastError(
                        "A VMS_DispatchDesc has an incompatible size.");
                    return nullptr;
                }
                if (desc.abiVersion != VMS_ABI_VERSION)
                {
                    SetLastError(
                        "A VMS_DispatchDesc has an incompatible ABI version.");
                    return nullptr;
                }

                const auto found = g_shaderObjects.find(desc.shaderHandle);
                if (found == g_shaderObjects.end())
                {
                    SetLastError(
                        "A dispatch references an unknown shader handle.");
                    return nullptr;
                }
                std::shared_ptr<ShaderObject> shader = found->second;
                if (!shader->rootSignature || !shader->pipelineState)
                {
                    SetLastError(
                        "A dispatch references a ShaderObject that is not ready for the active device.");
                    return nullptr;
                }
                if (shader->deviceGeneration != deviceGeneration)
                {
                    SetLastError(
                        "A dispatch references a ShaderObject from another D3D12 device generation.");
                    return nullptr;
                }

                const std::array<void*, kResourceCount> nativeResources{
                    desc.visibleRequests,
                    desc.indirectArgs,
                    desc.instances,
                    desc.meshlets,
                    desc.vertices,
                    desc.indices,
                };
                for (void* resource : nativeResources)
                {
                    if (!resource)
                    {
                        SetLastError("A dispatch resource is null.");
                        return nullptr;
                    }
                }

                DispatchRequest request{};
                request.shader = std::move(shader);
                for (UINT resourceIndex = 0;
                     resourceIndex < kResourceCount;
                     ++resourceIndex)
                {
                    request.resources[resourceIndex] =
                        static_cast<ID3D12Resource*>(nativeResources[resourceIndex]);
                }
                request.rendererListIndex = desc.rendererListIndex;
                request.maxRequestCount = desc.maxRequestCount;
                std::memcpy(
                    request.viewProjection.data(),
                    desc.viewProjectionColumnMajor,
                    sizeof(desc.viewProjectionColumnMajor));
                batch->dispatches.emplace_back(std::move(request));
            }

            DispatchBatchRequest* registeredBatch = batch.get();
            eventData = RegisterPendingDispatchBatch(std::move(batch));
            try
            {
                MarkDispatchBatchQueued(*registeredBatch);
            }
            catch (...)
            {
                std::unique_ptr<DispatchBatchRequest> canceledBatch =
                    TakePendingDispatchBatch(eventData);
                if (canceledBatch)
                {
                    CompleteDispatchBatch(
                        *canceledBatch, nullptr, 0, false);
                }
                throw;
            }
        }

        SetLastError({});
        return eventData;
    }
    catch (const std::exception& exception)
    {
        SetLastError(
            std::string("Dispatch batch creation failed: ") + exception.what());
        return nullptr;
    }
    catch (...)
    {
        SetLastError(
            "Dispatch batch creation failed with a non-standard exception.");
        return nullptr;
    }
}

void* UNITY_INTERFACE_API VMS_CreateDispatchRequest(
    const VMS_DispatchDesc* desc)
{
    return VMS_CreateDispatchBatchRequest(desc, desc ? 1u : 0u);
}

void UNITY_INTERFACE_API VMS_DestroyDispatchRequest(void* request)
{
    std::unique_ptr<DispatchBatchRequest> batch =
        TakePendingDispatchBatch(request);
    if (!batch)
        return;
    CompleteDispatchBatch(*batch, nullptr, 0, false);
    CollectRetiredDispatchBatches();
    CollectRetiredShaderObjects();
}

UnityRenderingEventAndData UNITY_INTERFACE_API VMS_GetRenderEventFunc()
{
    return OnRenderEvent;
}

int UNITY_INTERFACE_API VMS_GetDispatchEventId()
{
    return g_dispatchEventId.load(std::memory_order_acquire);
}

int UNITY_INTERFACE_API VMS_GetStateBoundaryEventId()
{
    return g_stateBoundaryEventId.load(std::memory_order_acquire);
}

UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API UnityPluginLoad(
    IUnityInterfaces* unityInterfaces)
{
    g_unityInterfaces = unityInterfaces;
    g_unityGraphics = GetUnityInterface<IUnityGraphics>(g_unityInterfaces);
    g_unityGraphicsD3D12v8 =
        GetUnityInterface<IUnityGraphicsD3D12v8>(g_unityInterfaces);

    if (!g_unityGraphics)
    {
        g_supportStatus.store(
            VMS_SUPPORT_UNITY_D3D12_INTERFACE_UNAVAILABLE,
            std::memory_order_release);
        SetLastError("Unity did not provide IUnityGraphics.");
        return;
    }

    const int firstEventId = g_unityGraphics->ReserveEventIDRange(2);
    g_dispatchEventId.store(firstEventId, std::memory_order_release);
    g_stateBoundaryEventId.store(
        firstEventId < 0 ? -1 : firstEventId + 1,
        std::memory_order_release);
    g_unityGraphics->RegisterDeviceEventCallback(OnGraphicsDeviceEvent);
    OnGraphicsDeviceEvent(kUnityGfxDeviceEventInitialize);
}

UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API UnityPluginUnload()
{
    if (g_unityGraphics)
        g_unityGraphics->UnregisterDeviceEventCallback(OnGraphicsDeviceEvent);

    {
        std::lock_guard deviceLifecycleLock(g_deviceLifecycleMutex);
        g_supportStatus.store(VMS_SUPPORT_UNKNOWN, std::memory_order_release);
        AdvanceDeviceGeneration();
        ReleaseDeviceObjects(true);
        g_dispatchEventId.store(-1, std::memory_order_release);
        g_stateBoundaryEventId.store(-1, std::memory_order_release);
        g_unityGraphicsD3D12v8 = nullptr;
        g_unityGraphics = nullptr;
        g_unityInterfaces = nullptr;
    }
}
} // extern "C"
