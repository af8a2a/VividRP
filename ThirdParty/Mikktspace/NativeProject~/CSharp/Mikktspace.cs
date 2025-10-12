using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;

namespace Mikktspace.NET
{
    public static class MikkGenerator
    {
        public static bool GenerateTangentSpace(int faceCount, VerticesPerFaceHandler getVerticesPerFace,
            VertexPositionHandler getPosition, VertexNormalHandler getNormal, VertexUVHandler getUV,
            BasicTangentHandler setTangentBasic, float angularThreshold = 180)
        {
            var context = new MikktspaceContext(faceCount, getVerticesPerFace, getPosition, getNormal, getUV,
                setTangentBasic, null);

            return GenerateTangentSpace(context, angularThreshold);
        }

        public static bool GenerateTangentSpace(int faceCount, VerticesPerFaceHandler getVerticesPerFace,
            VertexPositionHandler getPosition, VertexNormalHandler getNormal, VertexUVHandler getUV,
            TangentHandler setTangent, float angularThreshold = 180)
        {
            var context = new MikktspaceContext(faceCount, getVerticesPerFace, getPosition, getNormal, getUV,
                null, setTangent);

            return GenerateTangentSpace(context, angularThreshold);
        }

        public static unsafe bool GenerateTangentSpace(MikktspaceContext context, float angularThreshold = 180)
        {
            try
            {
                // explicitly store the delegates on the stack so they're not collected by the GC during the execution of this function
                GetNumFaces _getNumFaces = context.GetNumFaces;
                GetNumVerticesOfFace _getNumVerticesOfFace = context.GetNumVerticesOfFace;

                GetMeshData _getPosition = context.GetPosition;
                GetMeshData _getNormal = context.GetNormal;
                GetMeshData _getTexCoord = context.GetTexCoord;

                SetTangentSpace _setTangentSpace = context.SetTangentSpace;
                SetTangentSpaceBasic _setTangentSpaceBasic = context.SetTangentSpaceBasic;

                var mikkInterface = new SMikkTSpaceInterface();

                mikkInterface.m_getNumFaces = Marshal.GetFunctionPointerForDelegate(_getNumFaces);
                mikkInterface.m_getNumVerticesOfFace = Marshal.GetFunctionPointerForDelegate(_getNumVerticesOfFace);

                mikkInterface.m_getPosition = Marshal.GetFunctionPointerForDelegate(_getPosition);
                mikkInterface.m_getNormal = Marshal.GetFunctionPointerForDelegate(_getNormal);
                mikkInterface.m_getTexCoord = Marshal.GetFunctionPointerForDelegate(_getTexCoord);

                if (context.UsesBasicTangentHandler)
                    mikkInterface.m_setTSpaceBasic = Marshal.GetFunctionPointerForDelegate(_setTangentSpaceBasic);
                else
                    mikkInterface.m_setTSpace = Marshal.GetFunctionPointerForDelegate(_setTangentSpace);

                var mikkContext = new SMikkTSpaceContext();
                mikkContext.m_pInterface = new IntPtr(&mikkInterface);

                var pContext = new IntPtr(&mikkContext);

                var result = Native.genMikktTangSpace(pContext, angularThreshold);

                GC.KeepAlive(context);

                GC.KeepAlive(_getNumFaces);
                GC.KeepAlive(_getNumVerticesOfFace);

                GC.KeepAlive(_getPosition);
                GC.KeepAlive(_getNormal);
                GC.KeepAlive(_getTexCoord);

                GC.KeepAlive(_setTangentSpace);
                GC.KeepAlive(_setTangentSpaceBasic);

                return result;
            }
            finally
            {

            }
        }
    }
}
