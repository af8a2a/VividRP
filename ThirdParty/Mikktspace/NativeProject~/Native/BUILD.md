Just a quick guide in case you want to build those from source.

This assumes you have appropriate tools installed (cmake, build toolchains, Visual Studio, NDK for Android)
and that you start in the root directory for Native (where CMakeLists.txt is locaed)

BUILD FOR WINDOWS:
1) Run cmake -G "Visual Studio 15 2017 Win64" -B build_win
2) Open the generated solution in Visual Studio and build (or use appropriate commandline tools)

BUILD FOR LINUX:
1) mkdir build_linux
2) cd build_linux
3) cmake ../
4) make

BUILD FOR ANDROID (needs NDK)
1) ndk-build

Mikktspace.NET wrapper Written by Frooxius for NeosVR (www.neosvr.com)