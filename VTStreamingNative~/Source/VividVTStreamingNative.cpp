#include <Windows.h>
#include <dstorage.h>
#include <wrl/client.h>
#include <zstd.h>

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

using Microsoft::WRL::ComPtr;

#if defined(VIVID_VT_STREAMING_EXPORTS)
#define VIVID_VT_API extern "C" __declspec(dllexport)
#else
#define VIVID_VT_API extern "C" __declspec(dllimport)
#endif

namespace
{
    struct FileState
    {
        ComPtr<IDStorageFile> File;
    };

    struct QueueState
    {
        ComPtr<IDStorageQueue> Queue;
        std::mutex Mutex;
    };

    struct BatchState
    {
        ComPtr<IDStorageQueue> Queue;
        ComPtr<IDStorageStatusArray> Status;
        std::vector<std::vector<std::uint8_t>> Results;
        std::uint64_t CancellationTag{};
        std::atomic<bool> Cancelled{false};
    };

    std::once_flag g_InitializeFlag;
    ComPtr<IDStorageFactory> g_Factory;
    std::unique_ptr<QueueState> g_HighPriorityQueue;
    std::unique_ptr<QueueState> g_NormalPriorityQueue;
    std::atomic<std::uint64_t> g_NextCancellationTag{1};
    thread_local std::string g_LastError;

    void SetError(const char* message, HRESULT result = S_OK)
    {
        g_LastError = message != nullptr ? message : "Unknown DirectStorage error";
        if (FAILED(result))
        {
            char suffix[32]{};
            std::snprintf(suffix, sizeof(suffix), " (0x%08X)", static_cast<unsigned>(result));
            g_LastError += suffix;
        }
    }

    bool CreateQueue(DSTORAGE_PRIORITY priority, const char* name, std::unique_ptr<QueueState>& output)
    {
        DSTORAGE_QUEUE_DESC desc{};
        desc.SourceType = DSTORAGE_REQUEST_SOURCE_FILE;
        desc.Capacity = DSTORAGE_MIN_QUEUE_CAPACITY;
        desc.Priority = priority;
        desc.Name = name;
        desc.Device = nullptr;

        auto queue = std::make_unique<QueueState>();
        const HRESULT result = g_Factory->CreateQueue(&desc, IID_PPV_ARGS(&queue->Queue));
        if (FAILED(result))
        {
            SetError("DirectStorage queue creation failed", result);
            return false;
        }

        output = std::move(queue);
        return true;
    }

    void InitializeDirectStorage()
    {
        const HRESULT factoryResult = DStorageGetFactory(IID_PPV_ARGS(&g_Factory));
        if (FAILED(factoryResult))
        {
            SetError("DStorageGetFactory failed", factoryResult);
            return;
        }

        if (!CreateQueue(DSTORAGE_PRIORITY_HIGH, "VividVT_High", g_HighPriorityQueue)
            || !CreateQueue(DSTORAGE_PRIORITY_NORMAL, "VividVT_Normal", g_NormalPriorityQueue))
        {
            g_HighPriorityQueue.reset();
            g_NormalPriorityQueue.reset();
            g_Factory.Reset();
        }
    }

    bool EnsureDirectStorage()
    {
        std::call_once(g_InitializeFlag, InitializeDirectStorage);
        return g_Factory && g_HighPriorityQueue && g_NormalPriorityQueue;
    }
}

VIVID_VT_API std::uint32_t VividVT_ZstdVersionNumber()
{
    return ZSTD_versionNumber();
}

VIVID_VT_API size_t VividVT_ZstdCompressBound(size_t sourceSize)
{
    return ZSTD_compressBound(sourceSize);
}

VIVID_VT_API size_t VividVT_ZstdCompress(
    void* destination,
    size_t destinationCapacity,
    const void* source,
    size_t sourceSize,
    int compressionLevel)
{
    return ZSTD_compress(
        destination,
        destinationCapacity,
        source,
        sourceSize,
        std::clamp(compressionLevel, 1, 3));
}

VIVID_VT_API size_t VividVT_ZstdDecompress(
    void* destination,
    size_t destinationCapacity,
    const void* source,
    size_t sourceSize)
{
    return ZSTD_decompress(destination, destinationCapacity, source, sourceSize);
}

VIVID_VT_API int VividVT_ZstdIsError(size_t code)
{
    return static_cast<int>(ZSTD_isError(code));
}

VIVID_VT_API const char* VividVT_ZstdGetErrorName(size_t code)
{
    return ZSTD_getErrorName(code);
}

VIVID_VT_API int VividVT_DSIsAvailable()
{
    return EnsureDirectStorage() ? 1 : 0;
}

VIVID_VT_API void* VividVT_DSOpenFile(const wchar_t* path)
{
    if (!EnsureDirectStorage() || path == nullptr || path[0] == L'\0')
        return nullptr;

    auto state = std::make_unique<FileState>();
    const HRESULT result = g_Factory->OpenFile(path, IID_PPV_ARGS(&state->File));
    if (FAILED(result))
    {
        SetError("DirectStorage failed to open file", result);
        return nullptr;
    }

    return state.release();
}

VIVID_VT_API void VividVT_DSCloseFile(void* fileHandle)
{
    std::unique_ptr<FileState> file(static_cast<FileState*>(fileHandle));
    if (file && file->File)
        file->File->Close();
}

