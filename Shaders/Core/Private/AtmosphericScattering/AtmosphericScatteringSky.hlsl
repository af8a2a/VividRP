#ifndef VIVIDRP_ATMOSPHERIC_SCATTERING_SKY_INCLUDED
#define VIVIDRP_ATMOSPHERIC_SCATTERING_SKY_INCLUDED

#include "../Sky/LightDefinition.cs.hlsl"
#include "../Sky/ShaderVariablesCompat.hlsl"
#include "../Sky/PhysicallyBasedSkyEvaluation.hlsl"

StructuredBuffer<CelestialBodyData> _CelestialBodyDatas;

void EvaluatePbrAtmosphere(float3 positionPS, float3 V, float distAlongRay, bool renderSunDisk,
                           out float3 skyColor, out float3 skyOpacity)
{
    skyColor = skyOpacity = 0;

    const float R = _PlanetaryRadius;
    const float2 n = float2(_AirDensityFalloff, _AerosolDensityFalloff);
    const float2 H = float2(_AirScaleHeight, _AerosolScaleHeight);
    const float3 O = positionPS;

    const float tFrag = abs(distAlongRay);

    float3 N;
    float r;
    float tEntry = IntersectAtmosphere(O, V, N, r).x;
    float tExit = IntersectAtmosphere(O, V, N, r).y;

    float NdotV = dot(N, V);
    float cosChi = -NdotV;
    float cosHor = ComputeCosineOfHorizonAngle(r);

    bool rayIntersectsAtmosphere = tEntry >= 0;
    bool lookAboveHorizon = cosChi >= cosHor;

    bool hitGround = distAlongRay < 0;
    bool rayEndsInsideAtmosphere = (tFrag < tExit) && !hitGround;

    if (!rayIntersectsAtmosphere)
        return;

    float2 Z = R * n;
    float r0 = r;
    float cosChi0 = cosChi;

    float r1 = 0;
    float cosChi1 = 0;
    float3 N1 = 0;

    if (tFrag < tExit)
    {
        float3 P1 = O + tFrag * -V;

        r1 = length(P1);
        N1 = P1 * rcp(r1);
        cosChi1 = dot(P1, -V) * rcp(r1);
        cosChi0 = (cosChi1 >= 0) ? cosChi0 : -cosChi0;
    }

    float2 ch0;
    float2 ch1 = 0;

    {
        float2 z0 = r0 * n;

        ch0.x = RescaledChapmanFunction(z0.x, Z.x, cosChi0);
        ch0.y = RescaledChapmanFunction(z0.y, Z.y, cosChi0);
    }

    if (tFrag < tExit)
    {
        float2 z1 = r1 * n;

        ch1.x = ChapmanUpperApprox(z1.x, abs(cosChi1)) * exp(Z.x - z1.x);
        ch1.y = ChapmanUpperApprox(z1.y, abs(cosChi1)) * exp(Z.y - z1.y);
    }

    float2 ch = abs(ch0 - ch1);

    float3 optDepth = ch.x * H.x * _AirSeaLevelExtinction.xyz
                    + ch.y * H.y * _AerosolSeaLevelExtinction;

    skyOpacity = 1 - TransmittanceFromOpticalDepth(optDepth);

    for (uint i = 0; i < _CelestialLightCount; i++)
    {
        CelestialBodyData light = _CelestialBodyDatas[i];
        float3 L = -light.forward.xyz;

        if (renderSunDisk && asint(light.angularRadius) != 0 && light.distanceFromCamera <= tFrag)
        {
            float c = dot(L, -V);

            if (-0.99999 < c && c < 0.99999)
            {
                float alpha = light.angularRadius;
                float beta = acos(c);
                float gamma = min(alpha, beta);
                gamma *= (PI - beta) * rcp(PI - gamma);

                float3 A = normalize(cross(L, -V));
                float3x3 rotationMatrix = RotationFromAxisAngle(A, sin(gamma), cos(gamma));
                L = mul(rotationMatrix, L);
            }
        }

        float height = r - R;
        float NdotL = dot(N, L);
        float3 projL = L - N * NdotL;
        float3 projV = V - N * NdotV;
        float phiL = acos(clamp(dot(projL, projV) * rsqrt(max(dot(projL, projL) * dot(projV, projV), FLT_EPS)), -1, 1));

        TexCoord4D tc = ConvertPositionAndOrientationToTexCoords(height, NdotV, NdotL, phiL);

        float3 radiance = 0;
        float LdotV = dot(L, V);

        radiance += lerp(SAMPLE_TEXTURE3D_LOD(_AirSingleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w0), 0).rgb,
                         SAMPLE_TEXTURE3D_LOD(_AirSingleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w1), 0).rgb,
                         tc.a) * AirPhase(LdotV);

        radiance += lerp(SAMPLE_TEXTURE3D_LOD(_AerosolSingleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w0), 0).rgb,
                         SAMPLE_TEXTURE3D_LOD(_AerosolSingleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w1), 0).rgb,
                         tc.a) * AerosolPhase(LdotV);

        radiance += lerp(SAMPLE_TEXTURE3D_LOD(_MultipleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w0), 0).rgb,
                         SAMPLE_TEXTURE3D_LOD(_MultipleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w1), 0).rgb,
                         tc.a) * MS_EXPOSURE_INV;

        if (rayEndsInsideAtmosphere)
        {
            float3 radiance1 = 0;

            float height1 = r1 - R;
            float NdotV1 = -cosChi1;
            float NdotL1 = dot(N1, L);
            float3 projL1 = L - N1 * NdotL1;
            float3 projV1 = V - N1 * NdotV1;
            float phiL1 = acos(clamp(dot(projL1, projV1) * rsqrt(max(dot(projL1, projL1) * dot(projV1, projV1), FLT_EPS)), -1, 1));

            tc = ConvertPositionAndOrientationToTexCoords(height1, NdotV1, NdotL1, phiL1);

            radiance1 += lerp(SAMPLE_TEXTURE3D_LOD(_AirSingleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w0), 0).rgb,
                              SAMPLE_TEXTURE3D_LOD(_AirSingleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w1), 0).rgb,
                              tc.a) * AirPhase(LdotV);

            radiance1 += lerp(SAMPLE_TEXTURE3D_LOD(_AerosolSingleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w0), 0).rgb,
                              SAMPLE_TEXTURE3D_LOD(_AerosolSingleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w1), 0).rgb,
                              tc.a) * AerosolPhase(LdotV);

            radiance1 += lerp(SAMPLE_TEXTURE3D_LOD(_MultipleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w0), 0).rgb,
                              SAMPLE_TEXTURE3D_LOD(_MultipleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w1), 0).rgb,
                              tc.a) * MS_EXPOSURE_INV;

            radiance = max(0, radiance - (1 - skyOpacity) * radiance1);
        }

        radiance *= light.color.rgb;
        skyColor += radiance;
    }

    AtmosphereArtisticOverride(cosHor, cosChi, skyColor, skyOpacity);
}

#endif
