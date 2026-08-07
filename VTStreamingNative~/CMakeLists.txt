cmake_minimum_required(VERSION 3.24)
project(VividVTStreamingNative VERSION 1.0.0 LANGUAGES C CXX)

if(NOT WIN32 OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "Vivid VT streaming v1 supports Windows x64 only")
endif()

include(FetchContent)

set(ZSTD_BUILD_PROGRAMS OFF CACHE BOOL "" FORCE)
set(ZSTD_BUILD_CONTRIB OFF CACHE BOOL "" FORCE)
set(ZSTD_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(ZSTD_BUILD_SHARED OFF CACHE BOOL "" FORCE)
set(ZSTD_BUILD_STATIC ON CACHE BOOL "" FORCE)
set(ZSTD_MULTITHREAD_SUPPORT OFF CACHE BOOL "" FORCE)
FetchContent_Declare(
    zstd
    GIT_REPOSITORY https://github.com/facebook/zstd.git
    GIT_TAG v1.5.7
    GIT_SHALLOW TRUE
    SOURCE_SUBDIR build/cmake)
FetchContent_MakeAvailable(zstd)

FetchContent_Declare(
    directstorage
    URL https://www.nuget.org/api/v2/package/Microsoft.Direct3D.DirectStorage/1.3.0
    DOWNLOAD_EXTRACT_TIMESTAMP TRUE)
FetchContent_GetProperties(directstorage)
if(NOT directstorage_POPULATED)
    FetchContent_Populate(directstorage)
endif()

file(GLOB_RECURSE DSTORAGE_HEADERS "${directstorage_SOURCE_DIR}/native/include/dstorage.h")
file(GLOB_RECURSE DSTORAGE_LIBRARIES "${directstorage_SOURCE_DIR}/native/lib/x64/dstorage.lib")
file(GLOB_RECURSE DSTORAGE_RUNTIME_DLLS
    "${directstorage_SOURCE_DIR}/native/bin/x64/dstorage.dll"
    "${directstorage_SOURCE_DIR}/native/bin/x64/dstoragecore.dll")
list(LENGTH DSTORAGE_HEADERS DSTORAGE_HEADER_COUNT)
list(LENGTH DSTORAGE_LIBRARIES DSTORAGE_LIBRARY_COUNT)
if(DSTORAGE_HEADER_COUNT EQUAL 0 OR DSTORAGE_LIBRARY_COUNT EQUAL 0)
    message(FATAL_ERROR "Microsoft.Direct3D.DirectStorage 1.3.0 native x64 layout was not found")
endif()
list(GET DSTORAGE_HEADERS 0 DSTORAGE_HEADER)
list(GET DSTORAGE_LIBRARIES 0 DSTORAGE_LIBRARY)
get_filename_component(DSTORAGE_INCLUDE_DIR "${DSTORAGE_HEADER}" DIRECTORY)

add_library(VividVTStreamingNative SHARED
    Source/VividVTStreamingNative.cpp)
target_compile_features(VividVTStreamingNative PRIVATE cxx_std_20)
target_compile_definitions(VividVTStreamingNative PRIVATE
    WIN32_LEAN_AND_MEAN
    NOMINMAX
    VIVID_VT_STREAMING_EXPORTS)
target_include_directories(VividVTStreamingNative PRIVATE
    "${DSTORAGE_INCLUDE_DIR}"
    "${zstd_SOURCE_DIR}/lib")
target_link_libraries(VividVTStreamingNative PRIVATE
    libzstd_static
    "${DSTORAGE_LIBRARY}"
    d3d12
    dxgi)

foreach(DSTORAGE_RUNTIME_DLL IN LISTS DSTORAGE_RUNTIME_DLLS)
    add_custom_command(TARGET VividVTStreamingNative POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            "${DSTORAGE_RUNTIME_DLL}"
            "$<TARGET_FILE_DIR:VividVTStreamingNative>")
endforeach()

if(MSVC)
    target_compile_options(VividVTStreamingNative PRIVATE /W4 /permissive- /EHsc)
endif()

install(TARGETS VividVTStreamingNative
    RUNTIME DESTINATION . COMPONENT Runtime
    LIBRARY DESTINATION . COMPONENT Runtime)
install(FILES ${DSTORAGE_RUNTIME_DLLS} DESTINATION . COMPONENT Runtime)

include(CTest)
if(BUILD_TESTING)
    add_executable(VividVTStreamingNativeTests
        Tests/VividVTStreamingNativeTests.cpp)
    target_compile_features(VividVTStreamingNativeTests PRIVATE cxx_std_20)
    target_link_libraries(VividVTStreamingNativeTests PRIVATE VividVTStreamingNative)
    if(MSVC)
        target_compile_options(VividVTStreamingNativeTests PRIVATE /W4 /permissive- /EHsc)
    endif()
    add_test(NAME VividVTStreamingNative.ZstdRoundTrip COMMAND VividVTStreamingNativeTests)
endif()
