using Unity.Mathematics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine;

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
        public int OriginalIndex; // 用于映射回原始的 DDGI Volume 数组
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

        private static float4 NormalizePlane(float4 plane)
        {
            float length = math.length(plane.xyz);
            return plane / length; // xyz 为法线，w 为距离原点的距离
        }
    }
}