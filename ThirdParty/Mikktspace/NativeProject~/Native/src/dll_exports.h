#pragma once

#if defined(ANDROID) || __linux__
	#define DLL_EXPORT extern "C"
	#define DLL_API
#else
	#define DLL_EXPORT extern "C" __declspec(dllexport)
	#define DLL_API __cdecl
#endif