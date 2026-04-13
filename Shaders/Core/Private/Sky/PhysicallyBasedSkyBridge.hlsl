#ifndef VIVIDRP_PHYSICALLY_BASED_SKY_BRIDGE_INCLUDED
#define VIVIDRP_PHYSICALLY_BASED_SKY_BRIDGE_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Private/Sky/CelestialBodyData.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Private/Sky/PhysicallyBasedSkyEvaluation.hlsl"

static const int VIEW_SAMPLE_COUNT = 12;
static const float MAX_SKY_RADIANCE = 60000.0f;

float4x4 _PixelCoordToViewDirWS;
float _SkyUseLUT;
int _SkyBakingViewSampleCount;
float4 _SkyCameraPositionPS;
float4 _SkySunDirection;
float4 _SkySunColor;
float4 _SkyPlanetParams;
float4 _SkyAirScattering;
float4 _SkyAirExtinction;
float4 _SkyAerosolScattering;
float4 _SkyAerosolExtinction;
float4 _SkyOzoneExtinction;
float4 _SkyOzoneParams;
float4 _SkyGroundTint;

int _RenderSunDisk;
int _HasGroundAlbedoTexture;
int _HasGroundEmissionTexture;
int _HasSpaceEmissionTexture;
float _GroundEmissionMultiplier;
float _SpaceEmissionMultiplier;
float4x4 _PlanetRotation;
float4x4 _SpaceRotation;

TEXTURECUBE(_GroundAlbedoTexture);
TEXTURECUBE(_GroundEmissionTexture);
TEXTURECUBE(_SpaceEmissionTexture);
TEXTURE2D(_DirectionalShadowTexture);

struct Attributes
{
    uint vertexID : SV_VertexID;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings Vert(Attributes input)
{
    Varyings output;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID, UNITY_RAW_FAR_CLIP_VALUE);
    return output;
}

float3 GetSkyViewDirWS(float2 positionCS)
{
    float4 viewDirWS = mul(float4(positionCS.xy, 1.0f, 1.0f), _PixelCoordToViewDirWS);
    return normalize(viewDirWS.xyz);
}

float GetPlanetRadius()
{
    float boundPlanetRadius = _GroundAlbedo_PlanetRadius.w > 0.0f ? _GroundAlbedo_PlanetRadius.w : 0.0f;
    return max(max(boundPlanetRadius, _SkyPlanetParams.x), 1000.0f);
}

float GetAtmosphereRadius()
{
    float planetRadius = GetPlanetRadius();
    float boundAtmosphereRadius = _AtmosphericRadius > planetRadius ? _AtmosphericRadius : 0.0f;
    return max(max(boundAtmosphereRadius, _SkyPlanetParams.y), planetRadius + 1.0f);
}

float GetSkyExposureMultiplier()
{
    return max(_SkyPlanetParams.z, 0.0f);
}

bool HasMaterialOverrides()
{
    return _AtmosphericRadius > 0.0f;
}

bool GetRenderSunDiskEnabled()
{
    return _RenderSunDisk != 0 || _SkyPlanetParams.w > 0.5f;
}

float3 GetGroundAlbedoTint()
{
    return _GroundAlbedo_PlanetRadius.w > 0.0f
        ? _GroundAlbedo_PlanetRadius.xyz
        : _SkyGroundTint.rgb;
}

bool IntersectAtmosphereRay(float3 origin, float3 direction, float atmosphereRadius, out float entryDistance, out float exitDistance)
{
    float b = dot(origin, direction);
    float c = dot(origin, origin) - atmosphereRadius * atmosphereRadius;
    float discriminant = b * b - c;

    if (discriminant < 0.0f)
    {
        entryDistance = 0.0f;
        exitDistance = 0.0f;
        return false;
    }

    float sqrtDiscriminant = sqrt(discriminant);
    entryDistance = -b - sqrtDiscriminant;
    exitDistance = -b + sqrtDiscriminant;
    return exitDistance > 0.0f;
}

bool IntersectGroundRay(float3 origin, float3 direction, float planetRadius, out float distance)
{
    float b = dot(origin, direction);
    float c = dot(origin, origin) - planetRadius * planetRadius;
    float discriminant = b * b - c;

    if (discriminant < 0.0f)
    {
        distance = 0.0f;
        return false;
    }

    distance = -b - sqrt(discriminant);
    return distance > 0.0f;
}

