using System.Runtime.InteropServices;
using UnityEngine;

namespace VividRP.Runtime
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ShaderVariablesLightListInt2
    {
        public int x;
        public int y;

        public ShaderVariablesLightListInt2(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ShaderVariablesLightList
    {
        public Matrix4x4 g_mInvScrProjectionArr0;
        public Matrix4x4 g_mInvScrProjectionArr1;
        public Matrix4x4 g_mScrProjectionArr0;
        public Matrix4x4 g_mScrProjectionArr1;
        public Matrix4x4 g_mInvProjectionArr0;
        public Matrix4x4 g_mInvProjectionArr1;
        public Matrix4x4 g_mProjectionArr0;
        public Matrix4x4 g_mProjectionArr1;
        public Vector4 g_screenSize;
        public ShaderVariablesLightListInt2 g_viDimensions;
        public int g_iNrVisibLights;
        public uint g_isOrthographic;
        public uint g_BaseFeatureFlags;
        public int g_iNumSamplesMSAA;
        public uint _EnvLightIndexShift;
        public uint _DecalIndexShift;
    }
}
