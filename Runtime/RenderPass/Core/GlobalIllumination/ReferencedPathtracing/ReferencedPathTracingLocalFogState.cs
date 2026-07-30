using System;
using System.Collections.Generic;
using UnityEngine;

namespace VividRP.Runtime.RenderPass.Core
{
    internal readonly struct ReferencedPathTracingLocalFogState
        : IEquatable<ReferencedPathTracingLocalFogState>
    {
        private readonly struct Candidate
        {
            internal Candidate(
                int priority,
                ulong stableId,
                VividLocalVolumetricFogEngineData record)
            {
                this.priority = priority;
                this.stableId = stableId;
                this.record = record;
            }

            internal int priority { get; }
            internal ulong stableId { get; }
            internal VividLocalVolumetricFogEngineData record { get; }
        }

        internal const int ContractVersion = 1;
        private static readonly VividLocalVolumetricFogEngineData[]
            s_EmptyRecords = Array.Empty<VividLocalVolumetricFogEngineData>();
        private static readonly List<VividLocalVolumetricFog>
            s_RegisteredFogs = new();

        private ReferencedPathTracingLocalFogState(
            VividLocalVolumetricFogEngineData[] records,
            ulong[] stableIds,
            int unsupportedMaskCount,
            int unsupportedBlendCount,
            int truncatedCount)
        {
            this.records = records ?? s_EmptyRecords;
            this.unsupportedMaskCount = unsupportedMaskCount;
            this.unsupportedBlendCount = unsupportedBlendCount;
            this.truncatedCount = truncatedCount;
            signature = ComputeSignature(
                this.records,
                stableIds ?? Array.Empty<ulong>(),
                unsupportedMaskCount,
                unsupportedBlendCount,
                truncatedCount);
        }

        internal VividLocalVolumetricFogEngineData[] records { get; }
        internal int count => records?.Length ?? 0;
        internal int unsupportedMaskCount { get; }
        internal int unsupportedBlendCount { get; }
        internal int truncatedCount { get; }
        internal ulong signature { get; }

        internal static ReferencedPathTracingLocalFogState Disabled =>
            new(
                s_EmptyRecords,
                Array.Empty<ulong>(),
                0,
                0,
                0);

        internal static ReferencedPathTracingLocalFogState Resolve(
            Camera camera,
            bool volumetricsEnabled)
        {
            if (camera == null || !volumetricsEnabled)
                return Disabled;

            VividLocalVolumetricFogManager.GetRegisteredFogs(
                s_RegisteredFogs);
            var candidates =
                new List<Candidate>(s_RegisteredFogs.Count);
            var unsupportedMaskCount = 0;
            var unsupportedBlendCount = 0;

            for (var index = 0; index < s_RegisteredFogs.Count; index++)
            {
                var fog = s_RegisteredFogs[index];
                if (fog == null || !fog.IsActive())
                    continue;

                if (fog.blendingMode
                    != VividLocalVolumetricFogBlendingMode.Additive)
                {
                    unsupportedBlendCount++;
                    continue;
                }

                var parameters = fog.parameters;
                if (parameters.maskMode
                        == VividLocalVolumetricFogMaskMode.Material
                    || fog.TryGetVolumeMask(out _, out _))
                {
                    unsupportedMaskCount++;
                    continue;
                }

                var stableId =
                    EntityId.ToULong(fog.GetEntityId());
                var record = fog.ConvertToEngineData(camera);
                // Stage two intentionally supports the analytic fade field only.
                // Removing texture animation from the record also keeps temporal
                // accumulation stable for an unused texture transform.
                record.textureScaleOffset0 = Vector4.zero;
                record.textureScaleOffset1 = new Vector4(
                    0.0f,
                    0.0f,
                    0.0f,
                    record.textureScaleOffset1.w);
                candidates.Add(
                    new Candidate(
                        fog.priority,
                        stableId,
                        record));
            }

            candidates.Sort(CompareCandidates);
            var maximumCount =
                VividVolumetricUtility
                    .ResolveMaxLocalVolumetricFogCount(
                        VividRenderPipelineGlobalSettings.instance);
            maximumCount =
                VividLocalVolumetricFogManager
                    .ClampVisibleLocalVolumetricFogCount(maximumCount);
            var recordCount = Mathf.Min(
                candidates.Count,
                maximumCount);
            var truncatedCount =
                Mathf.Max(candidates.Count - recordCount, 0);
            if (recordCount == 0)
            {
                return new ReferencedPathTracingLocalFogState(
                    s_EmptyRecords,
                    Array.Empty<ulong>(),
                    unsupportedMaskCount,
                    unsupportedBlendCount,
                    truncatedCount);
            }

            var records =
                new VividLocalVolumetricFogEngineData[recordCount];
            var stableIds = new ulong[recordCount];
            for (var index = 0; index < recordCount; index++)
            {
                records[index] = candidates[index].record;
                stableIds[index] = candidates[index].stableId;
            }

            return new ReferencedPathTracingLocalFogState(
                records,
                stableIds,
                unsupportedMaskCount,
                unsupportedBlendCount,
                truncatedCount);
        }

        public bool Equals(ReferencedPathTracingLocalFogState other)
        {
            return signature == other.signature;
        }

        public override bool Equals(object obj)
        {
            return obj is ReferencedPathTracingLocalFogState other
                && Equals(other);
        }

        public override int GetHashCode()
        {
            return signature.GetHashCode();
        }

        private static int CompareCandidates(
            Candidate left,
            Candidate right)
        {
            var priorityComparison =
                right.priority.CompareTo(left.priority);
            return priorityComparison != 0
                ? priorityComparison
                : left.stableId.CompareTo(right.stableId);
        }

        private static ulong ComputeSignature(
            IReadOnlyList<VividLocalVolumetricFogEngineData> records,
            IReadOnlyList<ulong> stableIds,
            int unsupportedMaskCount,
            int unsupportedBlendCount,
            int truncatedCount)
        {
            var hash = ReferencedPathTracingStableHash.OffsetBasis;
            ReferencedPathTracingStableHash.Add(
                ref hash,
                ContractVersion);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                records.Count);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                unsupportedMaskCount);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                unsupportedBlendCount);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                truncatedCount);
            for (var index = 0; index < records.Count; index++)
            {
                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    stableIds[index]);
                AddRecord(ref hash, records[index]);
            }

            return hash;
        }

        private static void AddRecord(
            ref ulong hash,
            VividLocalVolumetricFogEngineData record)
        {
            AddVector(ref hash, record.worldToLocalRow0);
            AddVector(ref hash, record.worldToLocalRow1);
            AddVector(ref hash, record.worldToLocalRow2);
            AddVector(ref hash, record.scatteringExtinction);
            AddVector(ref hash, record.positiveFade);
            AddVector(ref hash, record.negativeFade);
            AddVector(ref hash, record.distanceFade);
            AddVector(ref hash, record.parameters);
            AddVector(ref hash, record.textureScaleOffset0);
            AddVector(ref hash, record.textureScaleOffset1);
        }

        private static void AddVector(ref ulong hash, Vector4 value)
        {
            ReferencedPathTracingStableHash.Add(ref hash, value.x);
            ReferencedPathTracingStableHash.Add(ref hash, value.y);
            ReferencedPathTracingStableHash.Add(ref hash, value.z);
            ReferencedPathTracingStableHash.Add(ref hash, value.w);
        }
    }
}