float3 SanitizeSkyRadiance(float3 color)
{
    if (any(isnan(color)) || any(isinf(color)))
        return 0.0f;

    return clamp(max(color, 0.0f), 0.0f, MAX_SKY_RADIANCE);
}

float3 SampleGroundAlbedo(float3 groundNormal)
{
    
    float3 albedo = GetGroundAlbedoTint();
    if (_HasGroundAlbedoTexture != 0)
        albedo *= SAMPLE_TEXTURECUBE(_GroundAlbedoTexture, sampler_TrilinearClamp, mul(groundNormal, (float3x3)_PlanetRotation)).rgb;
    return albedo;
}

float3 SampleGroundEmission(float3 groundNormal)
{
    if (_HasGroundEmissionTexture == 0)
        return 0.0f;

    return SAMPLE_TEXTURECUBE(_GroundEmissionTexture, sampler_TrilinearClamp, mul(groundNormal, (float3x3)_PlanetRotation)).rgb * _GroundEmissionMultiplier;
}

float3 SampleSpaceEmission(float3 viewDirection)
{
    if (_HasSpaceEmissionTexture == 0)
        return 0.0f;

    return SAMPLE_TEXTURECUBE(_SpaceEmissionTexture, sampler_TrilinearClamp, mul(viewDirection, (float3x3)_SpaceRotation)).rgb * _SpaceEmissionMultiplier;
}

int GetViewSampleCount()
{
    return _SkyBakingViewSampleCount > 0 ? _SkyBakingViewSampleCount : VIEW_SAMPLE_COUNT;
}

bool IsViewAboveHorizon(float3 viewDirection)
{
    float3 cameraPosition = _SkyCameraPositionPS.xyz;
    float radialDistance = max(length(cameraPosition), GetPlanetRadius() + 1.0f);
    float3 planetUp = cameraPosition * rcp(radialDistance);
    float cosChi = clamp(dot(planetUp, normalize(viewDirection)), -1.0f, 1.0f);

    return cosChi >= ComputeCosineOfHorizonAngle(radialDistance);
}

void ApplyArtisticOverrides(float3 viewDirection, inout float3 skyColor)
{
    if (!HasMaterialOverrides())
        return;

    float3 cameraPosition = _SkyCameraPositionPS.xyz;
    float radialDistance = max(length(cameraPosition), GetPlanetRadius() + 1.0f);
    float3 planetUp = cameraPosition * rcp(radialDistance);
    float cosHor = ComputeCosineOfHorizonAngle(radialDistance);
    float cosChi = clamp(dot(planetUp, normalize(viewDirection)), -1.0f, 1.0f);
    float3 skyOpacity = 0.0f;

    AtmosphereArtisticOverride(cosHor, cosChi, skyColor, skyOpacity);
}

float ComputeMoonPhase(CelestialBodyData moon, float3 viewDirectionWS)
{
    float3 moonCenter = -moon.forward.xyz * moon.distanceFromCamera;
    float radialDistance = moon.distanceFromCamera;
    float rcpRadialDistance = rcp(radialDistance);
    float2 t = IntersectSphere(moon.radius, dot(-moon.forward.xyz, viewDirectionWS), radialDistance, rcpRadialDistance);
    float3 N = normalize(t.x * viewDirectionWS - moonCenter);

    return saturate(-dot(N, moon.sunDirection));
}

float ComputeEarthshine(CelestialBodyData moon)
{
    float sinPhase = sqrt(max(1.0f - dot(moon.sunDirection, moon.forward), 0.0f)) * INV_SQRT2;
    float earthshine = 1.0f - sinPhase * sqrt(sinPhase);

    return earthshine * moon.earthshine;
}

bool CanSampleCelestialSurfaceTexture(CelestialBodyData light)
{
#if defined(VIVIDRP_SKY_BINDLESS_SURFACE_TEXTURES)
    return light.surfaceTextureScaleOffset.x > 0.0f && light.surfaceTextureIndex != 0xffffffffu;
#else
    return false;
#endif
}

