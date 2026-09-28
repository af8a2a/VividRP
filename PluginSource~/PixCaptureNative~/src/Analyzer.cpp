#include <windows.h>
#include <d3d12.h>
#include <wrl/client.h>
#include <PixApi.h>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>
#include <iomanip>
#include <memory>
#include <thread>
#include <cmath>
#include <cctype>
#include <bcrypt.h>
#include <experimental/PixApiCommonExperimentalInterfaces.h>
#include "Validation.h"

using Microsoft::WRL::ComPtr;
namespace fs = std::filesystem;
namespace
{
    struct Error { HRESULT hr; const char* operation; };
    void Check(HRESULT hr, const char* operation) { if (FAILED(hr)) throw Error{hr, operation}; }
    std::string Utf8(const std::wstring& value)
    {
        if (value.empty()) return {};
        int length = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
        if (!length) throw Error{HRESULT_FROM_WIN32(GetLastError()), "utf8"};
        std::string result(length, '\0');
        WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), result.data(), length, nullptr, nullptr);
        return result;
    }
    std::string Json(const std::string& text)
    {
        std::ostringstream out;
        out << '"';
        for (unsigned char c : text)
        {
            if (c == '"' || c == '\\') out << '\\' << c;
            else if (c < 0x20) out << "\\u" << std::hex << std::setw(4) << std::setfill('0') << static_cast<int>(c);
            else out << c;
        }
        out << '"';
        return out.str();
    }
    ComPtr<IPixFactory> CreateFactory(const fs::path& pix)
    {
        // This Preview's factory resolves additional engine modules relative to
        // CWD (also required by Microsoft's C++ sample). This is an isolated
        // helper process; all caller paths have already been made absolute.
        fs::current_path(pix);
        // Resolve the explicitly selected SDK installation, not PATH/the CWD.
        HMODULE module = LoadLibraryExW((pix / L"pixapi.dll").c_str(), nullptr,
            LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        if (!module) throw Error{HRESULT_FROM_WIN32(GetLastError()), "load_pix_api"};
        using Create = HRESULT(WINAPI*)(REFIID, void**);
        auto create = reinterpret_cast<Create>(GetProcAddress(module, "PixCreateFactory"));
        if (!create) throw Error{HRESULT_FROM_WIN32(ERROR_PROC_NOT_FOUND), "PixCreateFactory_export"};
        ComPtr<IPixFactory> factory;
        Check(create(IID_PPV_ARGS(&factory)), "PixCreateFactory");
        // The module remains loaded until process exit, outliving all COM objects.
        return factory;
    }
    void SaveNew(const fs::path& path, const std::string& result)
    {
        HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) throw Error{HRESULT_FROM_WIN32(GetLastError()), "create_result_file"};
        DWORD written = 0;
        BOOL ok = WriteFile(file, result.data(), static_cast<DWORD>(result.size()), &written, nullptr);
        DWORD error = GetLastError();
        if (ok && written == result.size()) ok = FlushFileBuffers(file);
        if (!ok) error = GetLastError();
        CloseHandle(file);
        if (!ok || written != result.size()) throw Error{HRESULT_FROM_WIN32(error ? error : ERROR_WRITE_FAULT), "write_result_file"};
    }

    int Validate(const fs::path& pix, const fs::path& path, const std::string& id, const std::string& pass, const fs::path& output, bool allQueues)
    {
        vivid::pix::Validation result;
        UINT64 queueCount = 0;
        HRESULT hr = S_OK;
        std::vector<std::string> eventSample;
        try
        {
            auto factory = CreateFactory(pix);
            ComPtr<IPixGpuCaptureDocument> document;
            Check(factory->OpenGpuCaptureDocument(path.c_str(), IID_PPV_ARGS(&document)), "open_capture_failed");
            ComPtr<IPixCollection> queues;
            Check(document->GetQueues(IID_PPV_ARGS(&queues)), "get_queues_failed");
            queueCount = queues->GetCount();
            std::vector<vivid::pix::QueueEvents> capturedQueues;
            for (UINT64 q = 0; q < queueCount; ++q)
            {
                ComPtr<IPixGpuCaptureQueueInfo> queue;
                Check(queues->Get(q, IID_PPV_ARGS(&queue)), "get_queue_failed");
                std::vector<vivid::pix::Event> events;
                events.reserve(queue->GetEventCount());
                for (UINT i = 0; i < queue->GetEventCount(); ++i)
                {
                    PIX_EVENT_INFO event{};
                    Check(queue->GetEvent(i, &event), "get_event_failed");
                    std::string name = event.Name ? event.Name : "";
                    if (eventSample.size() < 16)
                    {
                        std::ostringstream sample;
                        sample << "{\"queue\":" << q << ",\"index\":" << event.Index << ",\"parent\":" << event.ParentIndex
                               << ",\"name\":" << Json(name.substr(0, 512)) << ",\"apiCallData\":"
                               << Json(std::string(event.ApiCallData ? event.ApiCallData : "").substr(0, 512)) << '}';
                        eventSample.push_back(sample.str());
                    }
                    events.push_back({event.Index, event.ParentIndex, name,
                        vivid::pix::IsWork(name, event.ApiCallData ? event.ApiCallData : "")});
                }
                capturedQueues.push_back({queue->GetType() == PIX_QUEUE_TYPE_GRAPHICS, std::move(events)});
            }
            result = vivid::pix::ValidateQueues(capturedQueues, id, pass, allQueues);
        }
        catch (const Error& error) { result.success = false; result.code = error.operation; hr = error.hr; }
        std::ostringstream json;
        json << std::boolalpha << "{\n  \"schemaVersion\": 2,\n  \"success\": " << result.success
             << ",\n  \"code\": " << Json(result.code)
             << ",\n  \"hresult\": " << static_cast<int32_t>(hr)
             << ",\n  \"sessionId\": " << Json(id) << ",\n  \"capturePath\": " << Json(Utf8(fs::absolute(path).wstring()))
             << ",\n  \"expectedPass\": " << Json(pass) << ",\n  \"pixInstall\": " << Json(Utf8(pix.wstring()))
             << ",\n  \"queueCount\": " << queueCount << ",\n  \"gpuWorkEventCount\": " << result.workCount
             << ",\n  \"boundaryMode\": " << Json(allQueues ? "all" : "graphics")
             << ",\n  \"validatedQueueScopes\": " << result.validatedScopes
             << ",\n  \"beginMarkerFound\": " << result.beginFound << ",\n  \"endMarkerFound\": " << result.endFound
             << ",\n  \"markersOrdered\": " << result.ordered << ",\n  \"expectedPassFound\": " << result.passFound;
        if (!result.success)
        {
            json << ",\n  \"eventSample\": [";
            for (size_t i = 0; i < eventSample.size(); ++i) json << (i ? "," : "") << eventSample[i];
            json << ']';
        }
        json << "\n}\n";
        SaveNew(output, json.str());
        std::cout << json.str();
        return result.success ? 0 : 2;
    }

