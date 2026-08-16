using System;
using System.Collections.Generic;
using UnityEngine;

namespace VividRP.Runtime.RenderPass.Core
{
    internal readonly struct ReferencedPathTracingLocalFogState
        : IEquatable<ReferencedPathTracingLocalFogState>
    {
        internal readonly struct Candidate
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

        private sealed class CandidateComparer : IComparer<Candidate>
        {
            public int Compare(Candidate left, Candidate right)
            {
                return CompareCandidates(left, right);
            }
        }

        internal const int ContractVersion = 3;
        internal const int MaximumMaskTextureSlotCount = 16;
        private static readonly VividLocalVolumetricFogEngineData[]
            s_EmptyRecords = Array.Empty<VividLocalVolumetricFogEngineData>();
        private static readonly Texture3D[] s_EmptyMaskTextures =
            Array.Empty<Texture3D>();
        private static readonly List<VividLocalVolumetricFog>
            s_RegisteredFogs = new();
        private static readonly CandidateComparer s_CandidateComparer = new();

        internal sealed class BuildWorkspace
        {
            internal readonly List<Candidate> candidates = new();
            internal readonly List<Candidate> selectedCandidates = new();
            internal readonly List<Texture3D> maskTextures =
                new(MaximumMaskTextureSlotCount);
            internal readonly Dictionary<ulong, int> maskTextureSlots =
                new(MaximumMaskTextureSlotCount);
            internal VividLocalVolumetricFogEngineData[] records =
                Array.Empty<VividLocalVolumetricFogEngineData>();
            internal Texture3D[] maskTextureArray = Array.Empty<Texture3D>();
            internal ulong[] stableIds = Array.Empty<ulong>();
            internal ulong[] maskTextureStableIds = Array.Empty<ulong>();
            internal uint[] maskTextureUpdateCounts = Array.Empty<uint>();
        }

        private ReferencedPathTracingLocalFogState(
            VividLocalVolumetricFogEngineData[] records,
            int recordCount,
            Texture3D[] maskTextures,
            int maskTextureCount,
            ulong[] stableIds,
            ulong[] maskTextureStableIds,
            uint[] maskTextureUpdateCounts,
            int unsupportedProceduralMaterialCount,
            int maskSlotOverflowCount,
            int unsupportedBlendCount,
            int truncatedCount)
        {
            this.records = records ?? s_EmptyRecords;
            count = Mathf.Clamp(recordCount, 0, this.records.Length);
            this.maskTextures = maskTextures ?? s_EmptyMaskTextures;
            this.maskTextureCount = Mathf.Clamp(
                maskTextureCount,
                0,
                this.maskTextures.Length);
            this.unsupportedProceduralMaterialCount =
                unsupportedProceduralMaterialCount;
            this.maskSlotOverflowCount = maskSlotOverflowCount;
            this.unsupportedBlendCount = unsupportedBlendCount;
            this.truncatedCount = truncatedCount;
            signature = ComputeSignature(
                this.records,
                count,
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
        internal int count { get; }
        internal int maskTextureCount { get; }
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
                0,
                s_EmptyMaskTextures,
                0,
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
            return Resolve(camera, volumetricsEnabled, null);
        }

        internal static ReferencedPathTracingLocalFogState Resolve(
            Camera camera,
            bool volumetricsEnabled,
            BuildWorkspace workspace)
        {
            if (camera == null || !volumetricsEnabled)
                return Disabled;

            VividLocalVolumetricFogManager.GetRegisteredFogs(
                s_RegisteredFogs);
            var candidates = workspace?.candidates
                ?? new List<Candidate>(s_RegisteredFogs.Count);
            candidates.Clear();
            if (candidates.Capacity < s_RegisteredFogs.Count)
                candidates.Capacity = s_RegisteredFogs.Count;

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

            candidates.Sort(s_CandidateComparer);
            var maximumCount =
                VividVolumetricUtility
                    .ResolveMaxLocalVolumetricFogCount(
                        VividRenderPipelineGlobalSettings.instance);
            maximumCount =
                VividLocalVolumetricFogManager
                    .ClampVisibleLocalVolumetricFogCount(maximumCount);
            var selectedCapacity =
                Mathf.Min(candidates.Count, maximumCount);
            var selectedCandidates = workspace?.selectedCandidates
                ?? new List<Candidate>(selectedCapacity);
            var maskTextures = workspace?.maskTextures
                ?? new List<Texture3D>(MaximumMaskTextureSlotCount);
            var maskTextureSlots = workspace?.maskTextureSlots
                ?? new Dictionary<ulong, int>(
                    MaximumMaskTextureSlotCount);
            selectedCandidates.Clear();
            maskTextures.Clear();
            maskTextureSlots.Clear();
            if (selectedCandidates.Capacity < selectedCapacity)
                selectedCandidates.Capacity = selectedCapacity;

            var maskSlotOverflowCount = 0;
            var truncatedCount = 0;
            for (var index = 0; index < candidates.Count; index++)
            {
                if (selectedCandidates.Count >= maximumCount)
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

                selectedCandidates.Add(
                    new Candidate(
                        candidate.priority,
                        candidate.stableId,
                        candidate.maskTextureStableId,
                        candidate.maskTextureUpdateCount,
                        candidate.maskTexture,
                        record));
            }

            var recordCount = selectedCandidates.Count;
            VividLocalVolumetricFogEngineData[] records;
            Texture3D[] maskTextureArray;
            ulong[] stableIds;
            ulong[] maskTextureStableIds;
            uint[] maskTextureUpdateCounts;
            if (workspace == null)
            {
                records = recordCount > 0
                    ? new VividLocalVolumetricFogEngineData[recordCount]
                    : s_EmptyRecords;
                stableIds = recordCount > 0
                    ? new ulong[recordCount]
                    : Array.Empty<ulong>();
                maskTextureStableIds = recordCount > 0
                    ? new ulong[recordCount]
                    : Array.Empty<ulong>();
                maskTextureUpdateCounts = recordCount > 0
                    ? new uint[recordCount]
                    : Array.Empty<uint>();
                maskTextureArray = maskTextures.Count > 0
                    ? maskTextures.ToArray()
                    : s_EmptyMaskTextures;
            }
            else
            {
                EnsureCapacity(ref workspace.records, recordCount);
                EnsureCapacity(ref workspace.stableIds, recordCount);
                EnsureCapacity(
                    ref workspace.maskTextureStableIds,
                    recordCount);
                EnsureCapacity(
                    ref workspace.maskTextureUpdateCounts,
                    recordCount);
                EnsureCapacity(
                    ref workspace.maskTextureArray,
                    maskTextures.Count);
                records = workspace.records;
                stableIds = workspace.stableIds;
                maskTextureStableIds = workspace.maskTextureStableIds;
                maskTextureUpdateCounts =
                    workspace.maskTextureUpdateCounts;
                maskTextureArray = workspace.maskTextureArray;
                for (var index = 0;
                     index < maskTextures.Count;
                     index++)
                {
                    maskTextureArray[index] = maskTextures[index];
                }
                Array.Clear(
                    maskTextureArray,
                    maskTextures.Count,
                    maskTextureArray.Length - maskTextures.Count);
            }

            for (var index = 0; index < recordCount; index++)
            {
                var candidate = selectedCandidates[index];
                records[index] = candidate.record;
                stableIds[index] = candidate.stableId;
                maskTextureStableIds[index] =
                    candidate.maskTextureStableId;
                maskTextureUpdateCounts[index] =
                    candidate.maskTextureUpdateCount;
            }

            return new ReferencedPathTracingLocalFogState(
                records,
                recordCount,
                maskTextureArray,
                maskTextures.Count,
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
            int recordCount,
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
                recordCount);
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
            for (var index = 0; index < recordCount; index++)
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

        private static void EnsureCapacity<T>(
            ref T[] values,
            int requiredCapacity)
        {
            var currentCapacity = values?.Length ?? 0;
            if (currentCapacity >= requiredCapacity)
                return;

            var doubledCapacity = currentCapacity <= int.MaxValue / 2
                ? currentCapacity * 2
                : int.MaxValue;
            values = new T[Mathf.Max(requiredCapacity, doubledCapacity)];
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