float3 SampleCelestialSurfaceTexture(CelestialBodyData light, float3 viewDirectionWS)
{
#if defined(VIVIDRP_SKY_BINDLESS_SURFACE_TEXTURES)
    float2 projection = float2(dot(viewDirectionWS, light.right), dot(viewDirectionWS, light.up));
    float2 angles = float2(FastASin(projection.x), FastASin(-projection.y));
    float2 uv = saturate(angles * rcp(max(light.angularRadius, 1e-6f)) * 0.5f + 0.5f);
    Texture2D surfaceTexture = GetBindlessTexture2D(NonUniformResourceIndex(light.surfaceTextureIndex));
    return SAMPLE_TEXTURE2D(surfaceTexture, sampler_LinearClamp, uv).rgb;
#else
    return 1.0f.xxx;
#endif
}

bool IsSkyBakingPass()
{
    return _SkyBakingViewSampleCount > 0;
}

float SampleDirectionalShadow(float2 positionCS)
{
    if (IsSkyBakingPass())
        return 1.0f;

    float2 uv = (floor(positionCS) + 0.5f) * _ScreenSize.zw;
    return saturate(SAMPLE_TEXTURE2D_LOD(_DirectionalShadowTexture, sampler_PointClamp, saturate(uv), 0).x);
}

float3 RenderSunDisk(inout float tFrag, float tExit, float3 V)
{
    float3 radiance = 0;

    // Intersect and shade emissive celestial bodies.
    // Unfortunately, they don't write depth.
    for (uint i = 0; i < _CelestialBodyCount; i++)
    {
        CelestialBodyData light = _CelestialBodyDatas[i];

        // Celestial body must be outside the atmosphere (request from Pierre D).
        float lightDist = max(light.distanceFromCamera, tExit);

        if (asint(light.angularRadius) != 0 && lightDist < tFrag)
        {
            // We may be able to see the celestial body.
            float3 L = -light.forward.xyz;

            float LdotV    = -dot(L, V);
            float radInner = light.angularRadius;

            if (LdotV >= light.flareCosInner) // Sun disk.
            {
                tFrag = lightDist;
                float3 color = light.surfaceColor;

                if (light.type != 0)
                    color *= ComputeMoonPhase(light, V) * INV_PI + ComputeEarthshine(light); // Lambertian BRDF

                //todo:
                // if (light.surfaceTextureScaleOffset.x > 0)
                // {
                //     float2 proj   = float2(dot(V, light.right), dot(V, light.up));
                //     float2 angles = float2(FastASin(proj.x), FastASin(-proj.y));
                //     float2 uv = angles * rcp(radInner) * 0.5 + 0.5;
                //     color *= SampleCookie2D(uv, light.surfaceTextureScaleOffset);
                // }

                radiance = color;
            }
            else if (LdotV >= light.flareCosOuter) // Flare region.
            {
                float rad = acos(LdotV);
                float r   = max(0, rad - radInner);
                float w   = saturate(1 - r * rcp(light.flareSize));

                float3 color = light.flareColor;
                color *= SafePositivePow(w, light.flareFalloff);
                radiance += color;
            }
        }
    }

    return radiance;
}

