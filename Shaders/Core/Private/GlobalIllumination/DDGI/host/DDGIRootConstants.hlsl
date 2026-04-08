/*
* Copyright (c) 2019-2023, NVIDIA CORPORATION.  All rights reserved.
*
* NVIDIA CORPORATION and its licensors retain all intellectual property
* and proprietary rights in and to this software, related documentation
* and any modifications thereto.  Any use, reproduction, disclosure or
* distribution of this software and related documentation without an express
* license agreement from NVIDIA CORPORATION is strictly prohibited.
*/

#ifndef RTXGI_DDGI_ROOT_CONSTANTS_H
#define RTXGI_DDGI_ROOT_CONSTANTS_H


struct DDGIRootConstants
{
    uint volumeIndex;
    uint volumeConstantsIndex;
    uint volumeResourceIndicesIndex;
    // Split uint3 into three uints to prevent internal padding
    // while keeping these values at the end of the struct
    uint  reductionInputSizeX;
    uint  reductionInputSizeY;
    uint  reductionInputSizeZ;

};

#endif // RTXGI_DDGI_ROOT_CONSTANTS_H
