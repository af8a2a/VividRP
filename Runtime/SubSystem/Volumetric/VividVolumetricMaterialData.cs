using System.Runtime.InteropServices;
using UnityEngine;

namespace VividRP.Runtime
{
    [StructLayout(LayoutKind.Sequential)]
    public struct VividVolumetricMaterialBounds
    {
        public Vector4 rightExtentX;
        public Vector4 upExtentY;
        public Vector4 centerExtentZ;

        internal static int Stride => Marshal.SizeOf<VividVolumetricMaterialBounds>();

        internal Vector3 right => new(rightExtentX.x, rightExtentX.y, rightExtentX.z);
        internal Vector3 up => new(upExtentY.x, upExtentY.y, upExtentY.z);
        internal Vector3 center => new(centerExtentZ.x, centerExtentZ.y, centerExtentZ.z);
        internal float extentX => rightExtentX.w;
        internal float extentY => upExtentY.w;
        internal float extentZ => centerExtentZ.w;

        internal static VividVolumetricMaterialBounds Create(
            Vector3 right,
            Vector3 up,
            Vector3 center,
            Vector3 extents)
        {
            return new VividVolumetricMaterialBounds
            {
                rightExtentX = new Vector4(right.x, right.y, right.z, extents.x),
                upExtentY = new Vector4(up.x, up.y, up.z, extents.y),
                centerExtentZ = new Vector4(center.x, center.y, center.z, extents.z)
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VividVolumetricMaterialRenderingData
    {
        public Vector4 viewSpaceBounds;
        public uint startSliceIndex;
        public uint sliceCount;
        public uint padding0;
        public uint padding1;
        public Vector4 obbVertexPositionWS0;
        public Vector4 obbVertexPositionWS1;
        public Vector4 obbVertexPositionWS2;
        public Vector4 obbVertexPositionWS3;
        public Vector4 obbVertexPositionWS4;
        public Vector4 obbVertexPositionWS5;
        public Vector4 obbVertexPositionWS6;
        public Vector4 obbVertexPositionWS7;

        internal static int Stride => Marshal.SizeOf<VividVolumetricMaterialRenderingData>();
    }
}
