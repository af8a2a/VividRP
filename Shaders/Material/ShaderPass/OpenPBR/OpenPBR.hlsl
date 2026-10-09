#ifndef __OPENPBR_BRIDGE__
#define __OPENPBR_BRIDGE__
// Surface energy and LTC textures are imported from the original vendor data.
// Keep the array mode overridable for numerical reference checks.
#define OPENPBR_LANGUAGE_TARGET_SLANG 1
#ifndef OPENPBR_USE_TEXTURE_LUTS
#define OPENPBR_USE_TEXTURE_LUTS 1
#endif
#if OPENPBR_USE_TEXTURE_LUTS
#include "OpenPBRTextureLuts.hlsl"
#endif
#define OPENPBR_FAST_RCP_SQRT(value) rsqrt(value)
#define OPENPBR_FAST_SQRT(value) sqrt(value)
#define OPENPBR_FAST_NORMALIZE(value) normalize(value)

#ifndef VIVIDRP_OPENPBR_FEATURE_EnableSheenAndCoat
    #define VIVIDRP_OPENPBR_FEATURE_EnableSheenAndCoat true
#endif
#define VIVIDRP_OPENPBR_FEATURE_EnableDispersion false
#define VIVIDRP_OPENPBR_FEATURE_EnableTranslucency true
#define VIVIDRP_OPENPBR_FEATURE_EnableMetallic true
#define VIVIDRP_OPENPBR_SELECT_FEATURE_IMPL(name) VIVIDRP_OPENPBR_FEATURE_##name
#define VIVIDRP_OPENPBR_SELECT_FEATURE(name) VIVIDRP_OPENPBR_SELECT_FEATURE_IMPL(name)
#define OPENPBR_GET_SPECIALIZATION_CONSTANT(name) VIVIDRP_OPENPBR_SELECT_FEATURE(name)

#include "OpenPBRUnityHLSLInterop.hlsl"
#include "Vendor/openpbr.h"
#include "OpenPBRUnityHLSLStructFactories.hlsl"
#include "OpenPBRNEE.hlsl"

#endif
