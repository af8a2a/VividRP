cmake_minimum_required(VERSION 3.20)

project(UnityPlugin VERSION 0.1.0 LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

if (MSVC)
    add_compile_options(/W4 /permissive- /Zc:__cplusplus)
    add_definitions(-D_CRT_SECURE_NO_WARNINGS)
endif ()

# Paths
set(PLUGIN_API_DIR ${CMAKE_SOURCE_DIR}/PluginAPI)
set(NVAPI_DIR ${CMAKE_SOURCE_DIR}/External/NVAPI)

add_library(UnityPlugin SHARED
        src/Plugin.h
        src/Plugin.cpp
        src/Plugin.def
)

target_include_directories(UnityPlugin
        PRIVATE
        ${PLUGIN_API_DIR}
        ${NVAPI_DIR}
)

target_link_directories(UnityPlugin PRIVATE ${NVAPI_DIR}/amd64)
target_link_libraries(UnityPlugin PRIVATE nvapi64)

if (WIN32)
    set_target_properties(UnityPlugin PROPERTIES
            OUTPUT_NAME "Unity_NVAPI"
            RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin
            LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin
            ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib
    )
endif ()
