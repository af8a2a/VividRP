cmake_minimum_required(VERSION 3.20)

project(VividMeshShader VERSION 0.1.0 LANGUAGES CXX)

if (NOT WIN32)
    message(FATAL_ERROR "VividMeshShader currently supports Windows D3D12 only.")
endif ()
if (NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "VividMeshShader requires a 64-bit build.")
endif ()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

set(VMS_DXC_ROOT "" CACHE PATH
        "Root of a Microsoft.Direct3D.DXC package (contains build/native/include, lib/x64, and bin/x64)")

set(_VMS_DXC_HINTS
        "${VMS_DXC_ROOT}"
        "${CMAKE_SOURCE_DIR}/External/DXC"
        "E:/DirectX-Graphics-Samples/Samples/Desktop/D3D12MeshShaders/src/packages/Microsoft.Direct3D.DXC.1.8.2505.32")

find_path(VMS_DXC_INCLUDE_DIR
        NAMES dxcapi.h
        HINTS ${_VMS_DXC_HINTS}
        PATH_SUFFIXES include build/native/include)
find_library(VMS_DXCOMPILER_LIBRARY
        NAMES dxcompiler
        HINTS ${_VMS_DXC_HINTS}
        PATH_SUFFIXES lib/x64 build/native/lib/x64)
find_file(VMS_DXCOMPILER_DLL
        NAMES dxcompiler.dll
        HINTS ${_VMS_DXC_HINTS}
        PATH_SUFFIXES bin/x64 build/native/bin/x64)
find_file(VMS_DXIL_DLL
        NAMES dxil.dll
        HINTS ${_VMS_DXC_HINTS}
        PATH_SUFFIXES bin/x64 build/native/bin/x64)
find_file(VMS_DXC_LICENSE_MIT
        NAMES LICENCE-MIT.txt LICENSE-MIT.txt
        HINTS ${_VMS_DXC_HINTS})
find_file(VMS_DXC_LICENSE_LLVM
        NAMES LICENSE-LLVM.txt
        HINTS ${_VMS_DXC_HINTS})
find_file(VMS_DXC_LICENSE_MS
        NAMES LICENSE-MS.txt
        HINTS ${_VMS_DXC_HINTS})

if (NOT VMS_DXC_INCLUDE_DIR OR
    NOT VMS_DXCOMPILER_LIBRARY OR
    NOT VMS_DXCOMPILER_DLL OR
    NOT VMS_DXIL_DLL)
    message(FATAL_ERROR
            "DXC was not found. Set VMS_DXC_ROOT to a Microsoft.Direct3D.DXC native package.")
endif ()

add_library(VividMeshShader SHARED
        src/Plugin.h
        src/Plugin.cpp)

add_library(VividMeshShaderCompiler SHARED
        src/Compiler.h
        src/Compiler.cpp)

target_include_directories(VividMeshShader
        PRIVATE
        "${CMAKE_SOURCE_DIR}/PluginAPI")

target_include_directories(VividMeshShaderCompiler
        PRIVATE
        "${VMS_DXC_INCLUDE_DIR}")

target_compile_definitions(VividMeshShader
        PRIVATE
        WIN32_LEAN_AND_MEAN
        NOMINMAX
        _CRT_SECURE_NO_WARNINGS)

target_compile_definitions(VividMeshShaderCompiler
        PRIVATE
        WIN32_LEAN_AND_MEAN
        NOMINMAX
        _CRT_SECURE_NO_WARNINGS)

if (MSVC)
    target_compile_options(VividMeshShader PRIVATE /W4 /permissive- /Zc:__cplusplus)
    target_compile_options(VividMeshShaderCompiler PRIVATE /W4 /permissive- /Zc:__cplusplus)
    set_property(TARGET VividMeshShader PROPERTY
            MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    set_property(TARGET VividMeshShaderCompiler PROPERTY
            MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif ()

target_link_libraries(VividMeshShader
        PRIVATE
        d3d12
        dxgi)

target_link_libraries(VividMeshShaderCompiler
        PRIVATE
        "${VMS_DXCOMPILER_LIBRARY}")

set_target_properties(VividMeshShader PROPERTIES
        OUTPUT_NAME "VividMeshShader"
        RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
        LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
        ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib")

set_target_properties(VividMeshShaderCompiler PROPERTIES
        OUTPUT_NAME "VividMeshShaderCompiler"
        RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
        LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
        ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib")

foreach (_VMS_CONFIG Debug Release RelWithDebInfo MinSizeRel)
    string(TOUPPER "${_VMS_CONFIG}" _VMS_CONFIG_UPPER)
    set_target_properties(VividMeshShader PROPERTIES
            "RUNTIME_OUTPUT_DIRECTORY_${_VMS_CONFIG_UPPER}" "${CMAKE_BINARY_DIR}/bin"
            "LIBRARY_OUTPUT_DIRECTORY_${_VMS_CONFIG_UPPER}" "${CMAKE_BINARY_DIR}/bin"
            "ARCHIVE_OUTPUT_DIRECTORY_${_VMS_CONFIG_UPPER}" "${CMAKE_BINARY_DIR}/lib")
    set_target_properties(VividMeshShaderCompiler PROPERTIES
            "RUNTIME_OUTPUT_DIRECTORY_${_VMS_CONFIG_UPPER}" "${CMAKE_BINARY_DIR}/bin"
            "LIBRARY_OUTPUT_DIRECTORY_${_VMS_CONFIG_UPPER}" "${CMAKE_BINARY_DIR}/bin"
            "ARCHIVE_OUTPUT_DIRECTORY_${_VMS_CONFIG_UPPER}" "${CMAKE_BINARY_DIR}/lib")
endforeach ()

add_custom_command(TARGET VividMeshShaderCompiler POST_BUILD
        COMMAND "${CMAKE_COMMAND}" -E copy_if_different
                "${VMS_DXCOMPILER_DLL}" "$<TARGET_FILE_DIR:VividMeshShaderCompiler>"
        COMMAND "${CMAKE_COMMAND}" -E copy_if_different
                "${VMS_DXIL_DLL}" "$<TARGET_FILE_DIR:VividMeshShaderCompiler>"
        COMMENT "Copying DXC runtime next to VividMeshShaderCompiler.dll")

install(TARGETS VividMeshShader VividMeshShaderCompiler
        RUNTIME DESTINATION .)
install(FILES
        "${CMAKE_SOURCE_DIR}/LICENSE"
        "${VMS_DXCOMPILER_DLL}"
        "${VMS_DXIL_DLL}"
        DESTINATION .)

if (VMS_DXC_LICENSE_MIT AND VMS_DXC_LICENSE_LLVM AND VMS_DXC_LICENSE_MS)
    install(FILES
            "${VMS_DXC_LICENSE_MIT}"
            "${VMS_DXC_LICENSE_LLVM}"
            "${VMS_DXC_LICENSE_MS}"
            DESTINATION .)
else ()
    message(WARNING "DXC license files were not found; include them when redistributing the runtime DLLs.")
endif ()
