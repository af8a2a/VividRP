using Unity.Mathematics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    // float4 保证 16 字节对齐，契合 Burst SIMD 寄存器；w 分量不使用（填 0）
    public struct AABB
    {
        public float4 Center;   // w 不使用
        public float4 Extents;  // w 不使用；存储 AABB 半长 (Size / 2)
    }

    // 可选：如果你想要完全解耦，定义一个通用的 Culling 接口数据
    public struct CullingInstance
    {
        public AABB Bounds;
        public int OriginalIndex; // 用于映射回原始的数组
    }


    public static class CullingUtility
    {
        public static void ExtractFrustumPlanes(float4x4 viewProjMatrix, NativeArray<float4> planes)
        {
            // Unity.Mathematics float4x4 是列主序（c0–c3 为列）。
            // Gribb/Hartmann 算法要求按行访问，因此先转置。
            float4x4 m = math.transpose(viewProjMatrix);
            // 转置后 c0-c3 对应原始矩阵的行 0-3
            planes[0] = NormalizePlane(m.c3 + m.c0);  // 左
            planes[1] = NormalizePlane(m.c3 - m.c0);  // 右
            planes[2] = NormalizePlane(m.c3 + m.c1);  // 下
            planes[3] = NormalizePlane(m.c3 - m.c1);  // 上
            planes[4] = NormalizePlane(m.c3 + m.c2);  // 近
            planes[5] = NormalizePlane(m.c3 - m.c2);  // 远
        }

        public static void ExtractFrustumPlanes(Matrix4x4 viewProjMatrix, Vector4[] planes)
        {
            if (planes == null || planes.Length < 6)
                return;

            planes[0] = NormalizePlane(
                viewProjMatrix.m30 + viewProjMatrix.m00,
                viewProjMatrix.m31 + viewProjMatrix.m01,
                viewProjMatrix.m32 + viewProjMatrix.m02,
                viewProjMatrix.m33 + viewProjMatrix.m03);
            planes[1] = NormalizePlane(
                viewProjMatrix.m30 - viewProjMatrix.m00,
                viewProjMatrix.m31 - viewProjMatrix.m01,
                viewProjMatrix.m32 - viewProjMatrix.m02,
                viewProjMatrix.m33 - viewProjMatrix.m03);
            planes[2] = NormalizePlane(
                viewProjMatrix.m30 + viewProjMatrix.m10,
                viewProjMatrix.m31 + viewProjMatrix.m11,
                viewProjMatrix.m32 + viewProjMatrix.m12,
                viewProjMatrix.m33 + viewProjMatrix.m13);
            planes[3] = NormalizePlane(
                viewProjMatrix.m30 - viewProjMatrix.m10,
                viewProjMatrix.m31 - viewProjMatrix.m11,
                viewProjMatrix.m32 - viewProjMatrix.m12,
                viewProjMatrix.m33 - viewProjMatrix.m13);
            planes[4] = NormalizePlane(
                viewProjMatrix.m30 + viewProjMatrix.m20,
                viewProjMatrix.m31 + viewProjMatrix.m21,
                viewProjMatrix.m32 + viewProjMatrix.m22,
                viewProjMatrix.m33 + viewProjMatrix.m23);
            planes[5] = NormalizePlane(
                viewProjMatrix.m30 - viewProjMatrix.m20,
                viewProjMatrix.m31 - viewProjMatrix.m21,
                viewProjMatrix.m32 - viewProjMatrix.m22,
                viewProjMatrix.m33 - viewProjMatrix.m23);
        }

        private static float4 NormalizePlane(float4 plane)
        {
            float length = math.length(plane.xyz);
            return plane / length; // xyz 为法线，w 为距离原点的距离
        }

        private static Vector4 NormalizePlane(float x, float y, float z, float distance)
        {
            var length = Mathf.Sqrt(x * x + y * y + z * z);
            if (length <= 1e-6f)
                return Vector4.zero;

            var reciprocalLength = 1.0f / length;
            return new Vector4(
                x * reciprocalLength,
                y * reciprocalLength,
                z * reciprocalLength,
                distance * reciprocalLength);
        }
    }
}
