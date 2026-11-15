#ifndef VIVID_SDF_DIFFUSE_ATROUS_INCLUDED
#define VIVID_SDF_DIFFUSE_ATROUS_INCLUDED

Texture2D<float4> _SdfAtrousInput;
Texture2D<float> _SdfAtrousVariance;
#if !defined(VIVID_SDF_ATROUS_FINAL)
RWTexture2D<float> _SdfAtrousVarianceOutput;
#endif
uint _SdfAtrousStep;
uint _SdfAtrousUseRestirDomain;
uint _SdfAtrousHasVariance;
Texture2D<float4> _SdfAtrousScreenProbeMoments;
uint _SdfAtrousUseScreenProbeMoments;

struct SdfAtrousGuide
{
    float depth;
    float3 normal;
    float4 material;
    bool valid;
};

SdfAtrousGuide LoadSdfAtrousGuide(int2 pixel, int2 size)
{
    SdfAtrousGuide guide = (SdfAtrousGuide)0;
    if (any(pixel < 0) || any(pixel >= size))
        return guide;

    guide.depth = _SdfDiffuseDepth.Load(int3(pixel, 0));
    float4 packedNormal = _SdfDiffuseGBuffer1.Load(int3(pixel, 0));
    guide.valid = guide.depth != UNITY_RAW_FAR_CLIP_VALUE
        && VividIsSurfaceSummaryGBufferABIValid(packedNormal.a);
    guide.normal = VividDecodeSurfaceSummaryNormal(packedNormal.xy);
    guide.material = _SdfDiffuseGBuffer0.Load(int3(pixel, 0));
    return guide;
}

float4 LoadSdfAtrousLighting(int2 pixel)
{
    float4 lighting = _SdfAtrousInput.Load(int3(pixel, 0));
    // The ReSTIR prefilter has unpacked coverage. Recover the integration domain
    // from Resolve and carry it through the intermediate wavelet writes.
    if (_SdfAtrousUseRestirDomain != 0u && _SdfDiffuseCurrentResolved.Load(int3(pixel, 0)).a < 0.0)
        lighting.a = -lighting.a;
    return lighting;
}

float SdfAtrousLogLuminance(float3 lighting)
{
    return log2(1.0 + RestirLuminance(lighting));
}

float SdfAtrousNormalAgreement(SdfAtrousGuide center, SdfAtrousGuide tap)
{
    return tap.valid && tap.material.a == center.material.a
        ? saturate(dot(tap.normal, center.normal)) : 0.0;
}

float SdfAtrousGradient(float negativeDelta, float positiveDelta, float invalidDelta)
{
    if (min(abs(negativeDelta), abs(positiveDelta)) >= invalidDelta)
        return 0.0;
    // A one-sided slope keeps depth discontinuities out of the plane estimate.
    return abs(negativeDelta) < abs(positiveDelta) ? -negativeDelta : positiveDelta;
}

