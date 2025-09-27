using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    //-----------------------------------------------------------------------------
    // light extension
    //-----------------------------------------------------------------------------
    static class VisibleLightExtensionMethods
    {
        public struct VisibleLightAxisAndPosition
        {
            public Vector3 Position;
            public Vector3 Forward;
            public Vector3 Up;
            public Vector3 Right;
        }

        public static Vector3 GetPosition(this VisibleLight value)
        {
            return value.localToWorldMatrix.GetColumn(3);
        }

        public static Vector3 GetForward(this VisibleLight value)
        {
            return value.localToWorldMatrix.GetColumn(2);
        }

        public static Vector3 GetUp(this VisibleLight value)
        {
            return value.localToWorldMatrix.GetColumn(1);
        }

        public static Vector3 GetRight(this VisibleLight value)
        {
            return value.localToWorldMatrix.GetColumn(0);
        }

        public static VisibleLightAxisAndPosition GetAxisAndPosition(this VisibleLight value)
        {
            var matrix = value.localToWorldMatrix;
            VisibleLightAxisAndPosition output;
            output.Position = matrix.GetColumn(3);
            output.Forward = matrix.GetColumn(2);
            output.Up = matrix.GetColumn(1);
            output.Right = matrix.GetColumn(0);
            return output;
        }
    }

    //-----------------------------------------------------------------------------
    // structure definition
    //-----------------------------------------------------------------------------

    [GenerateHLSL]
    internal enum LightVolumeType
    {
        Cone,
        Sphere,
        Box,
        Count
    }

    [GenerateHLSL]
    internal enum LightCategory
    {
        Punctual,
        Area,
        Env,
        Decal,
        Count
    }

    [GenerateHLSL]
    internal enum LightFeatureFlags
    {
        // Light bit mask must match LightDefinitions.s_LightFeatureMaskFlags value
        Punctual = 1 << 12,
        Area = 1 << 13,
        Directional = 1 << 14,
        Env = 1 << 15,
        Sky = 1 << 16,
        SSRefraction = 1 << 17,
        SSReflection = 1 << 18,
        // If adding more light be sure to not overflow LightDefinitions.s_LightFeatureMaskFlags
    }

    // Caution: Order is important and is use for optimization in light loop
    [GenerateHLSL]
    internal enum GPULightType
    {
        Directional,
        Point,
        Spot,
        ProjectorPyramid,
        ProjectorBox,

        // AreaLight
        Tube, // Keep Line lights before Rectangle. This is needed because of a compiler bug (see LightLoop.hlsl)
        Rectangle,

        // Currently not supported in real time (just use for reference)
        Disc,
        // Sphere,
    };

    static class GPULightTypeExtension
    {
        public static bool IsAreaLight(this GPULightType lightType)
        {
            return lightType == GPULightType.Rectangle || lightType == GPULightType.Tube;
        }

        public static bool IsSpot(this GPULightType lightType)
        {
            return lightType == GPULightType.Spot || lightType == GPULightType.ProjectorBox || lightType == GPULightType.ProjectorPyramid;
        }
    }


    //Do not change these numbers!!
    //Its not a full power of 2 because the last light slot is reserved.
    internal enum FPTLMaxLightSizes
    {
        Low = 31,
        High = 63
    }

    [GenerateHLSL]
    class LightDefinitions
    {
        public static int s_MaxNrBigTileLightsPlusOne = 512; // may be overkill but the footprint is 2 bits per pixel using uint16.
        public static float s_ViewportScaleZ = 1.0f;
        public static int s_UseLeftHandCameraSpace = 1;

        public static int s_TileSizeFptl = 16;
        public static int s_TileSizeClustered = 32;
        public static int s_TileSizeBigTile = 64;

        // Tile indexing constants for indirect dispatch deferred pass : [2 bits for eye index | 15 bits for tileX | 15 bits for tileY]
        public static int s_TileIndexMask = 0x7FFF;
        public static int s_TileIndexShiftX = 0;
        public static int s_TileIndexShiftY = 15;
        public static int s_TileIndexShiftEye = 30;

        // feature variants
        public static int s_NumFeatureVariants = 29;

        // light list limits
        public static int s_LightListMaxCoarseEntries = 64;
        public static int s_LightClusterMaxCoarseEntries = 128;

        // We have room for ShaderConfig.FPTLMaxLightCount lights, plus 1 implicit value for length.
        // We allocate only 16 bits per light index & length, thus we divide by 2, and store in a word buffer.
        /// <summary>
        /// Maximum number of lights for a fine pruned light tile. This number can only be the prespecified possibilities in FPTLMaxLightSizes
        /// Lower count will mean some memory savings.
        /// Note: For any rendering bigger than 4k (in native) it is recommended to use Low count per tile, to avoid possible artifacts.
        /// </summary>
        public static int s_LightDwordPerFptlTile = (((int)FPTLMaxLightSizes.High + 1)) / 2;

        public static int s_LightClusterPackingCountBits = (int)Mathf.Ceil(Mathf.Log(Mathf.NextPowerOfTwo((int)FPTLMaxLightSizes.High), 2));
        public static int s_LightClusterPackingCountMask = (1 << s_LightClusterPackingCountBits) - 1;
        public static int s_LightClusterPackingOffsetBits = 32 - s_LightClusterPackingCountBits;
        public static int s_LightClusterPackingOffsetMask = (1 << s_LightClusterPackingOffsetBits) - 1;

        // Following define the maximum number of bits use in each feature category.
        public static uint s_LightFeatureMaskFlags = 0xFFF000;
        public static uint s_LightFeatureMaskFlagsOpaque = 0xFFF000 & ~((uint)LightFeatureFlags.SSRefraction); // Opaque don't support screen space refraction

        public static uint
            s_LightFeatureMaskFlagsTransparent = 0xFFF000 & ~((uint)LightFeatureFlags.SSReflection); // Transparent don't support screen space reflection

        public static uint s_MaterialFeatureMaskFlags = 0x000FFF; // don't use all bits just to be safe from signed and/or float conversions :/

        // Screen space shadow flags
        public static uint s_RayTracedScreenSpaceShadowFlag = 0x1000;
        public static uint s_ScreenSpaceColorShadowFlag = 0x100;
        public static uint s_InvalidScreenSpaceShadow = 0xff;
        public static uint s_ScreenSpaceShadowIndexMask = 0xff;

        //Contact shadow bit definitions
        public static int s_ContactShadowFadeBits = 8;
        public static int s_ContactShadowMaskBits = 32 - s_ContactShadowFadeBits;
        public static int s_ContactShadowFadeMask = (1 << s_ContactShadowFadeBits) - 1;
        public static int s_ContactShadowMaskMask = (1 << s_ContactShadowMaskBits) - 1;
    }

    [GenerateHLSL]
    struct SFiniteLightBound
    {
        public Vector3 boxAxisX; // Scaled by the extents (half-size)
        public Vector3 boxAxisY; // Scaled by the extents (half-size)
        public Vector3 boxAxisZ; // Scaled by the extents (half-size)
        public Vector3 center; // Center of the bounds (box) in camera space
        public float scaleXY; // Scale applied to the top of the box to turn it into a truncated pyramid (X = Y)
        public float radius; // Circumscribed sphere for the bounds (box)
    };

    [GenerateHLSL]
    struct LightVolumeData
    {
        public Vector3 lightPos; // Of light's "origin"
        public uint lightVolume; // Type index

        public Vector3 lightAxisX; // Normalized
        public uint lightCategory; // Category index

        public Vector3 lightAxisY; // Normalized
        public float radiusSq; // Cone and sphere: light range squared

        public Vector3 lightAxisZ; // Normalized
        public float cotan; // Cone: cotan of the aperture (half-angle)

        public Vector3 boxInnerDist; // Box: extents (half-size) of the inner box
        public uint featureFlags;

        public Vector3 boxInvRange; // Box: 1 / (OuterBoxExtents - InnerBoxExtents)
        public float affectVolumetric;
    };

    /// <summary>
    /// unsafe for array
    /// </summary>
    [GenerateHLSL(needAccessors = false, generateCBuffer = true)]
    //unsafe struct ShaderVariablesLightList
    struct ShaderVariablesLightList
    {
        public Matrix4x4 g_mInvScrProjectionArr;
        public Matrix4x4 g_mScrProjectionArr;
        public Matrix4x4 g_mInvProjectionArr;
        public Matrix4x4 g_mProjectionArr;

        public Vector4 g_screenSize;

        public Vector2Int g_viDimensions;
        public int g_iNrVisibLights;
        public uint g_isOrthographic;

        public uint g_BaseFeatureFlags;
        public int g_iNumSamplesMSAA;
        public uint _EnvLightIndexShift;
        public uint _DecalIndexShift;

        // From HDRP ShaderVariablesGlobal
        // Tile/Cluster
        public uint _NumTileFtplX;
        public uint _NumTileFtplY;
        public float g_fClustScale;
        public float g_fClustBase;

        public float g_fNearPlane;
        public float g_fFarPlane;
        public int g_iLog2NumClusters; // We need to always define these to keep constant buffer layouts compatible
        public uint g_isLogBaseBufferEnabled;

        public uint _NumTileClusteredX;
        public uint _NumTileClusteredY;
        public uint _EnvSliceSize; // Unused
        public uint _unused; // Unused

        //public uint _EnableDecalLayers;
    }


    [GenerateHLSL(PackingRules.Exact, false)]
    internal struct GPULightData
    {
        // Packing order depends on chronological access to avoid cache misses
        // Make sure to respect the 16-byte alignment
        public Vector3 positionWS;
        public uint lightLayerMask;

        public Vector3 color;
        public int lightFlags;

        public Vector4 lightAttenuation;

        public Vector3 dir;
        public int shadowLightIndex;

        public Vector4 lightOcclusionProbInfo;

        public int cookieLightIndex;
        public int shadowType;
        public float baseContribution;
        public float minRoughness;

        public Vector4 size; // Used by area (X = length or width, Y = height, Z = CosBarnDoorAngle, W = BarnDoorLength) and punctual lights (X = radius)

        public Vector3 forward;
        public float rangeAttenuationScale;
        public Vector3 up;
        public float rangeAttenuationBias;
        public Vector3 right;
        public float volumetricLightDimmer;
    };

    [GenerateHLSL(PackingRules.Exact, false)]
    struct DirectionalLightData
    {
        // Packing order depends on chronological access to avoid cache misses
        // Make sure to respect the 16-byte alignment
        public Vector3 positionWS;
        public uint lightLayerMask;

        public Vector3 color;
        public int lightFlags;

        public Vector4 lightAttenuation;

        public Vector3 dir;
        public int shadowlightIndex;

        public float minRoughness;
        public float lightDimmer; //TODO: make it used
        public float diffuseDimmer; //TODO: make it used
        public float specularDimmer; //TODO: make it used

        public float volumetricLightDimmer;
    };

    [GenerateHLSL(PackingRules.Exact, false)]
    struct EnvLightData
    {
        // EnvLightData is in ReflectionProbeManager, we just need this struct for index.
    }

    //-----------------------------------------------------------------------------

    internal static partial class ShaderGlobalKeywords
    {
        public static GlobalKeyword GPULightsCluster;
    }

    public static partial class ShaderKeywordStrings
    {
        /// <summary> Keyword used for GPULights.</summary>
        public const string GPULightsCluster = "_GPU_LIGHTS_CLUSTER";
    }
}