void EvaluatePbrAtmosphere(float3 positionPS, float3 V, float distAlongRay, bool renderSunDisk, out float3 skyColor, out float3 skyOpacity)
{
    skyColor = skyOpacity = 0.0f;

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
    bool rayIntersectsAtmosphere = (tEntry >= 0.0f);
    bool hitGround = distAlongRay < 0.0f;
    bool rayEndsInsideAtmosphere = (tFrag < tExit) && !hitGround;

    if (!rayIntersectsAtmosphere)
        return;

    float2 Z = R * n;
    float r0 = r;
    float cosChi0 = cosChi;
    float r1 = 0.0f;
    float cosChi1 = 0.0f;
    float3 N1 = 0.0f;

    if (tFrag < tExit)
    {
        float3 P1 = O + tFrag * -V;
        r1 = length(P1);
        N1 = P1 * rcp(r1);
        cosChi1 = dot(P1, -V) * rcp(r1);
        cosChi0 = (cosChi1 >= 0.0f) ? cosChi0 : -cosChi0;
    }

    float2 ch0;
    float2 ch1 = 0.0f;

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
    float3 opticalDepth = ch.x * H.x * _AirSeaLevelExtinction.xyz
                        + ch.y * H.y * _AerosolSeaLevelExtinction;
    skyOpacity = 1.0f - TransmittanceFromOpticalDepth(opticalDepth);

    for (uint i = 0; i < _CelestialLightCount; i++)
    {
        CelestialBodyData light = _CelestialBodyDatas[i];
        float3 L = -light.forward.xyz;

        float height = r - R;
        float NdotL = dot(N, L);
        float3 projL = L - N * NdotL;
        float3 projV = V - N * NdotV;
        float phiL = acos(clamp(dot(projL, projV) * rsqrt(max(dot(projL, projL) * dot(projV, projV), FLT_EPS)), -1.0f, 1.0f));

        TexCoord4D texCoord = ConvertPositionAndOrientationToTexCoords(height, NdotV, NdotL, phiL);
        float LdotV = dot(L, V);
        float3 radiance = 0.0f;

        radiance += lerp(
            SAMPLE_TEXTURE3D_LOD(_AirSingleScatteringTexture, s_linear_clamp_sampler, float3(texCoord.u, texCoord.v, texCoord.w0), 0).rgb,
            SAMPLE_TEXTURE3D_LOD(_AirSingleScatteringTexture, s_linear_clamp_sampler, float3(texCoord.u, texCoord.v, texCoord.w1), 0).rgb,
            texCoord.a) * AirPhase(LdotV);

        radiance += lerp(
            SAMPLE_TEXTURE3D_LOD(_AerosolSingleScatteringTexture, s_linear_clamp_sampler, float3(texCoord.u, texCoord.v, texCoord.w0), 0).rgb,
            SAMPLE_TEXTURE3D_LOD(_AerosolSingleScatteringTexture, s_linear_clamp_sampler, float3(texCoord.u, texCoord.v, texCoord.w1), 0).rgb,
            texCoord.a) * AerosolPhase(LdotV);

        radiance += lerp(
            SAMPLE_TEXTURE3D_LOD(_MultipleScatteringTexture, s_linear_clamp_sampler, float3(texCoord.u, texCoord.v, texCoord.w0), 0).rgb,
            SAMPLE_TEXTURE3D_LOD(_MultipleScatteringTexture, s_linear_clamp_sampler, float3(texCoord.u, texCoord.v, texCoord.w1), 0).rgb,
            texCoord.a) * MS_EXPOSURE_INV;

        if (rayEndsInsideAtmosphere)
        {
            float height1 = r1 - R;
            float NdotV1 = -cosChi1;
            float NdotL1 = dot(N1, L);
            float3 projL1 = L - N1 * NdotL1;
            float3 projV1 = V - N1 * NdotV1;
            float phiL1 = acos(clamp(dot(projL1, projV1) * rsqrt(max(dot(projL1, projL1) * dot(projV1, projV1), FLT_EPS)), -1.0f, 1.0f));
            texCoord = ConvertPositionAndOrientationToTexCoords(height1, NdotV1, NdotL1, phiL1);

            float3 radiance1 = 0.0f;
            radiance1 += lerp(
                SAMPLE_TEXTURE3D_LOD(_AirSingleScatteringTexture, s_linear_clamp_sampler, float3(texCoord.u, texCoord.v, texCoord.w0), 0).rgb,
                SAMPLE_TEXTURE3D_LOD(_AirSingleScatteringTexture, s_linear_clamp_sampler, float3(texCoord.u, texCoord.v, texCoord.w1), 0).rgb,
                texCoord.a) * AirPhase(LdotV);
            radiance1 += lerp(
                SAMPLE_TEXTURE3D_LOD(_AerosolSingleScatteringTexture, s_linear_clamp_sampler, float3(texCoord.u, texCoord.v, texCoord.w0), 0).rgb,
                SAMPLE_TEXTURE3D_LOD(_AerosolSingleScatteringTexture, s_linear_clamp_sampler, float3(texCoord.u, texCoord.v, texCoord.w1), 0).rgb,
                texCoord.a) * AerosolPhase(LdotV);
            radiance1 += lerp(
                SAMPLE_TEXTURE3D_LOD(_MultipleScatteringTexture, s_linear_clamp_sampler, float3(texCoord.u, texCoord.v, texCoord.w0), 0).rgb,
                SAMPLE_TEXTURE3D_LOD(_MultipleScatteringTexture, s_linear_clamp_sampler, float3(texCoord.u, texCoord.v, texCoord.w1), 0).rgb,
                texCoord.a) * MS_EXPOSURE_INV;

            radiance = max(0.0f, radiance - (1.0f - skyOpacity) * radiance1);
        }

        skyColor += radiance * light.color.rgb;
    }

    AtmosphereArtisticOverride(cosHor, cosChi, skyColor, skyOpacity);
}

