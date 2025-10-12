cmake_minimum_required(VERSION 2.8.12)

project(mikktspace LANGUAGES CXX)

include(CheckCXXCompilerFlag)
CHECK_CXX_COMPILER_FLAG("-std=c++11" COMPILER_SUPPORTS_CXX11)
if (COMPILER_SUPPORTS_CXX11)
	set(CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS} -std=c++11")
endif()

set(CMAKE_BUILD_TYPE Release)

file(GLOB_RECURSE mikktspace_HEADERS
	"src/*.h"
)

file(GLOB_RECURSE mikktspace_SOURCES
	"src/*.c"
    "src/*.cpp"
)

SET_SOURCE_FILES_PROPERTIES(${mikktspace_SOURCES} PROPERTIES LANGUAGE CXX )

add_library(mikktspace SHARED ${mikktspace_SOURCES} ${mikktspace_HEADERS})