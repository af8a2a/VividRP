using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace VividRP.Runtime.SubSystem.Decal
{
    internal struct DecalSourceData
    {
        public float3 worldCenter;
        public quaternion worldRotation;
        public float3 boxSize;
        public float4 baseColor;
        public float blendDistance;
        public float metallic;
        public float roughness;
    }

    internal struct DecalPreparedData
    {
        public float4x4 worldToDecal;
        public float4 worldAabbCenter;
        public float4 worldAabbExtents;
        public float4 baseColor;
        public float normalizedBlendDistance;
        public float clampedMetallic;
        public float clampedRoughness;
        public float padding;
    }

    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    internal struct PrepareDecalsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<DecalSourceData> Sources;
        [WriteOnly] public NativeArray<DecalPreparedData> Prepared;

        public void Execute(int index)
        {
            DecalSourceData src = Sources[index];

            // Match Quaternion.Euler(-90, 0, 0); Burst constant-folds this.
            quaternion projectorToDecal = quaternion.Euler(math.radians(-90f), 0f, 0f);
            quaternion decalSpaceRotation = math.mul(src.worldRotation, projectorToDecal);
            float3 decalSpaceSize = new(src.boxSize.x, src.boxSize.z, src.boxSize.y);
            float4x4 worldToDecal = math.inverse(float4x4.TRS(src.worldCenter, decalSpaceRotation, decalSpaceSize));

            // Box AABB uses the original world rotation and box size (matches BoundProxyUtility.CalculateWorldAabb).
            float3 halfExtents = src.boxSize * 0.5f;
            float3 axisX = math.mul(src.worldRotation, new float3(1f, 0f, 0f));
            float3 axisY = math.mul(src.worldRotation, new float3(0f, 1f, 0f));
            float3 axisZ = math.mul(src.worldRotation, new float3(0f, 0f, 1f));
            float3 aabbExtents = math.abs(axisX) * halfExtents.x
                                 + math.abs(axisY) * halfExtents.y
                                 + math.abs(axisZ) * halfExtents.z;

            float normalizedBlendDistance = 0f;
            if (src.blendDistance > 0f)
            {
                float minDimension = math.min(math.abs(src.boxSize.x), math.abs(src.boxSize.y));
                if (minDimension > 1e-5f)
                    normalizedBlendDistance = math.clamp(src.blendDistance / minDimension, 0f, 0.5f);
            }

            Prepared[index] = new DecalPreparedData
            {
                worldToDecal = worldToDecal,
                worldAabbCenter = new float4(src.worldCenter, 0f),
                worldAabbExtents = new float4(aabbExtents, 0f),
                baseColor = src.baseColor,
                normalizedBlendDistance = normalizedBlendDistance,
                clampedMetallic = math.saturate(src.metallic),
                clampedRoughness = math.saturate(src.roughness),
                padding = 0f,
            };
        }
    }
}
