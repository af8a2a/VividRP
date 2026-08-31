#include "Compiler.h"

#include <Windows.h>
#include <ObjIdl.h>
#include <Unknwn.h>

#include <dxcapi.h>
#include <wrl/client.h>

#include <algorithm>
#include <atomic>
#include <filesystem>
#include <memory>
#include <mutex>
#include <new>
#include <sstream>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

#pragma comment(lib, "dxcompiler")

namespace
{
using Microsoft::WRL::ComPtr;

struct CompileResult
{
    bool success = false;
    std::vector<std::uint8_t> dxil;
    std::string diagnostics;
};

struct IncludeRoot
{
    std::wstring logicalPrefix;
    std::filesystem::path physicalPath;
};

std::wstring Utf8ToWide(std::string_view text)
{
    if (text.empty())
        return {};
    if (text.size() > static_cast<std::size_t>(INT_MAX))
        return {};

    const int required = MultiByteToWideChar(
        CP_UTF8, MB_ERR_INVALID_CHARS, text.data(),
        static_cast<int>(text.size()), nullptr, 0);
    if (required <= 0)
        return {};

    std::wstring result(static_cast<std::size_t>(required), L'\0');
    const int written = MultiByteToWideChar(
        CP_UTF8, MB_ERR_INVALID_CHARS, text.data(),
        static_cast<int>(text.size()), result.data(), required);
    return written == required ? result : std::wstring{};
}

std::string WideToUtf8(std::wstring_view text)
{
    if (text.empty())
        return {};
    if (text.size() > static_cast<std::size_t>(INT_MAX))
        return {};

    const int required = WideCharToMultiByte(
        CP_UTF8, WC_ERR_INVALID_CHARS, text.data(),
        static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
    if (required <= 0)
        return {};

    std::string result(static_cast<std::size_t>(required), '\0');
    const int written = WideCharToMultiByte(
        CP_UTF8, WC_ERR_INVALID_CHARS, text.data(),
        static_cast<int>(text.size()), result.data(), required, nullptr, nullptr);
    return written == required ? result : std::string{};
}

std::string FormatHResult(const char* operation, HRESULT value)
{
    std::ostringstream stream;
    stream << operation << " failed with HRESULT 0x" << std::hex
           << std::uppercase << static_cast<std::uint32_t>(value) << '.';
    return stream.str();
}

void NormalizeSlashes(std::wstring& path)
{
    std::replace(path.begin(), path.end(), L'\\', L'/');
}

bool StartsWith(std::wstring_view text, std::wstring_view prefix)
{
    return text.size() >= prefix.size() &&
        text.compare(0, prefix.size(), prefix) == 0;
}

std::filesystem::path JoinPhysicalPath(
    const std::filesystem::path& root,
    std::wstring_view relativePath)
{
    while (!relativePath.empty() &&
           (relativePath.front() == L'/' || relativePath.front() == L'\\'))
    {
        relativePath.remove_prefix(1);
    }
    return (root / std::filesystem::path(relativePath)).lexically_normal();
}

class IncludeHandler final : public IDxcIncludeHandler
{
public:
    IncludeHandler(
        IDxcUtils* utils,
        std::vector<IncludeRoot> roots,
        std::wstring sourceName)
        : m_utils(utils), m_roots(std::move(roots))
    {
        NormalizeSlashes(sourceName);
        std::filesystem::path sourcePath;
        if (!sourceName.empty())
        {
            const std::filesystem::path candidate(sourceName);
            if (candidate.is_absolute())
            {
                sourcePath = candidate;
            }
            else
            {
                for (const IncludeRoot& root : m_roots)
                {
                    if (!root.logicalPrefix.empty() &&
                        StartsWith(sourceName, root.logicalPrefix))
                    {
                        sourcePath = JoinPhysicalPath(
                            root.physicalPath,
                            std::wstring_view(sourceName).substr(
                                root.logicalPrefix.size()));
                        break;
                    }
                }
            }
        }
        if (!sourcePath.empty())
            m_sourceDirectory = sourcePath.parent_path().lexically_normal();
    }

    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** object) override
    {
        if (!object)
            return E_POINTER;
        *object = nullptr;
        if (iid == __uuidof(IUnknown) || iid == __uuidof(IDxcIncludeHandler))
        {
            *object = static_cast<IDxcIncludeHandler*>(this);
            AddRef();
            return S_OK;
        }
        return E_NOINTERFACE;
    }

    ULONG STDMETHODCALLTYPE AddRef() override
    {
        return m_referenceCount.fetch_add(1, std::memory_order_relaxed) + 1;
    }

    ULONG STDMETHODCALLTYPE Release() override
    {
        const ULONG remaining =
            m_referenceCount.fetch_sub(1, std::memory_order_acq_rel) - 1;
        if (remaining == 0)
            delete this;
        return remaining;
    }

    HRESULT STDMETHODCALLTYPE LoadSource(
        LPCWSTR filename,
        IDxcBlob** includeSource) override
    {
        if (!filename || !includeSource)
            return E_INVALIDARG;
        *includeSource = nullptr;

        std::wstring requested(filename);
        NormalizeSlashes(requested);
        while (StartsWith(requested, L"./"))
            requested.erase(0, 2);
        std::vector<std::filesystem::path> candidates;

        for (const IncludeRoot& root : m_roots)
        {
            if (!root.logicalPrefix.empty() &&
                StartsWith(requested, root.logicalPrefix))
            {
                candidates.push_back(JoinPhysicalPath(
                    root.physicalPath,
                    std::wstring_view(requested).substr(
                        root.logicalPrefix.size())));
            }
        }

        const std::filesystem::path requestedPath(requested);
        if (requestedPath.is_absolute())
            candidates.push_back(requestedPath.lexically_normal());
        else
        {
            if (!m_sourceDirectory.empty())
                candidates.push_back(JoinPhysicalPath(
                    m_sourceDirectory, requested));

            {
                std::lock_guard lock(m_directoryMutex);
                for (const std::filesystem::path& directory :
                     m_loadedDirectories)
                {
                    candidates.push_back(JoinPhysicalPath(directory, requested));
                }
            }

            for (const IncludeRoot& root : m_roots)
                candidates.push_back(JoinPhysicalPath(root.physicalPath, requested));
        }

        std::vector<std::wstring> attempted;
        for (const std::filesystem::path& candidate : candidates)
        {
            const std::wstring nativePath = candidate.native();
            const bool alreadyAttempted = std::find(
                attempted.begin(), attempted.end(), nativePath) != attempted.end();
            if (alreadyAttempted)
                continue;
            attempted.push_back(nativePath);

            ComPtr<IDxcBlobEncoding> source;
            const HRESULT hr = m_utils->LoadFile(
                nativePath.c_str(), nullptr, &source);
            if (SUCCEEDED(hr) && source)
            {
                {
                    std::lock_guard lock(m_directoryMutex);
                    const std::filesystem::path directory = candidate.parent_path();
                    if (std::find(
                            m_loadedDirectories.begin(),
                            m_loadedDirectories.end(), directory) ==
                        m_loadedDirectories.end())
                    {
                        m_loadedDirectories.push_back(directory);
                    }
                }
                *includeSource = source.Detach();
                return S_OK;
            }
        }

        std::ostringstream diagnostic;
        diagnostic << "Unable to resolve include '" << WideToUtf8(requested)
                   << "'.";
        if (!attempted.empty())
        {
            diagnostic << " Tried:";
            for (const std::wstring& candidate : attempted)
                diagnostic << "\n  " << WideToUtf8(candidate);
        }
        {
            std::lock_guard lock(m_diagnosticMutex);
            m_failureDiagnostic = diagnostic.str();
        }
        return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
    }

    std::string FailureDiagnostic() const
    {
        std::lock_guard lock(m_diagnosticMutex);
        return m_failureDiagnostic;
    }

private:
    ~IncludeHandler() = default;

    std::atomic<ULONG> m_referenceCount{1};
    ComPtr<IDxcUtils> m_utils;
    std::vector<IncludeRoot> m_roots;
    std::filesystem::path m_sourceDirectory;
    std::mutex m_directoryMutex;
    std::vector<std::filesystem::path> m_loadedDirectories;
    mutable std::mutex m_diagnosticMutex;
    std::string m_failureDiagnostic;
};

bool ValidateCompileDesc(
    const VMSC_CompileDesc* desc,
    CompileResult& result)
{
    if (!desc || desc->structSize < sizeof(VMSC_CompileDesc))
    {
        result.diagnostics =
            "VMSC_CompileDesc is null or has an incompatible size.";
        return false;
    }
    if (desc->abiVersion != VMSC_ABI_VERSION)
    {
        result.diagnostics =
            "VMSC_CompileDesc has an incompatible ABI version.";
        return false;
    }
    if (!desc->sourceUtf8 || desc->sourceLength == 0)
    {
        result.diagnostics = "Shader source is null or empty.";
        return false;
    }
    if (!desc->entryPointUtf8 || desc->entryPointUtf8[0] == '\0')
    {
        result.diagnostics = "Shader entry point is null or empty.";
        return false;
    }
    if (!desc->targetProfileUtf8 || desc->targetProfileUtf8[0] == '\0')
    {
        result.diagnostics = "Shader target profile is null or empty.";
        return false;
    }
    if (desc->includeRootCount != 0 && !desc->includeRoots)
    {
        result.diagnostics =
            "Include roots are null while includeRootCount is non-zero.";
        return false;
    }
    constexpr std::uint32_t knownFlags =
        VMSC_COMPILE_DEBUG | VMSC_COMPILE_DISABLE_OPTIMIZATIONS;
    if ((desc->flags & ~knownFlags) != 0)
    {
        result.diagnostics = "VMSC_CompileDesc contains unknown compile flags.";
        return false;
    }
    return true;
}

bool BuildIncludeRoots(
    const VMSC_CompileDesc& desc,
    std::vector<IncludeRoot>& roots,
    CompileResult& result)
{
    roots.reserve(desc.includeRootCount);
    for (std::uint32_t index = 0; index < desc.includeRootCount; ++index)
    {
        const VMSC_IncludeRoot& input = desc.includeRoots[index];
        if (!input.physicalPathUtf8 || input.physicalPathUtf8[0] == '\0')
        {
            result.diagnostics =
                "An include root contains a null or empty physical path.";
            return false;
        }

        IncludeRoot root;
        if (input.logicalPrefixUtf8 && input.logicalPrefixUtf8[0] != '\0')
        {
            root.logicalPrefix = Utf8ToWide(input.logicalPrefixUtf8);
            if (root.logicalPrefix.empty())
            {
                result.diagnostics =
                    "An include root logical prefix is not valid UTF-8.";
                return false;
            }
            NormalizeSlashes(root.logicalPrefix);
            if (root.logicalPrefix.back() != L'/')
                root.logicalPrefix.push_back(L'/');
        }

        std::wstring physicalPath = Utf8ToWide(input.physicalPathUtf8);
        if (physicalPath.empty())
        {
            result.diagnostics =
                "An include root physical path is not valid UTF-8.";
            return false;
        }
        root.physicalPath =
            std::filesystem::path(physicalPath).lexically_normal();
        roots.push_back(std::move(root));
    }
    return true;
}

void AppendDiagnostic(std::string& destination, const std::string& addition)
{
    if (addition.empty())
        return;
    if (!destination.empty() && destination.back() != '\n')
        destination.push_back('\n');
    destination += addition;
}

CompileResult* FromHandle(std::uint64_t handle)
{
    return reinterpret_cast<CompileResult*>(
        static_cast<std::uintptr_t>(handle));
}

std::uint64_t ToHandle(CompileResult* result)
{
    return static_cast<std::uint64_t>(
        reinterpret_cast<std::uintptr_t>(result));
}
} // namespace

