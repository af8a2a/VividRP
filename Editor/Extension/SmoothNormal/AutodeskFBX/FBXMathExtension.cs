using Autodesk.Fbx;

namespace UnityEditor.Rendering.Universal
{
    public static class FBXMathExtension
    {
        public static FbxVector4 Abs(this FbxVector4 v)
        {
            var ret = v;
            ret.X = v.X > 0.0f ? v.X : -v.X;
            ret.Y = v.Y > 0.0f ? v.Y : -v.Y;
            ret.Z = v.Z > 0.0f ? v.Z : -v.Z;
            ret.W = v.W > 0.0f ? v.W : -v.W;
            return ret;
        }
    }
}