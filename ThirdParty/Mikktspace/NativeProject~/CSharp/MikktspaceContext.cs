using System;
using System.Collections.Generic;
using System.Text;

namespace Mikktspace.NET
{
    public delegate int VerticesPerFaceHandler(int face);
    public delegate void VertexPositionHandler(int face, int vertex, out float x, out float y, out float z);
    public delegate void VertexNormalHandler(int face, int vertex, out float x, out float y, out float z);
    public delegate void VertexUVHandler(int face, int vertex, out float u, out float v);

    public delegate void BasicTangentHandler(int face, int vertex, float x, float y, float z, float sign);
    public delegate void TangentHandler(int face, int vertex, float tangentX, float tangentY, float tangentZ,
        float bitangentX, float bitangentY, float bitangentZ, float tangentMagnitude, float bitangentMagnitude, bool isOrientationPreserving);

    public class MikktspaceContext
    {
        public readonly int FaceCount;
        public readonly VerticesPerFaceHandler GetVerticesPerFace;

        public readonly VertexPositionHandler GetVertexPosition;
        public readonly VertexNormalHandler GetVertexNormal;
        public readonly VertexUVHandler GetVertexUV;

        public readonly BasicTangentHandler SetTangentBasic;
        public readonly TangentHandler SetTangent;

        public bool UsesBasicTangentHandler => SetTangentBasic != null;

        public MikktspaceContext(int faceCount, VerticesPerFaceHandler getVerticesPerFace,
            VertexPositionHandler getVertexPosition, VertexNormalHandler getVertexNormal, VertexUVHandler getVertexUV,
            BasicTangentHandler setTangentBasic = null, TangentHandler setTangent = null)
        {
            if (faceCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(faceCount), "Face Count must be larger than 0");

            if (getVerticesPerFace == null)
                throw new ArgumentNullException(nameof(getVerticesPerFace), "All mesh data callbacks must be provided");

            if (getVertexNormal == null)
                throw new ArgumentNullException(nameof(getVerticesPerFace), "All mesh data callbacks must be provided");

            if (getVertexUV == null)
                throw new ArgumentNullException(nameof(getVerticesPerFace), "All mesh data callbacks must be provided");

            if (setTangentBasic == null && setTangent == null)
                throw new ArgumentException($"Both {nameof(setTangentBasic)} and {nameof(setTangent)} are null");

            if (setTangentBasic != null && setTangent != null)
                throw new ArgumentException($"{nameof(setTangentBasic)} and {nameof(setTangent)} cannot be used at the same time");

            this.FaceCount = faceCount;
            this.GetVerticesPerFace = getVerticesPerFace;

            this.GetVertexPosition = getVertexPosition;
            this.GetVertexNormal = getVertexNormal;
            this.GetVertexUV = getVertexUV;

            this.SetTangentBasic = setTangentBasic;
            this.SetTangent = setTangent;
        }

        // Native wrappers
        internal int GetNumFaces(IntPtr context) => FaceCount;
        internal int GetNumVerticesOfFace(IntPtr context, int face) => GetVerticesPerFace(face);

        internal unsafe void GetPosition(IntPtr context, IntPtr data, int face, int vert)
        {
            GetVertexPosition(face, vert, out float x, out float y, out float z);

            var floatData = (float*)data;

            *(floatData+0) = x;
            *(floatData+1) = y;
            *(floatData+2) = z;
        }

        internal unsafe void GetNormal(IntPtr context, IntPtr data, int face, int vert)
        {
            GetVertexNormal(face, vert, out float x, out float y, out float z);

            var floatData = (float*)data;

            *(floatData + 0) = x;
            *(floatData + 1) = y;
            *(floatData + 2) = z;
        }

        internal unsafe void GetTexCoord(IntPtr context, IntPtr data, int face, int vert)
        {
            GetVertexUV(face, vert, out float u, out float v);

            var floatData = (float*)data;

            *(floatData + 0) = u;
            *(floatData + 1) = v;
        }

        internal unsafe void SetTangentSpaceBasic(IntPtr context, IntPtr tangent, float sign, int face, int vert)
        {
            var tangentData = (float*)tangent;

            SetTangentBasic(face, vert, tangentData[0], tangentData[1], tangentData[2], sign);
        }

        internal unsafe void SetTangentSpace(IntPtr context, IntPtr tangent, IntPtr bitangent, float magS, float magT, bool isOrientationPreserving,
            int face, int vert)
        {
            var tangentData = (float*)tangent;
            var bitangentData = (float*)bitangent;

            SetTangent(face, vert, tangentData[0], tangentData[1], tangentData[2],
                bitangentData[0], bitangentData[1], bitangentData[2], magS, magT, isOrientationPreserving);
        }
    }
}
