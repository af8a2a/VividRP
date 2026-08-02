/*
* Copyright (c) 2024-2025, NVIDIA CORPORATION. All rights reserved.
*
* Permission is hereby granted, free of charge, to any person obtaining a
* copy of this software and associated documentation files (the "Software"),
* to deal in the Software without restriction, including without limitation
* the rights to use, copy, modify, merge, publish, distribute, sublicense,
* and/or sell copies of the Software, and to permit persons to whom the
* Software is furnished to do so, subject to the following conditions:
*
* The above copyright notice and this permission notice shall be included in
* all copies or substantial portions of the Software.
*
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL
* THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
* FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
* DEALINGS IN THE SOFTWARE.
*/

#ifndef _RTXCR_MATH_HLSLI_
#define _RTXCR_MATH_HLSLI_

// PIs
#ifndef RTXCR_PI
#define RTXCR_PI (3.141592653589f)
#endif

#ifndef RTXCR_TWO_PI
#define RTXCR_TWO_PI (2.0f * RTXCR_PI)
#endif

#ifndef RTXCR_FOUR_PI
#define RTXCR_FOUR_PI (4.0f * RTXCR_PI)
#endif

#ifndef RTXCR_ONE_OVER_PI
#define RTXCR_ONE_OVER_PI (1.0f / RTXCR_PI)
#endif

#ifndef RTXCR_ONE_OVER_TWO_PI
#define RTXCR_ONE_OVER_TWO_PI (1.0f / RTXCR_TWO_PI)
#endif

#ifndef RTXCR_PI_OVER_EIGHT
#define RTXCR_PI_OVER_EIGHT (0.626657069f) // sqrt(pi / 8.0f);
#endif

static const float3 RTXCR_WhiteColor = float3(1.0f, 1.0f, 1.0f);

// Safe sqrt for x between [0, 1]
float RTXCR_Sqrt01(float x)
{
    return max(sqrt(saturate(x)), 1e-7);
}

// Safe sqrt for x
float RTXCR_Sqrt0(float x)
{
    return sqrt(max(x, 1e-7));
}

float3 RTXCR_Sqrt0(float3 x)
{
    return sqrt(max(x, 1e-7));
}

float RTXCR_Atan2safe(float x, float y)
{
    return abs(x) + abs(y) < 1e-7 ? 0 : atan2(x, y);
}

float RTXCR_I0(float x)
{
    float val = 0.f;
    float x2i = 1.f;
    float ifact = 1.f;
    uint i4 = 1;

    [unroll]
    for (uint i = 0; i < 10; i++)
    {
        if (i > 1)
            ifact *= i;
        val += x2i / (ifact * ifact * i4);
        x2i *= x * x;
        i4 *= 4;
    }
    return val;
}

float RTXCR_LogI0(float x)
{
    if (x > 12)
    {
        return x + 0.5f * (-log(RTXCR_TWO_PI) + log(1.f / x) + 0.125f / x);
    }
    else
    {
        return log(RTXCR_I0(x));
    }
}

float RTXCR_PhiFunction(int p, float gammaI, float gammaT)
{
    return 2.f * p * gammaT - 2.f * gammaI + p * RTXCR_PI;
}

float RTXCR_Logistic(float x, float s)
{
    x = abs(x);
    float tmp = exp(-x / s);
    return tmp / (s * (1.f + tmp) * (1.f + tmp));
}

float RTXCR_LogisticCDF(float x, float s)
{
    return 1.f / (1.f + exp(-x / s));
}

float RTXCR_TrimmedLogistic(float x, float s, float a, float b)
{
    return RTXCR_Logistic(x, s) / (RTXCR_LogisticCDF(b, s) - RTXCR_LogisticCDF(a, s));
}

float RTXCR_SampleTrimmedLogistic(float u, float s, float a, float b)
{
    float k = RTXCR_LogisticCDF(b, s) - RTXCR_LogisticCDF(a, s);
    float x = -s * log(1.f / (u * k + RTXCR_LogisticCDF(a, s)) - 1.f);
    return clamp(x, a, b);
}

// 1D Gaussian distribution normalized over [-inf,inf]
float RTXCR_Gaussian1D(const float x, const float stddev)
{
    return exp(-x * x / (2.0f * stddev * stddev)) / (stddev * sqrt(2.0f * RTXCR_PI));
}

float RTXCR_PhiR(const float h)
{
    return -2.0 * asin(h);
}

float RTXCR_PhiTT(const float h, const float a) // a = 1.0 / eta_prime
{
    return RTXCR_PI - 2.0 * asin(h) + 2.0 * asin(h * a);
}

float RTXCR_PhiTRT(const float h, const float a) // a = 1.0 / eta_prime
{
    return -2.0 * asin(h) + 4.0 * asin(h * a);
}

// sample from normal distribution (Box-Muller transform)
float RTXCR_RandomGaussian1D(const float xi1, const float xi2)
{
    return sqrt(2.0f) * cos(2.0f * RTXCR_PI * xi1) * RTXCR_Sqrt0(-log(1 - xi2));
}

float2 RTXCR_PolarToCartesian(float r, float theta)
{
    return r * float2(cos(theta), sin(theta));
}

void RTXCR_CreateCoordinateSystemFromZ(bool rightHand, float3 zAxis, out float3 xAxis, out float3 yAxis)
{
    float yz = -zAxis.y * zAxis.z;
    yAxis = normalize(abs(zAxis.z) > 0.9999 ? float3(-zAxis.x * zAxis.y, 1.f - zAxis.y * zAxis.y, yz) :
                                              float3(-zAxis.x * zAxis.z, yz, 1.f - zAxis.z * zAxis.z));
    xAxis = rightHand ? cross(yAxis, zAxis) : cross(zAxis, yAxis);
}

// Spherical to Cartesian in the basis x, y, z
// z is up
float3 RTXCR_SphericalDirection(float sinTheta, float cosTheta, float phi, float3 x, float3 y, float3 z)
{
    return sinTheta * cos(phi) * x + sinTheta * sin(phi) * y + cosTheta * z;
}

float3 RTXCR_CalculateDiskSamplePosition(
    in const float rand,
    in const float r,
    in float3 centerPos,
    in float3 tangent,
    in float3 biTangent)
{
    // Sample Disk
    const float theta = rand * RTXCR_TWO_PI;
    const float2 diskSample = RTXCR_PolarToCartesian(r, theta);

    return centerPos + tangent * diskSample.xxx + biTangent * diskSample.yyy;
}

// Samples a direction within a hemisphere oriented along +Z axis with a cosine-weighted distribution
float3 RTXCR_SampleHemisphere(float2 u, out float pdf)
{
	const float a = sqrt(u.x);
	const float b = RTXCR_TWO_PI * u.y;

	const float3 result = float3(
		a * cos(b),
		a * sin(b),
		sqrt(1.0f - u.x));

	pdf = result.z * RTXCR_ONE_OVER_PI;

	return result;
}

#endif

