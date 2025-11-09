cmake_minimum_required(VERSION 3.20)
add_subdirectory(External/minhook)

project(UnityPlugin VERSION 0.1.0 LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

if (MSVC)
    add_compile_options(/W4 /permissive- /Zc:__cplusplus)
    add_definitions(-D_CRT_SECURE_NO_WARNINGS)
endif ()

# Paths
set(PLUGIN_API_DIR ${CMAKE_SOURCE_DIR}/PluginAPI)


add_library(UnityBindless SHARED
        src/Plugin.h
        src/Plugin.cpp
        src/HookWrapper.hpp
)

target_include_directories(UnityBindless
        PRIVATE
        ${PLUGIN_API_DIR}
)




if (WIN32)
    set_target_properties(UnityBindless PROPERTIES
            OUTPUT_NAME "UnityBindless"
            RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin
            LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin
            ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib
    )
endif ()


foreach(_lib ${NRD_LIB_DEBUG} ${NRD_LIB_RELEASE} ${NRI_LIB_DEBUG} ${NRI_LIB_RELEASE})
    if (NOT EXISTS ${_lib})
        message(STATUS "Note: missing optional library: ${_lib}")
    endif ()
endforeach()



# Link prebuilt NRD & NRI
target_link_libraries(UnityBindless PRIVATE
        $<IF:$<CONFIG:Debug>,${NRD_LIB_DEBUG},${NRD_LIB_RELEASE}>
        $<IF:$<CONFIG:Debug>,${NRI_LIB_DEBUG},${NRI_LIB_RELEASE}>
        minhook
)