VIVID_VT_API void* VividVT_DSCreateMemoryBatch(
    void* fileHandle,
    const std::int64_t* offsets,
    const int* sizes,
    const std::uint8_t* priorities,
    int commandCount)
{
    auto* file = static_cast<FileState*>(fileHandle);
    if (!EnsureDirectStorage()
        || file == nullptr
        || !file->File
        || offsets == nullptr
        || sizes == nullptr
        || priorities == nullptr
        || commandCount <= 0
        || commandCount > 64)
    {
        SetError("Invalid DirectStorage VT batch arguments");
        return nullptr;
    }

    bool highPriority = false;
    for (int index = 0; index < commandCount; ++index)
        highPriority |= priorities[index] != 0;
    QueueState* queueState = highPriority ? g_HighPriorityQueue.get() : g_NormalPriorityQueue.get();

    auto batch = std::make_unique<BatchState>();
    batch->Queue = queueState->Queue;
    batch->CancellationTag = g_NextCancellationTag.fetch_add(1, std::memory_order_relaxed);
    batch->Results.resize(static_cast<size_t>(commandCount));
    const HRESULT statusResult = g_Factory->CreateStatusArray(1, "VividVT_Batch", IID_PPV_ARGS(&batch->Status));
    if (FAILED(statusResult))
    {
        SetError("DirectStorage status array creation failed", statusResult);
        return nullptr;
    }

    std::scoped_lock lock(queueState->Mutex);
    for (int index = 0; index < commandCount; ++index)
    {
        if (offsets[index] < 0 || sizes[index] < 0)
        {
            SetError("Invalid DirectStorage VT read range");
            return nullptr;
        }

        auto& result = batch->Results[static_cast<size_t>(index)];
        result.resize(static_cast<size_t>(sizes[index]));
        DSTORAGE_REQUEST request{};
        request.Options.CompressionFormat = DSTORAGE_COMPRESSION_FORMAT_NONE;
        request.Options.SourceType = DSTORAGE_REQUEST_SOURCE_FILE;
        request.Options.DestinationType = DSTORAGE_REQUEST_DESTINATION_MEMORY;
        request.Source.File.Source = file->File.Get();
        request.Source.File.Offset = static_cast<UINT64>(offsets[index]);
        request.Source.File.Size = static_cast<UINT32>(sizes[index]);
        request.Destination.Memory.Buffer = result.data();
        request.Destination.Memory.Size = static_cast<UINT32>(sizes[index]);
        request.UncompressedSize = 0;
        request.CancellationTag = batch->CancellationTag;
        request.Name = "VividVT_Chunk";
        queueState->Queue->EnqueueRequest(&request);
    }

    queueState->Queue->EnqueueStatus(batch->Status.Get(), 0);
    queueState->Queue->Submit();
    return batch.release();
}

VIVID_VT_API int VividVT_DSGetBatchStatus(void* batchHandle)
{
    auto* batch = static_cast<BatchState*>(batchHandle);
    if (batch == nullptr || !batch->Status)
        return -1;
    if (!batch->Status->IsComplete(0))
        return 0;

    const HRESULT result = batch->Status->GetHResult(0);
    if (FAILED(result))
    {
        SetError("DirectStorage VT batch failed", result);
        return -1;
    }

    return 1;
}

VIVID_VT_API int VividVT_DSGetResultSize(void* batchHandle, int commandIndex)
{
    auto* batch = static_cast<BatchState*>(batchHandle);
    if (batch == nullptr || commandIndex < 0 || static_cast<size_t>(commandIndex) >= batch->Results.size())
        return -1;
    return static_cast<int>(batch->Results[static_cast<size_t>(commandIndex)].size());
}

VIVID_VT_API int VividVT_DSCopyResult(
    void* batchHandle,
    int commandIndex,
    void* destination,
    int destinationSize)
{
    auto* batch = static_cast<BatchState*>(batchHandle);
    if (batch == nullptr
        || destination == nullptr
        || commandIndex < 0
        || static_cast<size_t>(commandIndex) >= batch->Results.size())
    {
        return 0;
    }

    const auto& result = batch->Results[static_cast<size_t>(commandIndex)];
    if (destinationSize != static_cast<int>(result.size()))
        return 0;
    std::memcpy(destination, result.data(), result.size());
    return 1;
}

VIVID_VT_API void VividVT_DSCancelBatch(void* batchHandle)
{
    auto* batch = static_cast<BatchState*>(batchHandle);
    if (batch == nullptr || !batch->Queue)
        return;

    batch->Cancelled.store(true, std::memory_order_release);
    batch->Queue->CancelRequestsWithTag(UINT64_MAX, batch->CancellationTag);
    batch->Queue->Submit();
}

VIVID_VT_API void VividVT_DSReleaseBatch(void* batchHandle)
{
    std::unique_ptr<BatchState> batch(static_cast<BatchState*>(batchHandle));
    if (!batch)
        return;
    if (batch->Status && !batch->Status->IsComplete(0))
    {
        batch->Queue->CancelRequestsWithTag(UINT64_MAX, batch->CancellationTag);
        batch->Queue->Submit();
        while (!batch->Status->IsComplete(0))
            SwitchToThread();
    }
}

VIVID_VT_API const char* VividVT_DSGetLastError()
{
    return g_LastError.c_str();
}
