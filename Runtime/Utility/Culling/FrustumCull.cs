using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace VividRP.Runtime
{
    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    public struct FrustumCullingJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float4> FrustumPlanes; // 长度固定为 6

        [ReadOnly] public NativeArray<CullingInstance> Instances;

        // 输出结果：使用 ParallelWriter 支持多线程并发写入
        [WriteOnly] public NativeList<int>.ParallelWriter VisibleIndices;

        public void Execute(int index)
        {
            AABB bounds = Instances[index].Bounds;
            bool isVisible = true;

            // 遍历 6 个视锥平面
            for (int i = 0; i < 6; i++)
            {
                float4 plane = FrustumPlanes[i];
                float3 normal = plane.xyz;
                float planeDistance = plane.w;

                // 核心数学优化：计算 AABB 在平面法线上的投影半径
                // 由于使用了 Unity.Mathematics，这里的 math.abs 和 math.dot 会被 Burst 编译为高效的 SIMD 指令
                float r = math.dot(bounds.Extents, math.abs(normal));

                // 计算 AABB 中心点到平面的有向距离
                float d = math.dot(normal, bounds.Center) + planeDistance;

                // 如果中心点在平面的负半轴，且距离大于半径，说明整个 AABB 都在平面外（被剔除）
                if (d < -r)
                {
                    isVisible = false;
                    break; // 只要在一个平面外，直接判定不可见
                }
            }

            if (isVisible)
            {
                // 记录可见的原始索引
                VisibleIndices.AddNoResize(Instances[index].OriginalIndex);
            }
        }
    }
}