using Unity.Mathematics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine;

namespace VividRP.Runtime
{
    // 16 字节对齐，完美契合 SIMD 寄存器
    public struct AABB
    {
        public float3 Center;
        public float3 Extents; // AABB的半长 (Size / 2)
    }

    // 可选：如果你想要完全解耦，定义一个通用的 Culling 接口数据
    public struct CullingInstance
    {
        public AABB Bounds;
        public int OriginalIndex; // 用于映射回原始的 DDGI Volume 数组
    }


    public static class CullingUtility
    {
        public static void ExtractFrustumPlanes(float4x4 viewProjMatrix, ref NativeArray<float4> planes)
        {

            // float4x4 在 Unity.Mathematics 中是按列存储的 (c0, c1, c2, c3)
            // 左平面
            planes[0] = NormalizePlane(viewProjMatrix.c3 + viewProjMatrix.c0);
            // 右平面
            planes[1] = NormalizePlane(viewProjMatrix.c3 - viewProjMatrix.c0);
            // 下平面
            planes[2] = NormalizePlane(viewProjMatrix.c3 + viewProjMatrix.c1);
            // 上平面
            planes[3] = NormalizePlane(viewProjMatrix.c3 - viewProjMatrix.c1);
            // 近平面 (注意：OpenGL/Vulkan 默认深度裁剪是 0~1 或 -1~1，Unity 现代 API 统一处理为对应矩阵)
            planes[4] = NormalizePlane(viewProjMatrix.c3 + viewProjMatrix.c2);
            // 远平面
            planes[5] = NormalizePlane(viewProjMatrix.c3 - viewProjMatrix.c2);
        }

        private static float4 NormalizePlane(float4 plane)
        {
            float length = math.length(plane.xyz);
            return plane / length; // xyz 为法线，w 为距离原点的距离
        }
    }
}