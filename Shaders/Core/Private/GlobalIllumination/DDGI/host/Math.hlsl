/*
* Copyright (c) 2019-2023, NVIDIA CORPORATION.  All rights reserved.
*
* NVIDIA CORPORATION and its licensors retain all intellectual property
* and proprietary rights in and to this software, related documentation
* and any modifications thereto.  Any use, reproduction, disclosure or
* distribution of this software and related documentation without an express
* license agreement from NVIDIA CORPORATION is strictly prohibited.
*/

#pragma once

#include "Types.h"

namespace rtxgi
{

    static const float RTXGI_PI = 3.1415926535897932f;
    static const float RTXGI_2PI = 6.2831853071795864f;

    enum class ECoordinateSystem
    {
        LH_YUP = 0,
        LH_ZUP,
        RH_YUP,
        RH_ZUP,
    };


}
