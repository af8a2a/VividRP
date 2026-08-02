#ifndef VIVIDRP_OPENPBR_VOLUME_BRIDGE_INCLUDED
#define VIVIDRP_OPENPBR_VOLUME_BRIDGE_INCLUDED

// Volume-only OpenPBR bridge for ray generation. Keeping the BSDF aggregate
// out of this translation unit avoids pulling surface-lobe LUTs into the
// material-medium transport path.
#define OPENPBR_LANGUAGE_TARGET_SLANG 1
#define OPENPBR_USE_TEXTURE_LUTS 0
#define OPENPBR_FAST_RCP_SQRT(value) rsqrt(value)
#define OPENPBR_FAST_SQRT(value) sqrt(value)
#define OPENPBR_FAST_NORMALIZE(value) normalize(value)

#include "OpenPBRUnityHLSLInterop.hlsl"
#include "Vendor/openpbr_homogeneous_volume.h"

#endif
