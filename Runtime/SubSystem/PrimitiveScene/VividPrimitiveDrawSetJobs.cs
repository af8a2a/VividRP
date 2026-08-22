using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Runtime.PrimitiveScene
{
    [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    internal struct VividPrimitiveFrustumCullJob : IJobParallelFor
    {
        private const int FrustumPlaneCount = 6;

        [ReadOnly]
        internal NativeArray<VividPrimitiveCullRecord> CullingRecords;

        [ReadOnly]
        internal NativeArray<float4> FrustumPlanes;

        [WriteOnly]
        internal NativeArray<byte> Visibility;

        internal uint CameraCullingMask;
        internal VividInstancePassMask RequiredPassMask;
        internal int FrustumCount;

        public void Execute(int index)
        {
            VividPrimitiveCullRecord record = CullingRecords[index];
            bool visible = record.Handle.IsValid
                && (record.Flags & VividPrimitiveFlags.Valid) != 0
                && (record.Flags & VividPrimitiveFlags.Disabled) == 0
                && (record.PassMask & RequiredPassMask) != 0
                && (record.CameraLayerMask & CameraCullingMask) != 0u;

            if (visible && (record.Flags & VividPrimitiveFlags.Skinned) == 0)
                visible = IntersectsAnyFrustum(record.BoundsMin, record.BoundsMax);

            Visibility[index] = visible ? (byte) 1 : (byte) 0;
        }

        private bool IntersectsAnyFrustum(float3 boundsMin, float3 boundsMax)
        {
            float3 center = (boundsMin + boundsMax) * 0.5f;
            float3 extents = math.max((boundsMax - boundsMin) * 0.5f, float3.zero);
            int frustumCount = math.min(FrustumCount, FrustumPlanes.Length / FrustumPlaneCount);
            for (int frustumIndex = 0; frustumIndex < frustumCount; frustumIndex++)
            {
                bool intersects = true;
                int planeOffset = frustumIndex * FrustumPlaneCount;
                for (int planeIndex = 0; planeIndex < FrustumPlaneCount; planeIndex++)
                {
                    float4 plane = FrustumPlanes[planeOffset + planeIndex];
                    float radius = math.dot(extents, math.abs(plane.xyz));
                    float distance = math.dot(plane.xyz, center) + plane.w;
                    if (distance < -radius)
                    {
                        intersects = false;
                        break;
                    }
                }

                if (intersects)
                    return true;
            }

            return false;
        }
    }

    [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    internal struct VividPrimitiveBuildDrawSetJob : IJob
    {
        internal const int RendererListCount = (int) VividRendererListID.Count;

        [ReadOnly]
        internal NativeArray<VividPrimitiveCullRecord> CullingRecords;

        [ReadOnly]
        internal NativeArray<byte> Visibility;

        [ReadOnly]
        internal NativeArray<VividPrimitiveDrawSourceData> DrawSources;

        [WriteOnly]
        internal NativeArray<VividPrimitiveDrawSetEntry> Entries;

        [WriteOnly]
        internal NativeArray<uint> LegacyInstanceIndices;

        internal NativeArray<VividPrimitiveDrawBucket> Buckets;
        internal NativeArray<int> BucketCounts;
        internal NativeArray<int> BucketWriteCursors;
        internal NativeArray<VividPrimitiveDrawSetBuildResult> Result;

        public void Execute()
        {
            ClearOutputs();

            int visiblePrimitiveCount = 0;
            int recordCount = math.min(CullingRecords.Length, Visibility.Length);
            for (int recordIndex = 0; recordIndex < recordCount; recordIndex++)
            {
                if (Visibility[recordIndex] == 0)
                    continue;

                visiblePrimitiveCount++;
                CountDrawSources(CullingRecords[recordIndex]);
            }

            int drawCount = BuildBucketRanges();
            for (int recordIndex = 0; recordIndex < recordCount; recordIndex++)
            {
                if (Visibility[recordIndex] != 0)
                    ScatterDrawSources(CullingRecords[recordIndex]);
            }

            int nonEmptyBucketCount = 0;
            for (int bucketIndex = 0; bucketIndex < RendererListCount; bucketIndex++)
            {
                if (BucketCounts[bucketIndex] > 0)
                    nonEmptyBucketCount++;
            }

            Result[0] = new VividPrimitiveDrawSetBuildResult
            {
                VisiblePrimitiveCount = visiblePrimitiveCount,
                DrawCount = drawCount,
                NonEmptyBucketCount = nonEmptyBucketCount,
            };
        }

        private void ClearOutputs()
        {
            int bucketCount = math.min(RendererListCount, Buckets.Length);
            bucketCount = math.min(bucketCount, BucketCounts.Length);
            bucketCount = math.min(bucketCount, BucketWriteCursors.Length);
            for (int bucketIndex = 0; bucketIndex < bucketCount; bucketIndex++)
            {
                BucketCounts[bucketIndex] = 0;
                BucketWriteCursors[bucketIndex] = 0;
                Buckets[bucketIndex] = new VividPrimitiveDrawBucket
                {
                    RendererListID = (VividRendererListID) bucketIndex,
                };
            }
            Result[0] = default;
        }

        private void CountDrawSources(in VividPrimitiveCullRecord record)
        {
            if (!TryGetSectionRange(record, out int sectionStart, out int sectionEnd))
                return;

            for (int sourceIndex = sectionStart; sourceIndex < sectionEnd; sourceIndex++)
            {
                if (TryResolveSource(record, sourceIndex, out _, out int bucketIndex))
                    BucketCounts[bucketIndex]++;
            }
        }

        private int BuildBucketRanges()
        {
            int drawOffset = 0;
            for (int bucketIndex = 0; bucketIndex < RendererListCount; bucketIndex++)
            {
                int drawCount = BucketCounts[bucketIndex];
                BucketWriteCursors[bucketIndex] = drawOffset;
                Buckets[bucketIndex] = new VividPrimitiveDrawBucket
                {
                    RendererListID = (VividRendererListID) bucketIndex,
                    DrawOffset = (uint) drawOffset,
                    DrawCount = (uint) drawCount,
                };
                drawOffset += drawCount;
            }
            return drawOffset;
        }

        private void ScatterDrawSources(in VividPrimitiveCullRecord record)
        {
            if (!TryGetSectionRange(record, out int sectionStart, out int sectionEnd))
                return;

            for (int sourceIndex = sectionStart; sourceIndex < sectionEnd; sourceIndex++)
            {
                if (!TryResolveSource(record, sourceIndex, out VividPrimitiveDrawSourceData source, out int bucketIndex))
                    continue;

                int destinationIndex = BucketWriteCursors[bucketIndex]++;
                Entries[destinationIndex] = new VividPrimitiveDrawSetEntry
                {
                    PrimitiveIndex = (uint) record.Handle.Index,
                    PrimitiveGeneration = record.Handle.Generation,
                    DrawSectionIndex = source.AbsoluteDrawSectionIndex,
                    LegacyInstanceIndex = source.LegacyInstanceIndex,
                };
                LegacyInstanceIndices[destinationIndex] = source.LegacyInstanceIndex;
            }
        }

        private bool TryGetSectionRange(
            in VividPrimitiveCullRecord record,
            out int sectionStart,
            out int sectionEnd)
        {
            sectionStart = 0;
            sectionEnd = 0;
            if (record.DrawSectionOffset > int.MaxValue || record.DrawSectionCount > int.MaxValue)
                return false;

            sectionStart = (int) record.DrawSectionOffset;
            int sectionCount = (int) record.DrawSectionCount;
            if (sectionStart < 0 || sectionStart >= DrawSources.Length || sectionCount <= 0)
                return false;

            sectionCount = math.min(sectionCount, DrawSources.Length - sectionStart);
            sectionEnd = sectionStart + sectionCount;
            return sectionEnd > sectionStart;
        }

        private bool TryResolveSource(
            in VividPrimitiveCullRecord record,
            int sourceIndex,
            out VividPrimitiveDrawSourceData source,
            out int bucketIndex)
        {
            source = DrawSources[sourceIndex];
            bucketIndex = -1;
            if ((source.Flags & VividPrimitiveDrawSourceFlags.Valid) == 0
                || source.LegacyInstanceIndex == uint.MaxValue
                || source.AbsoluteDrawSectionIndex != (uint) sourceIndex
                || !source.PrimitiveHandle.Equals(record.Handle))
            {
                return false;
            }

            VividRendererListID rendererListID = source.RendererListID;
            if ((record.Flags & VividPrimitiveFlags.FlipWindingOrder) != 0
                && (rendererListID & VividRendererListID.CullOff) == 0)
            {
                rendererListID ^= VividRendererListID.CullFront;
            }

            bucketIndex = (int) rendererListID;
            return (uint) bucketIndex < (uint) RendererListCount;
        }
    }
}
