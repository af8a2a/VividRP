cmake_minimum_required(VERSION 3.20)

project(NRDUnityPlugin VERSION 0.1.0 LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

if (MSVC)
    add_compile_options(/W4 /permissive- /Zc:__cplusplus)
    add_definitions(-D_CRT_SECURE_NO_WARNINGS)
endif ()

# Paths
set(PLUGIN_API_DIR ${CMAKE_SOURCE_DIR}/Source/PluginAPI)

set(NRD_DIR ${CMAKE_SOURCE_DIR}/Source/NRD_SDK)
set(MATHLIB_DIR ${CMAKE_SOURCE_DIR}/Source/MathLib)


add_library(NRDUnityPlugin SHARED
        Source/NRDWarpper.h
        Source/NRDWarpper.cpp
        Source/Timer.cpp
        Source/Timer.h
)

target_include_directories(NRDUnityPlugin
        PRIVATE
        ${PLUGIN_API_DIR}
        ${NRD_DIR}/Include
        ${NRD_DIR}/Integration
        ${MATHLIB_DIR}

)

target_compile_definitions(NRDUnityPlugin
        PRIVATE
        UNITY_PLUGIN
        WIN32_LEAN_AND_MEAN
        NOMINMAX
)

if (WIN32)
    set_target_properties(NRDUnityPlugin PROPERTIES
            OUTPUT_NAME "NRDUnityPlugin"
            RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin
            LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin
            ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib
    )
endif ()

# Validate library locations
set(NRD_LIB_DEBUG   ${NRD_DIR}/Lib/Debug/NRD.lib)
set(NRD_LIB_RELEASE ${NRD_DIR}/Lib/Release/NRD.lib)

foreach(_lib ${NRD_LIB_DEBUG} ${NRD_LIB_RELEASE} ${NRI_LIB_DEBUG} ${NRI_LIB_RELEASE})
    if (NOT EXISTS ${_lib})
        message(STATUS "Note: missing optional library: ${_lib}")
    endif ()
endforeach()

# Link prebuilt NRD & NRI
target_link_libraries(NRDUnityPlugin PRIVATE
        $<IF:$<CONFIG:Debug>,${NRD_LIB_DEBUG},${NRD_LIB_RELEASE}>
)

# Copy runtime DLLs next to the plugin
set(NRD_DLL_DEBUG   ${NRD_DIR}/Lib/Debug/NRD.dll)
set(NRD_DLL_RELEASE ${NRD_DIR}/Lib/Release/NRD.dll)