using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.RenderPass.Core
{
    internal enum ReferencedPathTracingLightType : uint
    {
        Invalid = 0,
        Directional = 1,
        Point = 2,
        Spot = 3,
        Rectangle = 4,
        Tube = 5,
        Disc = 6,
        Environment = 7,
        EmissiveTriangle = 8,
    }

    [Flags]
    internal enum ReferencedPathTracingLightFlags : uint
    {
        None = 0,
        Singular = 1u << 0,
        Infinite = 1u << 1,
        BsdfReachable = 1u << 2,
        OneSided = 1u << 3,
        CastsShadows = 1u << 4,
        HasStableId = 1u << 5,
        UsesAreaMeasure = 1u << 6,
        UsesLineMeasure = 1u << 7,
        AffectVolumetric = 1u << 8,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ReferencedPathTracingLightRecord
    {
        internal const int Stride = 160;

        internal Vector3 positionWS;
        internal float range;

        internal Vector3 forwardWS;
        internal float angularDiameter;

        internal Vector3 rightWS;
        internal float shapeRadius;

        internal Vector3 upWS;
        internal float barnDoorCosAngle;

        internal Vector3 radiometricColor;
        internal float selectionWeight;

        internal Vector2 areaSize;
        internal Vector2 spotAngleParameters;

        internal Vector2 rangeAttenuation;
        internal float barnDoorLength;
        internal float shadowStrength;

        internal float selectionPdf;
        internal float cdf;
        internal uint renderingLayerMask;
        internal uint shadowRenderingLayerMask;

        internal uint stableIdLow;
        internal uint stableIdHigh;
        internal uint lightType;
        internal uint flags;

        internal float volumetricDimmer;
        internal float volumetricShadowDimmer;
        internal float volumetricFadeDistance;
        internal float padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ReferencedPathTracingLightListParameters
    {
        internal const int Stride = 48;
        internal const uint Version = 3;
        internal const uint DistributionModeCdf = 1;

        internal uint lightCount;
        internal uint activeLightCount;
        internal uint unsupportedLightCount;
        internal uint unstableLightCount;

        internal float totalSelectionWeight;
        internal float inverseTotalSelectionWeight;
        internal uint signatureLow;
        internal uint signatureHigh;

        internal uint version;
        internal uint distributionMode;
        internal uint incompleteLocalProposalLightCount;
        internal uint reserved1;

        internal static ReferencedPathTracingLightListParameters CreateEmpty()
        {
            return new ReferencedPathTracingLightListParameters
            {
                version = Version,
                distributionMode = DistributionModeCdf,
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ReferencedPathTracingLightListStorageBlock
    {
        internal const int WordCount = 12;
        internal const int Stride = WordCount * sizeof(uint);

        internal uint word0;
        internal uint word1;
        internal uint word2;
        internal uint word3;
        internal uint word4;
        internal uint word5;
        internal uint word6;
        internal uint word7;
        internal uint word8;
        internal uint word9;
        internal uint word10;
        internal uint word11;

        internal uint GetWord(int wordIndex)
        {
            return wordIndex switch
            {
                0 => word0,
                1 => word1,
                2 => word2,
                3 => word3,
                4 => word4,
                5 => word5,
                6 => word6,
                7 => word7,
                8 => word8,
                9 => word9,
                10 => word10,
                11 => word11,
                _ => 0u,
            };
        }

        internal void SetWord(int wordIndex, uint value)
        {
            switch (wordIndex)
            {
                case 0: word0 = value; break;
                case 1: word1 = value; break;
                case 2: word2 = value; break;
                case 3: word3 = value; break;
                case 4: word4 = value; break;
                case 5: word5 = value; break;
                case 6: word6 = value; break;
                case 7: word7 = value; break;
                case 8: word8 = value; break;
                case 9: word9 = value; break;
                case 10: word10 = value; break;
                case 11: word11 = value; break;
            }
        }

        internal static ReferencedPathTracingLightListStorageBlock
            FromParameters(ReferencedPathTracingLightListParameters parameters)
        {
            return new ReferencedPathTracingLightListStorageBlock
            {
                word0 = parameters.lightCount,
                word1 = parameters.activeLightCount,
                word2 = parameters.unsupportedLightCount,
                word3 = parameters.unstableLightCount,
                word4 = FloatToUInt(parameters.totalSelectionWeight),
                word5 = FloatToUInt(parameters.inverseTotalSelectionWeight),
                word6 = parameters.signatureLow,
                word7 = parameters.signatureHigh,
                word8 = parameters.version,
                word9 = parameters.distributionMode,
                word10 = parameters.incompleteLocalProposalLightCount,
                word11 = parameters.reserved1,
            };
        }

        private static uint FloatToUInt(float value)
        {
            return unchecked((uint)BitConverter.SingleToInt32Bits(value));
        }
    }

    internal readonly struct ReferencedPathTracingLightSpatialIndexBuildResult
    {
        internal ReferencedPathTracingLightSpatialIndexBuildResult(
            uint[] words,
            int wordCount,
            Vector3 boundsMin,
            Vector3 inverseBoundsExtent,
            int finiteLightCount,
            int unboundedLightCount,
            int overflowCellCount,
            ulong signature)
        {
            this.words = words ?? Array.Empty<uint>();
            this.wordCount = Mathf.Clamp(wordCount, 0, this.words.Length);
            this.boundsMin = boundsMin;
            this.inverseBoundsExtent = inverseBoundsExtent;
            this.finiteLightCount = finiteLightCount;
            this.unboundedLightCount = unboundedLightCount;
            this.overflowCellCount = overflowCellCount;
            this.signature = signature;
        }

        internal uint[] words { get; }

        internal int wordCount { get; }

        internal Vector3 boundsMin { get; }

        internal Vector3 inverseBoundsExtent { get; }

        internal int finiteLightCount { get; }

        internal int unboundedLightCount { get; }

        internal int overflowCellCount { get; }

        internal ulong signature { get; }
    }

    internal readonly struct ReferencedPathTracingLightListBuildResult
    {
        internal ReferencedPathTracingLightListBuildResult(
            ReferencedPathTracingLightRecord[] records,
            int recordCount,
            ReferencedPathTracingLightListParameters parameters,
            ReferencedPathTracingLightSpatialIndexBuildResult spatialIndex,
            ReferencedPathTracingLightListStorageBlock[] storageBlocks,
            int storageBlockCount)
        {
            this.records = records ?? Array.Empty<ReferencedPathTracingLightRecord>();
            this.recordCount = Mathf.Clamp(recordCount, 0, this.records.Length);
            this.parameters = parameters;
            this.spatialIndex = spatialIndex;
            this.storageBlocks = storageBlocks
                ?? Array.Empty<ReferencedPathTracingLightListStorageBlock>();
            this.storageBlockCount = Mathf.Clamp(
                storageBlockCount,
                0,
                this.storageBlocks.Length);
        }

        internal ReferencedPathTracingLightRecord[] records { get; }

        internal int recordCount { get; }

        internal ReferencedPathTracingLightListParameters parameters { get; }

        internal ReferencedPathTracingLightSpatialIndexBuildResult spatialIndex
        {
            get;
        }

        internal ReferencedPathTracingLightListStorageBlock[] storageBlocks
        {
            get;
        }

        internal int storageBlockCount { get; }
    }

    internal static class ReferencedPathTracingLightSpatialIndexBuilder
    {
        internal const uint Version = 1;
        internal const int GridResolution = 32;
        internal const int CellCapacity = 64;
        internal const int AxisCount = 3;
        internal const int HeaderWordCount = 24;
        internal const int CellHeaderWordCount = 2;
        internal const uint CellOverflowMask = 1u << 31;
        internal const int EmptyStorageBlockCount =
            1
            + (
                HeaderWordCount
                + AxisCount
                    * GridResolution
                    * GridResolution
                    * CellHeaderWordCount
                + ReferencedPathTracingLightListStorageBlock.WordCount
                - 1)
            / ReferencedPathTracingLightListStorageBlock.WordCount;

        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;
        private const float MinimumBoundsExtent = 0.001f;

        internal sealed class CellBuilder
        {
            internal readonly List<uint> lightIndices = new(CellCapacity);
            internal bool overflow;
        }

        internal readonly struct InfluenceBounds
        {
            internal InfluenceBounds(Vector3 min, Vector3 max)
            {
                this.min = min;
                this.max = max;
            }

            internal Vector3 min { get; }

            internal Vector3 max { get; }
        }

        internal sealed class BuildWorkspace
        {
            internal readonly List<uint> m_UnboundedLightIndices = new();
            internal readonly List<uint> m_FiniteLightIndices = new();
            internal readonly List<InfluenceBounds> m_FiniteLightBounds = new();
            internal readonly List<uint> m_PackedLightIndices = new();
            internal readonly CellBuilder[] m_Cells =
                new CellBuilder[GetCellCount()];
            internal readonly uint[] m_CellOffsets =
                new uint[GetCellCount()];
            internal readonly uint[] m_CellCountsAndFlags =
                new uint[GetCellCount()];
            internal uint[] m_Words = Array.Empty<uint>();
            internal ReferencedPathTracingLightListStorageBlock[]
                m_StorageBlocks =
                    Array.Empty<ReferencedPathTracingLightListStorageBlock>();
        }

        internal static ReferencedPathTracingLightSpatialIndexBuildResult Build(
            ReferencedPathTracingLightRecord[] records,
            int recordCount,
            ReferencedPathTracingLightListParameters listParameters,
            BuildWorkspace workspace)
        {
            records ??= Array.Empty<ReferencedPathTracingLightRecord>();
            recordCount = Mathf.Clamp(recordCount, 0, records.Length);

            var unboundedLightIndices = workspace?.m_UnboundedLightIndices
                ?? new List<uint>();
            var finiteLightIndices = workspace?.m_FiniteLightIndices
                ?? new List<uint>();
            var finiteLightBounds = workspace?.m_FiniteLightBounds
                ?? new List<InfluenceBounds>();
            var packedLightIndices = workspace?.m_PackedLightIndices
                ?? new List<uint>();
            var cells = workspace?.m_Cells
                ?? new CellBuilder[GetCellCount()];
            var cellOffsets = workspace?.m_CellOffsets
                ?? new uint[cells.Length];
            var cellCountsAndFlags = workspace?.m_CellCountsAndFlags
                ?? new uint[cells.Length];
            unboundedLightIndices.Clear();
            finiteLightIndices.Clear();
            finiteLightBounds.Clear();
            packedLightIndices.Clear();
            ClearCells(cells);

            var boundsMin = new Vector3(
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity);
            var boundsMax = new Vector3(
                float.NegativeInfinity,
                float.NegativeInfinity,
                float.NegativeInfinity);

            for (var lightIndex = 0; lightIndex < recordCount; lightIndex++)
            {
                var record = records[lightIndex];
                if (!TryCreateInfluenceBounds(record, out var bounds))
                {
                    unboundedLightIndices.Add((uint)lightIndex);
                    continue;
                }

                finiteLightIndices.Add((uint)lightIndex);
                finiteLightBounds.Add(bounds);
                boundsMin = Vector3.Min(boundsMin, bounds.min);
                boundsMax = Vector3.Max(boundsMax, bounds.max);
            }

            Vector3 inverseBoundsExtent;
            if (finiteLightIndices.Count > 0)
            {
                ExpandDegenerateBounds(ref boundsMin, ref boundsMax);
                var boundsExtent = boundsMax - boundsMin;
                inverseBoundsExtent = new Vector3(
                    1.0f / boundsExtent.x,
                    1.0f / boundsExtent.y,
                    1.0f / boundsExtent.z);
            }
            else
            {
                boundsMin = Vector3.zero;
                inverseBoundsExtent = Vector3.zero;
            }

            for (var finiteIndex = 0;
                 finiteIndex < finiteLightIndices.Count;
                 finiteIndex++)
            {
                RasterizeInfluenceBounds(
                    finiteLightIndices[finiteIndex],
                    finiteLightBounds[finiteIndex],
                    boundsMin,
                    inverseBoundsExtent,
                    cells);
            }

            var cellHeaderWordOffset = HeaderWordCount;
            var lightIndexWordOffset =
                cellHeaderWordOffset + cells.Length * CellHeaderWordCount;
            var minimumPackedCapacity =
                unboundedLightIndices.Count + cells.Length;
            if (packedLightIndices.Capacity < minimumPackedCapacity)
                packedLightIndices.Capacity = minimumPackedCapacity;
            packedLightIndices.AddRange(unboundedLightIndices);
            var overflowCellCount = 0;
            for (var cellIndex = 0; cellIndex < cells.Length; cellIndex++)
            {
                var cell = cells[cellIndex];
                cellOffsets[cellIndex] = (uint)packedLightIndices.Count;
                cellCountsAndFlags[cellIndex] = 0u;
                if (cell == null)
                    continue;

                packedLightIndices.AddRange(cell.lightIndices);
                var countAndFlags = (uint)cell.lightIndices.Count;
                if (cell.overflow)
                {
                    countAndFlags |= CellOverflowMask;
                    overflowCellCount++;
                }

                cellCountsAndFlags[cellIndex] = countAndFlags;
            }

            var wordCount = lightIndexWordOffset + packedLightIndices.Count;
            uint[] words;
            if (workspace == null)
            {
                words = new uint[wordCount];
            }
            else
            {
                EnsureCapacity(ref workspace.m_Words, wordCount);
                words = workspace.m_Words;
            }

            words[0] = Version;
            words[1] = GridResolution;
            words[2] = CellCapacity;
            words[3] = (uint)cells.Length;
            words[4] = (uint)cellHeaderWordOffset;
            words[5] = (uint)lightIndexWordOffset;
            words[6] = (uint)packedLightIndices.Count;
            words[7] = (uint)overflowCellCount;
            words[8] = FloatToUInt(boundsMin.x);
            words[9] = FloatToUInt(boundsMin.y);
            words[10] = FloatToUInt(boundsMin.z);
            words[11] = FloatToUInt(inverseBoundsExtent.x);
            words[12] = FloatToUInt(inverseBoundsExtent.y);
            words[13] = FloatToUInt(inverseBoundsExtent.z);
            words[14] = 0u;
            words[15] = (uint)unboundedLightIndices.Count;
            words[16] = (uint)finiteLightIndices.Count;
            words[17] = listParameters.signatureLow;
            words[18] = listParameters.signatureHigh;
            words[19] = HeaderWordCount;

            for (var cellIndex = 0; cellIndex < cells.Length; cellIndex++)
            {
                var wordOffset =
                    cellHeaderWordOffset
                    + cellIndex * CellHeaderWordCount;
                words[wordOffset] = cellOffsets[cellIndex];
                words[wordOffset + 1] = cellCountsAndFlags[cellIndex];
            }

            for (var lightIndex = 0;
                 lightIndex < packedLightIndices.Count;
                 lightIndex++)
            {
                words[lightIndexWordOffset + lightIndex] =
                    packedLightIndices[lightIndex];
            }

            var signature = ComputeSignature(
                listParameters,
                boundsMin,
                inverseBoundsExtent,
                finiteLightIndices.Count,
                unboundedLightIndices.Count,
                overflowCellCount);
            words[20] = (uint)signature;
            words[21] = (uint)(signature >> 32);
            words[22] = 0u;
            words[23] = 0u;
            return new ReferencedPathTracingLightSpatialIndexBuildResult(
                words,
                wordCount,
                boundsMin,
                inverseBoundsExtent,
                finiteLightIndices.Count,
                unboundedLightIndices.Count,
                overflowCellCount,
                signature);
        }

        internal static ReferencedPathTracingLightListStorageBlock[]
            CreateStorageBlocks(
                ReferencedPathTracingLightListParameters parameters,
                ReferencedPathTracingLightSpatialIndexBuildResult spatialIndex,
                BuildWorkspace workspace,
                out int storageBlockCount)
        {
            var spatialWords = spatialIndex.words ?? Array.Empty<uint>();
            var spatialWordCount = Mathf.Clamp(
                spatialIndex.wordCount,
                0,
                spatialWords.Length);
            var spatialBlockCount =
                (spatialWordCount
                    + ReferencedPathTracingLightListStorageBlock.WordCount
                    - 1)
                / ReferencedPathTracingLightListStorageBlock.WordCount;
            storageBlockCount = 1 + spatialBlockCount;
            ReferencedPathTracingLightListStorageBlock[] blocks;
            if (workspace == null)
            {
                blocks =
                    new ReferencedPathTracingLightListStorageBlock[
                        storageBlockCount];
            }
            else
            {
                EnsureCapacity(
                    ref workspace.m_StorageBlocks,
                    storageBlockCount);
                blocks = workspace.m_StorageBlocks;
                for (var blockIndex = 1;
                     blockIndex < storageBlockCount;
                     blockIndex++)
                {
                    blocks[blockIndex] = default;
                }
            }

            blocks[0] =
                ReferencedPathTracingLightListStorageBlock.FromParameters(
                    parameters);

            for (var wordIndex = 0;
                 wordIndex < spatialWordCount;
                 wordIndex++)
            {
                var blockIndex =
                    1
                    + wordIndex
                        / ReferencedPathTracingLightListStorageBlock.WordCount;
                var blockWordIndex =
                    wordIndex
                    % ReferencedPathTracingLightListStorageBlock.WordCount;
                var block = blocks[blockIndex];
                block.SetWord(blockWordIndex, spatialWords[wordIndex]);
                blocks[blockIndex] = block;
            }

            return blocks;
        }

        private static void ClearCells(CellBuilder[] cells)
        {
            for (var cellIndex = 0; cellIndex < cells.Length; cellIndex++)
            {
                var cell = cells[cellIndex];
                if (cell == null)
                    continue;

                cell.lightIndices.Clear();
                cell.overflow = false;
            }
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

        private static bool TryCreateInfluenceBounds(
            ReferencedPathTracingLightRecord record,
            out InfluenceBounds bounds)
        {
            bounds = default;
            var flags = (ReferencedPathTracingLightFlags)record.flags;
            if ((flags & ReferencedPathTracingLightFlags.Infinite) != 0
                || !HasFiniteVector(record.positionWS))
            {
                return false;
            }

            var range = ResolveInfluenceRange(record);
            if (!IsFinite(range) || range <= 0.0f)
                return false;

            var shapeExtent = ResolveShapeExtent(record);
            if (!HasFiniteVector(shapeExtent))
                return false;

            var rangeExtent = new Vector3(range, range, range);
            var totalExtent = shapeExtent + rangeExtent;
            var min = record.positionWS - totalExtent;
            var max = record.positionWS + totalExtent;
            if (!HasFiniteVector(min) || !HasFiniteVector(max))
                return false;

            bounds = new InfluenceBounds(min, max);
            return true;
        }

        private static float ResolveInfluenceRange(
            ReferencedPathTracingLightRecord record)
        {
            var range = Mathf.Max(record.range, 0.0f);
            var scale = record.rangeAttenuation.x;
            var bias = record.rangeAttenuation.y;
            if (IsFinite(scale)
                && IsFinite(bias)
                && scale > 0.0f
                && bias > 0.0f)
            {
                var attenuationRange =
                    Mathf.Sqrt(Mathf.Sqrt(bias) / scale);
                if (IsFinite(attenuationRange))
                    range = Mathf.Max(range, attenuationRange);
            }

            return range;
        }

        private static Vector3 ResolveShapeExtent(
            ReferencedPathTracingLightRecord record)
        {
            var lightType =
                (ReferencedPathTracingLightType)record.lightType;
            var absoluteRight = Abs(record.rightWS);
            var absoluteUp = Abs(record.upWS);
            return lightType switch
            {
                ReferencedPathTracingLightType.Rectangle =>
                    0.5f
                    * (Mathf.Max(record.areaSize.x, 0.0f) * absoluteRight
                        + Mathf.Max(record.areaSize.y, 0.0f) * absoluteUp),
                ReferencedPathTracingLightType.Disc =>
                    Mathf.Max(record.shapeRadius, 0.0f)
                    * (absoluteRight + absoluteUp),
                ReferencedPathTracingLightType.Tube =>
                    0.5f
                    * Mathf.Max(record.areaSize.x, 0.0f)
                    * absoluteRight,
                _ => Vector3.zero,
            };
        }

        private static void RasterizeInfluenceBounds(
            uint lightIndex,
            InfluenceBounds bounds,
            Vector3 boundsMin,
            Vector3 inverseBoundsExtent,
            CellBuilder[] cells)
        {
            var minimum = ScaleToGrid(
                bounds.min,
                boundsMin,
                inverseBoundsExtent);
            var maximum = ScaleToGrid(
                bounds.max,
                boundsMin,
                inverseBoundsExtent);

            RasterizeProjection(
                lightIndex,
                0,
                minimum.y,
                maximum.y,
                minimum.z,
                maximum.z,
                cells);
            RasterizeProjection(
                lightIndex,
                1,
                minimum.x,
                maximum.x,
                minimum.z,
                maximum.z,
                cells);
            RasterizeProjection(
                lightIndex,
                2,
                minimum.x,
                maximum.x,
                minimum.y,
                maximum.y,
                cells);
        }

        private static void RasterizeProjection(
            uint lightIndex,
            int axis,
            int minimumU,
            int maximumU,
            int minimumV,
            int maximumV,
            CellBuilder[] cells)
        {
            var cellsPerAxis = GridResolution * GridResolution;
            for (var v = minimumV; v <= maximumV; v++)
            {
                for (var u = minimumU; u <= maximumU; u++)
                {
                    var cellIndex =
                        axis * cellsPerAxis + u + v * GridResolution;
                    var cell = cells[cellIndex] ??= new CellBuilder();
                    if (cell.lightIndices.Count < CellCapacity)
                        cell.lightIndices.Add(lightIndex);
                    else
                        cell.overflow = true;
                }
            }
        }

        private static Vector3Int ScaleToGrid(
            Vector3 position,
            Vector3 boundsMin,
            Vector3 inverseBoundsExtent)
        {
            var normalized = Vector3.Scale(
                position - boundsMin,
                inverseBoundsExtent);
            return new Vector3Int(
                ScaleToGrid(normalized.x),
                ScaleToGrid(normalized.y),
                ScaleToGrid(normalized.z));
        }

        private static int ScaleToGrid(float normalized)
        {
            return Mathf.Clamp(
                Mathf.FloorToInt(normalized * GridResolution),
                0,
                GridResolution - 1);
        }

        private static void ExpandDegenerateBounds(
            ref Vector3 boundsMin,
            ref Vector3 boundsMax)
        {
            for (var axis = 0; axis < 3; axis++)
            {
                if (boundsMax[axis] - boundsMin[axis]
                    >= MinimumBoundsExtent)
                {
                    continue;
                }

                var center =
                    0.5f * (boundsMin[axis] + boundsMax[axis]);
                boundsMin[axis] =
                    center - 0.5f * MinimumBoundsExtent;
                boundsMax[axis] =
                    center + 0.5f * MinimumBoundsExtent;
            }
        }

        private static int GetCellCount()
        {
            return AxisCount * GridResolution * GridResolution;
        }

        private static ulong ComputeSignature(
            ReferencedPathTracingLightListParameters listParameters,
            Vector3 boundsMin,
            Vector3 inverseBoundsExtent,
            int finiteLightCount,
            int unboundedLightCount,
            int overflowCellCount)
        {
            var hash = FnvOffsetBasis;
            Hash(ref hash, Version);
            Hash(ref hash, GridResolution);
            Hash(ref hash, CellCapacity);
            Hash(ref hash, listParameters.signatureLow);
            Hash(ref hash, listParameters.signatureHigh);
            Hash(ref hash, boundsMin.x);
            Hash(ref hash, boundsMin.y);
            Hash(ref hash, boundsMin.z);
            Hash(ref hash, inverseBoundsExtent.x);
            Hash(ref hash, inverseBoundsExtent.y);
            Hash(ref hash, inverseBoundsExtent.z);
            Hash(ref hash, finiteLightCount);
            Hash(ref hash, unboundedLightCount);
            Hash(ref hash, overflowCellCount);
            return hash;
        }

        private static void Hash(ref ulong hash, uint value)
        {
            hash ^= value;
            hash *= FnvPrime;
        }

        private static void Hash(ref ulong hash, int value)
        {
            Hash(ref hash, unchecked((uint)value));
        }

        private static void Hash(ref ulong hash, float value)
        {
            Hash(
                ref hash,
                unchecked((uint)BitConverter.SingleToInt32Bits(value)));
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(
                Mathf.Abs(value.x),
                Mathf.Abs(value.y),
                Mathf.Abs(value.z));
        }

        private static bool HasFiniteVector(Vector3 value)
        {
            return IsFinite(value.x)
                && IsFinite(value.y)
                && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static uint FloatToUInt(float value)
        {
            return unchecked((uint)BitConverter.SingleToInt32Bits(value));
        }
    }

    internal static class ReferencedPathTracingLightListBuilder
    {
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;
        private const float DirectionEpsilon = 1e-8f;
        private const float FiniteDirectionalThreshold = 1e-6f;

        internal readonly struct Candidate
        {
            internal Candidate(
                ulong stableId,
                ReferencedPathTracingLightRecord record)
            {
                this.stableId = stableId;
                this.record = record;
            }

            internal ulong stableId { get; }

            internal ReferencedPathTracingLightRecord record { get; }
        }

        private sealed class CandidateComparer : IComparer<Candidate>
        {
            public int Compare(Candidate lhs, Candidate rhs)
            {
                return CompareCandidates(lhs, rhs);
            }
        }

        private static readonly CandidateComparer s_CandidateComparer = new();

        internal sealed class BuildWorkspace
        {
            internal readonly List<Candidate> m_Candidates = new();
            internal ReferencedPathTracingLightRecord[] m_Records =
                Array.Empty<ReferencedPathTracingLightRecord>();
            internal readonly ReferencedPathTracingLightSpatialIndexBuilder
                .BuildWorkspace m_SpatialIndexWorkspace = new();
        }

        internal static ReferencedPathTracingLightListBuildResult Build(
            IReadOnlyList<VividLightRenderData> sceneLights)
        {
            return Build(sceneLights, null);
        }

        internal static ReferencedPathTracingLightListBuildResult Build(
            IReadOnlyList<VividLightRenderData> sceneLights,
            BuildWorkspace workspace)
        {
            var sceneLightCount = sceneLights?.Count ?? 0;
            var candidates = workspace?.m_Candidates
                ?? new List<Candidate>(sceneLightCount);
            candidates.Clear();
            if (candidates.Capacity < sceneLightCount)
                candidates.Capacity = sceneLightCount;

            uint unsupportedLightCount = 0;
            uint unstableLightCount = 0;

            if (sceneLights != null)
            {
                for (var lightIndex = 0; lightIndex < sceneLights.Count; lightIndex++)
                {
                    var source = sceneLights[lightIndex];
                    if (!IsEnabledAndActive(source))
                        continue;

                    var stableId = EntityId.ToULong(source.lightEntityId);
                    if (stableId == EntityId.ToULong(EntityId.None))
                    {
                        unstableLightCount++;
                        continue;
                    }

                    if (!TryCreateRecord(source, stableId, out var record))
                    {
                        unsupportedLightCount++;
                        continue;
                    }

                    candidates.Add(new Candidate(stableId, record));
                }
            }

            candidates.Sort(s_CandidateComparer);
            var recordCount = 0;
            for (var candidateIndex = 0;
                 candidateIndex < candidates.Count;)
            {
                var nextCandidateIndex = candidateIndex + 1;
                while (nextCandidateIndex < candidates.Count
                    && candidates[nextCandidateIndex].stableId
                        == candidates[candidateIndex].stableId)
                {
                    nextCandidateIndex++;
                }

                var duplicateCount =
                    nextCandidateIndex - candidateIndex;
                if (duplicateCount == 1)
                    recordCount++;
                else
                    unstableLightCount += (uint)duplicateCount;

                candidateIndex = nextCandidateIndex;
            }

            ReferencedPathTracingLightRecord[] records;
            if (workspace == null)
            {
                records = recordCount > 0
                    ? new ReferencedPathTracingLightRecord[recordCount]
                    : Array.Empty<ReferencedPathTracingLightRecord>();
            }
            else
            {
                EnsureCapacity(ref workspace.m_Records, recordCount);
                records = workspace.m_Records;
            }

            var recordIndex = 0;
            for (var candidateIndex = 0;
                 candidateIndex < candidates.Count;)
            {
                var nextCandidateIndex = candidateIndex + 1;
                while (nextCandidateIndex < candidates.Count
                    && candidates[nextCandidateIndex].stableId
                        == candidates[candidateIndex].stableId)
                {
                    nextCandidateIndex++;
                }

                if (nextCandidateIndex - candidateIndex == 1)
                {
                    records[recordIndex++] =
                        candidates[candidateIndex].record;
                }

                candidateIndex = nextCandidateIndex;
            }

            double totalSelectionWeight = 0.0;
            uint activeLightCount = 0;
            uint incompleteLocalProposalLightCount = 0;

            for (var lightIndex = 0; lightIndex < recordCount; lightIndex++)
            {
                var record = records[lightIndex];
                if (record.selectionWeight <= 0.0f)
                    continue;

                totalSelectionWeight += record.selectionWeight;
                activeLightCount++;
                if (RequiresGlobalProposalSupport(
                    (ReferencedPathTracingLightType)record.lightType))
                {
                    incompleteLocalProposalLightCount++;
                }
            }

            var parameters =
                ReferencedPathTracingLightListParameters.CreateEmpty();
            parameters.lightCount = (uint)recordCount;
            parameters.activeLightCount = activeLightCount;
            parameters.unsupportedLightCount = unsupportedLightCount;
            parameters.unstableLightCount = unstableLightCount;
            parameters.incompleteLocalProposalLightCount =
                incompleteLocalProposalLightCount;

            if (totalSelectionWeight > 0.0
                && totalSelectionWeight <= float.MaxValue)
            {
                parameters.totalSelectionWeight = (float)totalSelectionWeight;
                parameters.inverseTotalSelectionWeight =
                    1.0f / parameters.totalSelectionWeight;
                AssignSelectionDistribution(
                    records,
                    recordCount,
                    totalSelectionWeight);
            }

            var signature = ComputeSignature(
                records,
                recordCount,
                parameters);
            parameters.signatureLow = (uint)signature;
            parameters.signatureHigh = (uint)(signature >> 32);
            var spatialIndex =
                ReferencedPathTracingLightSpatialIndexBuilder.Build(
                    records,
                    recordCount,
                    parameters,
                    workspace?.m_SpatialIndexWorkspace);
            var storageBlocks =
                ReferencedPathTracingLightSpatialIndexBuilder
                    .CreateStorageBlocks(
                        parameters,
                        spatialIndex,
                        workspace?.m_SpatialIndexWorkspace,
                        out var storageBlockCount);
            return new ReferencedPathTracingLightListBuildResult(
                records,
                recordCount,
                parameters,
                spatialIndex,
                storageBlocks,
                storageBlockCount);
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

        private static bool TryCreateRecord(
            VividLightRenderData source,
            ulong stableId,
            out ReferencedPathTracingLightRecord record)
        {
            record = default;
            if (!TryResolveLightType(source, out var lightType)
                || !HasFiniteColor(source.color))
            {
                return false;
            }

            var isDirectional =
                lightType == ReferencedPathTracingLightType.Directional;
            if (!isDirectional
                && (!HasFiniteVector(source.positionWS)
                    || !IsFinite(source.range)
                    || source.range <= 0.0f))
            {
                return false;
            }

            var width = SanitizeNonNegative(source.areaSize.x);
            var height = SanitizeNonNegative(source.areaSize.y);
            var shapeRadius = SanitizeNonNegative(source.shapeRadius);
            if ((lightType == ReferencedPathTracingLightType.Rectangle
                    && (width <= 0.0f || height <= 0.0f))
                || (lightType == ReferencedPathTracingLightType.Tube
                    && width <= 0.0f)
                || (lightType == ReferencedPathTracingLightType.Disc
                    && shapeRadius <= 0.0f))
            {
                return false;
            }

            if (lightType == ReferencedPathTracingLightType.Tube)
                height = 0.0f;
            else if (lightType == ReferencedPathTracingLightType.Disc)
                width = height = 2.0f * shapeRadius;

            var angularDiameter = isDirectional
                ? Mathf.Clamp(
                    SanitizeNonNegative(source.angularDiameter),
                    0.0f,
                    90.0f) * Mathf.Deg2Rad
                : 0.0f;
            var radiometricColor = new Vector3(
                Mathf.Max(source.color.x, 0.0f),
                Mathf.Max(source.color.y, 0.0f),
                Mathf.Max(source.color.z, 0.0f));
            ResolveSpotAngleParameters(
                source.lightType,
                source.innerSpotAngle,
                source.spotAngle,
                out var angleScale,
                out var angleOffset);
            ResolveRangeAttenuation(
                source,
                isDirectional,
                out var rangeAttenuationScale,
                out var rangeAttenuationBias);

            var flags = ResolveFlags(source, lightType, angularDiameter);
            record = new ReferencedPathTracingLightRecord
            {
                positionWS = isDirectional ? Vector3.zero : source.positionWS,
                range = isDirectional ? 0.0f : Mathf.Max(source.range, 0.001f),
                forwardWS = NormalizeDirection(source.forwardWS, Vector3.forward),
                angularDiameter = angularDiameter,
                rightWS = NormalizeDirection(source.rightWS, Vector3.right),
                shapeRadius = shapeRadius,
                upWS = NormalizeDirection(source.upWS, Vector3.up),
                barnDoorCosAngle = IsAreaLight(lightType)
                    ? Mathf.Cos(
                        Mathf.Clamp(
                            SanitizeNonNegative(source.barnDoorAngle),
                            0.0f,
                            90.0f) * Mathf.Deg2Rad)
                    : 0.0f,
                radiometricColor = radiometricColor,
                selectionWeight = ComputeSelectionWeight(
                    lightType,
                    radiometricColor,
                    width,
                    height,
                    shapeRadius),
                areaSize = new Vector2(width, height),
                spotAngleParameters = new Vector2(angleScale, angleOffset),
                rangeAttenuation = new Vector2(
                    rangeAttenuationScale,
                    rangeAttenuationBias),
                barnDoorLength = IsAreaLight(lightType)
                    ? SanitizeNonNegative(source.barnDoorLength)
                    : 0.0f,
                shadowStrength = Mathf.Clamp01(
                    SanitizeNonNegative(source.shadowStrength)),
                renderingLayerMask = source.renderingLayerMask,
                shadowRenderingLayerMask = source.shadowRenderingLayerMask,
                stableIdLow = (uint)stableId,
                stableIdHigh = (uint)(stableId >> 32),
                lightType = (uint)lightType,
                flags = (uint)flags,
                volumetricDimmer = Mathf.Clamp(
                    SanitizeNonNegative(source.volumetricDimmer),
                    0.0f,
                    VividAdditionalLightData.MaxVolumetricDimmer),
                volumetricShadowDimmer = Mathf.Clamp01(
                    SanitizeNonNegative(
                        source.volumetricShadowDimmer)),
                volumetricFadeDistance = SanitizeNonNegative(
                    source.volumetricFadeDistance),
                padding = 0.0f,
            };
            return true;
        }

        private static bool TryResolveLightType(
            VividLightRenderData source,
            out ReferencedPathTracingLightType lightType)
        {
            lightType = source.lightType switch
            {
                LightType.Directional =>
                    ReferencedPathTracingLightType.Directional,
                LightType.Point => ReferencedPathTracingLightType.Point,
                LightType.Spot => ReferencedPathTracingLightType.Spot,
                LightType.Rectangle =>
                    ReferencedPathTracingLightType.Rectangle,
                LightType.Tube => ReferencedPathTracingLightType.Tube,
                LightType.Disc => ReferencedPathTracingLightType.Disc,
                _ => ReferencedPathTracingLightType.Invalid,
            };
            return lightType != ReferencedPathTracingLightType.Invalid;
        }

        private static ReferencedPathTracingLightFlags ResolveFlags(
            VividLightRenderData source,
            ReferencedPathTracingLightType lightType,
            float angularDiameter)
        {
            var flags = ReferencedPathTracingLightFlags.HasStableId;
            if ((source.flags
                    & VividLightRenderDataFlags.AffectVolumetric) != 0)
            {
                flags |=
                    ReferencedPathTracingLightFlags.AffectVolumetric;
            }
            if ((source.flags & VividLightRenderDataFlags.CastShadows) != 0
                && source.shadowStrength > 0.0f)
            {
                flags |= ReferencedPathTracingLightFlags.CastsShadows;
            }

            switch (lightType)
            {
                case ReferencedPathTracingLightType.Directional:
                    flags |= ReferencedPathTracingLightFlags.Infinite;
                    flags |= angularDiameter > FiniteDirectionalThreshold
                        ? ReferencedPathTracingLightFlags.BsdfReachable
                        : ReferencedPathTracingLightFlags.Singular;
                    break;
                case ReferencedPathTracingLightType.Point:
                case ReferencedPathTracingLightType.Spot:
                    flags |= ReferencedPathTracingLightFlags.Singular;
                    break;
                case ReferencedPathTracingLightType.Rectangle:
                case ReferencedPathTracingLightType.Disc:
                    flags |= ReferencedPathTracingLightFlags.BsdfReachable
                        | ReferencedPathTracingLightFlags.OneSided
                        | ReferencedPathTracingLightFlags.UsesAreaMeasure;
                    break;
                case ReferencedPathTracingLightType.Tube:
                    flags |= ReferencedPathTracingLightFlags.Singular
                        | ReferencedPathTracingLightFlags.OneSided
                        | ReferencedPathTracingLightFlags.UsesLineMeasure;
                    break;
            }

            return flags;
        }

        private static float ComputeSelectionWeight(
            ReferencedPathTracingLightType lightType,
            Vector3 color,
            float width,
            float height,
            float shapeRadius)
        {
            var basePower = Mathf.Max(color.x, Mathf.Max(color.y, color.z));
            if (basePower <= 0.0f)
                return 0.0f;

            var weight = lightType switch
            {
                ReferencedPathTracingLightType.Rectangle =>
                    basePower * width * height,
                ReferencedPathTracingLightType.Tube => basePower * width,
                ReferencedPathTracingLightType.Disc =>
                    basePower * Mathf.PI * shapeRadius * shapeRadius,
                _ => basePower,
            };
            return IsFinite(weight) ? Mathf.Max(weight, 0.0f) : 0.0f;
        }

        private static void AssignSelectionDistribution(
            ReferencedPathTracingLightRecord[] records,
            int recordCount,
            double totalSelectionWeight)
        {
            double accumulatedWeight = 0.0;
            var lastActiveIndex = -1;
            for (var lightIndex = 0; lightIndex < recordCount; lightIndex++)
            {
                var record = records[lightIndex];
                if (record.selectionWeight > 0.0f)
                {
                    accumulatedWeight += record.selectionWeight;
                    lastActiveIndex = lightIndex;
                }

                record.selectionPdf =
                    (float)(record.selectionWeight / totalSelectionWeight);
                record.cdf =
                    (float)(accumulatedWeight / totalSelectionWeight);
                records[lightIndex] = record;
            }

            if (lastActiveIndex < 0)
                return;

            var lastRecord = records[lastActiveIndex];
            lastRecord.cdf = 1.0f;
            records[lastActiveIndex] = lastRecord;
            for (var lightIndex = lastActiveIndex + 1;
                 lightIndex < recordCount;
                 lightIndex++)
            {
                var record = records[lightIndex];
                record.cdf = 1.0f;
                records[lightIndex] = record;
            }
        }

        private static int CompareCandidates(Candidate lhs, Candidate rhs)
        {
            var stableIdOrder = lhs.stableId.CompareTo(rhs.stableId);
            return stableIdOrder != 0
                ? stableIdOrder
                : lhs.record.lightType.CompareTo(rhs.record.lightType);
        }

        private static bool IsEnabledAndActive(VividLightRenderData source)
        {
            const VividLightRenderDataFlags requiredFlags =
                VividLightRenderDataFlags.Enabled
                | VividLightRenderDataFlags.ActiveInHierarchy;
            return (source.flags & requiredFlags) == requiredFlags;
        }

        private static bool IsAreaLight(
            ReferencedPathTracingLightType lightType)
        {
            return lightType == ReferencedPathTracingLightType.Rectangle
                || lightType == ReferencedPathTracingLightType.Disc;
        }

        private static void ResolveSpotAngleParameters(
            LightType lightType,
            float innerSpotAngle,
            float outerSpotAngle,
            out float angleScale,
            out float angleOffset)
        {
            if (lightType != LightType.Spot)
            {
                angleScale = 0.0f;
                angleOffset = 1.0f;
                return;
            }

            var innerHalfAngleDegrees = Mathf.Clamp(
                SanitizeNonNegative(innerSpotAngle) * 0.5f,
                0.0f,
                89.0f);
            var minimumOuterHalfAngle =
                Mathf.Min(innerHalfAngleDegrees + 0.001f, 89.0f);
            var outerHalfAngleDegrees = Mathf.Clamp(
                SanitizeNonNegative(outerSpotAngle) * 0.5f,
                minimumOuterHalfAngle,
                89.0f);
            var cosInner = Mathf.Cos(innerHalfAngleDegrees * Mathf.Deg2Rad);
            var cosOuter = Mathf.Cos(outerHalfAngleDegrees * Mathf.Deg2Rad);
            var angleRange = Mathf.Max(cosInner - cosOuter, 0.001f);
            angleScale = 1.0f / angleRange;
            angleOffset = -cosOuter * angleScale;
        }

        private static void ResolveRangeAttenuation(
            VividLightRenderData source,
            bool isDirectional,
            out float scale,
            out float bias)
        {
            if (isDirectional)
            {
                scale = 0.0f;
                bias = 0.0f;
                return;
            }

            var range = Mathf.Max(source.range, 0.001f);
            scale = IsFinite(source.rangeAttenuationScale)
                && source.rangeAttenuationScale > 0.0f
                    ? source.rangeAttenuationScale
                    : 1.0f / Mathf.Max(range * range, 1e-6f);
            bias = IsFinite(source.rangeAttenuationBias)
                && source.rangeAttenuationBias > 0.0f
                    ? source.rangeAttenuationBias
                    : 1.0f;
        }

        private static Vector3 NormalizeDirection(
            Vector3 direction,
            Vector3 fallback)
        {
            if (!HasFiniteVector(direction))
                return fallback;

            var lengthSquared = direction.sqrMagnitude;
            return lengthSquared > DirectionEpsilon
                ? direction / Mathf.Sqrt(lengthSquared)
                : fallback;
        }

        private static bool HasFiniteColor(Vector3 value)
        {
            return HasFiniteVector(value);
        }

        private static bool RequiresGlobalProposalSupport(
            ReferencedPathTracingLightType lightType)
        {
            // The local point/spot support tests use the same range and cone
            // windows as candidate evaluation. Area/line lights are indexed
            // by conservative bounds and use center-based local importance,
            // so they retain a global proposal to cover their full shapes.
            return lightType == ReferencedPathTracingLightType.Rectangle
                || lightType == ReferencedPathTracingLightType.Tube
                || lightType == ReferencedPathTracingLightType.Disc
                || lightType
                    == ReferencedPathTracingLightType.EmissiveTriangle;
        }

        private static bool HasFiniteVector(Vector3 value)
        {
            return IsFinite(value.x)
                && IsFinite(value.y)
                && IsFinite(value.z);
        }

        private static float SanitizeNonNegative(float value)
        {
            return IsFinite(value) ? Mathf.Max(value, 0.0f) : 0.0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static ulong ComputeSignature(
            ReferencedPathTracingLightRecord[] records,
            int recordCount,
            ReferencedPathTracingLightListParameters parameters)
        {
            var hash = FnvOffsetBasis;
            Hash(ref hash, parameters.version);
            Hash(ref hash, parameters.distributionMode);
            Hash(ref hash, parameters.lightCount);
            Hash(ref hash, parameters.activeLightCount);
            Hash(ref hash, parameters.unsupportedLightCount);
            Hash(ref hash, parameters.unstableLightCount);
            Hash(ref hash, parameters.totalSelectionWeight);
            Hash(ref hash, parameters.inverseTotalSelectionWeight);
            Hash(
                ref hash,
                parameters.incompleteLocalProposalLightCount);

            for (var lightIndex = 0; lightIndex < recordCount; lightIndex++)
                Hash(ref hash, records[lightIndex]);

            return hash;
        }

        private static void Hash(
            ref ulong hash,
            ReferencedPathTracingLightRecord record)
        {
            Hash(ref hash, record.positionWS);
            Hash(ref hash, record.range);
            Hash(ref hash, record.forwardWS);
            Hash(ref hash, record.angularDiameter);
            Hash(ref hash, record.rightWS);
            Hash(ref hash, record.shapeRadius);
            Hash(ref hash, record.upWS);
            Hash(ref hash, record.barnDoorCosAngle);
            Hash(ref hash, record.radiometricColor);
            Hash(ref hash, record.selectionWeight);
            Hash(ref hash, record.areaSize.x);
            Hash(ref hash, record.areaSize.y);
            Hash(ref hash, record.spotAngleParameters.x);
            Hash(ref hash, record.spotAngleParameters.y);
            Hash(ref hash, record.rangeAttenuation.x);
            Hash(ref hash, record.rangeAttenuation.y);
            Hash(ref hash, record.barnDoorLength);
            Hash(ref hash, record.shadowStrength);
            Hash(ref hash, record.selectionPdf);
            Hash(ref hash, record.cdf);
            Hash(ref hash, record.renderingLayerMask);
            Hash(ref hash, record.shadowRenderingLayerMask);
            Hash(ref hash, record.stableIdLow);
            Hash(ref hash, record.stableIdHigh);
            Hash(ref hash, record.lightType);
            Hash(ref hash, record.flags);
            Hash(ref hash, record.volumetricDimmer);
            Hash(ref hash, record.volumetricShadowDimmer);
            Hash(ref hash, record.volumetricFadeDistance);
        }

        private static void Hash(ref ulong hash, Vector3 value)
        {
            Hash(ref hash, value.x);
            Hash(ref hash, value.y);
            Hash(ref hash, value.z);
        }

        private static void Hash(ref ulong hash, float value)
        {
            Hash(
                ref hash,
                unchecked((uint)BitConverter.SingleToInt32Bits(value)));
        }

        private static void Hash(ref ulong hash, uint value)
        {
            hash ^= value & 0xffu;
            hash *= FnvPrime;
            hash ^= (value >> 8) & 0xffu;
            hash *= FnvPrime;
            hash ^= (value >> 16) & 0xffu;
            hash *= FnvPrime;
            hash ^= (value >> 24) & 0xffu;
            hash *= FnvPrime;
        }
    }
}
