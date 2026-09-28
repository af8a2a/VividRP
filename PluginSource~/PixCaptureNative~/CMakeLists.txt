cmake_minimum_required(VERSION 3.24)
project(VividPix LANGUAGES CXX)

if(NOT WIN32 OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "Vivid PIX requires Windows x64.")
endif()
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(PIX_INSTALL_DIR "" CACHE PATH "PIX Preview installation containing include/PixApi.h and pixapi.dll")
set(PIX_EVENT_RUNTIME_DIR "" CACHE PATH "Extracted WinPixEventRuntime 1.0.240308001 NuGet package")
set(PIX_D3D12_DIR "" CACHE PATH "Extracted Microsoft.Direct3D.D3D12 1.721.1-preview NuGet package")
if(NOT EXISTS "${PIX_INSTALL_DIR}/include/PixApi.h" OR NOT EXISTS "${PIX_EVENT_RUNTIME_DIR}/Include/WinPixEventRuntime/pix3.h")
    message(FATAL_ERROR "Set PIX_INSTALL_DIR and PIX_EVENT_RUNTIME_DIR; see README.md.")
endif()
if(NOT EXISTS "${PIX_D3D12_DIR}/build/native/include/d3d12.h")
    message(FATAL_ERROR "Set PIX_D3D12_DIR to Microsoft.Direct3D.D3D12 1.721.1-preview.")
endif()

add_library(VividPixCapture SHARED src/Capture.cpp)
target_include_directories(VividPixCapture PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}/../NVAPINative~/PluginAPI"
    "${PIX_EVENT_RUNTIME_DIR}/Include/WinPixEventRuntime")
target_compile_definitions(VividPixCapture PRIVATE USE_PIX WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(VividPixCapture PRIVATE d3d12 dxgi
    "${PIX_EVENT_RUNTIME_DIR}/bin/x64/WinPixEventRuntime.lib")

add_executable(vivid-pix-analyzer src/Analyzer.cpp)
target_include_directories(vivid-pix-analyzer PRIVATE "${PIX_D3D12_DIR}/build/native/include" "${PIX_INSTALL_DIR}/include")
target_compile_definitions(vivid-pix-analyzer PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(vivid-pix-analyzer PRIVATE ole32 bcrypt)

add_executable(PixValidationChecks tests/ValidationChecks.cpp)
enable_testing()
add_test(NAME PixValidationChecks COMMAND PixValidationChecks)
option(PIX_BUILD_GPU_SMOKE "Build opt-in standalone D3D12 capture integration check" OFF)
if(PIX_BUILD_GPU_SMOKE)
    add_executable(PixCaptureSmoke tests/CaptureSmoke.cpp src/Capture.cpp)
    target_include_directories(PixCaptureSmoke PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}/../NVAPINative~/PluginAPI"
        "${PIX_EVENT_RUNTIME_DIR}/Include/WinPixEventRuntime")
    target_compile_definitions(PixCaptureSmoke PRIVATE USE_PIX WIN32_LEAN_AND_MEAN NOMINMAX)
    if(MSVC)
        target_compile_options(PixCaptureSmoke PRIVATE /W4 /permissive- /utf-8)
    endif()
    target_link_libraries(PixCaptureSmoke PRIVATE d3d12 dxgi d3dcompiler
        "${PIX_EVENT_RUNTIME_DIR}/bin/x64/WinPixEventRuntime.lib")
    add_custom_command(TARGET PixCaptureSmoke POST_BUILD COMMAND ${CMAKE_COMMAND} -E copy_if_different
        "${PIX_EVENT_RUNTIME_DIR}/bin/x64/WinPixEventRuntime.dll" "$<TARGET_FILE_DIR:PixCaptureSmoke>")
endif()
foreach(target VividPixCapture vivid-pix-analyzer PixValidationChecks)
    if(MSVC)
        target_compile_options(${target} PRIVATE /W4 /permissive- /utf-8)
    endif()
endforeach()
