cmake_minimum_required(VERSION 3.20)

project(VividRP_METIS LANGUAGES C)

if(NOT WIN32 OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "VividRP's METIS plugin must be built for Windows x86_64.")
endif()

set(GKLIB_BUILD_APPS OFF CACHE BOOL "" FORCE)
set(SHARED OFF CACHE BOOL "" FORCE)
set(ASSERT OFF CACHE BOOL "" FORCE)
set(ASSERT2 OFF CACHE BOOL "" FORCE)
set(DEBUG OFF CACHE BOOL "" FORCE)
set(GKREGEX ON CACHE BOOL "" FORCE)
add_subdirectory(GKlib GKlib-build EXCLUDE_FROM_ALL)

set(generated_include_dir "${CMAKE_CURRENT_BINARY_DIR}/generated/include")
file(MAKE_DIRECTORY "${generated_include_dir}")
file(READ "${CMAKE_CURRENT_SOURCE_DIR}/Source/include/metis.h" metis_header)
file(WRITE "${generated_include_dir}/metis.h"
    "#define IDXTYPEWIDTH 32\n#define REALTYPEWIDTH 32\n${metis_header}")

file(GLOB metis_sources CONFIGURE_DEPENDS
    "${CMAKE_CURRENT_SOURCE_DIR}/Source/libmetis/*.c")

add_library(metis SHARED ${metis_sources})
target_include_directories(metis PRIVATE
    "${generated_include_dir}"
    "${CMAKE_CURRENT_SOURCE_DIR}/Source/libmetis"
    "${CMAKE_CURRENT_SOURCE_DIR}/GKlib/include")
target_compile_definitions(metis PRIVATE
    _WINDLL
    WIN32
    MSC
    _CRT_SECURE_NO_DEPRECATE
    USE_GKREGEX
    NDEBUG
    NDEBUG2)
target_link_libraries(metis PRIVATE GKlib::GKlib)
set_target_properties(metis PROPERTIES
    C_STANDARD 99
    C_STANDARD_REQUIRED ON
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/bin"
    ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/lib"
    PDB_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/pdb")
