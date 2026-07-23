#ifndef __OPENPBR_BRIDGE__
#define __OPENPBR_BRIDGE__
// V1 uses the self-contained array LUT path. Transmission, dispersion and fuzz are
// intentionally disabled by the StandardLit adapter; coat and metallic stay enabled.
#define OPENPBR_LANGUAGE_TARGET_SLANG 1
#define OPENPBR_USE_TEXTURE_LUTS 0
#define OPENPBR_FAST_RCP_SQRT(value) rsqrt(value)
#define OPENPBR_FAST_SQRT(value) sqrt(value)
#define OPENPBR_FAST_NORMALIZE(value) normalize(value)

#define VIVIDRP_OPENPBR_FEATURE_EnableSheenAndCoat true
#define VIVIDRP_OPENPBR_FEATURE_EnableDispersion false
#define VIVIDRP_OPENPBR_FEATURE_EnableTranslucency false
#define VIVIDRP_OPENPBR_FEATURE_EnableMetallic true
#define VIVIDRP_OPENPBR_SELECT_FEATURE_IMPL(name) VIVIDRP_OPENPBR_FEATURE_##name
#define VIVIDRP_OPENPBR_SELECT_FEATURE(name) VIVIDRP_OPENPBR_SELECT_FEATURE_IMPL(name)
#define OPENPBR_GET_SPECIALIZATION_CONSTANT(name) VIVIDRP_OPENPBR_SELECT_FEATURE(name)

#include "OpenPBRUnityHLSLInterop.hlsl"
#include "Vendor/openpbr.h"
#include "OpenPBRUnityHLSLStructFactories.hlsl"

#endif
