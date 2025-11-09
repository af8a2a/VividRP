#pragma once

#include <cstdint>
#include <d3d12.h>

#include "IUnityInterface.h"


extern "C" {
UNITY_INTERFACE_EXPORT uint32_t UNITY_INTERFACE_API GetSRVDescriptorHeapCount();

UNITY_INTERFACE_EXPORT bool UNITY_INTERFACE_API CreateSRVDescriptor(ID3D12Resource *pTexture, uint32_t index);

UNITY_INTERFACE_EXPORT bool UNITY_INTERFACE_API CreateUAVDescriptor(ID3D12Resource *pTexture, uint32_t index);

UNITY_INTERFACE_EXPORT bool UNITY_INTERFACE_API CheckBindlessSupport();

}
