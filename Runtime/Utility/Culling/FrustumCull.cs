using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace VividRP.Runtime
{
    [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
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

                // AABB 在平面法线方向的投影半径
                float r = math.dot(bounds.Extents.xyz, math.abs(normal));

                // AABB 中心点到平面的有向距离
                float d = math.dot(normal, bounds.Center.xyz) + planeDistance;

                // 整个 AABB 位于平面负半空间则剔除
                if (d < -r)
                {
                    isVisible = false;
                    break;
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