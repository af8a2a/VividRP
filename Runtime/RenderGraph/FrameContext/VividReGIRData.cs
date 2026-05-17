using System.Runtime.InteropServices;
using UnityEngine;

namespace VividRP.Runtime
{
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
    public struct VividReGIRParameters
    {
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
        public uint pad0;
        public uint pad1;
        public uint pad2;

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
