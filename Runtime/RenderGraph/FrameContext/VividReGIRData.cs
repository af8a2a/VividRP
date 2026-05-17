using System.Runtime.InteropServices;
using UnityEngine;

namespace VividRP.Runtime
{
    public enum VividReGIRMode : uint
    {
        Disabled = 0u,
        Grid = 1u,
        Onion = 2u,
    }

    public enum VividReGIRSourceSamplingMode : uint
    {
        Uniform = 0u,
        PowerRIS = 1u,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VividReGIRLightData
    {
        public const uint TypePoint = 0u;
        public const uint TypeSpot = 1u;
        public const uint TypeTube = 2u;
        public const uint TypeRectangle = 3u;

        public Vector3 positionWS;
        public float range;
        public Vector3 color;
        public uint lightType;
        public Vector3 directionWS;
        public float angleScale;
        public Vector3 rightWS;
        public float angleOffset;
        public Vector3 upWS;
        public float shapeRadius;
        public Vector2 areaSize;
        public float power;
        public uint renderingLayerMask;

        internal static int Stride => Marshal.SizeOf<VividReGIRLightData>();
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct VividReGIRParameters
    {
        public const int OnionMaxLayerGroups = 8;
        public const int OnionMaxRings = 52;

        public Vector3 centerWS;
        public float cellSize;
        public uint gridSizeX;
        public uint gridSizeY;
        public uint gridSizeZ;
        public uint lightsPerCell;
        public uint lightCount;
        public uint slotCount;
        public uint buildSamples;
        public float samplingJitter;
        public uint frameIndex;
        public VividReGIRMode mode;
        public VividReGIRSourceSamplingMode sourceSamplingMode;
        public uint lightPdfTextureWidth;
        public uint lightPdfTextureHeight;
        public uint lightPdfTextureMipCount;
        public uint onionCellCount;
        public uint onionLayerGroupCount;
        public float onionCubicRootFactor;
        public float onionLinearFactor;
        public uint onionRingCount;
        public uint pad0;

        public fixed float onionLayerInnerRadius[OnionMaxLayerGroups];
        public fixed float onionLayerOuterRadius[OnionMaxLayerGroups];
        public fixed float onionLayerInvLogLayerScale[OnionMaxLayerGroups];
        public fixed uint onionLayerCount[OnionMaxLayerGroups];
        public fixed float onionLayerInvEquatorialCellAngle[OnionMaxLayerGroups];
        public fixed uint onionLayerCellsPerLayer[OnionMaxLayerGroups];
        public fixed uint onionLayerRingOffset[OnionMaxLayerGroups];
        public fixed uint onionLayerRingCount[OnionMaxLayerGroups];
        public fixed float onionLayerEquatorialCellAngle[OnionMaxLayerGroups];
        public fixed float onionLayerScale[OnionMaxLayerGroups];
        public fixed uint onionLayerCellOffset[OnionMaxLayerGroups];
        public fixed uint onionLayerPad[OnionMaxLayerGroups];

        public fixed float onionRingCellAngle[OnionMaxRings];
        public fixed float onionRingInvCellAngle[OnionMaxRings];
        public fixed uint onionRingCellOffset[OnionMaxRings];
        public fixed uint onionRingCellCount[OnionMaxRings];

        internal static int Stride => Marshal.SizeOf<VividReGIRParameters>();
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VividReGIRReservoir
    {
        public const uint InvalidLightIndex = uint.MaxValue;

        public uint lightIndex;
        public float weight;
        public uint pad0;
        public uint pad1;

        internal static int Stride => Marshal.SizeOf<VividReGIRReservoir>();
    }
}
