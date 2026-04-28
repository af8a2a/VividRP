using System.Runtime.InteropServices;
using UnityEngine;

namespace VividRP.Runtime
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ShaderVariablesVolumetric
    {
        public static readonly int ConstantBufferShaderId = Shader.PropertyToID("ShaderVariablesVolumetric");

        public Matrix4x4 _VBufferCoordToViewDirWS;
        public Vector4 _VBufferViewportSize;
        public Vector4 _VBufferViewportScale;
        public Vector4 _VBufferDepthEncodingParams;
        public Vector4 _VBufferDepthDecodingParams;
        public Vector4 _VBufferGeometryParams;
        public Vector4 _VBufferFogScattering;
        public Vector4 _VBufferFogHeightParams;
        public Vector4 _VBufferFogControlParams;
        public Vector4 _VBufferLocalFogParams;
    }
}
