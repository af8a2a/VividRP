#pragma once

#include <cstddef>
#include <cstdint>

#if defined(_WIN32)
#define VMSC_EXPORT __declspec(dllexport)
#define VMSC_CALL __cdecl
#else
#define VMSC_EXPORT
#define VMSC_CALL
#endif

enum VMSC_CompileFlags : std::uint32_t
{
    VMSC_COMPILE_NONE = 0,
    VMSC_COMPILE_DEBUG = 1u << 0,
    VMSC_COMPILE_DISABLE_OPTIMIZATIONS = 1u << 1,
};

struct VMSC_IncludeRoot
{
    // A Unity-style virtual prefix, for example
    // "Packages/com.vivid.render-pipelines/". An empty prefix makes the
    // physical path a normal include search root.
    const char* logicalPrefixUtf8;
    const char* physicalPathUtf8;
};

struct VMSC_CompileDesc
{
    std::uint32_t structSize;
    std::uint32_t abiVersion;
    const char* sourceUtf8;
    std::uint64_t sourceLength;
    const char* sourceNameUtf8;
    const char* entryPointUtf8;
    const char* targetProfileUtf8;
    const VMSC_IncludeRoot* includeRoots;
    std::uint32_t includeRootCount;
    std::uint32_t flags;
};

static_assert(sizeof(void*) == 8, "The VMSC ABI is 64-bit only.");
static_assert(sizeof(VMSC_IncludeRoot) == 16);
static_assert(offsetof(VMSC_IncludeRoot, physicalPathUtf8) == 8);
static_assert(sizeof(VMSC_CompileDesc) == 64);
static_assert(offsetof(VMSC_CompileDesc, sourceUtf8) == 8);
static_assert(offsetof(VMSC_CompileDesc, sourceLength) == 16);
static_assert(offsetof(VMSC_CompileDesc, sourceNameUtf8) == 24);
static_assert(offsetof(VMSC_CompileDesc, entryPointUtf8) == 32);
static_assert(offsetof(VMSC_CompileDesc, targetProfileUtf8) == 40);
static_assert(offsetof(VMSC_CompileDesc, includeRoots) == 48);
static_assert(offsetof(VMSC_CompileDesc, includeRootCount) == 56);
static_assert(offsetof(VMSC_CompileDesc, flags) == 60);

inline constexpr std::uint32_t VMSC_ABI_VERSION = 1;

extern "C"
{
VMSC_EXPORT std::uint32_t VMSC_CALL VMSC_GetAbiVersion();
VMSC_EXPORT const char* VMSC_CALL VMSC_GetCompilerVersion();

// The result handle owns immutable DXIL and diagnostics. Even compilation
// failures normally return a non-zero handle so callers can read diagnostics.
VMSC_EXPORT std::uint64_t VMSC_CALL VMSC_Compile(
    const VMSC_CompileDesc* desc);
VMSC_EXPORT std::uint32_t VMSC_CALL VMSC_GetResultSuccess(
    std::uint64_t resultHandle);
VMSC_EXPORT const void* VMSC_CALL VMSC_GetResultData(
    std::uint64_t resultHandle);
VMSC_EXPORT std::uint64_t VMSC_CALL VMSC_GetResultSize(
    std::uint64_t resultHandle);
VMSC_EXPORT const char* VMSC_CALL VMSC_GetResultDiagnostics(
    std::uint64_t resultHandle);
VMSC_EXPORT std::uint64_t VMSC_CALL VMSC_GetResultDiagnosticsSize(
    std::uint64_t resultHandle);
VMSC_EXPORT void VMSC_CALL VMSC_DestroyResult(std::uint64_t resultHandle);
}
