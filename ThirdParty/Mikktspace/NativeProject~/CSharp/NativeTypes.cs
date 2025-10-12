using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;

namespace Mikktspace.NET
{
    [StructLayout(LayoutKind.Sequential)]
    struct SMikkTSpaceInterface
    {
        public IntPtr m_getNumFaces;
        public IntPtr m_getNumVerticesOfFace;

        public IntPtr m_getPosition;
        public IntPtr m_getNormal;
        public IntPtr m_getTexCoord;

        public IntPtr m_setTSpaceBasic;
        public IntPtr m_setTSpace;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SMikkTSpaceContext
    {
        public IntPtr m_pInterface;
        public IntPtr m_pUserData;
    }

    // Delegates
    delegate int GetNumFaces(IntPtr pContext);
    delegate int GetNumVerticesOfFace(IntPtr pContext, int iface);

    delegate void GetMeshData(IntPtr pContext, IntPtr data, int iFace, int iVert);

    delegate void SetTangentSpaceBasic(IntPtr pContext, IntPtr fvTangent, float fSign, int iFace, int iVert);
    delegate void SetTangentSpace(IntPtr pContext, IntPtr fvTangent, IntPtr fvBiTangent, float fMagS, float fMagT,
        [MarshalAs(UnmanagedType.Bool)] bool bIsOrientationPreserving, int iFace, int iVert);
}