#include "Analysis.h"
}

int wmain(int argc, wchar_t** argv)
{
    const bool validate = argc > 1 && std::wstring(argv[1]) == L"validate";
    if (argc < 7 || (validate && argc > 8) || (!validate && argc < 8) ||
        (argc >= 8 && std::wstring(argv[7]) != L"all" && std::wstring(argv[7]) != L"graphics"))
    {
        std::cerr << "Usage: vivid-pix-analyzer ACTION PIX_INSTALL capture.wpix SESSION EXPECTED_PASS new-result.json graphics|all [--queue ID --event INDEX --offset N --count 1..256 --marker NAME --counter ID --type INDEX --stage INDEX --experiment GUID --timeout_seconds 1..300 --request_id ID]\n";
        return 1;
    }
    HRESULT com = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(com)) return 3;
    int result = 3;
    try
    {
        auto pix = fs::absolute(argv[2]), path = fs::absolute(argv[3]), output = fs::absolute(argv[6]);
        if (fs::exists(output)) throw Error{HRESULT_FROM_WIN32(ERROR_FILE_EXISTS), "result_file_exists"};
        const bool all = argc >= 8 && std::wstring(argv[7]) == L"all";
        if (validate) result = Validate(pix, path, Utf8(argv[4]), Utf8(argv[5]), output, all);
        else result = Analyze(pix, path, Utf8(argv[4]), Utf8(argv[5]), output, all, ParseOptions(argc, argv));
    }
    catch (const Error& error) { std::cerr << error.operation << ": 0x" << std::hex << error.hr << '\n'; }
    catch (const std::exception& error) { std::cerr << error.what() << '\n'; }
    CoUninitialize();
    return result;
}