void FilterSdfDiffuseAtrousPixel(uint2 pixel)
{
    int2 size = int2(_SdfDiffuseScreenSize.xy);
    if (any(int2(pixel) >= size))
        return;

    int2 centerPixel = int2(pixel);
    float4 center = LoadSdfAtrousLighting(centerPixel);
    float filteredVariance = 0.0;
    if (abs(center.a) > 0.0)
    {
        SdfAtrousGuide guide = LoadSdfAtrousGuide(centerPixel, size);
        float centerLuma = SdfAtrousLogLuminance(center.rgb);
        float4 moments = _SdfAtrousUseScreenProbeMoments != 0u
            ? _SdfAtrousScreenProbeMoments.Load(int3(centerPixel, 0)) : 0.0;
        float momentTrust = moments.w > 0.0 ? smoothstep(2.0, 8.0, moments.z) : 0.0;
        bool firstNative = _SdfAtrousUseScreenProbeMoments != 0u && _SdfAtrousStep == 1u;
        const int2 axes[4] = { int2(-1, 0), int2(1, 0), int2(0, -1), int2(0, 1) };
        const float invalidDelta = 2.0;
        float relativeDepth = max(abs(guide.depth), 1e-4);
        float maxSlope = lerp(0.035 * relativeDepth, 0.02, unity_OrthoParams.w);
        float4 deltas = invalidDelta;
        float varianceSum = 0.0;
        float varianceWeight = 0.0;
        float nativeVarianceSum = 2.0 * max(0.0, moments.y);
        float nativeVarianceWeight = 2.0;
        float nativePlaneTrust = 1.0;
        [unroll]
        for (uint i = 0u; i < 4u; ++i)
        {
            int2 tapPixel = centerPixel + axes[i];
            SdfAtrousGuide tap = LoadSdfAtrousGuide(tapPixel, size);
            float delta = tap.depth - guide.depth;
            if (SdfAtrousNormalAgreement(guide, tap) > 0.85 && abs(delta) <= maxSlope)
            {
                deltas[i] = delta;
                if (_SdfAtrousUseScreenProbeMoments != 0u && momentTrust > 0.0)
                {
                    float4 tapMoments = _SdfAtrousScreenProbeMoments.Load(int3(tapPixel, 0));
                    float3 materialDelta = tap.material.rgb - guide.material.rgb;
                    float meanDelta = abs(tapMoments.x - moments.x);
                    float scale = 0.01 + 2.5 * sqrt(max(max(0.0, moments.y), max(0.0, tapMoments.y)));
                    float distance = meanDelta / scale;
                    float trust = smoothstep(2.0, 8.0, tapMoments.z)
                        * exp2(-distance * distance - 16.0 * dot(materialDelta, materialDelta));
                    nativePlaneTrust = min(nativePlaneTrust, trust);
                    nativeVarianceSum += trust * max(0.0, tapMoments.y);
                    nativeVarianceWeight += trust;
                }
                if ((_SdfAtrousHasVariance == 0u || (firstNative && momentTrust < 1.0))
                    && abs(delta) <= 0.02 * relativeDepth)
                {
                    float4 lighting = LoadSdfAtrousLighting(tapPixel);
                    float difference = SdfAtrousLogLuminance(lighting.rgb) - centerLuma;
                    varianceSum += abs(lighting.a) * difference * difference;
                    varianceWeight += abs(lighting.a);
                }
            }
            else
                nativePlaneTrust = 0.0;
        }
        float2 gradient = float2(SdfAtrousGradient(deltas.x, deltas.y, invalidDelta),
            SdfAtrousGradient(deltas.z, deltas.w, invalidDelta));
        float variance = !firstNative && _SdfAtrousHasVariance != 0u
            ? max(0.0, _SdfAtrousVariance.Load(int3(centerPixel, 0)))
            : 0.5 * varianceSum / max(1.0, varianceWeight);
        if (_SdfAtrousUseScreenProbeMoments != 0u && momentTrust > 0.0 && nativePlaneTrust > 0.0)
        {
            // The entire wavelet footprint must remain in a stable radiance
            // region. A one-pixel test misses borders reached by later scales.
            [loop]
            for (uint i = 0u; i < 25u; ++i)
            {
                if (i == 12u)
                    continue;
                int2 offset = int2(i % 5u, i / 5u) - 2;
                int2 tapPixel = centerPixel + offset * int(_SdfAtrousStep);
                if (any(tapPixel < 0) || any(tapPixel >= size))
                {
                    nativePlaneTrust = 0.0;
                    continue;
                }
                float4 tapMoments = _SdfAtrousScreenProbeMoments.Load(int3(tapPixel, 0));
                float delta = abs(tapMoments.x - moments.x);
                float scale = 0.01 + 2.5 * sqrt(max(max(0.0, moments.y), max(0.0, tapMoments.y)));
                float distance = delta / scale;
                nativePlaneTrust = min(nativePlaneTrust, smoothstep(2.0, 8.0, tapMoments.z) * exp2(-distance * distance));
                if (nativePlaneTrust == 0.0)
                    break;
            }
        }
        // Keep the original fine-scale uncertainty at geometry, material and
        // illumination boundaries; only stable plane interiors borrow variance.
        float nativeVariance = lerp(max(0.0, moments.y), nativeVarianceSum / nativeVarianceWeight, nativePlaneTrust);
        if (firstNative)
            variance = lerp(0.5 * varianceSum / max(1.0, varianceWeight), nativeVariance, momentTrust);
        else if (_SdfAtrousUseScreenProbeMoments != 0u)
            // Pixels share sparse probes: wavelet taps do not provide independent
            // observations. Preserve native temporal uncertainty at larger scales.
            variance = max(variance, momentTrust * nativePlaneTrust * nativeVariance);
        // Mature native history supplies temporal uncertainty instead of treating
        // a real illumination edge as spatial noise. Fresh history keeps the prior.
        float lumaFloor = lerp(0.15, 0.01, momentTrust);
        float lumaScale = rcp(lumaFloor + 2.5 * sqrt(variance));
        float inverseCoverage = rcp(max(abs(center.a), 1e-4));
        // Device depth is affine on a projected plane. Orthographic depth has a fixed range.
        float depthTolerance = lerp(0.006 * abs(guide.depth), 0.0002, unity_OrthoParams.w) + 1e-6;
        const float kernel[5] = { 1.0, 4.0, 6.0, 4.0, 1.0 };
        float totalWeight = kernel[2] * kernel[2];
        float3 sum = center.rgb * totalWeight;
        float varianceNumerator = variance * totalWeight * totalWeight;
#if defined(VIVID_SDF_ATROUS_GEOMETRY_GUARD)
        bool coarseGeometryEdge = false;
#endif

        [loop]
        for (int y = -2; y <= 2; ++y)
        {
            [loop]
            for (int x = -2; x <= 2; ++x)
            {
                if (x == 0 && y == 0)
                    continue;
                int2 offset = int2(x, y) * int(_SdfAtrousStep);
                int2 tapPixel = centerPixel + offset;
                SdfAtrousGuide tap = LoadSdfAtrousGuide(tapPixel, size);
                float normalAgreement = SdfAtrousNormalAgreement(guide, tap);
                float depthError = abs(tap.depth - guide.depth - dot(gradient, float2(offset))) / depthTolerance;
                if (normalAgreement <= 0.8 || depthError >= 3.0)
                {
                    // Keep the already filtered fine-scale result where the
                    // coarsest footprint crosses a surface boundary. Averaging
                    // only its surviving plane taps can lift contact shadows.
#if defined(VIVID_SDF_ATROUS_GEOMETRY_GUARD)
                    coarseGeometryEdge = coarseGeometryEdge || (_SdfAtrousStep >= 4u
                        && all(tapPixel >= 0) && all(tapPixel < size));
#endif
                    continue;
                }

                float4 lighting = LoadSdfAtrousLighting(tapPixel);
                if (abs(lighting.a) <= 0.0 || (lighting.a < 0.0) != (center.a < 0.0))
                    continue;

                float3 albedoDelta = tap.material.rgb - guide.material.rgb;
                float lumaDelta = abs(SdfAtrousLogLuminance(lighting.rgb) - centerLuma);
                float coverageWeight = saturate(abs(lighting.a) * inverseCoverage);
                float weight = kernel[x + 2] * kernel[y + 2] * coverageWeight
                    * PositivePow(normalAgreement, 16.0)
                    * exp2(-depthError * depthError - 16.0 * dot(albedoDelta, albedoDelta) - lumaScale * lumaDelta);
                sum += weight * lighting.rgb;
                totalWeight += weight;
                float tapVariance = firstNative
                    ? lerp(variance, max(0.0, _SdfAtrousScreenProbeMoments.Load(int3(tapPixel, 0)).y), momentTrust)
                    : _SdfAtrousHasVariance != 0u
                    ? max(0.0, _SdfAtrousVariance.Load(int3(tapPixel, 0))) : variance;
                varianceNumerator += weight * weight * tapVariance;
            }
        }
#if defined(VIVID_SDF_ATROUS_GEOMETRY_GUARD)
        if (!coarseGeometryEdge)
#endif
            center.rgb = sum / totalWeight;
        // Neighboring pixels can share probes or reservoirs. Keep a conservative variance floor.
        filteredVariance = max(0.25 * variance, varianceNumerator / (totalWeight * totalWeight));
#if defined(VIVID_SDF_ATROUS_GEOMETRY_GUARD) && !defined(VIVID_SDF_ATROUS_FINAL)
        // A rejected coarse footprint keeps both the fine signal and its
        // uncertainty, so the following scale cannot trust a fictitious blur.
        if (coarseGeometryEdge)
            filteredVariance = variance;
#endif
    }

#if defined(VIVID_SDF_ATROUS_FINAL)
    center.a = abs(center.a);
#else
    _SdfAtrousVarianceOutput[pixel] = filteredVariance;
#endif
    _SdfDiffuseOutput[pixel] = center;
}

[numthreads(8, 8, 1)]
#if defined(VIVID_SDF_ATROUS_FINAL)
void FilterSdfDiffuseAtrousFinal(uint3 id : SV_DispatchThreadID)
#else
void FilterSdfDiffuseAtrous(uint3 id : SV_DispatchThreadID)
#endif
{
    FilterSdfDiffuseAtrousPixel(id.xy);
}

#endif