extern "C"
{
std::uint32_t VMSC_CALL VMSC_GetAbiVersion()
{
    return VMSC_ABI_VERSION;
}

const char* VMSC_CALL VMSC_GetCompilerVersion()
{
    thread_local std::string version;
    version = "DXC version unavailable";

    ComPtr<IDxcCompiler3> compiler;
    if (FAILED(DxcCreateInstance(
            CLSID_DxcCompiler, IID_PPV_ARGS(&compiler))))
    {
        return version.c_str();
    }

    ComPtr<IDxcVersionInfo> versionInfo;
    if (FAILED(compiler.As(&versionInfo)))
        return version.c_str();

    UINT32 major = 0;
    UINT32 minor = 0;
    if (FAILED(versionInfo->GetVersion(&major, &minor)))
        return version.c_str();

    version = "DXC " + std::to_string(major) + "." +
        std::to_string(minor);
    return version.c_str();
}

std::uint64_t VMSC_CALL VMSC_Compile(const VMSC_CompileDesc* desc)
{
    std::unique_ptr<CompileResult> result(
        new (std::nothrow) CompileResult());
    if (!result)
        return 0;

    try
    {
        if (!ValidateCompileDesc(desc, *result))
            return ToHandle(result.release());

        std::vector<IncludeRoot> roots;
        if (!BuildIncludeRoots(*desc, roots, *result))
            return ToHandle(result.release());

        const std::wstring entryPoint = Utf8ToWide(desc->entryPointUtf8);
        const std::wstring targetProfile = Utf8ToWide(desc->targetProfileUtf8);
        if (entryPoint.empty() || targetProfile.empty())
        {
            result->diagnostics =
                "Shader entry point or target profile is not valid UTF-8.";
            return ToHandle(result.release());
        }

        std::wstring sourceName;
        if (desc->sourceNameUtf8 && desc->sourceNameUtf8[0] != '\0')
        {
            sourceName = Utf8ToWide(desc->sourceNameUtf8);
            if (sourceName.empty())
            {
                result->diagnostics = "Shader source name is not valid UTF-8.";
                return ToHandle(result.release());
            }
        }

        ComPtr<IDxcUtils> utils;
        HRESULT hr = DxcCreateInstance(CLSID_DxcUtils, IID_PPV_ARGS(&utils));
        if (FAILED(hr))
        {
            result->diagnostics =
                FormatHResult("DxcCreateInstance(CLSID_DxcUtils)", hr);
            return ToHandle(result.release());
        }

        ComPtr<IDxcCompiler3> compiler;
        hr = DxcCreateInstance(CLSID_DxcCompiler, IID_PPV_ARGS(&compiler));
        if (FAILED(hr))
        {
            result->diagnostics =
                FormatHResult("DxcCreateInstance(CLSID_DxcCompiler)", hr);
            return ToHandle(result.release());
        }

        ComPtr<IDxcIncludeHandler> includeHandler;
        includeHandler.Attach(new IncludeHandler(
            utils.Get(), roots, std::move(sourceName)));
        IncludeHandler* vividIncludeHandler =
            static_cast<IncludeHandler*>(includeHandler.Get());

        std::vector<std::wstring> argumentStorage{
            L"-E", entryPoint,
            L"-T", targetProfile,
            L"-HV", L"2021",
            L"-Ges",
        };
        if ((desc->flags & VMSC_COMPILE_DISABLE_OPTIMIZATIONS) != 0)
            argumentStorage.push_back(L"-Od");
        else
            argumentStorage.push_back(L"-O3");
        if ((desc->flags & VMSC_COMPILE_DEBUG) != 0)
        {
            argumentStorage.push_back(L"-Zi");
            argumentStorage.push_back(L"-Qembed_debug");
        }
        for (const IncludeRoot& root : roots)
        {
            if (!root.logicalPrefix.empty())
                continue;
            argumentStorage.push_back(L"-I");
            argumentStorage.push_back(root.physicalPath.native());
        }

        std::vector<LPCWSTR> arguments;
        arguments.reserve(argumentStorage.size());
        for (const std::wstring& argument : argumentStorage)
            arguments.push_back(argument.c_str());

        const DxcBuffer source{
            desc->sourceUtf8,
            static_cast<SIZE_T>(desc->sourceLength),
            DXC_CP_UTF8,
        };
        ComPtr<IDxcResult> compileResult;
        hr = compiler->Compile(
            &source,
            arguments.data(),
            static_cast<UINT32>(arguments.size()),
            includeHandler.Get(),
            IID_PPV_ARGS(&compileResult));
        if (FAILED(hr))
        {
            result->diagnostics =
                FormatHResult("IDxcCompiler3::Compile", hr);
            AppendDiagnostic(
                result->diagnostics,
                vividIncludeHandler->FailureDiagnostic());
            return ToHandle(result.release());
        }

        ComPtr<IDxcBlobUtf8> messages;
        if (SUCCEEDED(compileResult->GetOutput(
                DXC_OUT_ERRORS, IID_PPV_ARGS(&messages), nullptr)) &&
            messages && messages->GetStringLength() != 0)
        {
            result->diagnostics.assign(
                messages->GetStringPointer(), messages->GetStringLength());
        }
        AppendDiagnostic(
            result->diagnostics,
            vividIncludeHandler->FailureDiagnostic());

        HRESULT compileStatus = E_FAIL;
        hr = compileResult->GetStatus(&compileStatus);
        if (FAILED(hr))
        {
            AppendDiagnostic(
                result->diagnostics,
                FormatHResult("IDxcResult::GetStatus", hr));
            return ToHandle(result.release());
        }
        if (FAILED(compileStatus))
        {
            if (result->diagnostics.empty())
            {
                result->diagnostics =
                    FormatHResult("DXC shader compilation", compileStatus);
            }
            return ToHandle(result.release());
        }

        ComPtr<IDxcBlob> object;
        hr = compileResult->GetOutput(
            DXC_OUT_OBJECT, IID_PPV_ARGS(&object), nullptr);
        if (FAILED(hr) || !object || object->GetBufferSize() == 0)
        {
            AppendDiagnostic(
                result->diagnostics,
                FAILED(hr)
                    ? FormatHResult(
                        "IDxcResult::GetOutput(DXC_OUT_OBJECT)", hr)
                    : "DXC returned no shader object.");
            return ToHandle(result.release());
        }

        const auto* begin =
            static_cast<const std::uint8_t*>(object->GetBufferPointer());
        result->dxil.assign(begin, begin + object->GetBufferSize());
        result->success = true;
    }
    catch (const std::exception& exception)
    {
        result->success = false;
        result->dxil.clear();
        result->diagnostics =
            std::string("Shader compilation failed: ") + exception.what();
    }
    catch (...)
    {
        result->success = false;
        result->dxil.clear();
        result->diagnostics =
            "Shader compilation failed with a non-standard exception.";
    }
    return ToHandle(result.release());
}

std::uint32_t VMSC_CALL VMSC_GetResultSuccess(std::uint64_t resultHandle)
{
    const CompileResult* result = FromHandle(resultHandle);
    return result && result->success ? 1u : 0u;
}

const void* VMSC_CALL VMSC_GetResultData(std::uint64_t resultHandle)
{
    const CompileResult* result = FromHandle(resultHandle);
    return result && !result->dxil.empty() ? result->dxil.data() : nullptr;
}

std::uint64_t VMSC_CALL VMSC_GetResultSize(std::uint64_t resultHandle)
{
    const CompileResult* result = FromHandle(resultHandle);
    return result ? static_cast<std::uint64_t>(result->dxil.size()) : 0;
}

const char* VMSC_CALL VMSC_GetResultDiagnostics(
    std::uint64_t resultHandle)
{
    const CompileResult* result = FromHandle(resultHandle);
    return result ? result->diagnostics.c_str() : nullptr;
}

std::uint64_t VMSC_CALL VMSC_GetResultDiagnosticsSize(
    std::uint64_t resultHandle)
{
    const CompileResult* result = FromHandle(resultHandle);
    return result
        ? static_cast<std::uint64_t>(result->diagnostics.size())
        : 0;
}

void VMSC_CALL VMSC_DestroyResult(std::uint64_t resultHandle)
{
    delete FromHandle(resultHandle);
}
} // extern "C"