void EvaluateAtmosphericFallback(float3 viewDirWS, float rayLength, out float3 skyColor, out float3 skyTransmittance)
{
    skyColor = 0.0f;
    skyTransmittance = 1.0f;

    int sampleCount = GetViewSampleCount();
    float stepLength = rayLength / sampleCount;
    if (stepLength <= 0.0f)
        return;

    float3 cameraPosition = _SkyCameraPositionPS.xyz;

    [loop]
    for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
    {
        float sampleDistance = (sampleIndex + 0.5f) * stepLength;
        float3 samplePosition = cameraPosition + viewDirWS * sampleDistance;
        float radialDistance = max(length(samplePosition), _PlanetaryRadius);
        float height = max(radialDistance - _PlanetaryRadius, 0.0f);
        float3 sigmaE = AtmosphereExtinction(height);
        float3 transmittanceOverSegment = TransmittanceFromOpticalDepth(sigmaE * stepLength);
        float3 scattering = 0.0f;

        for (uint lightIndex = 0; lightIndex < _CelestialLightCount; lightIndex++)
        {
            CelestialBodyData light = _CelestialBodyDatas[lightIndex];
            float3 L = -light.forward.xyz;
            float airPhase = AirPhase(-dot(L, viewDirWS));
            float aerosolPhase = AerosolPhase(-dot(L, viewDirWS));
            float cosTheta = dot(samplePosition, L) * rcp(radialDistance);
            float3 sunTransmittance = EvaluateSunColorAttenuation(cosTheta, radialDistance);
            float3 phaseScatter = AirScatter(height) * airPhase + AerosolScatter(height) * aerosolPhase;
            scattering += light.color.rgb * sunTransmittance * phaseScatter;
        }

        skyColor += IntegrateOverSegment(scattering, transmittanceOverSegment, skyTransmittance, sigmaE);
        skyTransmittance *= transmittanceOverSegment;
    }
}

float3 EvaluateGroundColor(float3 groundPoint, float3 viewTransmittance)
{
    float3 groundNormal = normalize(groundPoint);
    float3 groundColor = SampleGroundEmission(groundNormal);
    float3 elevatedGroundPoint = groundPoint + groundNormal;
    float radialDistance = max(length(elevatedGroundPoint), GetPlanetRadius());
    float3 groundAlbedo = SampleGroundAlbedo(groundNormal);

    for (uint lightIndex = 0; lightIndex < _CelestialLightCount; lightIndex++)
    {
        CelestialBodyData light = _CelestialBodyDatas[lightIndex];
        float3 L = -light.forward.xyz;
        float cosTheta = dot(elevatedGroundPoint, L) * rcp(radialDistance);
        float3 sunTransmittance = EvaluateSunColorAttenuation(cosTheta, radialDistance);
        float ndotl = saturate(dot(groundNormal, L));
        groundColor += groundAlbedo * INV_PI * ndotl * sunTransmittance * light.color.rgb;
    }

    return SanitizeSkyRadiance(groundColor * viewTransmittance);
}

float3 EvaluateSpaceColor(float3 viewDirection, float3 viewTransmittance)
{
    return SanitizeSkyRadiance(SampleSpaceEmission(viewDirection) * viewTransmittance);
}

