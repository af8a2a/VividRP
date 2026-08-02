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

// Extension of "Crash Course in BRDF Implementation" specific for RTXCR

#ifndef _RTXCR_BSDFEXT_HLSLI_
#define _RTXCR_BSDFEXT_HLSLI_

#include "RtxcrMath.hlsli"

// Evaluate Lamberian Diffuse BRDF
float3 RTXCR_EvalLambertianBRDF(const float3 N, const float3 L, const float3 diffuseAlbedo)
{
    const float NoL = min(max(1e-5f, dot(N, L)), 1.0f);
	return diffuseAlbedo * (RTXCR_ONE_OVER_PI * NoL).xxx;
}

// Calculates the monochromatic base reflectivity from a given incident and transmitted IoR
float RTXCR_CalculateBaseReflectivity(const float incidentIoR, const float transmittedIoR)
{
    const float tmp = (incidentIoR - transmittedIoR) / (incidentIoR + transmittedIoR);
    return tmp * tmp;
}

// Analytical fresnel equation for dielectric material:
// https://en.wikipedia.org/wiki/Fresnel_equations#Power_(intensity)_reflection_and_transmission_coefficients
// For approximation versions, check the bsdf shader.
float RTXCR_DielectricFresnel(
    const float eta,        // refracted / reflected ior
    const float cosThetaI)  // cosine between of angle normal/half-vector and incident direction
{
    // cosThetaT^2 = 1 - sinThetaT^2 = 1 - (sinThetaI / eta)^2 = 1 - (1 - cosThetaI^2) / eta^2
    const float cosThetaT2 = 1.0f - (1.0f - cosThetaI * cosThetaI) / (eta * eta);
    // Handle TIR (Total internal reflection: https://en.wikipedia.org/wiki/Total_internal_reflection)
    if (cosThetaT2 < 0.0f)
    {
        return 1.0f;
    }

    const float cosThetaT = sqrt(cosThetaT2); // refracted angle cosine

    const float n1t1 = cosThetaI;
    const float n1t2 = cosThetaT;
    const float n2t1 = cosThetaI * eta;
    const float n2t2 = cosThetaT * eta;
    const float rs = (n1t2 - n2t1) / (n1t2 + n2t1);
    const float rp = (n1t1 - n2t2) / (n1t1 + n2t2);
    const float fres = 0.5f * (rs * rs + rp * rp);

    return saturate(fres);
}

// Schlick's approximation to Fresnel term
float RTXCR_EvalFresnelSchlick(float f0, float NdotS)
{
	return f0 + (1.0f - f0) * pow(1.0f - NdotS, 5.0f);
}

// Calculates Beer-Lambert attenuation at a specified distance through a medium with a specified attenuation coefficient.
float3 RTXCR_EvalBeerLambertAttenuation(in const float3 attenuationCoefficient, in const float distance)
{
    return exp(-attenuationCoefficient * distance);
}

float3 RTXCR_SampleDirectionHenyeyGreenstein(float2 rndSample, in float g, in float3 wo)
{
    float cosTheta;
    if (abs(g) < 1e-3f)
    {
        cosTheta = 1 - 2 * rndSample.x;
    }
    else
    {
        const float sqrTerm = (1 - g * g) / (1 - g + 2 * g * rndSample.x);
        cosTheta = (1 + g * g - sqrTerm * sqrTerm) / (2 * g);
    }

    // Compute direction for Henyey-Greenstein sample
    const float sinTheta = sqrt(max((float) 0, 1 - cosTheta * cosTheta));
    const float phi = RTXCR_TWO_PI * rndSample.y;
    float3 x, y;
    const float3 z = wo;
    RTXCR_CreateCoordinateSystemFromZ(true, z, x, y);
    const float3 wi = RTXCR_SphericalDirection(sinTheta, cosTheta, phi, x, y, z);
    return wi;
}

// Linear RGB → perceptual luminance (Rec.709).
float RTXCR_Luminance(const float3 rgb)
{
	return dot(rgb, float3(0.2126f, 0.7152f, 0.0722f));
}

#endif

