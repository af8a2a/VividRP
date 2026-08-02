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
                ulong maskTextureStableId,
                uint maskTextureUpdateCount,
                Texture3D maskTexture,
                VividLocalVolumetricFogEngineData record)
            {
                this.priority = priority;
                this.stableId = stableId;
                this.maskTextureStableId = maskTextureStableId;
                this.maskTextureUpdateCount = maskTextureUpdateCount;
                this.maskTexture = maskTexture;
                this.record = record;
            }

            internal int priority { get; }
            internal ulong stableId { get; }
            internal ulong maskTextureStableId { get; }
            internal uint maskTextureUpdateCount { get; }
            internal Texture3D maskTexture { get; }
            internal VividLocalVolumetricFogEngineData record { get; }
        }

        internal const int ContractVersion = 3;
        internal const int MaximumMaskTextureSlotCount = 16;
        private static readonly VividLocalVolumetricFogEngineData[]
            s_EmptyRecords = Array.Empty<VividLocalVolumetricFogEngineData>();
        private static readonly Texture3D[] s_EmptyMaskTextures =
            Array.Empty<Texture3D>();
        private static readonly List<VividLocalVolumetricFog>
            s_RegisteredFogs = new();

        private ReferencedPathTracingLocalFogState(
            VividLocalVolumetricFogEngineData[] records,
            Texture3D[] maskTextures,
            ulong[] stableIds,
            ulong[] maskTextureStableIds,
            uint[] maskTextureUpdateCounts,
            int unsupportedProceduralMaterialCount,
            int maskSlotOverflowCount,
            int unsupportedBlendCount,
            int truncatedCount)
        {
            this.records = records ?? s_EmptyRecords;
            this.maskTextures = maskTextures ?? s_EmptyMaskTextures;
            this.unsupportedProceduralMaterialCount =
                unsupportedProceduralMaterialCount;
            this.maskSlotOverflowCount = maskSlotOverflowCount;
            this.unsupportedBlendCount = unsupportedBlendCount;
            this.truncatedCount = truncatedCount;
            signature = ComputeSignature(
                this.records,
                stableIds ?? Array.Empty<ulong>(),
                maskTextureStableIds ?? Array.Empty<ulong>(),
                maskTextureUpdateCounts ?? Array.Empty<uint>(),
                unsupportedProceduralMaterialCount,
                maskSlotOverflowCount,
                unsupportedBlendCount,
                truncatedCount);
        }

        internal VividLocalVolumetricFogEngineData[] records { get; }
        internal Texture3D[] maskTextures { get; }
        internal int count => records?.Length ?? 0;
        internal int unsupportedMaskCount =>
            unsupportedProceduralMaterialCount
            + maskSlotOverflowCount;
        internal int unsupportedProceduralMaterialCount { get; }
        internal int maskSlotOverflowCount { get; }
        internal int unsupportedBlendCount { get; }
        internal int truncatedCount { get; }
        internal ulong signature { get; }

        internal static ReferencedPathTracingLocalFogState Disabled =>
            new(
                s_EmptyRecords,
                s_EmptyMaskTextures,
                Array.Empty<ulong>(),
                Array.Empty<ulong>(),
                Array.Empty<uint>(),
                0,
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
            var unsupportedProceduralMaterialCount = 0;
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
                var hasMask = fog.TryGetVolumeMask(
                    out var maskTexture,
                    out var alphaOnly);
                if (parameters.maskMode
                        == VividLocalVolumetricFogMaskMode.Material
                    && parameters.materialMask != null
                    && fog.UsesProceduralVolumetricMaterial()
                    && !hasMask)
                {
                    unsupportedProceduralMaterialCount++;
                    continue;
                }

                var stableId =
                    EntityId.ToULong(fog.GetEntityId());
                var record = fog.ConvertToEngineData(camera);
                ulong maskTextureStableId = 0;
                uint maskTextureUpdateCount = 0;
                if (hasMask)
                {
                    // The final explicit texture slot is assigned after
                    // priority sorting and record-count truncation.
                    record.parameters.w = 0.0f;
                    record.textureScaleOffset0.w =
                        alphaOnly ? 1.0f : 0.0f;
                    maskTextureStableId =
                        EntityId.ToULong(maskTexture.GetEntityId());
                    maskTextureUpdateCount = maskTexture.updateCount;
                }
                else
                {
                    // Keep the homogeneous record independent of unused
                    // texture animation so accumulation is not reset.
                    record.parameters.w = -1.0f;
                    record.textureScaleOffset0 = Vector4.zero;
                    record.textureScaleOffset1 = new Vector4(
                        0.0f,
                        0.0f,
                        0.0f,
                        record.textureScaleOffset1.w);
                }

                candidates.Add(
                    new Candidate(
                        fog.priority,
                        stableId,
                        maskTextureStableId,
                        maskTextureUpdateCount,
                        maskTexture,
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
            var selectedRecords =
                new List<VividLocalVolumetricFogEngineData>(
                    Mathf.Min(candidates.Count, maximumCount));
            var selectedCandidates =
                new List<Candidate>(
                    Mathf.Min(candidates.Count, maximumCount));
            var maskTextures =
                new List<Texture3D>(MaximumMaskTextureSlotCount);
            var maskTextureSlots =
                new Dictionary<ulong, int>(
                    MaximumMaskTextureSlotCount);
            var maskSlotOverflowCount = 0;
            var truncatedCount = 0;
            for (var index = 0; index < candidates.Count; index++)
            {
                if (selectedRecords.Count >= maximumCount)
                {
                    truncatedCount++;
                    continue;
                }

                var candidate = candidates[index];
                var record = candidate.record;
                if (candidate.maskTexture != null)
                {
                    if (!maskTextureSlots.TryGetValue(
                            candidate.maskTextureStableId,
                            out var maskTextureSlot))
                    {
                        if (maskTextures.Count
                            >= MaximumMaskTextureSlotCount)
                        {
                            maskSlotOverflowCount++;
                            continue;
                        }

                        maskTextureSlot = maskTextures.Count;
                        maskTextureSlots.Add(
                            candidate.maskTextureStableId,
                            maskTextureSlot);
                        maskTextures.Add(candidate.maskTexture);
                    }

                    record.parameters.w = maskTextureSlot;
                }

                selectedRecords.Add(record);
                selectedCandidates.Add(candidate);
            }

            var recordCount = selectedRecords.Count;
            if (recordCount == 0)
            {
                return new ReferencedPathTracingLocalFogState(
                    s_EmptyRecords,
                    maskTextures.ToArray(),
                    Array.Empty<ulong>(),
                    Array.Empty<ulong>(),
                    Array.Empty<uint>(),
                    unsupportedProceduralMaterialCount,
                    maskSlotOverflowCount,
                    unsupportedBlendCount,
                    truncatedCount);
            }

            var records = selectedRecords.ToArray();
            var stableIds = new ulong[recordCount];
            var maskTextureStableIds = new ulong[recordCount];
            var maskTextureUpdateCounts = new uint[recordCount];
            for (var index = 0; index < recordCount; index++)
            {
                stableIds[index] = selectedCandidates[index].stableId;
                maskTextureStableIds[index] =
                    selectedCandidates[index].maskTextureStableId;
                maskTextureUpdateCounts[index] =
                    selectedCandidates[index].maskTextureUpdateCount;
            }

            return new ReferencedPathTracingLocalFogState(
                records,
                maskTextures.ToArray(),
                stableIds,
                maskTextureStableIds,
                maskTextureUpdateCounts,
                unsupportedProceduralMaterialCount,
                maskSlotOverflowCount,
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
            IReadOnlyList<ulong> maskTextureStableIds,
            IReadOnlyList<uint> maskTextureUpdateCounts,
            int unsupportedProceduralMaterialCount,
            int maskSlotOverflowCount,
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
                unsupportedProceduralMaterialCount);
            ReferencedPathTracingStableHash.Add(
                ref hash,
                maskSlotOverflowCount);
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
                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    maskTextureStableIds[index]);
                ReferencedPathTracingStableHash.Add(
                    ref hash,
                    (ulong)maskTextureUpdateCounts[index]);
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
