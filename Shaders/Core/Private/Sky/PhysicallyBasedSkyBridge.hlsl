#ifndef VIVIDRP_PHYSICALLY_BASED_SKY_BRIDGE_INCLUDED
#define VIVIDRP_PHYSICALLY_BASED_SKY_BRIDGE_INCLUDED

// Keep the bridge limited to Vivid-specific runtime policy.
// The main sky entry point now follows the HDRP split between the top-level shader,
// rendering helpers, and evaluation helpers.
float _SkyUseLUT;

bool HasSkyViewLut()
{
    return _SkyUseLUT > 0.5f;
}

void EvaluateDistantAtmosphereWithLut(float3 viewDirectionWS, out float3 skyColor, out float3 skyOpacity)
{
    skyColor = 0.0f;
    skyOpacity = 0.0f;

    if (!HasSkyViewLut())
        return;

    EvaluateDistantAtmosphere(viewDirectionWS, skyColor, skyOpacity);
}

#endif
