using UnityEngine.Rendering;

namespace VividRP.Runtime
{
        [GenerateHLSL]
        internal enum StencilUsage
        {
            Clear = 0,

            // Note: first bit is free and can still be used by both phases.

            // --- Following bits are used before transparent rendering ---
            IsUnlit = (1 << 0), // Unlit materials (shader and shader graph) except for the shadow matte
            RequiresDeferredLighting = (1 << 1),
            SubsurfaceScattering = (1 << 2),     //  SSS, Split Lighting
            TraceReflectionRay = (1 << 3),     //  SSR or RTR - slot is reuse in transparent
            Decals = (1 << 4),     //  Use to tag when an Opaque Decal is render into DBuffer
            ObjectMotionVector = (1 << 5),     //  Animated object (for motion blur, SSR, SSAO, TAA)

            // --- Stencil  is cleared after opaque rendering has finished ---

            // --- Following bits are used exclusively for what happens after opaque ---
            WaterExclusion = (1 << 0),    // Prevents water surface from being rendered.
            ExcludeFromTUAndAA = (1 << 1),    // Disable Temporal Upscaling and Antialiasing for certain objects
            DistortionVectors = (1 << 2), // Distortion pass - reset after distortion pass, shared with SMAA
            SMAA = (1 << 2),              // Subpixel Morphological Antialiasing
            // Reserved TraceReflectionRay = (1 << 3) for transparent SSR or RTR
            Refractive = (1 << 4),        // Indicates there's a refractive object
            WaterSurface = (1 << 5),      // Reserved for water surface usage (If update the value of 'STENCILUSAGE_WATER_SURFACE' in LensFlareCommon.hlsl)

            // --- Following are user bits, we don't touch them inside HDRP and is up to the user to handle them ---
            UserBit0 = (1 << 6),
            UserBit1 = (1 << 7),

            HDRPReservedBits = 255 & ~(UserBit0 | UserBit1),
        }

    
}