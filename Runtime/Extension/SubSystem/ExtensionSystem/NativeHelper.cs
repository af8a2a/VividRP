using System;
using System.Runtime.InteropServices;

namespace UnityEngine.Rendering.Universal
{
    public static class NativeHelper
    {
        
        /// <summary>
        /// Packs a UnityEngine.Matrix4x4 into a float[16] in column-major order (HLSL-compatible).
        /// </summary>
        public static float[] Pack(this Matrix4x4 m)
        {
            return new float[16]
            {
                m.m00, m.m10, m.m20, m.m30, // Column 0
                m.m01, m.m11, m.m21, m.m31, // Column 1
                m.m02, m.m12, m.m22, m.m32, // Column 2
                m.m03, m.m13, m.m23, m.m33  // Column 3
            };
        }

        
        public static float[] Pack(this Vector4 v)
        {
            return new float[4]
            {
                v.x,v.y,v.z,v.w
            };
        }
        
        public static float[] Pack(this Vector3 v)
        {
            return new float[3]
            {
                v.x,v.y,v.z
            };
        }

        public static float[] Pack(this Vector2 v)
        {
            return new float[2]
            {
                v.x,v.y
            };
        }


    }
    
    public class PinnedStruct<T> : IDisposable where T : struct
    {
        private IntPtr ptr;

        public PinnedStruct()
        {
            ptr = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
        }

        public IntPtr Pointer => ptr;

        public void Update(ref T value)
        {
            Marshal.StructureToPtr(value, ptr, false);
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(ptr);
            ptr = IntPtr.Zero;
        }
    }

}