float3 EvaluateSky(float3 directionWS, float2 positionCS)
{
        const float R = _PlanetaryRadius;
        const float3 V = GetSkyViewDirWS(positionCS);
        const bool renderSunDisk = _RenderSunDisk != 0;
        float3 N; float r; // These params correspond to the entry point

    #ifdef LOCAL_SKY
        const float3 O = _WorldSpaceCameraPos;

        float tEntry = IntersectAtmosphere(O, V, N, r).x;
        float tExit  = IntersectAtmosphere(O, V, N, r).y;

        float cosChi = -dot(N, V);
        float cosHor = ComputeCosineOfHorizonAngle(r);
    #else
        N = float3(0, 1, 0);
        r = _PlanetaryRadius;
        float cosChi = -dot(N, V);
        float cosHor = 0.0f;
        const float3 O = N * r;

        float tEntry = 0.0f;
        float tExit  = IntersectSphere(_AtmosphericRadius, -dot(N, V), r).y;
    #endif

        bool rayIntersectsAtmosphere = (tEntry >= 0);
        bool lookAboveHorizon        = (cosChi >= cosHor);

        float  tFrag    = FLT_INF;
        float3 radiance = 0;

        if (renderSunDisk)
            radiance = RenderSunDisk(tFrag, tExit, V);

        if (rayIntersectsAtmosphere && !lookAboveHorizon) // See the ground?
        {
            float tGround = tEntry + IntersectSphere(R, cosChi, r).x;

            if (tGround < tFrag)
            {
                // Closest so far.
                // Make it negative to communicate to EvaluatePbrAtmosphere that we intersected the ground.
                tFrag = -tGround;

                radiance = 0;

                float3 gP = O + tGround * -V;
                float3 gN = normalize(gP);

                if (_HasGroundEmissionTexture)
                {
                    float4 ts = SAMPLE_TEXTURECUBE(_GroundEmissionTexture, sampler_TrilinearClamp, mul(gN, (float3x3)_PlanetRotation));
                    radiance += _GroundEmissionMultiplier * ts.rgb;
                }

                float3 albedo = _GroundAlbedo.xyz;

                if (_HasGroundAlbedoTexture)
                {
                    albedo *= SAMPLE_TEXTURECUBE(_GroundAlbedoTexture,sampler_TrilinearClamp , mul(gN, (float3x3)_PlanetRotation)).rgb;
                }

                float3 gBrdf = INV_PI * albedo;

                // Shade the ground.
                for (uint i = 0; i < _CelestialLightCount; i++)
                {
                    CelestialBodyData light = _CelestialBodyDatas[i];

                    float3 L          = -light.forward.xyz;
                    float3 intensity  = light.color.rgb;

                #ifdef LOCAL_SKY
                    intensity *= SampleGroundIrradianceTexture(dot(gN, L));
                #else
                    float3 opticalDepth = ComputeAtmosphericOpticalDepth(r, dot(N, L), true);
                    intensity *= TransmittanceFromOpticalDepth(opticalDepth) * saturate(dot(N, L));
                #endif

                    radiance += gBrdf * intensity;
                }
            }
        }
        else if (tFrag == FLT_INF) // See the stars?
        {
            if (_HasSpaceEmissionTexture)
            {
                // V points towards the camera.
                float4 ts = SAMPLE_TEXTURECUBE(_SpaceEmissionTexture, sampler_TrilinearClamp, mul(-V, (float3x3)_SpaceRotation));
                radiance += _SpaceEmissionMultiplier * ts.rgb;
            }
        }

        float3 skyColor = 0, skyOpacity = 0;

        #ifdef LOCAL_SKY
        if (rayIntersectsAtmosphere)
            EvaluatePbrAtmosphere(_WorldSpaceCameraPos, V, tFrag, renderSunDisk, skyColor, skyOpacity);
        #else
        if (lookAboveHorizon)
            EvaluateDistantAtmosphere(-V, skyColor, skyOpacity);
        #endif

        skyColor += radiance * (1 - skyOpacity);
        skyColor *= _IntensityMultiplier;

        return float4(skyColor, 1.0);
}

float3 EvaluateSkyColor(float2 positionCS)
{
    float3 viewDirWS = -GetSkyViewDirWS(positionCS);
    return SanitizeSkyRadiance(EvaluateSky(viewDirWS, positionCS));
}

#endif
